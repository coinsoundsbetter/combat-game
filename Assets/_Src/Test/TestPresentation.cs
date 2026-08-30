using _Src.GGPO_Extension;
using UnityEngine;

namespace _Src.Test {
    /// <summary>
    /// 测试:表现层
    /// </summary>
    public class TestPresentation : MonoBehaviour {
        [SerializeField] private int maxPlayerNum = 2;
        [SerializeField] private Transform[] playerViews;
        [SerializeField, Min(0)] private float positionSmoothing = 18f;
        
        private Battle<FighterInput, FighterState> m_Battle;
        private Vector3[] m_InitialPositions;
        private Vector3[] m_RollbackVisualOffsets;
        private bool[] m_HasDisplayedPosition;
        private int m_LastRollbackRevision;

        private void Start() {
            m_InitialPositions = new Vector3[maxPlayerNum];
            m_RollbackVisualOffsets = new Vector3[maxPlayerNum];
            m_HasDisplayedPosition = new bool[maxPlayerNum];
            var count = Mathf.Min(playerViews.Length, m_InitialPositions.Length);
            for (int i =  0; i < count; ++i) {
                var view = playerViews[i];
                if (view == null) {
                    continue;
                }

                m_InitialPositions[i] = view.position;
            }
        }

        private void LateUpdate() {
            var currentBattle = TestDriver.BattleInstance;
            if (!ReferenceEquals(m_Battle, currentBattle)) {
                m_Battle = currentBattle;
                m_LastRollbackRevision = m_Battle != null ? m_Battle.RollbackRevision : 0;
                System.Array.Clear(m_RollbackVisualOffsets, 0, m_RollbackVisualOffsets.Length);
                System.Array.Clear(m_HasDisplayedPosition, 0, m_HasDisplayedPosition.Length);
            }

            if (m_Battle == null) {
                return;
            }

            DisplayPlayers();
        }

        private void DisplayPlayers() {
            var count = Mathf.Min(playerViews.Length, m_InitialPositions.Length);
            var alpha = positionSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-positionSmoothing * Time.unscaledDeltaTime);
            var rollbackOccurred = m_Battle.RollbackRevision != m_LastRollbackRevision;
            m_LastRollbackRevision = m_Battle.RollbackRevision;
            FighterState[] predictedStates = null;
            FighterState[] correctedStates = null;
            if (rollbackOccurred && !m_Battle.TryGetLastRollbackStateChange(
                    out _, out predictedStates, out correctedStates)) {
                rollbackOccurred = false;
            }

            for (var playerIndex = 0; playerIndex < count; playerIndex++) {
                var view = playerViews[playerIndex];
                if (view == null)
                    continue;

                var stateFrame = m_Battle.CurrentFrame;
                if (!m_Battle.TryGetStateFromHistory(stateFrame, out var states) ||
                    playerIndex >= states.Length)
                    continue;

                var target = m_InitialPositions[playerIndex] +
                             Vector3.right * states[playerIndex].PosX /
                             (float)TestSimulator.PositionUnitsPerWorldUnits;

                if (!m_HasDisplayedPosition[playerIndex]) {
                    view.position = target;
                    m_HasDisplayedPosition[playerIndex] = true;
                    continue;
                }

                // 只叠加同一逻辑帧在回滚前后的状态差，不将正常帧推进误判为修正。
                var correctionX = 0f;
                var offsetBeforeCorrection = m_RollbackVisualOffsets[playerIndex].x;
                if (rollbackOccurred && playerIndex < predictedStates.Length &&
                    playerIndex < correctedStates.Length) {
                    correctionX = (predictedStates[playerIndex].PosX -
                                   correctedStates[playerIndex].PosX) /
                                  (float)TestSimulator.PositionUnitsPerWorldUnits;
                    m_RollbackVisualOffsets[playerIndex] += Vector3.right * correctionX;
                }

                m_RollbackVisualOffsets[playerIndex] = Vector3.Lerp(
                    m_RollbackVisualOffsets[playerIndex], Vector3.zero, alpha);
                view.position = target + m_RollbackVisualOffsets[playerIndex];

                if (rollbackOccurred && Mathf.Abs(correctionX) > 0.0001f) {
                    TestDiagnostics.Record(
                        "VIEW",
                        $"Revision={m_Battle.RollbackRevision} Player={playerIndex} " +
                        $"IsLocal={(m_Battle.IsLocalPlayer(playerIndex) ? 1 : 0)} " +
                        $"CorrectionWorld={correctionX:F4} " +
                        $"OffsetBefore={offsetBeforeCorrection:F4} " +
                        $"OffsetAfter={m_RollbackVisualOffsets[playerIndex].x:F4} " +
                        $"TargetX={target.x:F4} DisplayX={view.position.x:F4} " +
                        $"Smoothing={positionSmoothing:F2}");
                }
            }
        }
    }
}
