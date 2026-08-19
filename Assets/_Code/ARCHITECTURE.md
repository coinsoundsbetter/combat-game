# GLM Fighter Architecture

This document summarizes the current prototype architecture so a new session can continue development without relying on chat history.

## Goal

GLM Fighter is a Unity 3D presentation, fixed 2D side-view, 1v1 P2P fighting-game prototype.

The combat model is deterministic. Network play exchanges inputs only. Unity physics, `Rigidbody`, `Collider`, `NetworkTransform`, and Animator transitions must not decide combat results.

## Core Rule

```text
Core = combat truth
Runtime = input and presentation
Network = P2P transport
Prefab = visual character asset
Animator = animation playback only
```

Do not put these in Animator, prefab scripts, Unity physics, or animation events:

```text
Damage
Hitbox active frames
Hit confirmation
Combo legality
Health changes
Final movement results
Network sync state
```

Those belong in `Core/BattleSimulation.cs` or future Core-side combat modules.

## Main Files

```text
Assets/_Code/Core/
- SimMath.cs
- FighterInput.cs
- BattleTypes.cs
- BattleSimulation.cs
- CombatTimelineTypes.cs

Assets/_Code/Network/
- TransportPacket.cs
- LiteNetPacketCodec.cs
- LiteNetLibRoom.cs

Assets/_Code/Runtime/
- NetworkBattleRunner.cs
- BattleNetworkCoordinator.cs
- BattleInputSync.cs
- BattleChecksumTracker.cs
- BattleTickDriver.cs
- BattleSessionController.cs
- BattleDebugHud.cs
- BattleRoleCatalog.cs
- BattleInputReader.cs
- BattleSceneBootstrap.cs
- BattlePresentationController.cs
- FighterRoleDefinition.cs
- MotionTimelineAsset.cs
- FighterView.cs
- FighterAvatar.cs
- FighterAnimationDriver.cs

Assets/_Code/Editor/
- MotionDataEditorWindow.cs       Motion Timeline editor window

Assets/_Code/Plugins/
- LiteNetLib.dll
```

## Main Loop

`Runtime/NetworkBattleRunner.cs` remains the single Unity runtime entry point
and composition root. Battle responsibilities are delegated to focused runtime
helpers so the entry point does not own transport buffers, view lifetimes, or
scene bootstrap details.

Responsibilities:

```text
Compose and drive the runtime helpers
Expose the external character / ready / debug API
Call the fixed 60 FPS tick driver
Route received transport packets to input, checksum, and session logic
Apply presentation and debug output once per render frame
```

The helper ownership is:

```text
BattleRoleCatalog              RoleDefinition lookup and role conversion
BattleNetworkCoordinator       LiteNetLib room lifecycle and packet transport
BattleInputSync                Delayed input buffers and redundancy
BattleChecksumTracker          Checksum history and first desync detection
BattleTickDriver               Render-time accumulator and fixed simulation ticks
BattleSessionController        Lobby, Ready, Start, Local, and Leave transitions
BattleDebugHud                 Temporary OnGUI state display and commands
BattlePresentationController   FighterView and LogicWorldDebugView lifetimes
BattleSceneBootstrap           Temporary camera, light, and ground creation
BattleInputReader              Temporary keyboard mapping
```

Current P2P flow:

```text
One player chooses Listen
Other player chooses Connect
Listener becomes P1
Connector becomes P2
Both players sync CharacterIndex and Ready
Listener sends StartBattle after both players are ready
During battle, peers exchange frame inputs only
Both peers simulate locally
```

## Combat Simulation

`Core/BattleSimulation.cs` owns combat truth.

Current responsibilities:

```text
Fighter role stats
Fighter position
Facing
Movement
Jumping with Motion Timeline EntityOffset frames
Guard
Light attacks
Startup / active / recovery
Hitstun
Blockstun
Pushback
KO
Motion Timeline-driven EntityOffset, HitBox, and body queries
Base HurtBox queries from FighterRoleDefinition
Checksum
Snapshot / restore
```

The simulation currently does not use Unity physics. Combat collision is integer rectangle math using `SimRect`.
For a role with a cooked `JumpMove`, the jump arc is authored by the move's
per-frame `EntityOffset.Y`. The current move frame resolves the entity center;
hurtboxes, pushboxes, hitboxes, and presentation all follow that same center.
Every playable role must provide jump frames; missing Jump Motion Timeline is a
configuration error rather than a gravity-based fallback.

Core is authoritative for movement and combat:

```text
Position
Entity center from the current Motion Timeline frame
Velocity
Jump phase
Attack phase
Hit detection
Damage application
Hitstun / blockstun
KO state
```

Presentation may display these results, but must not replace them.

## Logic Body

Every playable role defines one standing base HurtBox size directly on `Runtime/FighterRoleDefinition.cs`, separate from the Unity prefab / render hierarchy:

```text
Base HurtBox width
Base HurtBox height
```

The default HurtBox is centered above the LogicPosition: its horizontal center is zero and its vertical center is half of the configured height. A Body Track can author a per-frame Bounds size offset relative to that role's original Bounds, while `EntityOffset` moves the complete entity frame.

Core combat box queries read HitBoxes from `MotionTimelineAsset` and use the `FighterRoleDefinition` standing HurtBox size only for the default HurtBox. Runtime never synthesizes gameplay boxes from prefab colliders, renderer bounds, or Animator poses.

`MotionTimelineAsset` can author the body frame per frame:

```text
EntityOffset = per-frame LogicEntity movement
BoundsSizeOffset = per-frame width and height delta from the role's original Bounds
Flags = motion state such as Active / Invulnerable / CanCancel
```

Body Keys are change points, not independent frame ranges. At any frame, the
Body state is the state from the latest Body Key whose frame is less than or
equal to the current frame, and it remains active until the next Body Key.
When `Lerp` is enabled, the state transitions linearly between those two Keys.
When `Active` is disabled, the Body Track contributes no per-frame state.

Motion Timeline data is edited directly on `MotionTimelineAsset`. The asset stores authoring tracks and expands them into match-local deterministic data; it is not a prefab or Animator data source.

`BattleRoleCatalog` / `FighterRoleDefinition` bake the deterministic parts of
`MotionTimelineAsset` into `CombatMoveData` before a match starts. Core then
reads only the baked entity offset, Bounds size offset, frame flags, and HitBox
rectangles. It must not read prefab Transforms or Animator skeleton pose at
runtime.

## Fighter Role Data

`Runtime/FighterRoleDefinition.cs` is the Unity asset used to configure a playable role / character.

Each role definition currently contains:

```text
Role id
Prefab
Max health
Walk speed
Jump MotionTimeline reference
Light Attack MotionTimeline reference
```

Character-owned combat numbers belong here, not in generic motion data:

```text
Damage
Hitstun frames
Blockstun frames
Pushback
Character-specific attack tuning
```

At runtime, `FighterRoleDefinition` converts designer-friendly values into integer `Core.FighterRoleStats`.

Conversion rule:

```text
Inspector field names stay simple for designers
Size fields = Unity units
Speed fields = Unity units per second
Gravity = Unity units per second squared
Timing fields = frames
Core simulation = integer fixed-unit values
1000 core units = 1 Unity unit
```

Keep the conversion at the RoleDefinition boundary. Do not ask designers to author core-unit values directly.

`FighterRoleStats` is copied into each `FighterState`. This means:

```text
P1 / P2 are player slots
RoleStats belong to the selected role / character
The same player slot can use different role data depending on character selection
```

`BattleSimulation.Reset(playerOneRoleStats, playerTwoRoleStats)` initializes each slot with the selected role data.

## Motion Timeline

`Runtime/MotionTimelineAsset.cs` is the Unity asset format for move-frame data. `Core/CombatTimelineTypes.cs` defines the compact in-memory logic types used by deterministic simulation; it is not an authoring asset.

Core-side move data is organized as:

```text
CombatMoveData
├── MoveId
├── TotalFrames
├── FrameRate
├── Loop
├── Attack            // optional attack defaults for offensive moves
└── CombatFrameData[]
    ├── Flags
    ├── EntityOffset
    ├── BoundsSizeOffset
    └── CombatBox[]   // Hit / Hurt / Push / etc.
```

`MotionTimelineAsset` stores a motion duration and its authoring tracks:

```text
TotalFrames
FrameRate
Loop
MotionTrackDefinition[]    // timeline tracks
  MotionTimelineHitBoxTrackDefinition  // HitBox lane
    MotionTimelineHitBoxKey             // frame, center, size, active
  MotionTimelineBodyTrackDefinition    // Body lane
    MotionTimelineBodyKey               // entity/body offsets
  MotionTimelineStateTrackDefinition   // named state ranges, e.g. JumpStartup
```

At match setup, `MotionTimelineAsset.BuildRuntimeMoveData()` expands the tracks into match-local deterministic `CombatMoveData` and `CombatFrameData[]`. These expanded arrays are not serialized back onto the asset and are not rollback state.

The generated runtime move data describes what happens on each logic frame:

```text
Move id
Frame rate
Loop flag
Per-frame entity offset
Per-frame Bounds size offset
Per-frame HitBox rectangles
```

Move duration is deterministic:

```text
DurationSeconds = TotalFrames / FrameRate
Simulation ticks = ceil(TotalFrames * CoreFrameRate / FrameRate)
```

Core maps simulation ticks to `FighterState.MotionFrame` / `MoveFrame` using the move frame rate. A non-looping move clamps to its final frame until its move duration completes. Jump move data may enter a deterministic Landing phase after physical ground contact so the authored jump motion can finish before returning to Idle.

Per-frame flags currently include:

```text
Startup
Active
Recovery
Airborne
Landing
CanAcceptCommand
CanGuard
CanCancel
Invulnerable
```

Generated runtime frames currently include:

```text
Hit
```

The default HurtBox is not duplicated into every move asset. Core creates the standing body profile from the role's `FighterRoleDefinition`, then applies an authored Body Track `BoundsSizeOffset` to the original half extents. `EntityOffset` moves the entity anchor; the Bounds remain centered on the original body center. Jump, crouch, guard, and attack body changes should be authored on a Body Track. `Hit` boxes only participate while the generated frame has `CombatFrameFlags.Active`.

Motion data should not own role-specific damage, hitstun, blockstun, pushback values, or character tuning numbers. Those remain on `FighterRoleDefinition`.

Current role Motion Timeline slots:

```text
Idle
WalkForward
WalkBackward
Guard
Crouch
Jump
LightAttack
HeavyAttack
Hitstun
Blockstun
KO
```

`JumpStartup`, `Jump`, `Fall`, and `Landing` share the same Jump Motion Timeline. This lets the Jump animation include startup / anticipation and landing frames. Core still controls velocity and position; physical ground contact moves the fighter into Landing until the authored Jump motion duration completes.

`LightAttack` uses `CombatMoveData.TotalFrames`, per-frame `CombatFrameFlags.Active`, and per-frame `Hit` boxes for logic simulation. Light attack damage, hitstun, blockstun, and pushback remain on `FighterRoleDefinition`.

`Editor/MotionDataEditorWindow.cs` is the direct Motion Timeline editor. It edits Track keys, uses the role SO and an animation clip only as temporary SceneView context, and does not store either reference as gameplay truth.

The editor tool is allowed to read Unity animation clips and scene preview data. Runtime Core builds and consumes deterministic runtime move data from the saved Track configuration.

Animation clips are editor-time sampling references only. Root-motion curves are not copied into Motion Timeline data and are not applied by Core; deterministic movement comes from Core input and timeline rules. If a clip contains root motion, it must be ignored for gameplay movement.

Current F1 data setup:

```text
F1
├── Timeline_Jump
└── Timeline_LightAttack
```

The remaining move slots are intentionally empty in the current prototype. The F1 role currently consumes only the Jump and LightAttack Motion Timeline references.

The removed legacy path must not be reintroduced:

```text
No CombatTimelineDefinition Unity asset
No legacy Timeline cooker/editor
No separate MotionDataDefinition authoring/cooked chain
No separate CombatLogicBody asset
No bone-based CombatLogicBody authoring
No prefab or Animator data read by Core
```

## Character Prefab

Recommended prefab structure:

```text
Fighter.prefab
├── FighterAvatar
└── VisualRoot
    ├── Animator
    ├── FighterAnimationDriver
    └── Model / Armature / Mesh
```

Optional sockets:

```text
Sockets
├── Center
├── Head
├── Chest
├── Hand_L
├── Hand_R
├── Foot_L
└── Foot_R
```

The prefab root represents the fighter foot position.

## FighterAvatar

`Runtime/FighterAvatar.cs` is a pure reference adapter. It should be attached to the prefab root.

It stores:

```text
VisualRoot
Animator
Sockets
Facing rotations
```

Default facing settings:

```text
Facing Right Euler = 0, 90, 0
Facing Left Euler  = 0, -90, 0
```

If an imported model faces the wrong direction, adjust these fields on `FighterAvatar`. Do not change combat logic.

## FighterView

`Runtime/FighterView.cs` bridges simulation state to Unity presentation.

Responsibilities:

```text
Instantiate the character prefab
Create an outer combat root
Sync FighterState.Position to the root
Sync FighterState.Facing to VisualRoot rotation
Call FighterAnimationDriver
Create debug hitbox / hurtbox only when requested
```

Debug boxes are off by default. They are created lazily only when `Show Boxes` is enabled in the temporary HUD.

Debug boxes are visualizations of Core logic body queries:

```text
GetHurtboxes(FighterState)
GetPushboxes(FighterState)
TryGetAttackHitboxes(FighterState)
```

They are not Unity colliders and do not decide combat results.
They show the boxes returned by Core queries. The default HurtBox comes from `FighterRoleDefinition`; HitBoxes come from the current Motion Timeline frame.
`GetPushboxes` remains as a reserved simulation/debug API, but no pushbox asset data is currently authored.

## Logic World Debug View

`Runtime/LogicWorldDebugView.cs` is a separate visualization of the Core logic world. It is not the character render entity and does not use the character prefab, Animator, Unity physics, or Unity colliders.

It displays:

```text
Logic root marker
Facing marker
Core Hurt boxes
Core active Hit boxes
```

`NetworkBattleRunner` exposes debug switches:

```text
Debug Logic World
Draw Logic World Debug Entities
Draw Logic World Debug Hud
Log Logic World Debug
Logic World Debug Log Interval Frames
```

This lets logic entities remain visible even when regular presentation is disabled.

## Animation

`Runtime/FighterAnimationDriver.cs` controls animation playback in code.

Recommended placement:

```text
VisualRoot
- Animator
- FighterAnimationDriver
```

If a prefab does not contain `FighterAnimationDriver`, `FighterView` adds one to `VisualRoot` at runtime and binds the Animator.

The driver uses:

```csharp
animator.CrossFade(stateName, transitionDuration, 0);
```

Current presentation behavior:

```text
FighterView applies Core position to the combat root.
FighterView applies Core facing to VisualRoot rotation.
FighterAnimationDriver chooses an Animator state from FighterState.Phase and CurrentAttack.
Animator plays the selected clip as presentation only.
```

Animation priority:

```text
KO
Hitstun
Blockstun
LightAttack
Guard
JumpStartup
Jump
Fall
WalkForward / WalkBackward
Idle
```

Default state names:

```text
Idle
WalkForward
WalkBackward
Jump
Guard
LightAttack
Hitstun
KO
```

State names are serialized fields on `FighterAnimationDriver`, so each character can override names in the prefab Inspector.

`JumpStartup` defaults to the same animation state as `Jump`. This lets the Jump clip begin with crouch / anticipation while the combat root stays on the ground. The simulation applies vertical velocity only after the startup frames end.

The Animator Controller should only contain states and assigned animation clips. It does not need parameters or transitions for the current code-driven animation model.

Presentation synchronization:

```text
Core is authoritative: position, jumping, attack phase, hit detection, damage, and hit reactions are computed by Core.
Presentation follows Core state and samples timeline-backed Animator states by Core MotionFrame.
Timeline-backed Animator playback is locked to deterministic Motion Timeline time; Root Motion is not used for gameplay movement.
```

For a timeline-backed motion, presentation time is derived from the same Core MotionFrame used by gameplay box queries.

Presentation synchronization maps Core motion state to animation sampling:

```text
Animator motion time = FighterState.MotionFrame / MotionTimeline.FrameRate
```

or at minimum ensure that the selected Animator state, motion id, frame rate, and clip length match the Motion Timeline. This is required for visual poses to align exactly with Hitbox / Hurtbox frames.

Current controller:

```text
Assets/_Art/Character/Fighter.controller
```

It has been cleaned to:

```text
No parameters
No Any State transitions
No state transitions
States and clips only
```

## Temporary UI

The current UI is immediate-mode OnGUI and is intentionally temporary:

```text
Local / P2P
Listen / Connect
Character index
Ready / Unready
Show Boxes
Leave
```

`NetworkBattleRunner` also has runtime switches for headless / logic-only style testing:

```text
Enable Presentation
Enable Temporary Gui
Create Default Scene Objects
Auto Start Local Battle
```

Future UI can call these `NetworkBattleRunner` members:

```csharp
SelectLocalCharacter(int characterIndex)
SetReady(bool ready)

LocalCharacterIndex
RemoteCharacterIndex
LocalReady
RemoteReady
HasOpponent
HasPlayerAssignment
BattleStarted
```

## How To Run

1. Open a Unity scene.
2. Create an empty GameObject.
3. Add `GLMFighter.Runtime.NetworkBattleRunner`.
4. Drag the character prefab into `Fighter Prefab`.
5. Press Play.

For P2P:

```text
Both instances select P2P
One chooses Listen
The other enters address and chooses Connect
Both choose a character index
Both press Ready
Battle starts automatically
```

## Asset Placement

Keep code under:

```text
Assets/_Code
```

Keep art assets outside `_Code`, for example:

```text
Assets/_Art/Characters/Fighters/Fighter/Prefabs/Fighter.prefab
Assets/_Art/Character/Fighter.controller
```

## Next Recommended Work

1. Create `FighterRoleDefinition` assets for each playable character.
2. Assign those assets to `NetworkBattleRunner.fighterRoles` in the same order on both peers.
3. Author complete Motion Timeline assets for every gameplay motion.
4. Tune Core jump behavior to the authored Jump Motion Timeline apex and landing frames.
5. Move animation state-name overrides into role or animation-specific assets if characters diverge.
6. Build combo logic in Core, not Animator.
7. Keep animation as presentation of Core state.
