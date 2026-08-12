using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using System.Collections.Generic;

namespace FightGame
{
    // 主循环：把 输入 → 回滚同步 → 确定性模拟 → 快照 串起来，表现层自读 State。
    // FixedUpdate 固定 60fps 驱动模拟；Update 由 Presenter 自行渲染。
    public class GameManager : MonoBehaviour
    {
        public FighterPresenter fighter0;
        public FighterPresenter fighter1;
        public int hostPort = 11000;
        public string clientHost = "127.0.0.1";
        [Range(0, 6)] public int inputDelay = 1; // 本机/LAN 用 1 即可；跨网用 2~3

        NetworkTransport net;
        RollbackNetcode rollback;
        ReplayRecorder recorder = new ReplayRecorder();
        GameState state;
        GameState initialState;

        int realTick = -1;
        bool inFight;
        bool inReplay;
        bool localMode;          // Solo 本地测试：不走网络，两角色都本机控制
        int localPlayerId = 0;  // 网络模式下本机控制的座位（0=主机, 1=客户端）
        int hostStartResend;    // 主机开局后继续重发 Start 的计数

        // 回放播放
        ReplayRecorder replay;
        List<int> replayFrames;
        int replayIdx;

        string replayPath;

        public GameState State => state;

        public enum UIMode { Menu, Lobby, Fight }
        UIMode ui = UIMode.Menu;
        string status = "Choose Host / Client / Solo.";

        void Awake()
        {
            Time.fixedDeltaTime = 1f / 60f; // 模拟固定 60fps
            Application.runInBackground = true; // 失焦也继续跑模拟，避免回滚网游一边冻结
            replayPath = Path.Combine(Application.persistentDataPath, "replay.bin");
            state = Simulation.CreateInitialState(0);
            initialState = state;
            if (fighter0 != null) fighter0.Init(this, 0);
            if (fighter1 != null) fighter1.Init(this, 1);
        }

        void FixedUpdate()
        {   
            if (net != null) net.Poll();

            if (inReplay) { StepReplay(); return; }
            if (inFight)
            {
                if (localMode) LocalTick();
                else FightTick();
                return;
            }
            LobbyTick();
        }

        // ---------- 大厅 / 准备 ----------
        void LobbyTick()
        {
            if (ui != UIMode.Lobby || net == null) return;

            if (!net.IsHost && !net.Connected) { net.ResendHello(); status = "Connecting..."; return; }
            if (net.LocalReady && !net.RemoteReady && !net.Started) net.SendReady(); // 重发防丢

            // 双方 Ready 即开始。主机立即开，并在 FightTick 里继续重发 Start 防 Client 错过首包。
            // 这样两端开局 skew ≈ 单程延迟（本机≈0），不会像之前那样甩开 10 帧。
            if (net.IsHost && net.LocalReady && net.RemoteReady)
            {
                net.SendStart();
                BeginFight();
                return;
            }
            if (!net.IsHost && net.Started) { BeginFight(); return; }

            status = $"connected={net.Connected} localReady={net.LocalReady} remoteReady={net.RemoteReady}";
        }

        void BeginFight()
        {
            localMode = false;
            localPlayerId = net.MyPlayerId;
            inFight = true;
            ui = UIMode.Fight;
            realTick = -1;
            hostStartResend = 0;
            state = Simulation.CreateInitialState(net.Seed);
            initialState = state.Clone();
            rollback = new RollbackNetcode(inputDelay);
            recorder.StartRecording();
            status = "FIGHT!";
        }

        // Solo：单实例本地测试，两角色都本机控制，无需网络/回滚。
        void BeginLocalFight()
        {
            localMode = true;
            localPlayerId = 0;
            inFight = true;
            ui = UIMode.Fight;
            realTick = -1;
            net = null;
            rollback = null;
            state = Simulation.CreateInitialState((uint)System.DateTime.Now.Ticks);
            initialState = state.Clone();
            recorder.StartRecording();
            status = "SOLO FIGHT!  P0: A/D/W/J/K   P1: ←/→/↑ + N/M";
        }

        // ---------- 战斗主循环（网络）----------
        void FightTick()
        {
            // 主机继续重发 Start 一小段时间，防止 Client 错过首个 Start 包
            if (net != null && net.IsHost && hostStartResend < 30) { net.SendStart(); hostStartResend++; }

            realTick++;
            int localFrame = realTick;

            // 1) 采集本地输入（带 inputDelay，由 TargetFrame 体现）
            InputState li = ReadLocalInput();
            rollback.SetLocalInput(localFrame, li);
            net.SendInput(localFrame, li);

            // 2) 收远端输入；若与之前模拟用的预测值不符且已模拟过 → 触发回滚
            while (net.PollInput(out int f, out InputState ri))
            {
                bool needRollback = (f <= rollback.SimFrame) && rollback.IsUsed(f) && !rollback.GetUsed(f).Equals(ri);
                rollback.SetRemoteInput(f, ri);
                if (needRollback) DoRollback();
            }

            // 3) 推进到本帧目标（受输入延迟 + 预测上限约束）
            int target = rollback.TargetFrame(realTick);
            while (rollback.SimFrame < target)
            {
                int f = rollback.SimFrame + 1;
                InputState local  = rollback.GetLocal(f);
                InputState remote = rollback.IsConfirmed(f) ? rollback.GetRemote(f) : rollback.PredictRemote(f);
                rollback.SetUsed(f, remote);
                // 关键：始终按 player0/player1 规范顺序喂模拟，与本地座位无关，否则两端 desync。
                InputState p0 = (localPlayerId == 0) ? local  : remote;
                InputState p1 = (localPlayerId == 0) ? remote : local;
                Simulation.AdvanceFrame(state, p0, p1);
                rollback.SaveSnapshot(f, state);
                rollback.SimFrame = f;
                recorder.Record(f, p0, p1);
            }
        }

        // Solo 循环：两玩家输入都本地读取，直接推进一帧。
        void LocalTick()
        {
            realTick++;
            InputState p0 = ReadLocalInput(0);
            InputState p1 = ReadLocalInput(1);
            Simulation.AdvanceFrame(state, p0, p1);
            recorder.Record(state.frame, p0, p1);
        }

        // 回滚：回到最近确认帧，用正确输入重模拟到当前帧。
        void DoRollback()
        {
            int lc = rollback.SimFrame;
            while (lc >= 0 && !rollback.IsConfirmed(lc)) lc--;
            int upTo = rollback.SimFrame;

            state = (lc < 0) ? initialState.Clone() : rollback.LoadSnapshot(lc);
            rollback.SimFrame = lc;

            for (int g = lc + 1; g <= upTo; g++)
            {
                InputState local  = rollback.GetLocal(g);
                InputState remote = rollback.IsConfirmed(g) ? rollback.GetRemote(g) : rollback.PredictRemote(g);
                rollback.SetUsed(g, remote);
                InputState p0 = (localPlayerId == 0) ? local  : remote;
                InputState p1 = (localPlayerId == 0) ? remote : local;
                Simulation.AdvanceFrame(state, p0, p1);
                rollback.SaveSnapshot(g, state);
                rollback.SimFrame = g;
                recorder.Record(g, p0, p1); // 覆盖之前预测值
            }
        }

        // 网络模式：每个实例只控制"自己"，统一用 P0 键位（A/D/W/J/K）。
        // localPlayerId 只决定输入喂给 fighter0 还是 fighter1，不改变键位。
        // （P1 键位=方向键 只在 Solo 同机双人时用，见 LocalTick。）
        InputState ReadLocalInput() => ReadLocalInput(0);

        InputState ReadLocalInput(int player)
        {
            var kb = Keyboard.current;
            if (kb == null) return default;

            byte bits = 0;
            if (player == 0)
            {
                if (kb[Key.A].isPressed) bits |= (byte)InputBits.Left;
                if (kb[Key.D].isPressed) bits |= (byte)InputBits.Right;
                if (kb[Key.W].isPressed) bits |= (byte)InputBits.Up;
                if (kb[Key.J].isPressed) bits |= (byte)InputBits.Punch;
                if (kb[Key.K].isPressed) bits |= (byte)InputBits.Block;
            }
            else
            {
                if (kb[Key.LeftArrow].isPressed)  bits |= (byte)InputBits.Left;
                if (kb[Key.RightArrow].isPressed) bits |= (byte)InputBits.Right;
                if (kb[Key.UpArrow].isPressed)    bits |= (byte)InputBits.Up;
                if (kb[Key.N].isPressed)          bits |= (byte)InputBits.Punch;
                if (kb[Key.M].isPressed)          bits |= (byte)InputBits.Block;
            }
            return new InputState(bits);
        }

        // ---------- 回放 ----------
        public void StartReplay()
        {
            if (!File.Exists(replayPath)) { status = "No replay file."; return; }
            replay = ReplayRecorder.Load(replayPath);
            replayFrames = replay.SortedFrames();
            replayIdx = 0;
            inReplay = true;
            inFight = false;
            state = Simulation.CreateInitialState(0);
            status = "Replay playing...";
        }

        void StepReplay()
        {
            if (replayIdx >= replayFrames.Count) { inReplay = false; status = "Replay finished."; return; }
            int f = replayFrames[replayIdx];
            var inp = replay.Get(f);
            Simulation.AdvanceFrame(state, inp.l, inp.r);
            replayIdx++;
        }

        void SaveReplay()
        {
            recorder.Stop();
            recorder.Save(replayPath);
            status = "Replay saved: " + replayPath;
        }

        // ---------- 极简 UI（OnGUI，免搭 Canvas）----------
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, 360));
            GUILayout.Label(status);

            if (ui == UIMode.Menu)
            {
                if (GUILayout.Button("Solo 本地测试")) { BeginLocalFight(); }
                if (GUILayout.Button("Host"))   { net = new NetworkTransport(); net.StartHost(hostPort);   ui = UIMode.Lobby; status = "Hosting on " + hostPort; }
                if (GUILayout.Button("Client")) { net = new NetworkTransport(); net.StartClient(clientHost, hostPort); ui = UIMode.Lobby; status = "Connecting to " + clientHost; }
            }
            else if (ui == UIMode.Lobby)
            {
                if (GUILayout.Button("Ready")) net.SendReady();
                if (GUILayout.Button("Back")) { ui = UIMode.Menu; status = "Menu"; }
            }
            else if (ui == UIMode.Fight)
            {
                string simInfo = rollback != null ? $" sim:{rollback.SimFrame}" : " (solo)";
                GUILayout.Label($"P0 HP:{state.fighters[0].health}  P1 HP:{state.fighters[1].health}  frame:{state.frame}{simInfo}");
                if (GUILayout.Button("Save Replay"))  SaveReplay();
                if (GUILayout.Button("Play Replay")) { SaveReplay(); StartReplay(); }
                if (GUILayout.Button("Return to Menu")) { inFight = false; localMode = false; ui = UIMode.Menu; status = "Menu"; }
            }
            GUILayout.EndArea();
        }
    }
}
