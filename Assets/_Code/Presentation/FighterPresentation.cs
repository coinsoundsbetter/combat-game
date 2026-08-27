using _Code.Simulation;
using UnityEngine;

namespace _Code.Presentation {
    /// <summary>
    /// 将确定性逻辑状态映射至 Unity 场景对象。
    /// 远端角色使用确认帧缓冲，避免将回滚修正直接显示出来。
    /// 不会向逻辑层写入任何数据。
    /// </summary>
    public sealed class FighterPresentation : MonoBehaviour {
        private const int PlayerCount = 2;

        public GameMain gameMain;
        public Transform player1View;
        public Transform player2View;
        public PrimitiveType demoPrimitive = PrimitiveType.Cube;
        public float unitsPerLogicX = 0.25f;
        public float playerSpacing = 3f;
        public float verticalPosition = 0.5f;
        public bool useConfirmedRemotePresentation = true;
        [Min(0)] public int remoteDisplayDelayFrames = 2;
        [Min(0f)] public float remotePositionSmoothing = 18f;

        private readonly Transform[] m_PlayerViews = new Transform[PlayerCount];
        private readonly Vector3[] m_BasePositions = new Vector3[PlayerCount];
        private readonly Vector3[] m_DisplayPositions = new Vector3[PlayerCount];
        private readonly bool[] m_HasDisplayPosition = new bool[PlayerCount];

        private void Awake() {
            if (gameMain == null)
                gameMain = GetComponent<GameMain>();

            m_PlayerViews[0] = player1View;
            m_PlayerViews[1] = player2View;
            CreateDemoCameraIfMissing();
            CreateMissingDemoViews();
            CacheBasePositions();
        }

        private void LateUpdate() {
            if (gameMain == null)
                return;

            for (var playerIndex = 0; playerIndex < PlayerCount; playerIndex++) {
                var view = m_PlayerViews[playerIndex];
                if (view == null)
                    continue;

                if (gameMain.IsLocalPlayer(playerIndex) ||
                    !useConfirmedRemotePresentation) {
                    PlayerState currentState;
                    if (!gameMain.TryGetRenderPlayerState(
                            playerIndex,
                            out _,
                            out currentState,
                            out _))
                        continue;

                    SetDisplayPosition(
                        playerIndex,
                        GetWorldPosition(playerIndex, currentState.X));
                    continue;
                }

                PlayerState confirmedState;
                if (!gameMain.TryGetConfirmedPlayerState(
                        playerIndex,
                        remoteDisplayDelayFrames,
                        out confirmedState))
                    continue;

                var targetPosition = GetWorldPosition(playerIndex, confirmedState.X);
                if (!m_HasDisplayPosition[playerIndex]) {
                    SetDisplayPosition(playerIndex, targetPosition);
                    continue;
                }

                var smoothingAlpha = remotePositionSmoothing <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-remotePositionSmoothing * Time.unscaledDeltaTime);
                SetDisplayPosition(
                    playerIndex,
                    Vector3.Lerp(m_DisplayPositions[playerIndex], targetPosition, smoothingAlpha));
            }
        }

        private void CreateMissingDemoViews() {
            for (var playerIndex = 0; playerIndex < PlayerCount; playerIndex++) {
                if (m_PlayerViews[playerIndex] != null)
                    continue;

                var demoObject = GameObject.CreatePrimitive(demoPrimitive);
                demoObject.name = $"Player {playerIndex + 1} Presentation";
                demoObject.transform.position = new Vector3(
                    playerIndex == 0 ? -playerSpacing : playerSpacing,
                    verticalPosition,
                    0f);

                var renderer = demoObject.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = playerIndex == 0
                        ? new Color(0.9f, 0.2f, 0.2f)
                        : new Color(0.2f, 0.45f, 0.95f);

                m_PlayerViews[playerIndex] = demoObject.transform;
            }
        }

        private static void CreateDemoCameraIfMissing() {
            if (Camera.main != null)
                return;

            var cameraObject = new GameObject("Presentation Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -12f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
        }

        private void CacheBasePositions() {
            for (var playerIndex = 0; playerIndex < PlayerCount; playerIndex++) {
                m_BasePositions[playerIndex] = m_PlayerViews[playerIndex].position;
                m_DisplayPositions[playerIndex] = m_BasePositions[playerIndex];
                m_HasDisplayPosition[playerIndex] = true;
            }
        }

        private Vector3 GetWorldPosition(int playerIndex, int logicX) {
            return m_BasePositions[playerIndex] +
                   Vector3.right * logicX * unitsPerLogicX;
        }

        private void SetDisplayPosition(int playerIndex, Vector3 position) {
            m_DisplayPositions[playerIndex] = position;
            m_HasDisplayPosition[playerIndex] = true;
            m_PlayerViews[playerIndex].position = position;
        }
    }
}
