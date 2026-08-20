using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace _Src.Core
{
    public class BattleGame : MonoBehaviour
    {
        [Header("网络配置")] 
        public int localPort = 7000;
        public string remoteIp = "127.0.0.1";
        public int remotePort = 7001;

        [Header("模拟配置")] public int simulationFramePerSecond = 60;
        public int maxRollbackFrames = 120;
        public int maxSimulationStepsPerUpdate = 4;

        [Header("角色")] public GameObject playerPrefab;
        
        //本地输入
        private Dictionary<int, InputFrame> m_LocalInputs = new Dictionary<int, InputFrame>();
        //已收到的远程输入
        private Dictionary<int, InputFrame> m_RemoteInputs = new Dictionary<int, InputFrame>();
        //当时预测使用的远程输入
        private Dictionary<int, InputFrame> m_PredictedRemoteInputs = new Dictionary<int, InputFrame>();
        //每帧开始前的状态快照
        private Dictionary<int, GameState> m_Snapshots = new Dictionary<int, GameState>();
        //当前预测的远程输入
        private InputFrame m_PredictedRemoteInput = InputFrame.Empty;

        private void Start()
        {
            InitNetwork();
            InitGameState();
        }
        
        private void Update()
        {
            //收包
            PollNetwork();
            
            //执行预测错误的回滚
            if (m_EarliestRollbackFrame >= 0)
            {
                RollbackAndResimulate();
            }
        }

        private UdpClient m_UDPClient;
        private IPEndPoint m_RemoteEp;
        private void InitNetwork()
        {
            m_UDPClient = new UdpClient(localPort);
            m_RemoteEp = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
            m_UDPClient.Client.Blocking = false;
        }

        private void PollNetwork()
        {
            
        }

        private GameState m_GameState;
        private float m_SimulateDeltaTime;
        private int m_CurrentFrame;
        private float m_Accumulator;
        private int m_EarliestRollbackFrame = -1; //发现预测错误的帧,从这帧开始回滚
        private int m_InRollbackFrame = 0; //正在回滚的帧
        private void InitGameState()
        {
            m_GameState = new GameState();
            m_CurrentFrame = 0;
            m_Accumulator = 0;
            m_Snapshots[0] = m_GameState;
        }

        private void RollbackAndResimulate()
        {
            int rollbackFrame = m_EarliestRollbackFrame;
            m_EarliestRollbackFrame = -1;
            if (rollbackFrame < 0)
            {
                return;
            }

            GameState oldState;
            if (!m_Snapshots.TryGetValue(rollbackFrame, out oldState))
            {
                return;
            }

            //从预测错误的帧,重新模拟一遍到当前帧
            var targetFrame = m_CurrentFrame;
            m_GameState = oldState.Clone();
            var predictInput = GetLastAckRemoteInput(rollbackFrame); //why?
            for (int frame = rollbackFrame; frame < targetFrame; frame++)
            {
                InputFrame localInput;
                if (!m_LocalInputs.TryGetValue(frame, out localInput))
                {
                    break;
                }

                m_Snapshots[frame] = m_GameState.Clone();

                InputFrame remoteInput;
                if (m_RemoteInputs.TryGetValue(frame, out remoteInput))
                {
                    predictInput = remoteInput;
                }
                else
                {
                    //为什么回滚的时候还要预测?
                    //因为回滚的时候并不一定所有帧都拿到了数据
                    //对于还未确认的帧,我们仍然需要继续使用预测推进模拟
                    remoteInput = predictInput;
                    m_PredictedRemoteInputs[frame] = remoteInput;
                }
                
                SimulateFrame(ref localInput, ref remoteInput);
            }
        }

        private void SimulateFrame(ref InputFrame local, ref InputFrame remote)
        {
            
        }

        private InputFrame GetLastAckRemoteInput(int frame)
        {
            for (int i = frame - 1; i >= 0; i--)
            {
                InputFrame input;
                if (m_RemoteInputs.TryGetValue(i, out input))
                {
                    return input;
                }
            }

            return InputFrame.Empty;
        }
    }
}
