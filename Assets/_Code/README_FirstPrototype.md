# GLM Fighter First Prototype

This prototype is a local 1v1 fighting-game foundation for a 3D Unity project with a fixed 2D side-view camera.

## How to Run

### Local Test

1. Open any empty Unity scene.
2. Create an empty GameObject.
3. Add `GLMFighter.Runtime.NetworkBattleRunner`.
4. Press Play.
5. Select `Local`.
6. Choose `Start Local`.

The runner creates two 3D cube fighters, a ground block, a side-view orthographic camera, and a directional light if the scene does not already have them.

### Network Test

1. Open any empty Unity scene.
2. Create an empty GameObject.
3. Add `GLMFighter.Runtime.NetworkBattleRunner`.
4. Run two players from the editor, a build, or Unity Multiplayer Play Mode.
5. Select `P2P` on both players.
6. On one player, choose `Listen`.
7. On the other player, enter the listener address and choose `Connect`.
8. After both players are connected, choose a character slot and press `Ready`.
9. The listener starts the fight automatically when both players are ready.

This first network version uses LiteNetLib directly. One player listens on the selected port, the other connects by address, and both players enter a room-ready flow before the fight starts.

The current fight model is P2P delay-based input sync. Both peers run the same local battle simulation and only exchange frame inputs. The listener is not authoritative for combat results.

Each input packet includes the latest input plus recent input history. This lets the prototype use unreliable transport for fight inputs while still recovering from occasional dropped packets.

LiteNetLib `2.1.4` is included as `Assets/_Code/Plugins/LiteNetLib.dll`.

Network HUD fields:
- `Frame`: current local simulation frame.
- `Local latest`: latest local input frame buffered for sending.
- `Remote latest`: latest remote input frame received from the peer.
- `Waiting remote`: true when the local simulation is paused because remote input for the current frame has not arrived.
- `Focused`: whether this Unity instance currently has window focus.
- `Checksum frame`: latest simulation frame that produced a local checksum.
- `Local` / `Remote`: latest local and remote state checksums.
- `Desync`: first checksum frame where both peers disagreed, or `none`.

## Controls

Player 1:
- Move: `A` / `D`
- Jump: `W`
- Crouch: `S`
- Light attack: `J`
- Guard: `L`

Player 2:
- Move: `Left Arrow` / `Right Arrow`
- Jump: `Up Arrow`
- Crouch: `Down Arrow`
- Light attack: `N` or `Keypad 1`
- Guard: `Right Shift`

Shared:
- Toggle hitbox / hurtbox display from the battle HUD.
- Leave the current fight from the battle HUD.

## Logic-Only And Debug Views

`NetworkBattleRunner` has switches for separating logic from presentation:

- `Enable Presentation`: creates and updates rendered fighter prefabs through `FighterView`.
- `Enable Temporary Gui`: shows the temporary OnGUI lobby / HUD.
- `Create Default Scene Objects`: creates the fallback camera, light, and ground.
- `Auto Start Local Battle`: starts local simulation automatically, useful when the temporary GUI is disabled.

Logic world debug is separate from normal character presentation:

- `Debug Logic World`: master switch for logic-world debug.
- `Draw Logic World Debug Entities`: draws simple virtual logic entities for P1/P2, including root marker, facing marker, Hurt boxes, Push boxes, and active Hit boxes.
- `Draw Logic World Debug Hud`: draws the text summary panel.
- `Log Logic World Debug`: writes the text summary to Console at the selected interval.

You can turn `Enable Presentation` off and keep `Debug Logic World` plus `Draw Logic World Debug Entities` on to see the Core-side virtual fighters without rendering the character prefabs.

## Character Prefab

Character prefabs can add `GLMFighter.Runtime.FighterAvatar` on the prefab root.

Recommended structure:

```text
MyFighter.prefab
├── VisualRoot              // Animator here
│   └── Model / Armature / Mesh
├── Sockets
│   ├── Center
│   ├── Head
│   ├── Chest
│   ├── Hand_L
│   ├── Hand_R
│   ├── Foot_L
│   └── Foot_R
├── Effects
└── Audio
```

`FighterAvatar` is only a reference adapter for visuals, animator, and sockets. Movement, attacks, health, hitboxes, hurtboxes, and networking stay in the deterministic battle code.

The prefab root represents the fighter foot position. `VisualRoot` should be a child transform that owns the model and Animator. The first default assumes a standard Unity model faces its local Z axis, so `FighterAvatar` uses:

- `Facing Right Euler`: `0, 90, 0`
- `Facing Left Euler`: `0, -90, 0`

If your imported model uses a different forward axis, adjust these two fields on `FighterAvatar` instead of changing battle logic.

To use a prefab:

1. Put the character prefab under `Assets/_Art/Characters/Fighters/<CharacterName>/Prefabs`.
2. Add `FighterAvatar` to the prefab root.
3. Assign `VisualRoot` to the child that owns the rendered model and Animator.
4. Create a `GLM Fighter/Fighter Role Definition` asset.
5. Set `Standing HurtBox Size` and assign the Motion Timeline assets on the role asset.
6. Open the scene object that has `NetworkBattleRunner`.
7. Add the role asset to `Fighter Roles`.
8. Press Play.

If `Fighter Roles` is empty, the runner falls back to `Fighter Prefab` and default combat stats.

Role assets use simple designer-facing field names. Internally, movement-style fields are treated as Unity units per second, and size fields are treated as Unity units.

- `Standing HurtBox Size`
- `Walk Speed`
- `Jump Speed`
- `Gravity`
- `Max Fall Speed`
- Attack timing fields are frames

The runtime converts these values to deterministic integer simulation units internally.

Combat boxes should be authored as Motion Timeline Track keys. At match setup, the Core builds deterministic runtime frames from those tracks; it does not create gameplay boxes from prefab colliders, model bounds, role body size, or Animator poses at runtime.

Minimum required Motion Timeline setup:

- Configure the character's standing HurtBox size on its `Fighter Role Definition` asset.
- Assign Motion Timeline assets on the role asset for `Jump` and `LightAttack` as needed.
- Set the Timeline `Total Frames`, then add a single `HitBox Track` for attack shapes.
- Add HitBox keys for the frames where those boxes are active.
- Add a `Body Track` and Body keys for jump, crouch, guard, or attack body changes.

The standing HurtBox comes from the role asset. Its center is automatically placed above the LogicPosition using half of the configured height. The generated runtime move data applies Track-authored EntityOffset, HurtBoxOffset, and HurtBoxScale.

### Motion Timeline Box Editor

Use the dedicated editor for gameplay box work:

1. Open `GLM Fighter/Motion Timeline Editor`.
2. Assign or create a `Motion Timeline` asset.
3. Drag the character `Fighter Role Definition` and a temporary `SceneView Animation` into the editor. The editor instantiates `FighterRoleDefinition.Prefab` and reads the role's standing HurtBox size; neither reference is saved as gameplay data.
   Dropping the animation automatically sets `Total Frames` from its duration at the Timeline frame rate; `Total Frames` can then be manually overridden or reset with `Match Clip Length`.
4. Use `Play`, `Pause`, the frame slider, or the timeline strip to preview the motion. Each authored track is shown as a separate timeline row.
5. Add a `HitBox Track` from `Tracks`, then select it in the timeline.
6. Add HitBox keys on the selected track. Every key has its own frame range, center, size, and group.
7. Use the SceneView preview to inspect the active HitBoxes at the selected frame.
8. Save assets.

`MotionTimelineAsset` is a timeline resource. It stores `TotalFrames` and Track configurations, not serialized per-frame runtime data. A motion can contain Body, HitBox and State tracks; they are expanded into runtime `CombatMoveData` when the role enters a match.

Animation is code-driven by `FighterAnimationDriver`. The Animator Controller only needs states with matching names and assigned animation clips. Parameters and transitions are not required for the first hookup.

Default state names:

- `Idle`
- `WalkForward`
- `WalkBackward`
- `JumpStartup` uses `Jump` by default
- `Jump`
- `Guard`
- `LightAttack`
- `Hitstun`
- `KO`

The state names are serialized on `FighterAnimationDriver`, so they can be adjusted per prefab if a character uses different clip state names.

## Room Ready Flow

The current room flow is intentionally minimal:

1. One player listens.
2. The other player connects.
3. The listener becomes P1, the connector becomes P2.
4. Each side sends its selected character index and ready state.
5. The listener starts the battle only after both sides are ready.

The temporary OnGUI character selector is only a placeholder. A formal character-select UI can replace it later by driving the same local character index and ready state.

External UI can call these `NetworkBattleRunner` members:

- `SelectLocalCharacter(int characterIndex)`
- `SetReady(bool ready)`
- `LocalCharacterIndex`
- `RemoteCharacterIndex`
- `LocalReady`
- `RemoteReady`
- `HasOpponent`
- `HasPlayerAssignment`
- `BattleStarted`

## What Is Included

- Fixed 60 FPS battle simulation.
- Integer-based 2D combat math on the X/Y plane.
- 3D presentation objects viewed from a fixed side camera.
- `FighterAvatar` prefab adapter for future model and animation hookup.
- Two local players.
- Direct LiteNetLib listen/connect room flow.
- A simple Lobby screen for creating and joining rooms by address.
- Room player assignment, character index sync, and Ready-before-start flow.
- P2P combat simulation using delayed input sync.
- Redundant input bundles over unreliable transport.
- Network debug HUD for frame/input visibility.
- Periodic checksum exchange for desync detection.
- Movement, jump, guard, light attack, hitstun, blockstun, KO.
- Hurtbox and attack hitbox queries.
- Snapshot and restore support for future rollback netcode.

## What Is Intentionally Not Included Yet

- Real character models and animation.
- Input buffering and command motions.
- Replay recording.
- Cloud room listing.
- Relay/NAT traversal.
- Rollback manager.
- Matchmaking.
