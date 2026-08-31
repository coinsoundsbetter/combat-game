using System;
using _Src.GGPO_Extension;
using _Src.GGPO;
using UnityEngine;

namespace _Src.Test {
    public class TestDriver : MonoBehaviour {
        [SerializeField] private CoreSetting config;
        private Battle<FighterInput, FighterState> m_Battle;
        private Vector2 m_ScrollPos;
        private string m_LocalPort = "6666";
        private string m_RemoteIp = "127.0.0.1";
        private string m_RemotePort = "6667";
        private string m_SimulateDelayMS = "10";
        private int m_LocalPlayerIndex = 0;
        private float m_NextDiagnosticSummaryTime;
        private string m_DiagnosticStatus;
        public static Battle<FighterInput, FighterState> BattleInstance { get; private set; }

        private void Start() {
            Application.runInBackground = true;
        }

        private void OnDestroy() {
            if (m_Battle != null) {
                m_Battle.RemoteInputObserved -= OnRemoteInputObserved;
                m_Battle.RollbackObserved -= RecordCompletedRollback;
                m_Battle.TimeSyncWaitObserved -= RecordTimeSyncWait;
            }
            m_Battle?.Stop();
            m_Battle = null;
        }

        private void Update() {
            if (m_Battle == null)
                return;

            m_Battle.Update();
            RecordPeriodicSummary();
        }

        private void OnGUI() {
            var panelWidth = Mathf.Min(420f, Screen.width - 32f);
            var panelHeight = Mathf.Max(120f, Screen.height - 32f);

            GUILayout.BeginArea(
                new Rect(16f, 16f, panelWidth, panelHeight),
                GUI.skin.box);

            try {
                m_ScrollPos = GUILayout.BeginScrollView(m_ScrollPos);
                try {
                    GUILayout.Label("[游戏方式]");
                    m_LocalPlayerIndex = GUILayout.SelectionGrid(
                        m_LocalPlayerIndex,
                        new[] { "P1", "P2" },
                        2);
                    if (GUILayout.Button("1.本地双人")) {
                        StartLocal();
                    }

                    if (GUILayout.Button("2.远程对战")) {
                        StartOnline();
                    }

                    GUILayout.Label("[连接]");
                    GUILayout.Label("本地端口");
                    m_LocalPort = GUILayout.TextField(m_LocalPort);
                    GUILayout.Label("对端地址");
                    m_RemoteIp = GUILayout.TextField(m_RemoteIp);
                    m_RemotePort = GUILayout.TextField(m_RemotePort);
                    GUILayout.Label("延迟模拟");
                    m_SimulateDelayMS = GUILayout.TextField(m_SimulateDelayMS);

                    if (m_Battle != null) {
                        GUILayout.Label($"连接：{GetConnectionStatus(m_Battle.ConnectionState)}");
                        var checksumState = !m_Battle.IsChecksumVerificationAvailable
                            ? "本地对局不校验"
                            : m_Battle.LastVerifiedChecksumStateFrame < 0
                            ? "等待首个校验"
                            : $"状态帧 {m_Battle.LastVerifiedChecksumStateFrame}";
                        GUILayout.Label(
                            $"校验：{checksumState}，不一致 {m_Battle.ChecksumMismatchCount}");

                        m_Battle.TryGetLastConfirmedFrame(out var lastConfirmedFrame);
                        GUILayout.Label(
                            $"逻辑帧：{m_Battle.CurrentFrame}，确认帧：{lastConfirmedFrame}，" +
                            $"预测距离：{m_Battle.CurrentFrame - lastConfirmedFrame}");
                        GUILayout.Label(
                            $"回滚：{m_Battle.RollbackCount}，上次深度：{m_Battle.LastRollbackDepth}，" +
                            $"远端预测累计：{m_Battle.PredictedRemoteInputCount}");
                        GUILayout.Label(
                            m_Battle.IsSyncTestEnabled
                                ? $"SyncTest：通过 {m_Battle.SyncTestCount} 次，" +
                                  $"最近状态帧 {m_Battle.LastSyncTestFrame}"
                                : "SyncTest：关闭（syncTestRollbackFrames=0）");
                        if (m_Battle.IsReliableInputAvailable) {
                            GUILayout.Label(
                                $"输入ACK：待确认 {m_Battle.PendingLocalInputCount}，" +
                                $"已确认 {m_Battle.ReceivedInputAckCount}");
                        }
                        if (m_Battle.IsTimeSyncAvailable) {
                            GUILayout.Label(
                                $"TimeSync：本机优势 {m_Battle.TimeSyncLocalAdvantage:F2}，" +
                                $"远端报告 {m_Battle.TimeSyncRemoteAdvantage:F2}，" +
                                $"等待 {m_Battle.TimeSyncWaitCount}");
                        }
                    }

                    GUILayout.Label("[诊断日志]");
                    GUILayout.Label($"内存日志：{TestDiagnostics.LineCount} 行");
                    if (GUILayout.Button("保存诊断日志"))
                        SaveDiagnostics();
                    if (!string.IsNullOrEmpty(m_DiagnosticStatus))
                        GUILayout.TextArea(m_DiagnosticStatus);
                }
                finally {
                    GUILayout.EndScrollView();
                }
            }
            finally {
                GUILayout.EndArea();
            }
        }

        private void StartLocal() {
            BeginDiagnostics("local", 0);
            m_Battle = new Battle<FighterInput, FighterState>();
            BattleInstance = m_Battle;
            m_Battle.Initialize(
                config,
                new GgpoLocalTransport<FighterInput>(),
                new TestInput(),
                new TestSimulator(config));
            m_Battle.RegisterPlayer(new PlayerRegistration() {
                Index = 0,
                NetType = NetPlayerType.Local,
            });
            m_Battle.RegisterPlayer(new PlayerRegistration() {
                Index = 1,
                NetType = NetPlayerType.Local,
            });
            m_Battle.Start();
            AttachDiagnostics();
        }

        private void StartOnline() {
            if (!int.TryParse(m_LocalPort, out var localPort) || localPort is < 1 or > 65535) {
                return;
            }

            if (!int.TryParse(m_RemotePort, out var remotePort) || remotePort is < 1 or > 65535) {
                return;
            }

            if (!int.TryParse(m_SimulateDelayMS, out var delayMs) || delayMs < 0) {
                return;
            }
            
            BeginDiagnostics("online", delayMs);
            m_Battle = new Battle<FighterInput, FighterState>();
            BattleInstance = m_Battle;
            m_Battle.Initialize(
                config,
                new GgpoUdpTransport<FighterInput>(
                    localPort,
                    m_RemoteIp,
                    remotePort,
                    new TestInputCodec(),
                    delayMs),
                new TestInput(),
                new TestSimulator(config));
            for (var playerIndex = 0; playerIndex < 2; playerIndex++) {
                m_Battle.RegisterPlayer(new PlayerRegistration {
                    Index = playerIndex,
                    NetType = playerIndex == m_LocalPlayerIndex
                        ? NetPlayerType.Local
                        : NetPlayerType.Remote,
                });
            }
            m_Battle.Start();
            AttachDiagnostics();
        }

        private void BeginDiagnostics(string mode, int simulatedDelayMs) {
            var tickRate = config != null ? config.tickRate : 0;
            var inputDelay = config != null ? config.inputDelayFrames : 0;
            var maxRollback = config != null ? config.maxRollbackFrames : 0;
            var syncTestFrames = config != null
                ? config.syncTestRollbackFrames
                : 0;
            TestDiagnostics.BeginSession(
                $"Mode={mode} LocalPort={m_LocalPort} Remote={m_RemoteIp}:{m_RemotePort} " +
                $"LocalPlayer=P{m_LocalPlayerIndex + 1} SimulatedReceiveDelayMs={simulatedDelayMs} " +
                $"TickRate={tickRate} InputDelayFrames={inputDelay} " +
                $"MaxRollbackFrames={maxRollback} SyncTestFrames={syncTestFrames}");
            m_NextDiagnosticSummaryTime = Time.unscaledTime;
            m_DiagnosticStatus = "诊断记录中，复现拉回后点击保存。";
        }

        private void AttachDiagnostics() {
            m_Battle.RemoteInputObserved += OnRemoteInputObserved;
            m_Battle.RollbackObserved += RecordCompletedRollback;
            m_Battle.TimeSyncWaitObserved += RecordTimeSyncWait;
            TestDiagnostics.Record("SESSION", "BattleStarted=1");
        }

        private static string GetConnectionStatus(GgpoConnectionState state) {
            switch (state) {
                case GgpoConnectionState.NotStarted:
                    return "尚未开始";
                case GgpoConnectionState.WaitingForPeer:
                    return "等待对端 READY";
                case GgpoConnectionState.PlayerIndexConflict:
                    return "玩家编号冲突（两端不能选择同一玩家）";
                case GgpoConnectionState.Synchronized:
                    return "已同步";
                default:
                    return state.ToString();
            }
        }

        private void RecordTimeSyncWait() {
            TestDiagnostics.Record(
                "TIMESYNC",
                $"Current={m_Battle.CurrentFrame} " +
                $"LocalAdvantage={m_Battle.TimeSyncLocalAdvantage:F3} " +
                $"RemoteReported={m_Battle.TimeSyncRemoteAdvantage:F3} " +
                $"Samples={m_Battle.TimeSyncSampleCount} " +
                $"WaitTotal={m_Battle.TimeSyncWaitCount}");
        }

        private void OnRemoteInputObserved(
            GgpoRemoteInputDiagnostic<FighterInput> diagnostic) {
            // 提前到达且未发生预测的普通输入不写日志，控制日志量。
            if (diagnostic.LatenessFrames <= 0 &&
                !diagnostic.WasPredicted &&
                !diagnostic.PredictionMismatch) {
                return;
            }

            TestDiagnostics.Record(
                "RX",
                $"Player={diagnostic.PlayerIndex} LocalFrame={diagnostic.LocalFrame} " +
                $"InputFrame={diagnostic.InputFrame} Lateness={diagnostic.LatenessFrames} " +
                $"LastConfirmed={diagnostic.LastConfirmedFrame} " +
                $"HasSimulated={(diagnostic.HasSimulated ? 1 : 0)} " +
                $"WasPredicted={(diagnostic.WasPredicted ? 1 : 0)} " +
                $"Mismatch={(diagnostic.PredictionMismatch ? 1 : 0)} " +
                $"Used=({diagnostic.UsedInput}) Actual=({diagnostic.ActualInput})");
        }

        private void RecordCompletedRollback() {
            m_Battle.TryGetLastConfirmedFrame(out var lastConfirmedFrame);
            TestDiagnostics.Record(
                "ROLLBACK",
                $"Revision={m_Battle.RollbackRevision} " +
                $"Start={m_Battle.LastRollbackStartFrame} " +
                $"End={m_Battle.LastRollbackEndFrame} " +
                $"Depth={m_Battle.LastRollbackDepth} " +
                $"LastConfirmed={lastConfirmedFrame} " +
                $"PredictionDistance={m_Battle.CurrentFrame - lastConfirmedFrame}");

            if (!m_Battle.TryGetLastRollbackStateChange(
                    out var stateFrame,
                    out var predictedStates,
                    out var correctedStates)) {
                return;
            }

            var count = Math.Min(predictedStates.Length, correctedStates.Length);
            for (var playerIndex = 0; playerIndex < count; playerIndex++) {
                var deltaUnits = correctedStates[playerIndex].PosX -
                                 predictedStates[playerIndex].PosX;
                TestDiagnostics.Record(
                    "CORRECTION",
                    $"StateFrame={stateFrame} Player={playerIndex} " +
                    $"IsLocal={(m_Battle.IsLocalPlayer(playerIndex) ? 1 : 0)} " +
                    $"PredictedPosX={predictedStates[playerIndex].PosX} " +
                    $"CorrectedPosX={correctedStates[playerIndex].PosX} " +
                    $"DeltaUnits={deltaUnits} " +
                    $"DeltaWorld={deltaUnits / (float)TestSimulator.PositionUnitsPerWorldUnits:F4}");
            }
        }

        private void RecordPeriodicSummary() {
            if (Time.unscaledTime < m_NextDiagnosticSummaryTime)
                return;

            m_NextDiagnosticSummaryTime = Time.unscaledTime + 1f;
            m_Battle.TryGetLastConfirmedFrame(out var lastConfirmedFrame);
            TestDiagnostics.Record(
                "STAT",
                $"Current={m_Battle.CurrentFrame} Confirmed={lastConfirmedFrame} " +
                $"PredictionDistance={m_Battle.CurrentFrame - lastConfirmedFrame} " +
                $"PredictedTotal={m_Battle.PredictedRemoteInputCount} " +
                $"RollbackTotal={m_Battle.RollbackCount} " +
                $"LastRollbackDepth={m_Battle.LastRollbackDepth} " +
                $"TimeSyncLocalAdvantage={m_Battle.TimeSyncLocalAdvantage:F3} " +
                $"TimeSyncRemoteReported={m_Battle.TimeSyncRemoteAdvantage:F3} " +
                $"TimeSyncSamples={m_Battle.TimeSyncSampleCount} " +
                $"TimeSyncWaitTotal={m_Battle.TimeSyncWaitCount} " +
                $"SyncTestCount={m_Battle.SyncTestCount} " +
                $"SyncTestFrame={m_Battle.LastSyncTestFrame} " +
                $"PendingLocalInputs={m_Battle.PendingLocalInputCount} " +
                $"InputAckTotal={m_Battle.ReceivedInputAckCount} " +
                $"ChecksumMismatch={m_Battle.ChecksumMismatchCount}");
        }

        private void SaveDiagnostics() {
            try {
                var label = $"port{m_LocalPort}_p{m_LocalPlayerIndex + 1}";
                var path = TestDiagnostics.Save(label);
                m_DiagnosticStatus = $"已保存：{path}";
            }
            catch (Exception exception) {
                m_DiagnosticStatus = $"保存失败：{exception.Message}";
                Debug.LogException(exception);
            }
        }
    }
}
