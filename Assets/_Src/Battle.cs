using System.Collections.Generic;
using _Code.Simulation;
using _Src.Config;
using _Src.GGPO;
using _Src.Input;
using UnityEngine;

namespace _Src {
    public class Battle {
        public bool IsRunning { get; private set; }
        public bool IsReplaying { get; private set; }

        public Battle(BattleConfig config, BattleInput input) {
            m_Config = config;
            m_Input = input;
            m_FrameInputs = new FighterInput[config.playerNum];
        }
        
        public void Init() {
            IsRunning = true;
            m_Session = new GgpoSession<FighterInput>(new GgpoCallback<FighterInput>() {
                AdvanceFrame = AdvanceFrame,
                LoadGameState = LoadGameState,
                SaveGameState = SaveGameState,
                OnSessionStarted = OnSessionStarted
            })
           // m_Session = new GgpoSession<FighterInput>()
        }

        private void OnSessionStarted() {
        }


        public void Dispose() {
            IsRunning = false;
        }

        public void Update() {
            if (IsRunning) {
                return;
            }
            
            m_TickAccumator += Time.unscaledDeltaTime;
            var tickCount = 0;
            while (m_TickAccumator >= m_Config.tickRate && tickCount < m_Config.maxTickPerUnityUpdate) {
                Tick();
                m_TickAccumator -= m_Config.tickRate;
                tickCount++;
            }
        }
        
        public bool TryGetNetworkStat(
            out int currentFrame,
            out int lastConfirmedFrame,
            out int rollbackCount) {
            currentFrame = 0;
            lastConfirmedFrame = 0;
            rollbackCount = 0;
            return true;
        }

        private BattleConfig m_Config;
        private BattleInput m_Input;
        private GgpoSession<FighterInput> m_Session;
        private FighterInput[] m_FrameInputs;
        private float m_TickAccumator;
        private List<int> m_LocalPlayerIndices = new List<int>();
        private List<int> m_RemotePlayerIndices = new List<int>();

        private void Tick() {
            if (IsReplaying) {
                return;
            }
            
            //添加本地输入
            for (int i = 0; i < m_LocalPlayerIndices.Count; i++) {
                var playerIndex = m_LocalPlayerIndices[i];
                m_Session.AddLocalInput(playerIndex, m_Input.ReadInput(playerIndex));
            }
            
            //处理远端输入,并执行回滚
            m_Session.Idle(0);
            
            //确定本帧所有玩家的最终输入
            if (!m_Session.TrySynqhronizeInputs(m_FrameInputs)) {
                
            }
        }
        
        private void AdvanceFrame(int arg1, FighterInput[] arg2) {
        }
        
        private void LoadGameState(byte[] obj) {
        }
        
        private GgpoSavedState SaveGameState(int arg) {
            return null;
        }
    }
}