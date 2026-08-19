# GLM Fighter 项目 Wiki

> 基于当前工作区源码、Unity 场景与资源整理。更新时间：2026-08-19。

后续功能开发按 [DEVELOPMENT_PLAN.md](D:/Software/ForkSourceFolder/GLM-Fighter/Assets/_Code/DEVELOPMENT_PLAN.md) 推进。

## 1. 项目定位

GLM Fighter 是一个 Unity 3D、固定 2D 横向视角的 1v1 格斗原型。

当前项目的核心目标不是做完整产品，而是验证以下基础链路：

- 固定帧、可复现的格斗逻辑。
- 本地双人战斗。
- 基于 LiteNetLib 的点对点联机。
- 只同步输入、两端各自运行战斗模拟。
- 用时间线数据驱动跳跃、攻击帧和逻辑碰撞盒。
- 将逻辑世界与 Unity 角色表现、Animator、物理系统解耦。

当前结论：项目已经是“可运行的战斗原型基础”，但还不是完整格斗游戏。核心循环、联机房间、F1 角色和调试工具已经存在；角色内容、正式 UI、连招、回滚、匹配和产品化流程仍未完成。

## 2. 当前状态总览

| 模块 | 状态 | 当前情况 |
|---|---|---|
| Unity 项目 | 已建立 | Unity 6000.0.42f1，URP，Input System 等包已配置 |
| 战斗核心 | 可用原型 | 60 FPS、整数逻辑坐标、移动/跳跃/防御/轻攻击/受击/KO |
| 逻辑碰撞 | 可用原型 | 使用 `SimRect` AABB，不使用 Unity Collider 决定结果 |
| 角色数据 | 单角色接入 | `F1.asset` 已配置，角色槽位当前为 1 |
| Motion Timeline | 当前主流程可用 | F1 使用 `Timeline_Jump` 与 `Timeline_LightAttack` |
| P2P 联机 | 已接入原型 | Listen/Connect、P1/P2 分配、Ready、输入延迟与冗余 |
| 反不同步检测 | 已接入 | 周期性 checksum，只检测，不修复 |
| 回滚 | 未实现 | 有 `BattleSnapshot` Capture/Restore，但没有回滚管理器 |
| 角色表现 | 已接入 | F1 Prefab、Animator、代码驱动状态切换 |
| 临时 UI | 已接入 | `OnGUI` 大厅与战斗 HUD |
| 逻辑世界调试 | 已接入 | 可独立显示虚拟逻辑实体、Hurt/Push/Hit 盒 |
| 连招/指令 | 未实现 | 当前只有按键直接触发轻攻击 |
| 正式游戏流程 | 未完成 | 尚未形成完整选人、比赛、结算、重赛流程 |
| 自动化测试 | 未发现 | 工作区没有可见的测试源码；目前不能据此证明运行时行为完整正确 |

## 3. 运行入口与场景

### 3.1 当前主要测试场景

主要战斗场景是：

```text
Assets/_Scene/Main.unity
```

场景中有一个挂载 `GLMFighter.Runtime.NetworkBattleRunner` 的对象，当前配置包括：

```text
Enable Presentation          true
Enable Temporary Gui         true
Create Default Scene Objects true
Debug Logic World            true
Draw Logic World Entities    true
Draw Logic World HUD         true
Jump Debug                   true
Character Slot Count         1
Input Delay                  3 frames
Input Redundancy             12 frames
Checksum Interval            30 frames
Default Port                 7777
```

`NetworkBattleRunner` 是运行时组合根和唯一 Unity 入口。它负责组装各个 Runtime helper，并在每个渲染帧中驱动网络、固定 tick、表现和调试输出。

### 3.2 启动方式

1. 打开 `Assets/_Scene/Main.unity`。
2. 点击 Play。
3. 临时 UI 中选择 `Local`，点击 `Start Local`。
4. 也可以打开 `P2P`：一端选择 `Listen`，另一端输入地址后选择 `Connect`。

如果场景没有相机、灯光或地面，`BattleSceneBootstrap` 会按配置创建临时对象。这个 Bootstrap 只属于原型表现层，不属于战斗逻辑。

## 4. 架构原则

项目目前应遵循这条依赖方向：

```text
Core 战斗真值
   ↑
Runtime 输入、会话、表现、调试
   ↑
Network LiteNetLib 传输
```

更具体地说：

- `Core` 决定位置、朝向、阶段、攻击结果、生命值、受击和胜负。
- `Runtime` 读取输入、启动模拟、创建角色表现和显示调试信息。
- `Network` 只负责收发数据包和连接生命周期。
- `Animator` 只播放表现，不决定伤害、命中帧或移动结果。
- Prefab 上的 Renderer、Animator、Collider 不参与战斗结果计算。
- Unity 物理系统目前不参与格斗碰撞。

禁止把以下逻辑放进 Animator、Prefab 脚本或动画事件：

```text
Damage
Hit confirmation
Hitbox active frames
Health changes
Combo legality
Final movement result
Network synchronization state
```

## 5. 代码目录

```text
Assets/_Code/
├── Core/
│   ├── BattleSimulation.cs       战斗状态机、移动、跳跃、攻击、命中、胜负
│   ├── BattleTypes.cs             FighterState、AttackSpec、角色统计
│   ├── CombatTimelineTypes.cs     CombatMoveData、CombatFrameData、CombatBox
│   ├── FighterInput.cs            与 Unity 无关的输入结构
│   └── SimMath.cs                 整数坐标、矩形和单位转换
├── Runtime/
│   ├── NetworkBattleRunner.cs     Unity 运行入口与组合根
│   ├── BattleSessionController.cs 大厅、Ready、Local/P2P 会话状态
│   ├── BattleTickDriver.cs        渲染时间到 60 FPS tick 的转换
│   ├── BattleInputSync.cs         输入延迟、缓存、冗余和等待
│   ├── BattleChecksumTracker.cs   checksum 记录与不同步检测
│   ├── BattleNetworkCoordinator.cs 网络传输 facade
│   ├── BattleRoleCatalog.cs       角色槽位、角色数据和 Prefab 查找
│   ├── FighterRoleDefinition.cs   角色 ScriptableObject 与 Core 数据转换
│   ├── MotionTimelineAsset.cs      当前角色实际使用的时间线资源
│   ├── FighterView.cs              Core 状态到 Unity 角色的桥接
│   ├── FighterAvatar.cs            Prefab 引用适配器
│   ├── FighterAnimationDriver.cs  代码驱动 Animator
│   ├── LogicWorldDebugView.cs      Core 逻辑世界可视化
│   └── BattleDebugHud.cs            临时 OnGUI
├── Network/
│   ├── LiteNetLibRoom.cs           LiteNetLib 房间和连接
│   ├── LiteNetPacketCodec.cs       网络包序列化
│   └── TransportPacket.cs          传输包结构
├── Editor/
│   └── MotionDataEditorWindow.cs   当前编辑器实际打开的 Motion Timeline 编辑器
└── Plugins/
    └── LiteNetLib.dll              LiteNetLib 2.1.4
```

## 6. Core 战斗模拟

### 6.1 固定帧与单位

`BattleSimulation` 使用：

```text
逻辑帧率：60 FPS
核心单位：1000 core units = 1 Unity unit
场地 X 范围：-5200 到 5200 core units
初始位置：P1 = -1400，P2 = 1400
地面 Y：0
```

模拟器不依赖 `Time.deltaTime`，而是由 `BattleTickDriver` 以固定 1/60 秒调用 `Step`。

### 6.2 Fighter 状态

当前阶段枚举包括：

```text
Idle
Walk
Guard
Crouch
JumpStartup
Jump
Fall
Landing
AttackStartup
AttackActive
AttackRecovery
Hitstun
Blockstun
Knockdown
KO
```

每个 Fighter 状态包含：

- 玩家槽位、角色统计和生命值。
- 位置、速度、朝向。
- 当前 Phase、攻击类型、当前 Move。
- PhaseFrame、MotionFrame、MotionTicks。
- 是否落地、当前攻击是否已经命中。

### 6.3 当前已实现战斗行为

- 左右移动。
- 跳跃、下落、落地。
- 站立防御和蹲下。
- 轻攻击。
- 攻击 Startup / Active / Recovery。
- HurtBox 与 HitBox 相交判定。
- PushBox 身体分离。
- 命中后的伤害、击退、Hitstun。
- 防御命中后的 Blockstun 与击退。
- 生命值归零后 KO 和胜者记录。
- 状态快照 `BattleSnapshot`。
- 基于状态和角色数据的 checksum。

攻击解析顺序为：

```text
读取输入
→ 更新朝向
→ 更新双方移动/阶段
→ 处理身体 PushBox 分离
→ P1 攻击 P2
→ P2 攻击 P1
→ 推进阶段计时器
→ 更新 MotionFrame
→ 更新胜负
```

### 6.4 当前攻击内容边界

`AttackKind` 已定义 `Light` 和 `Heavy`，但当前 `FighterRoleDefinition` 将 Heavy 设置为 `AttackKind.None`，所以 Heavy 输入不会产生攻击。

当前实际可用的是轻攻击：

```text
伤害：40
Hitstun：14 frames
Blockstun：8 frames
Pushback：角色数据中的 13.2 Unity units/sec 转换值
```

轻攻击的 Startup、Active、Recovery 主要由 `Timeline_LightAttack` 的时间线和激活 HitBox 推导。F1 的时间线是 38 帧，当前 HitBox 在第 10 到第 14 帧有效。

## 7. 逻辑碰撞盒

所有格斗盒都是整数矩形 `SimRect`：

```text
CenterX / CenterY
HalfWidth / HalfHeight
```

当前查询入口：

```csharp
BattleSimulation.GetHurtboxes(state)
BattleSimulation.GetPushboxes(state)
BattleSimulation.TryGetAttackHitboxes(state, out hitboxes)
```

规则：

- 默认 HurtBox 来自 `FighterRoleDefinition.StandingHurtBoxSize`。
- F1 当前默认 HurtBox 为 0.64 x 1.70 Unity units。
- Body Track 可以通过中心偏移和尺寸偏移改变当前帧的身体盒。
- HitBox 来自当前 Motion Timeline 的 HitBox Track。
- Debug Box 只是 Core 查询结果的可视化，不是 Collider。

实体坐标关系为：

```text
LogicPosition + MotionFrame.EntityOffset = EntityCenter
EntityCenter + Body/HitBox local data = world combat box
```

## 8. 跳跃实现

当前跳跃不是 Unity 重力或 Rigidbody 驱动，而是由角色 Jump Timeline 的每帧 `EntityOffset.Y` 驱动。

进入跳跃时：

1. Fighter 进入 `JumpStartup`。
2. 读取 Jump Timeline 的 `JumpStartup` 状态轨道。
3. 状态仍为 Startup 时，逻辑位置保持在地面。
4. Startup 结束后进入 `Jump`。
5. 每个逻辑 tick 根据当前与下一 MotionFrame 的 Y 偏移计算速度。
6. Y 偏移下降至地面后进入 `Landing`，直到时间线结束再回到 `Idle`。

因此，每个可战斗角色必须提供有效的 Jump Timeline，并且必须含 Body Track 和至少一个 key。缺失时，角色不能正常进入战斗。

当前 F1 的实际 Jump Timeline：

```text
Assets/_Art/Character/F1/Timeline_Jump.asset
帧率：60
总帧数：80
包含 State Track 与 Body Track
最高 EntityOffset.Y：约 3.0 Unity units
```

## 9. 角色与表现层

### 9.1 F1 资源

当前接入的角色资源位于：

```text
Assets/_Art/Character/F1/F1.asset
Assets/_Art/Character/F1/F1.prefab
Assets/_Art/Character/F1/F1.controller
Assets/_Art/Character/F1/Timeline_Jump.asset
Assets/_Art/Character/F1/Timeline_LightAttack.asset
```

Prefab 的主要结构是：

```text
F1
└── VisualRoot
    └── 模型 / Animator
```

Prefab 根节点挂有 `FighterAvatar`，`VisualRoot` 上挂有 `FighterAnimationDriver`。

### 9.2 表现同步

`FighterView` 创建一个外层战斗根节点，并把 Core 的位置和朝向同步到 Unity：

- 外层根节点使用 Core `Position`。
- VisualRoot 使用 Core `Facing` 旋转。
- `FighterAnimationDriver` 根据 Phase 和 CurrentAttack 选择 Animator State。
- 如果当前 Move 有时间线帧，Animator 会被暂停并直接定位到对应 MotionFrame。
- Root Motion 不用于游戏移动。

F1 Controller 当前有无参数、无 Any State Transition、无普通状态 Transition 的纯状态集合：

```text
Idle
WalkForward
WalkBackward
Jump
LightAttack
Hitstun
Defense
KO
```

当前代码对 Blockstun 默认状态名是 `Defense`；F1 Prefab 又把 `blockstunState` 配置为 `Guard`，但 Controller 中存在 `Defense` 而不一定存在 `Guard`。这是表现层需要复核的配置点，不影响 Core 战斗结果。

## 10. 联机流程

### 10.1 房间和角色分配

网络基于 `LiteNetLib.dll` 2.1.4，使用一个连接的最小房间：

```text
Listener  = P1
Connector = P2
```

流程：

```text
Listen / Connect
→ 建立连接
→ Listener 本地分配 P1，并发送 P2 分配
→ 双方同步角色索引和 Ready 状态
→ 双方 Ready 后，Listener 发送 StartBattle
→ 两边按相同角色数据开始本地模拟
```

当前只允许一个对手，房间满时会拒绝新的连接。

### 10.2 包类型与可靠性

包类型包括：

```text
AssignPlayer
LobbyState
StartBattle
InputBundle
Checksum
```

传输方式：

| 数据 | 方式 | 目的 |
|---|---|---|
| 玩家分配、Lobby、Ready、Start | ReliableOrdered | 不能丢失的会话控制 |
| 输入 Bundle | Unreliable | 依靠历史冗余恢复丢包 |
| Checksum | ReliableOrdered | 确保不同步诊断信息到达 |

### 10.3 延迟输入同步

默认参数：

```text
InputDelayFrames = 3
InputRedundancyFrames = 12
```

本地输入不会直接用于当前逻辑帧，而是写入 `simulationFrame + 3`。每次发送最新输入时，会附带最近 12 帧中已经存在的输入。

如果某一逻辑帧缺少远端输入，模拟会等待，不会用预测输入推进。这是 delay-based input sync，不是 rollback。

### 10.4 不同步检测

默认每 30 个逻辑帧计算一次 `BattleSimulation.ComputeChecksum()`，双方比较相同帧的 checksum，并记录首次不同步帧。

当前检测器只负责：

- 记录本地 checksum。
- 记录远端 checksum。
- 找到首次不一致帧。

当前检测器不负责：

- 自动恢复状态。
- 请求快照。
- 回滚重演。
- 断线重连。

## 11. 当前输入

输入由 `BattleInputReader` 临时直接读取键盘：

| 玩家 | 移动 | 跳跃 | 蹲下 | 轻攻击 | 防御 |
|---|---|---|---|---|---|
| P1 | A / D | W | S | J | L |
| P2 | 左右方向键 | 上方向键 | 下方向键 | N / 小键盘 1 | 右 Shift |

这是临时映射，后续可以替换为正式 Input System、命令输入和输入缓冲，而不应改变 `FighterInput` 或 Core 规则的职责边界。

## 12. 调试能力

### 12.1 常规战斗 HUD

临时 HUD 可显示：

- P1/P2 生命值。
- 当前逻辑帧和胜者。
- 当前模式与本地玩家编号。
- 本地输入。
- P2P 的本地/远端输入最新帧。
- 是否等待远端输入。
- Checksum 帧、本地值、远端值和 Desync 帧。
- 显示/隐藏 HurtBox、PushBox、HitBox。

### 12.2 Logic World Debug

可以关闭普通角色表现，只保留逻辑世界调试：

```text
Enable Presentation = false
Debug Logic World = true
Draw Logic World Debug Entities = true
```

这样可以单独观察 Core 的实体中心、朝向、HurtBox、PushBox 和当前有效 HitBox，适合定位“逻辑正确但动画不对”或“动画看起来对但逻辑盒不对”的问题。

## 13. Motion 数据流程

本次清理后，项目只保留 `MotionTimelineAsset` 作为角色 Motion 的编辑和运行时数据源。

### 13.1 当前主流程

当前角色、编辑器和场景实际使用：

```text
MotionTimelineAsset
    ↓
MotionTimelineTrackDefinition
    ↓
BuildRuntimeMoveData()
    ↓
CombatMoveData
    ↓
BattleSimulation
```

对应文件：

```text
Runtime/MotionTimelineAsset.cs
Runtime/FighterRoleDefinition.cs
Editor/MotionDataEditorWindow.cs
Assets/_Art/Character/F1/Timeline_Jump.asset
Assets/_Art/Character/F1/Timeline_LightAttack.asset
```

当前编辑器菜单是 `GLM Fighter/Motion Timeline Editor`，编辑器内部打开的资源类型是 `MotionTimelineAsset`。

当前主流程支持的 Track 类型：

```text
Body
HitBox
State
```

### 13.2 本次清理结果

已移除未接入主链路的以下内容：

```text
Runtime/MotionDataDefinition.cs
Runtime/MotionDataAuthoringDefinition.cs
Editor/MotionDataCooker.cs
Runtime/F1_JumpMotion.asset
```

对应 `.meta` 文件也已移除。后续新增 Body、HitBox、State 或其他轨道，应直接扩展 `MotionTimelineAsset` 及其编辑器，不再创建第二套 Authoring/Cooked 数据模型。

## 14. 已知问题与风险

### 高优先级

1. **F1 角色资源已完成旧字段清理。** `F1.asset` 中原先遗留的 `jumpSpeed`、`gravity`、`idleMotionData` 等字段已移除；当前实际生效的是 `jumpTimeline`、`lightAttackTimeline` 和当前代码声明的字段。
2. **角色内容不完整。** 当前只有 F1，且主要配置了 Jump 和 LightAttack；Walk、Guard、Crouch、Hitstun、Blockstun、KO 等 Move Data 仍为空，表现上依赖默认状态或空 Move。
3. **P2P 没有回滚或预测。** 网络延迟或丢包会直接造成等待，体验和抗网络抖动能力有限。

### 中优先级

1. **HeavyAttack 没有真正接入。** Core 有枚举和基础结构，但角色数据关闭了 Heavy。
2. **攻击数据仍是单一轻攻击模型。** 没有指令、输入缓冲、连招窗口、取消、投技、无敌帧和多段命中。
3. **表现状态名存在配置不一致风险。** F1 Controller 中使用 `Defense`，Prefab 的 Blockstun 映射为 `Guard`，需要统一。
4. **场景 Bootstrap 是临时逻辑。** 每次运行可能创建临时 Ground、Camera、Light，正式场景应逐步移除自动创建依赖。
5. **当前 UI 是 OnGUI。** 适合原型调试，不适合作为正式大厅、选人和战斗 UI。

### 低优先级

1. 输入系统仍是直接 `Input.GetKey`。
2. 没有 Replay、Matchmaking、Relay/NAT Traversal、云房间列表。
3. 没有正式音效、特效事件消费系统；虽然存在部分时间线扩展结构，但尚未形成完整表现管线。

## 15. 推荐开发顺序

详细的里程碑、实施顺序和验收标准见 [DEVELOPMENT_PLAN.md](D:/Software/ForkSourceFolder/GLM-Fighter/Assets/_Code/DEVELOPMENT_PLAN.md)。本 Wiki 保留项目现状和架构约束，开发计划作为具体执行清单。

### 第一阶段：收敛数据链路

1. 清理 F1.asset 中已经不再被代码读取的旧序列化字段。
2. 统一 `FighterRoleDefinition` 的字段名称和序列化资源。
3. 加入资源校验：缺失 Jump、Motion Track、Animator State 时，在编辑器中给出明确错误。

### 第二阶段：完成单角色战斗闭环

1. 补齐 F1 的 Idle、Walk、Guard、Crouch、Hitstun、Blockstun、KO 数据。
2. 统一 Controller、Prefab 和 `FighterAnimationDriver` 的状态名。
3. 完成 HeavyAttack 或删除未使用的 Heavy 入口。
4. 增加 Core 自动化测试：移动、跳跃、命中、防御、KO、碰撞边界、MotionFrame 和 checksum。

### 第三阶段：完善格斗系统

1. 输入缓冲和指令识别。
2. 连招、取消、受击保护、无敌帧、多段 HitBox。
3. 投技、倒地、起身、空中攻击和更多角色状态。
4. 受击特效、音效和时间线 Effect Track 消费。

### 第四阶段：升级网络

1. 基于现有 `BattleSnapshot` 实现 rollback manager。
2. 加入输入预测和纠错重演。
3. 明确连接断开、重连和房间生命周期。
4. 在有稳定 Core 测试后，再做 Relay/NAT Traversal 和正式匹配。

## 16. 当前开发者快速检查清单

修改战斗规则时：

- 优先修改 `Core/BattleSimulation.cs` 和 Core 数据类型。
- 不要在 Animator 或 Collider 中加入战斗真值。
- 修改角色数值时，检查 `FighterRoleDefinition` 到 Core 单位的转换。
- 修改 Motion 时，确认编辑的是 F1 实际引用的 `MotionTimelineAsset`。
- 修改网络时，确认两端使用相同角色数据、帧率和时间线内容。
- 用 Logic World Debug 验证逻辑盒，再看角色动画表现。
- 修改公共数据结构后，检查 checksum 是否仍覆盖关键状态。

## 17. 交付判断

当前项目适合：

- 继续验证固定帧战斗规则。
- 做本地双人原型试玩。
- 做两实例 P2P 输入同步实验。
- 做 HitBox / HurtBox / Jump Timeline 调试。

当前项目还不适合直接作为：

- 完整可发布格斗游戏。
- 稳定的高延迟网络对战版本。
- 多角色生产管线。
- 依赖自动化测试保障的长期内容项目。

一句话总结：**GLM Fighter 已经搭好“确定性 Core + Unity 表现 + LiteNetLib P2P”的第一版骨架，下一步最重要的不是继续堆功能，而是先收敛 Motion 数据架构、补齐 F1 资源与测试，再进入连招和回滚。**
