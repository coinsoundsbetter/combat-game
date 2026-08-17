using GLMFighter.Core;
using GLMFighter.Network;
using System.Text;
using UnityEngine;

namespace GLMFighter.Runtime
{
    public enum BattleRunMode
    {
        Local,
        P2P
    }

    public sealed class NetworkBattleRunner : MonoBehaviour
    {
        [Header("Runtime")]
        [SerializeField] private bool enablePresentation = true;
        [SerializeField] private bool enableTemporaryGui = true;
        [SerializeField] private bool createDefaultSceneObjects = true;
        [SerializeField] private bool autoStartLocalBattle;

        [Header("Debug")]
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private bool debugLogicWorld;
        [SerializeField] private bool drawLogicWorldDebugEntities = true;
        [SerializeField] private bool drawLogicWorldDebugHud = true;
        [SerializeField] private bool logLogicWorldDebug;
        [SerializeField] private int logicWorldDebugLogIntervalFrames = 30;
        [SerializeField] private bool jumpDebug;

        [Header("Roles")]
        [SerializeField] private FighterRoleDefinition[] fighterRoles;
        [SerializeField] private GameObject fighterPrefab;
        [SerializeField] private int characterSlotCount = 1;

        [Header("Network")]
        [SerializeField] private int inputDelayFrames = 3;
        [SerializeField] private int inputRedundancyFrames = 12;
        [SerializeField] private int checksumIntervalFrames = 30;

        private readonly BattleSimulation _simulation = new BattleSimulation();
        private readonly BattleNetworkCoordinator _network = new BattleNetworkCoordinator();
        private BattleRoleCatalog _roles;
        private BattleInputSync _inputSync;
        private readonly BattleChecksumTracker _checksums = new BattleChecksumTracker();
        private BattlePresentationController _presentation;
        private BattleSessionController _session;
        private readonly BattleDebugHud _debugHud = new BattleDebugHud();
        private readonly BattleTickDriver _tickDriver = new BattleTickDriver();
        private FighterInput _lastLocalInput;
        private int _lastLogicWorldDebugLogFrame = -1;

        public int AssignedPlayerIndex => _session == null ? 0 : _session.AssignedPlayerIndex;
        public int LocalCharacterIndex => _session == null ? 0 : _session.LocalCharacterIndex;
        public int RemoteCharacterIndex => _session == null ? 0 : _session.RemoteCharacterIndex;
        public int CharacterSlotCount => _session == null ? Mathf.Max(1, characterSlotCount) : _session.CharacterSlotCount;
        public bool HasPlayerAssignment => _session != null && _session.HasPlayerAssignment;
        public bool HasOpponent => _network.HasOpponent;
        public bool LocalReady => _session != null && _session.LocalReady;
        public bool RemoteReady => _session != null && _session.RemoteReady;
        public bool BattleStarted => _session != null && _session.BattleStartedState;
        public bool EnablePresentation => enablePresentation;
        public bool EnableTemporaryGui => enableTemporaryGui;
        public bool DebugLogicWorld => debugLogicWorld;

        public void SelectLocalCharacter(int characterIndex)
        {
            _session.SelectLocalCharacter(characterIndex);
        }

        public void SetReady(bool ready)
        {
            _session.SetReady(ready);
        }

        public void SetPresentationEnabled(bool enabled)
        {
            if (enablePresentation == enabled)
            {
                return;
            }

            enablePresentation = enabled;

            if (enablePresentation)
            {
                _presentation.RebuildFighterViews(true, _session.GetPlayerOneRoleIndex(), _session.GetPlayerTwoRoleIndex());
                ApplyViews();
            }
            else
            {
                _presentation.RebuildFighterViews(false, 0, 0);
            }
        }

        public void SetTemporaryGuiEnabled(bool enabled)
        {
            enableTemporaryGui = enabled;
        }

        public void SetLogicWorldDebugEnabled(bool enabled)
        {
            debugLogicWorld = enabled;

            if (debugLogicWorld)
            {
                _presentation.RebuildLogicWorldDebugViews(true, drawLogicWorldDebugEntities);
                ApplyLogicWorldDebugViews();
            }
            else
            {
                _presentation.RebuildLogicWorldDebugViews(false, false);
            }
        }

        public string GetLogicWorldDebugText()
        {
            StringBuilder builder = new StringBuilder(512);
            builder.Append("LogicWorld Frame ");
            builder.Append(_simulation.Frame);
            builder.Append(" Winner ");
            builder.Append(_simulation.WinnerIndex < 0 ? "none" : "P" + (_simulation.WinnerIndex + 1));
            builder.AppendLine();
            AppendFighterDebug(builder, "P1", _simulation.PlayerOne);
            AppendFighterDebug(builder, "P2", _simulation.PlayerTwo);

            if (jumpDebug)
            {
                AppendJumpDebug(builder, "P1", _simulation.PlayerOne);
                AppendJumpDebug(builder, "P2", _simulation.PlayerTwo);
            }

            return builder.ToString();
        }

        private void Awake()
        {
            characterSlotCount = Mathf.Max(1, characterSlotCount);
            Application.targetFrameRate = BattleSimulation.FramesPerSecond;
            Application.runInBackground = true;
            _debugHud.Configure(defaultPort);
            _roles = new BattleRoleCatalog(fighterRoles, fighterPrefab, characterSlotCount);
            _inputSync = new BattleInputSync(inputDelayFrames, inputRedundancyFrames);
            _presentation = new BattlePresentationController(transform, _simulation, _roles);
            _session = new BattleSessionController(
                _simulation,
                _roles,
                _network,
                _inputSync,
                _checksums,
                _tickDriver);
            _session.BattleStarted += HandleBattleStarted;
            _session.PreviewBattleReset += HandlePreviewBattleReset;

            if (createDefaultSceneObjects)
            {
                BattleSceneBootstrap.EnsureDefaultScene();
            }

            if (enablePresentation)
            {
                _presentation.RebuildFighterViews(true, 0, 0);
            }

            ApplyViews();
            ApplyLogicWorldDebugViews();

            if (autoStartLocalBattle)
            {
                StartLocalBattle();
            }
        }

        private void OnDestroy()
        {
            _network.Dispose();
            if (_presentation != null)
            {
                _presentation.Dispose();
            }
        }

        private void Update()
        {
            _network.Update(HandleNetworkPacket);

            if (!BattleStarted)
            {
                ApplyViews();
                ApplyLogicWorldDebugViews();
                MaybeLogLogicWorldDebug();
                return;
            }

            _tickDriver.Advance(Time.deltaTime, TickBattle);

            ApplyViews();
            ApplyLogicWorldDebugViews();
            MaybeLogLogicWorldDebug();
        }

        private void TickLocalBattle()
        {
            _lastLocalInput = BattleInputReader.ReadPlayerOne();
            _simulation.Step(_lastLocalInput, BattleInputReader.ReadPlayerTwo());
        }

        private void TickBattle()
        {
            if (_session.RunMode == BattleRunMode.Local)
            {
                TickLocalBattle();
            }
            else
            {
                TickNetworkBattle();
            }
        }

        private void TickNetworkBattle()
        {
            FighterInput localInput = ReadLocalInput();
            _lastLocalInput = localInput;
            _inputSync.CaptureLocalInput(
                _simulation.Frame,
                localInput,
                _network.SendInputBundle);

            FighterInput playerOneInput;
            FighterInput playerTwoInput;
            if (!_inputSync.TryGetInputsForFrame(
                    _simulation.Frame,
                    _session.AssignedPlayerIndex,
                    out playerOneInput,
                    out playerTwoInput))
            {
                return;
            }

            _simulation.Step(playerOneInput, playerTwoInput);
            SendPeriodicChecksum();
            _inputSync.Prune(_simulation.Frame - 30);
            _checksums.Prune(_simulation.Frame - 30);
        }

        private void HandleNetworkPacket(TransportPacket packet)
        {
            if (packet.Type == TransportPacketType.StartBattle)
            {
                _session.StartBattle(packet.AssignedPlayerIndex);
            }
            else if (packet.Type == TransportPacketType.AssignPlayer)
            {
                _session.AssignLocalPlayer(packet.AssignedPlayerIndex);
            }
            else if (packet.Type == TransportPacketType.LobbyState)
            {
                _session.StoreRemoteLobbyState(packet.CharacterIndex, packet.Ready);
            }
            else if (packet.Type == TransportPacketType.Input)
            {
                _inputSync.StoreRemoteInput(packet.InputFrame, packet.Input);
            }
            else if (packet.Type == TransportPacketType.InputBundle)
            {
                _inputSync.StoreRemoteInputBundle(packet.InputFrames);
            }
            else if (packet.Type == TransportPacketType.Checksum)
            {
                _checksums.StoreRemote(packet.ChecksumFrame, packet.ChecksumValue);
            }
        }

        private void HandleBattleStarted(int playerOneRoleIndex, int playerTwoRoleIndex)
        {
            _lastLogicWorldDebugLogFrame = -1;
            _lastLocalInput = FighterInput.Neutral;
            _presentation.RebuildFighterViews(enablePresentation, playerOneRoleIndex, playerTwoRoleIndex);
        }

        private void HandlePreviewBattleReset()
        {
            _presentation.RebuildFighterViews(enablePresentation, 0, 0);
            ApplyViews();
            ApplyLogicWorldDebugViews();
        }

        private void StartLocalBattle()
        {
            _session.StartLocalBattle();
        }

        private void ResetPreviewBattle()
        {
            _session.ResetPreviewBattle();
        }

        private void LeaveBattle()
        {
            _session.LeaveBattle();
        }

        private void ApplyViews()
        {
            _presentation.Apply(enablePresentation, _debugHud.ShowDebugBoxes);
        }

        private void ApplyLogicWorldDebugViews()
        {
            _presentation.ApplyLogicWorldDebug(debugLogicWorld, drawLogicWorldDebugEntities);
        }

        private void OnGUI()
        {
            _debugHud.Draw(
                _session,
                _network,
                _simulation,
                _inputSync,
                _checksums,
                debugLogicWorld,
                drawLogicWorldDebugHud,
                enableTemporaryGui,
                inputDelayFrames,
                inputRedundancyFrames,
                defaultPort,
                _lastLocalInput,
                GetLogicWorldDebugText,
                StartLocalBattle,
                ResetPreviewBattle,
                LeaveBattle,
                ApplyViews);
        }

        private void MaybeLogLogicWorldDebug()
        {
            if (!debugLogicWorld || !logLogicWorldDebug)
            {
                return;
            }

            int interval = Mathf.Max(1, logicWorldDebugLogIntervalFrames);

            if (_simulation.Frame == _lastLogicWorldDebugLogFrame)
            {
                return;
            }

            if (_simulation.Frame % interval != 0)
            {
                return;
            }

            _lastLogicWorldDebugLogFrame = _simulation.Frame;
            Debug.Log(GetLogicWorldDebugText());
        }

        private void AppendFighterDebug(StringBuilder builder, string label, FighterState state)
        {
            SimRect[] hurtboxes = _simulation.GetHurtboxes(state);
            SimRect[] pushboxes = _simulation.GetPushboxes(state);
            SimRect[] hitboxes;
            bool hasHitboxes = _simulation.TryGetAttackHitboxes(state, out hitboxes);

            builder.Append(label);
            builder.Append(" hp=");
            builder.Append(state.Health);
            builder.Append(" phase=");
            builder.Append(state.Phase);
            builder.Append(" attack=");
            builder.Append(state.CurrentAttack);
            builder.Append(" motion=");
            builder.Append(state.MotionFrame);
            builder.Append(" anchor=");
            AppendVector(builder, state.Position);
            builder.Append(" entity=");
            AppendVector(builder, _simulation.GetEntityCenter(state));
            builder.Append(" vel=");
            AppendVector(builder, state.Velocity);
            builder.Append(" facing=");
            builder.Append(state.Facing);
            builder.Append(" ground=");
            builder.Append(state.OnGround);
            builder.AppendLine();

            builder.Append("  Hurt ");
            AppendRects(builder, hurtboxes);
            builder.AppendLine();
            builder.Append("  Push ");
            AppendRects(builder, pushboxes);
            builder.AppendLine();
            builder.Append("  Hit ");
            AppendRects(builder, hasHitboxes ? hitboxes : new SimRect[0]);
            builder.AppendLine();
        }

        private void AppendJumpDebug(StringBuilder builder, string label, FighterState state)
        {
            CombatMoveData jumpMove = state.RoleStats.JumpMove;
            int currentFrameY = 0;
            int nextFrame = 0;
            int nextFrameY = 0;

            if (jumpMove.HasFrames)
            {
                currentFrameY = jumpMove.GetFrame(state.MotionFrame).EntityOffset.Y;
                nextFrame = jumpMove.GetFrameForSimulationTick(state.MotionTicks + 1);
                nextFrameY = jumpMove.GetFrame(nextFrame).EntityOffset.Y;
            }

            SimVector2 entityCenter = _simulation.GetEntityCenter(state);
            builder.Append(label);
            builder.Append(" JumpDebug phase=");
            builder.Append(state.Phase);
            builder.Append(" phaseFrame=");
            builder.Append(state.PhaseFrame);
            builder.Append(" motionFrame=");
            builder.Append(state.MotionFrame);
            builder.Append(" motionTick=");
            builder.Append(state.MotionTicks);
            builder.Append(" logicY=");
            builder.Append(SimMath.ToUnity(state.Position.Y).ToString("0.###"));
            builder.Append(" entityY=");
            builder.Append(SimMath.ToUnity(entityCenter.Y).ToString("0.###"));
            builder.Append(" timelineY=");
            builder.Append(SimMath.ToUnity(currentFrameY).ToString("0.###"));
            builder.Append(" nextFrame=");
            builder.Append(nextFrame);
            builder.Append(" nextTimelineY=");
            builder.Append(SimMath.ToUnity(nextFrameY).ToString("0.###"));
            builder.Append(" velocityY=");
            builder.Append(SimMath.ToUnity(state.Velocity.Y).ToString("0.###"));
            builder.Append(" onGround=");
            builder.Append(state.OnGround);
            builder.AppendLine();
        }

        private static void AppendVector(StringBuilder builder, SimVector2 value)
        {
            builder.Append('(');
            builder.Append(value.X);
            builder.Append(',');
            builder.Append(value.Y);
            builder.Append(')');
        }

        private static void AppendRects(StringBuilder builder, SimRect[] rects)
        {
            int count = rects == null ? 0 : rects.Length;
            builder.Append(count);

            int visibleCount = Mathf.Min(count, 4);

            for (int index = 0; index < visibleCount; index++)
            {
                builder.Append(' ');
                AppendRect(builder, rects[index]);
            }

            if (count > visibleCount)
            {
                builder.Append(" ...");
            }
        }

        private static void AppendRect(StringBuilder builder, SimRect rect)
        {
            builder.Append('[');
            builder.Append(rect.CenterX);
            builder.Append(',');
            builder.Append(rect.CenterY);
            builder.Append(" hw=");
            builder.Append(rect.HalfWidth);
            builder.Append(" hh=");
            builder.Append(rect.HalfHeight);
            builder.Append(']');
        }

        private FighterInput ReadLocalInput()
        {
            return _session.AssignedPlayerIndex == 0
                ? BattleInputReader.ReadPlayerOne()
                : BattleInputReader.ReadPlayerTwo();
        }

        private void SendPeriodicChecksum()
        {
            if (!_checksums.ShouldSend(_simulation.Frame, checksumIntervalFrames))
            {
                return;
            }

            int frame = _simulation.Frame;
            int checksum = _simulation.ComputeChecksum();
            _checksums.StoreLocal(frame, checksum, _network.SendChecksum);
        }

    }
}
