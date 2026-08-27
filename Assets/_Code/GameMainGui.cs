using UnityEngine;

namespace _Code
{
    public class GameMainGui : MonoBehaviour
    {
        public GameMain gameMain;

        private string m_LocalPort = "7000";
        private string m_RemoteIp = "127.0.0.1";
        private string m_RemotePort = "7001";
        private int m_LocalPlayerIndex;
        private string m_Error;

        private void Awake()
        {
            if (gameMain == null)
                gameMain = FindFirstObjectByType<GameMain>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 340f, 400f), GUI.skin.box);
            GUILayout.Label("GGPO Test");

            if (gameMain == null)
            {
                GUILayout.Label("Missing GameMain.");
                GUILayout.EndArea();
                return;
            }

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
            GUILayout.Label("本实例控制");
            m_LocalPlayerIndex = GUILayout.SelectionGrid(
                m_LocalPlayerIndex,
                new[] { "P1 (A/D/J)", "P2 (←/→/小键盘 1)" },
                2);

            if (GUILayout.Button("连接"))
                TryStartRemoteVersus();

            if (GUILayout.Button("重置"))
                gameMain.ResetSession();

            if (!string.IsNullOrEmpty(m_Error))
                GUILayout.Label(m_Error);

            GUILayout.EndArea();
        }

        private void TryStartRemoteVersus()
        {
            int localPort;
            int remotePort;
            if (!int.TryParse(m_LocalPort, out localPort) ||
                !int.TryParse(m_RemotePort, out remotePort) ||
                localPort < 1 || localPort > ushort.MaxValue ||
                remotePort < 1 || remotePort > ushort.MaxValue) {
                m_Error = "端口必须是 1 到 65535 的整数。";
                return;
            }

            try {
                gameMain.StartRemoteVersus(
                    localPort,
                    m_RemoteIp,
                    remotePort,
                    m_LocalPlayerIndex);
                m_Error = null;
            }
            catch (System.Exception exception) {
                m_Error = exception.Message;
                Debug.LogException(exception);
            }
        }
    }
}
