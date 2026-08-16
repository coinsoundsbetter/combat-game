using System;
using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Resolves character slots into deterministic role data and presentation prefabs.
    /// It is deliberately unaware of the battle session and network state.
    /// </summary>
    public sealed class BattleRoleCatalog
    {
        private readonly FighterRoleDefinition[] _roles;
        private readonly GameObject _fallbackPrefab;
        private readonly int _fallbackSlotCount;

        public BattleRoleCatalog(
            FighterRoleDefinition[] roles,
            GameObject fallbackPrefab,
            int fallbackSlotCount)
        {
            _roles = roles;
            _fallbackPrefab = fallbackPrefab;
            _fallbackSlotCount = Mathf.Max(1, fallbackSlotCount);
        }

        public int CharacterSlotCount
        {
            get { return _roles != null && _roles.Length > 0 ? _roles.Length : _fallbackSlotCount; }
        }

        public int ClampCharacterIndex(int characterIndex)
        {
            return Mathf.Clamp(characterIndex, 0, CharacterSlotCount - 1);
        }

        public FighterRoleDefinition GetRoleDefinition(int characterIndex)
        {
            if (_roles == null || _roles.Length == 0)
            {
                return null;
            }

            return _roles[Mathf.Clamp(characterIndex, 0, _roles.Length - 1)];
        }

        public FighterRoleStats GetRoleStats(int characterIndex)
        {
            FighterRoleDefinition role = GetRoleDefinition(characterIndex);
            if (role == null)
            {
                throw new InvalidOperationException(
                    "A FighterRoleDefinition with a Jump MotionTimelineAsset is required for battle.");
            }

            return role.ToRoleStats();
        }

        public GameObject GetRolePrefab(int characterIndex)
        {
            FighterRoleDefinition role = GetRoleDefinition(characterIndex);
            return role != null && role.Prefab != null ? role.Prefab : _fallbackPrefab;
        }
    }
}
