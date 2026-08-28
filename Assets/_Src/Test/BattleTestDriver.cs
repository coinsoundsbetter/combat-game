using UnityEngine;

namespace _Src.Test {
    public class BattleTestDriver : MonoBehaviour {

        private Battle m_Battle;
        private Vector2 m_ScrollPos;
        private string m_LocalPort = "6666";
        private string m_RemoteIp = "127.0.0.1";
        private string m_RemotePort = "6667";
        private string m_SimulateDelayMS = "10";
        
        private void Awake() {
            m_Battle = GetComponent<Battle>();
        }

        private void OnGUI() {
            m_ScrollPos = GUILayout.BeginScrollView(m_ScrollPos);
            GUILayout.Label("[游戏方式]");
            if (GUILayout.Button("1.本地双人")) {
                
            }

            if (GUILayout.Button("2.远程对战")) {
                
            }
            
            GUILayout.Label("[连接]");
            GUILayout.Label("本地端口");
            m_LocalPort = GUILayout.TextField(m_LocalPort);
            GUILayout.Label("对端地址");
            m_RemoteIp = GUILayout.TextField(m_RemoteIp);
            m_RemotePort = GUILayout.TextField(m_RemotePort);
            GUILayout.Label("延迟模拟");
            m_SimulateDelayMS = GUILayout.TextField(m_SimulateDelayMS);
            if (GUILayout.Button("连接")) {
                
            }
            
            GUILayout.Label("[录制/回放]");
            if (m_Battle.IsReplaying && m_Battle.TryGetNetworkStat(out var currentFrame, out var lastConfirmedFrame, out var rollbackCount)) {
                GUILayout.Label($"逻辑帧:{currentFrame},确认帧:{lastConfirmedFrame},回滚:{rollbackCount})");    
            }
            
            if (GUILayout.Button("加载回放")) {
                
            }
            if (GUILayout.Button("保存回放")) {
                
            }
        }
    }
}