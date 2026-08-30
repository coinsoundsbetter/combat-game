/*
using UnityEngine;

namespace _Code
{
    public class GameMainGui : MonoBehaviour
    {
        public GameMain gameMain;

        private string m_LocalPort = "7000";
        private string m_RemoteIp = "127.0.0.1";
        private string m_RemotePort = "7001";
        private string m_SimulatedReceiveDelayMilliseconds = "0";
        private string m_ReplayPath;
        private int m_LocalPlayerIndex;
        private string m_Error;
        private string m_ReplayMessage;
        private Vector2 m_ScrollPosition;

        private void Awake()
        {
            if (gameMain == null)
                gameMain = FindFirstObjectByType<GameMain>();

            if (gameMain != null)
                m_ReplayPath = gameMain.DefaultReplayPath;
        }

        private void OnGUI()
        {
            var panelHeight = Mathf.Max(120f, Screen.height - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, 340f, panelHeight), GUI.skin.box);
            GUILayout.Label("GGPO Test");

            if (gameMain == null)
            {
                GUILayout.Label("Missing GameMain.");
                GUILayout.EndArea();
                return;
            }

            m_ScrollPosition = GUILayout.BeginScrollView(m_ScrollPosition);

            if (GUILayout.Button("1. 本地双人"))
                gameMain.StartLocalVersus();

            GUILayout.Label("P1: A / D / J    P2: ← / → / 小键盘 1");

            GUILayout.Space(8f);

            GUILayout.Label("2. 远程对战");
            GUILayout.Label("本地 UDP 端口");
            m_LocalPort = GUILayout.TextField(m_LocalPort);
            GUILayout.Label("对端 IP");
            m_RemoteIp = GUILayout.TextField(m_RemoteIp);
            GUILayout.Label("对端 UDP 端口");
            m_RemotePort = GUILayout.TextField(m_RemotePort);
            GUILayout.Label("模拟单向接收延迟 (ms)");
            m_SimulatedReceiveDelayMilliseconds = GUILayout.TextField(
                m_SimulatedReceiveDelayMilliseconds);
            GUILayout.Label("本实例控制");
            m_LocalPlayerIndex = GUILayout.SelectionGrid(
                m_LocalPlayerIndex,
                new[] { "P1 (A/D/J)", "P2 (←/→/小键盘 1)" },
                2);

            if (GUILayout.Button("连接"))
                TryStartRemoteVersus();

            if (GUILayout.Button("重置"))
                gameMain.ResetSession();

            GUILayout.Space(8f);
            GUILayout.Label("3. 回放");
            GUILayout.Label("文件路径");
            m_ReplayPath = GUILayout.TextField(m_ReplayPath ?? gameMain.DefaultReplayPath);
            if (GUILayout.Button("保存已确认回放"))
                TrySaveReplay();
            if (GUILayout.Button("加载并播放回放"))
                TryStartReplay();

            if (gameMain.IsReplayMode) {
                var replayState = gameMain.IsReplayFinished ? "已播放完成" : "播放中";
                GUILayout.Label(
                    $"回放：{replayState}，帧 {gameMain.ReplayCurrentFrame}/" +
                    $"{gameMain.ReplayFinalFrame + 1}");
            }

            if (!string.IsNullOrEmpty(m_ReplayMessage))
                GUILayout.Label(m_ReplayMessage);

            int currentFrame;
            int lastConfirmedFrame;
            int predictedRemoteInputCount;
            int rollbackCount;
            if (gameMain.TryGetNetworkDiagnostics(
                    out currentFrame,
                    out lastConfirmedFrame,
                    out predictedRemoteInputCount,
                    out rollbackCount)) {
                GUILayout.Space(8f);
                GUILayout.Label($"逻辑帧: {currentFrame}，确认帧: {lastConfirmedFrame}");
                GUILayout.Label($"远端预测: {predictedRemoteInputCount}，回滚: {rollbackCount}");
            }

            if (!string.IsNullOrEmpty(m_Error))
                GUILayout.Label(m_Error);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void TryStartRemoteVersus()
        {
            int localPort;
            int remotePort;
            int simulatedReceiveDelayMilliseconds;
            if (!int.TryParse(m_LocalPort, out localPort) ||
                !int.TryParse(m_RemotePort, out remotePort) ||
                !int.TryParse(
                    m_SimulatedReceiveDelayMilliseconds,
                    out simulatedReceiveDelayMilliseconds) ||
                localPort < 1 || localPort > ushort.MaxValue ||
                remotePort < 1 || remotePort > ushort.MaxValue ||
                simulatedReceiveDelayMilliseconds < 0) {
                m_Error = "端口必须是 1 到 65535，延迟必须是非负整数。";
                return;
            }

            try {
                gameMain.StartRemoteVersus(
                    localPort,
                    m_RemoteIp,
                    remotePort,
                    m_LocalPlayerIndex,
                    simulatedReceiveDelayMilliseconds);
                m_Error = null;
            }
            catch (System.Exception exception) {
                m_Error = exception.Message;
                Debug.LogException(exception);
            }
        }

        private void TrySaveReplay()
        {
            string message;
            if (gameMain.TrySaveReplay(m_ReplayPath, out message))
                m_ReplayMessage = message;
            else
                m_ReplayMessage = message;
        }

        private void TryStartReplay()
        {
            string message;
            if (gameMain.TryStartReplay(m_ReplayPath, out message))
                m_ReplayMessage = message;
            else
                m_ReplayMessage = message;
        }
    }
}
*/
