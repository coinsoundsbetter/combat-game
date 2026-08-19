# GLM Fighter 开发计划

> 当前版本：确定性 1v1 格斗原型
>
> 文档更新时间：2026-08-19

## 1. 开发目标

GLM Fighter 的下一阶段目标是做出一个可以稳定验证核心格斗规则的单角色 Vertical Slice：

- 一个完整可操作的 F1 角色。
- 移动、跳跃、下蹲、防守、轻攻击、受击和 KO 闭环。
- 所有战斗结果由 Core 固定帧模拟决定。
- HitBox、HurtBox、Body 和动作帧可以在 Motion Timeline 中编辑。
- 本地双人和基础 P2P 仍然可用。
- 逻辑盒、角色表现和 Animator 状态能够对齐。
- 关键 Core 规则有可重复的自动化测试。

暂时不以“完整商业产品”为目标。匹配、回滚、多个角色、正式 UI 和大量招式会放在核心规则稳定之后。

## 2. 当前基线

当前已经具备：

- 60 FPS 固定帧 `BattleSimulation`。
- 整数逻辑坐标和 `SimRect` 碰撞盒。
- 本地双人输入。
- LiteNetLib Listen/Connect P2P 房间。
- Ready、角色槽位和输入延迟同步。
- checksum 不同步检测。
- F1 Prefab、Animator 和代码驱动表现。
- `MotionTimelineAsset` 时间线数据链。
- 逻辑世界调试视图。

当前主要限制：

- 只有 F1 角色。
- 目前主要配置了 Jump 和 LightAttack Timeline。
- Crouch/Guard Phase 已存在，但对应 Move Data 尚未完整接入。
- HeavyAttack、连招、指令输入、投技和高级防御尚未实现。
- 网络是 delay-based input sync，还没有 rollback。
- Core 自动化测试尚未形成稳定测试集。

## 3. 总体架构原则

### 3.1 战斗真值只在 Core

以下内容必须由 Core 决定：

- 位置和速度。
- 当前 Phase。
- MotionFrame。
- HitBox/HurtBox/PushBox 查询。
- 是否命中、是否防守成功。
- 伤害、击退、Hitstun、Blockstun。
- KO 和胜者。

Animator、Prefab、Renderer、Unity Collider、Animation Event 都不能成为战斗结果来源。

### 3.2 当前唯一 Motion 数据链

```text
MotionTimelineAsset
    ↓
FighterRoleDefinition
    ↓
CombatMoveData
    ↓
BattleSimulation
```

后续不再新增第二套 Authoring/Cooked Motion 定义。

### 3.3 碰撞和姿态各司其职

```text
HitBox/HurtBox 相交
    决定攻击是否接触身体

Guard/Crouch/Air 状态
    决定当前防守姿态

AttackHeight / AttackTags
    决定攻击能否被该姿态防住
```

最终判定仍然集中在 Core。

## 4. 里程碑总览

```text
M0 数据链路收敛       已完成
M1 下蹲与基础防守      下一阶段
M2 F1 战斗闭环         M1 之后
M3 输入与招式系统      核心战斗稳定后
M4 回滚联机            单机规则稳定后
M5 多角色与正式产品化  最后阶段
```

## 5. M0：数据链路收敛

状态：已完成。

已完成内容：

- 移除未接入的 MotionData Authoring/Cooked 链。
- 移除未使用的 Cooker。
- 移除孤立 `F1_JumpMotion.asset`。
- 清理 F1 角色资源中的旧 MotionData 序列化字段。
- 统一文档说明，当前只使用 `MotionTimelineAsset`。

后续约束：

- 新动作数据直接创建 `MotionTimelineAsset`。
- 新增轨道类型时扩展当前 Timeline 模型和编辑器。
- 不要重新引入平行的 Runtime MotionData 资源。

## 6. M1：下蹲与基础防守

目标：让 F1 完成站立、下蹲、防守、被防守命中和受击恢复的闭环。

### 6.1 Core 规则

修改重点：

[BattleSimulation.cs](D:/Software/ForkSourceFolder/GLM-Fighter/Assets/_Code/Core/BattleSimulation.cs)

需要确认并实现：

- 按住下蹲进入 `Crouch`。
- 松开下蹲回到 `Idle` 或 `Walk`。
- 按住防守进入 `Guard`。
- 松开防守回到 `Idle`。
- 防守和下蹲时停止水平移动。
- 攻击中、受击中、跳跃中不能直接进入站立防守。
- 明确防守/下蹲时是否允许直接攻击；第一版建议禁止。
- 防守命中不扣血，进入 `Blockstun`。
- Blockstun 结束后回到正确的地面状态。

需要重点检查现有 `CanAcceptCommand`。当前 Guard/Crouch 被视为可接受指令状态，可能导致防守或下蹲中直接触发攻击。M1 要明确修正这个状态转换规则。

### 6.2 角色数据

修改重点：

[FighterRoleDefinition.cs](D:/Software/ForkSourceFolder/GLM-Fighter/Assets/_Code/Runtime/FighterRoleDefinition.cs)

新增并接入：

```text
guardTimeline
crouchTimeline
```

在 `ToRoleStats()` 中转换为：

```text
GuardMove
CrouchMove
```

### 6.3 F1 Timeline 资源

新增：

```text
Assets/_Art/Character/F1/Timeline_Crouch.asset
Assets/_Art/Character/F1/Timeline_Guard.asset
```

Crouch Body Track：

- 下移身体中心。
- 减小身体高度。
- 根据实际姿态决定是否减小 PushBox。

Guard Body Track：

- 调整防守姿态的身体中心。
- 根据姿态调整身体盒。
- 第一版不把“防守成功”写进 Animator 或动画事件。

### 6.4 防守判定演进

M1 只实现基础站防：

```text
HitBox 与 HurtBox 相交
且 defender.Phase == Guard
且 defender.OnGround
→ Blockstun，不扣血
```

后续再扩展：

```text
AttackHeight
├── High
├── Mid
├── Low
└── Throw
```

以及：

```text
StandingGuard
CrouchingGuard
AirGuard
DirectionalGuard
```

碰撞盒负责“打到哪里”，姿态和攻击标签负责“能否防住”。

### 6.5 M1 验收标准

- 下蹲时逻辑 HurtBox 明显降低并缩小。
- 下蹲时不产生水平移动。
- 松开下蹲后稳定回到 Idle。
- 防守被轻攻击命中时生命值不变。
- 防守命中进入 Blockstun，并产生击退。
- 防守结束后可以正常移动。
- 防守或下蹲中不会意外触发攻击。
- 两个本地玩家拥有相同结果。
- 两个 P2P 实例的 checksum 不出现持续差异。
- Logic World Debug 与角色表现基本对齐。

## 7. M2：完成 F1 战斗闭环

目标：让 F1 的所有基础状态都有明确数据和表现，而不是依赖空 Move 或默认状态。

需要补齐的 Timeline：

```text
Idle
WalkForward
WalkBackward
Guard
Crouch
Jump
LightAttack
Hitstun
Blockstun
KO
```

主要工作：

- 为每个状态建立最小可用 Motion Timeline。
- 统一 F1 Animator Controller 状态名。
- 统一 `FighterAnimationDriver` 的默认状态映射。
- 确认 Blockstun、Hitstun、KO 的 MotionFrame 行为。
- 清理不再使用的 HeavyAttack 配置，或正式接入 HeavyAttack。
- 增加资源校验，避免缺失 Jump Timeline 时到运行时才报错。

验收目标：

- F1 每个可见状态都有对应动画。
- 逻辑盒和动画姿态不会明显错位。
- 所有状态转换都能在 Logic HUD 中追踪。
- 不使用 Unity 物理也可以稳定完成一局本地战斗。

## 8. M3：输入与招式系统

目标：从“按键直接触发动作”升级为可扩展格斗输入。

建议顺序：

1. 输入边沿检测和输入缓冲。
2. 指令序列，例如前、下、前加攻击。
3. 攻击取消窗口。
4. 连招规则和命中确认。
5. 多段 HitBox 和攻击 Group。
6. 无敌帧、Armor、投技和倒地。

输入识别仍应输出 Core 可消费的确定性指令，不应把连招判断放进 Animator。

## 9. M4：回滚联机

当前项目已有：

- `BattleSnapshot`。
- `Capture()`。
- `Restore()`。
- 输入帧缓存。
- checksum 检测。

但还缺少：

- 快照环形缓冲。
- 远端输入预测。
- 发现迟到输入后的回滚。
- 从旧快照重演到当前帧。
- 预测状态和真实状态的表现修正。
- 网络异常和重连策略。

建议在 M2 完成并有稳定 Core 测试后再做回滚。否则网络问题和战斗规则问题会同时出现，难以定位。

## 10. M5：多角色与正式产品化

后续内容包括：

- 多个 `FighterRoleDefinition`。
- 角色选人界面。
- 角色差异化攻击和移动数据。
- 正式 UI、结算和重赛流程。
- 音效、特效和 Effect Track 消费。
- Replay。
- Matchmaking、Relay、NAT Traversal。
- 断线、重连和房间生命周期。

这些工作都建立在 M1-M4 的 Core、数据和网络约束之上。

## 11. 推荐开发顺序

每次开发尽量遵循以下顺序：

```text
1. 先写清楚 Core 规则
2. 修改 Core 状态和数据结构
3. 创建/修改 Motion Timeline
4. 接入 FighterRoleDefinition
5. 接入 Animator 表现
6. 打开 Logic World Debug 验证逻辑盒
7. 做本地双人测试
8. 做 P2P checksum 测试
9. 更新本开发文档和项目 Wiki
```

## 12. 每个功能的完成定义

一个战斗功能只有同时满足以下条件，才算完成：

- Core 规则已经明确。
- 逻辑状态可以被固定帧复现。
- 角色数据不依赖 Prefab 或 Animator 才能计算结果。
- Motion Timeline 能表达必要的姿态和碰撞盒。
- 本地双人结果符合预期。
- Logic World Debug 能观察到关键盒和状态。
- P2P 两端 checksum 不持续分歧。
- 文档记录了数据来源、状态转换和限制。

## 13. 当前下一项任务

下一项建议直接实现 M1 的第一小步：

1. 在 `FighterRoleDefinition` 增加 `guardTimeline` 和 `crouchTimeline`。
2. 创建 F1 的 Guard/Crouch Timeline 资源。
3. 让 `GuardMove` 和 `CrouchMove` 进入 `FighterRoleStats`。
4. 调整 `BattleSimulation` 的状态转换，禁止防守/下蹲中意外攻击。
5. 用 Logic World Debug 验证 Body Track 对 HurtBox 的影响。

这一步完成后，再扩展高段、低段、投技等细分防守规则。
