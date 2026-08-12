# Unity 1v1 格斗（GGPO 风格回滚）最小实现

## 文件结构
```
_Scripts/
  Core/
    SimTypes.cs          数据结构（InputState / FighterState / GameState / MoveDef）
    Simulation.cs        确定性模拟 + AABB 伤害检测（纯函数，不碰 Unity）
  Netcode/
    RollbackNetcode.cs   自实现回滚：快照环形缓冲 + 输入延迟 + 预测 + 回滚
    NetworkTransport.cs  裸 UDP 传输 + 大厅握手（Hello/Welcome/Ready/Start/Input）
  Presentation/
    FighterPresenter.cs  表现层：读 State 驱动 Transform + Animator
  Replay/
    ReplayRecorder.cs    录制每帧输入 / 回放重模拟
  Game/
    GameManager.cs       主循环 + 大厅 UI + 回放控制
```

## 场景搭建
1. 新建 Unity 3D（URP 或内置都行）项目，把 `_Scripts` 文件夹整个拷进 `Assets/`。
2. 场景里放两个角色 GameObject（带 3D 模型 + Animator）：
   - 第一个挂 `FighterPresenter`，Animator 拖到 `animator`。
   - 第二个同样挂 `FighterPresenter`。
3. 新建空 GameObject 挂 `GameManager`，把两个 FighterPresenter 分别拖到 `fighter0`、`fighter1`。
4. 每个 Animator 里建状态：`Idle / Walk / Jump / Punch / Block / Hit`（名字必须完全一致），
   互相之间做 Transition 并勾掉 `Has Exit Time`（用 `Play` 直接切）。
5. 相机：正交相机，侧视，能看到 x ∈ [-3, 3] 米的范围。
6. 地面：一个 Plane 放在 y=0 即可（纯视觉，碰撞由模拟自己算）。

## 运行（同机两个 Unity 实例）
- 实例 A：运行 → 点 `Host` → 点 `Ready`。
- 实例 B：运行 → 点 `Client` → 点 `Ready`。
- 双方 Ready 后自动开始战斗。
- 控制：`A/D` 移动，`W` 跳，`J` 出拳，`K` 防御。

> 同机测试也可：Editor 里点 Host，再 Build 一个 exe 点 Client。

## 回放
- 战斗中点 `Save Replay` 保存到 `Application.persistentDataPath/replay.bin`。
- 点 `Play Replay` 从存档重新模拟播放。

## 设计要点（为什么这样写）
- **模拟/表现分离**：`Simulation.AdvanceFrame` 是纯函数，不调用任何 Unity API；表现层只读 `GameState`。这是回滚能成立的前提。
- **固定 60fps**：`Time.fixedDeltaTime = 1/60`，逻辑用整数坐标（cm），规避 float 跨平台差异。
- **输入延迟 = 2**：本地输入延迟 2 帧进入模拟，给网络 2 帧余量收远端输入，减少回滚频率。
- **预测**：远端输入未到时复用"最近一次确认的远端输入"继续模拟。
- **回滚**：远端真实输入迟到且与预测不符 → 回到最近确认帧，用正确输入重模拟到当前帧。
- **回放 = 重模拟**：录制每帧两玩家输入，回放时从初始状态重跑同一个 `AdvanceFrame`，零额外逻辑。
- **表现层只读最终状态**：回滚重模拟的中间帧不触发特效，避免重复播放。

## 已知简化（生产环境需补）
- 无时钟同步/RTT 估算：同机或 LAN 够用，跨网需加 ping/时钟对齐。
- 无丢包重传与输入冗余发送：UDP 丢包会多回滚，可加"每包带最近 N 帧输入"冗余。
- 动画用 Animator.Play 切状态，非逐帧采样；要帧精确可改为按 `moveFrame` 采样 AnimationClip。
- 无取消窗口、无投技/必杀、无回合制结算——框架已留好扩展点（`MoveDef` + `canAct` 判定）。
