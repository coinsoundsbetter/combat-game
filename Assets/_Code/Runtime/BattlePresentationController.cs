using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Owns Unity-side fighter and logic-world debug views.
    /// </summary>
    public sealed class BattlePresentationController
    {
        private readonly Transform _parent;
        private readonly BattleSimulation _simulation;
        private readonly BattleRoleCatalog _roles;

        private FighterView _playerOneView;
        private FighterView _playerTwoView;
        private LogicWorldDebugView _playerOneLogicDebugView;
        private LogicWorldDebugView _playerTwoLogicDebugView;

        public BattlePresentationController(
            Transform parent,
            BattleSimulation simulation,
            BattleRoleCatalog roles)
        {
            _parent = parent;
            _simulation = simulation;
            _roles = roles;
        }

        public void RebuildFighterViews(bool enabled, int playerOneRoleIndex, int playerTwoRoleIndex)
        {
            DisposeFighterViews();

            if (!enabled)
            {
                return;
            }

            _playerOneView = new FighterView(
                "Player One",
                _parent,
                new Color(0.9f, 0.25f, 0.2f, 1f),
                _simulation,
                _roles.GetRolePrefab(playerOneRoleIndex));

            _playerTwoView = new FighterView(
                "Player Two",
                _parent,
                new Color(0.1f, 0.55f, 0.95f, 1f),
                _simulation,
                _roles.GetRolePrefab(playerTwoRoleIndex));
        }

        public void RebuildLogicWorldDebugViews(bool enabled, bool drawEntities)
        {
            DisposeLogicWorldDebugViews();

            if (!enabled || !drawEntities)
            {
                return;
            }

            _playerOneLogicDebugView = new LogicWorldDebugView(
                "Player One",
                _parent,
                new Color(0.9f, 0.25f, 0.2f, 1f));

            _playerTwoLogicDebugView = new LogicWorldDebugView(
                "Player Two",
                _parent,
                new Color(0.1f, 0.55f, 0.95f, 1f));
        }

        public void Apply(bool presentationEnabled, bool showDebugBoxes)
        {
            if (!presentationEnabled || _playerOneView == null || _playerTwoView == null)
            {
                return;
            }

            _playerOneView.Apply(_simulation.PlayerOne, showDebugBoxes);
            _playerTwoView.Apply(_simulation.PlayerTwo, showDebugBoxes);
        }

        public void ApplyLogicWorldDebug(bool enabled, bool drawEntities)
        {
            if (!enabled || !drawEntities)
            {
                DisposeLogicWorldDebugViews();
                return;
            }

            if (_playerOneLogicDebugView == null || _playerTwoLogicDebugView == null)
            {
                RebuildLogicWorldDebugViews(true, true);
            }

            _playerOneLogicDebugView.Apply(_simulation.PlayerOne, _simulation, true);
            _playerTwoLogicDebugView.Apply(_simulation.PlayerTwo, _simulation, true);
        }

        public void Dispose()
        {
            DisposeFighterViews();
            DisposeLogicWorldDebugViews();
        }

        private void DisposeFighterViews()
        {
            if (_playerOneView != null)
            {
                _playerOneView.Dispose();
                _playerOneView = null;
            }

            if (_playerTwoView != null)
            {
                _playerTwoView.Dispose();
                _playerTwoView = null;
            }
        }

        private void DisposeLogicWorldDebugViews()
        {
            if (_playerOneLogicDebugView != null)
            {
                _playerOneLogicDebugView.Dispose();
                _playerOneLogicDebugView = null;
            }

            if (_playerTwoLogicDebugView != null)
            {
                _playerTwoLogicDebugView.Dispose();
                _playerTwoLogicDebugView = null;
            }
        }
    }
}
