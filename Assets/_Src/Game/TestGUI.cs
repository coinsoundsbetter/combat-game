using System;
using UnityEngine;

namespace _Src.Game
{
    /// <summary>
    /// Temporary IMGUI launcher for exercising local and two-process UDP games.
    /// Attach it to the same GameObject as GameMain.
    /// </summary>
    public class TestGUI : MonoBehaviour
    {
        private GameMain m_Main;
        private string m_LocalPort = "7000";
        private string m_TargetAddressWithPort = "127.0.0.1:7001";
        private int m_LocalPlayerIndex;
        private string m_Error;

        private void Awake()
        {
            m_Main = GetComponent<GameMain>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 300f, 300f), GUI.skin.box);
            GUILayout.Label("GGPO Test Launcher");

            if (m_Main == null)
            {
                GUILayout.Label("GameMain is required on this GameObject.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Local UDP port");
            m_LocalPort = GUILayout.TextField(m_LocalPort);

            if (GUILayout.Button("Start local two-player match"))
            {
                TryStart(PlayMode.Local, 0, null, 0);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Remote endpoint (IPv4:port)");
            m_TargetAddressWithPort = GUILayout.TextField(m_TargetAddressWithPort);

            GUILayout.Label("This instance controls");
            m_LocalPlayerIndex = GUILayout.SelectionGrid(
                m_LocalPlayerIndex,
                new[] { "Player 1", "Player 2" },
                2);

            if (GUILayout.Button("Start network match"))
            {
                string host;
                int port;
                if (!TryParseEndpoint(m_TargetAddressWithPort, out host, out port))
                {
                    m_Error = "Endpoint format must be IPv4:port.";
                }
                else
                {
                    TryStart(PlayMode.Remote, m_LocalPlayerIndex, host, port);
                }
            }

            GUILayout.Space(10f);
            if (m_Main.HasSession)
            {
                GUILayout.Label("Frame: " + m_Main.CurrentFrame);
                GUILayout.Label("P1 HP: " + m_Main.Player1Hp);
                GUILayout.Label("P2 HP: " + m_Main.Player2Hp);
            }
            else
            {
                GUILayout.Label("No session started.");
            }

            if (!string.IsNullOrEmpty(m_Error))
                GUILayout.Label(m_Error);

            GUILayout.EndArea();
        }

        private void TryStart(
            PlayMode playMode,
            int localPlayerIndex,
            string targetAddress,
            int targetPort)
        {
            int localPort;
            if (!int.TryParse(m_LocalPort, out localPort) ||
                localPort < 1 || localPort > ushort.MaxValue)
            {
                m_Error = "Local port must be between 1 and 65535.";
                return;
            }

            try
            {
                m_Main.InitSession(playMode, new ConnectInfo
                {
                    LocalPort = localPort,
                    TargetAddress = targetAddress,
                    TargetPort = targetPort,
                    LocalPlayerIndex = localPlayerIndex
                });
                m_Error = null;
            }
            catch (Exception exception)
            {
                m_Error = exception.Message;
                Debug.LogException(exception);
            }
        }

        private static bool TryParseEndpoint(
            string text,
            out string address,
            out int port)
        {
            address = null;
            port = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int separator = text.LastIndexOf(':');
            if (separator <= 0 || separator == text.Length - 1)
                return false;

            address = text.Substring(0, separator).Trim();
            return !string.IsNullOrEmpty(address) &&
                   int.TryParse(text.Substring(separator + 1), out port) &&
                   port >= 1 && port <= ushort.MaxValue;
        }
    }
}
