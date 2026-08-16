using GLMFighter.Core;
using System.Collections.Generic;
using UnityEngine;

namespace GLMFighter.Runtime
{
    public sealed class FighterView
    {
        private readonly Transform _root;
        private readonly Transform _visualRoot;
        private readonly Animator _animator;
        private readonly FighterAnimationDriver _animationDriver;
        private readonly Quaternion _facingRightRotation;
        private readonly Quaternion _facingLeftRotation;
        private readonly Transform _body;
        private readonly List<Transform> _hurtboxes = new List<Transform>();
        private readonly List<Transform> _hitboxes = new List<Transform>();
        private readonly List<Transform> _pushboxes = new List<Transform>();
        private readonly Renderer _bodyRenderer;
        private readonly BattleSimulation _simulation;
        private readonly Color _baseColor;

        public FighterView(string name, Transform parent, Color bodyColor, BattleSimulation simulation,
            GameObject avatarPrefab)
        {
            _simulation = simulation;
            _baseColor = bodyColor;

            if (avatarPrefab != null)
            {
                GameObject root = new GameObject(name);
                _root = root.transform;
                _root.SetParent(parent, false);

                GameObject instance = Object.Instantiate(avatarPrefab, _root);
                instance.name = name + " Avatar";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                ResolveAvatar(instance, out _visualRoot, out _animator, out _facingRightRotation,
                    out _facingLeftRotation);
                _animationDriver = ResolveAnimationDriver(instance, _visualRoot, _animator);
                _body = null;
                _bodyRenderer = null;
            }
            else
            {
                GameObject root = new GameObject(name);
                _root = root.transform;
                _root.SetParent(parent, false);
                _visualRoot = root.transform;
                _facingRightRotation = Quaternion.identity;
                _facingLeftRotation = Quaternion.Euler(0f, 180f, 0f);
                _animationDriver = null;
                _body = CreateBox(name + " Body", root.transform, bodyColor, out _bodyRenderer);
            }
        }

        public void Apply(FighterState state, bool showDebug)
        {
            float height = SimMath.ToUnity(GetBodyHalfHeight(state.RoleStats) * 2);
            float width = SimMath.ToUnity(GetBodyHalfWidth(state.RoleStats) * 2);
            SimVector2 entityCenter = _simulation.GetEntityCenter(state);
            _root.position = new Vector3(SimMath.ToUnity(entityCenter.X), SimMath.ToUnity(entityCenter.Y), 0f);
            _root.rotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            if (_body != null)
            {
                _body.localPosition = new Vector3(0f, height * 0.5f, 0f);
                _body.localScale = new Vector3(width, height, 0.8f);
                _body.localRotation = Quaternion.Euler(0f, state.Facing > 0 ? 0f : 180f, 0f);
                _bodyRenderer.material.color = TintForPhase(_baseColor, state.Phase);
            }
            else if (_visualRoot != null && _visualRoot != _root)
            {
                _visualRoot.localRotation = state.Facing > 0 ? _facingRightRotation : _facingLeftRotation;
            }

            if (_animationDriver != null)
            {
                _animationDriver.Apply(state);
            }

            if (showDebug)
            {
                SetDebugRects(_hurtboxes, _simulation.GetHurtboxes(state), CombatBoxKind.Hurt, -0.55f, 0.12f);
                SetDebugRects(_pushboxes, _simulation.GetPushboxes(state), CombatBoxKind.Push, -0.5f, 0.08f);

                SimRect[] hitboxes;
                if (_simulation.TryGetAttackHitboxes(state, out hitboxes))
                {
                    SetDebugRects(_hitboxes, hitboxes, CombatBoxKind.Hit, -0.7f, 0.16f);
                }
                else
                {
                    SetDebugRects(_hitboxes, new SimRect[0], CombatBoxKind.Hit, -0.7f, 0.16f);
                }
            }
            else
            {
                SetDebugBoxesActive(false);
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }
        }

        private static int GetBodyHalfWidth(FighterRoleStats roleStats)
        {
            return roleStats.StandingBody.HurtBoxHalfWidth > 0
                ? roleStats.StandingBody.HurtBoxHalfWidth
                : BattleSimulation.DefaultFighterRoleStats.StandingBody.HurtBoxHalfWidth;
        }

        private static int GetBodyHalfHeight(FighterRoleStats roleStats)
        {
            return roleStats.StandingBody.HurtBoxHalfHeight > 0
                ? roleStats.StandingBody.HurtBoxHalfHeight
                : BattleSimulation.DefaultFighterRoleStats.StandingBody.HurtBoxHalfHeight;
        }

        private void SetDebugRects(
            List<Transform> pool,
            SimRect[] rects,
            CombatBoxKind kind,
            float z,
            float depth)
        {
            EnsureDebugBoxes(pool, rects.Length, kind);

            for (int index = 0; index < pool.Count; index++)
            {
                bool active = index < rects.Length;
                pool[index].gameObject.SetActive(active);

                if (active)
                {
                    SetWorldRect(pool[index], rects[index], z, depth);
                }
            }
        }

        private void EnsureDebugBoxes(List<Transform> pool, int count, CombatBoxKind kind)
        {
            while (pool.Count < count)
            {
                Renderer renderer;
                Transform box = CreateBox(
                    _root.name + " " + kind + "box " + pool.Count,
                    _root,
                    ColorForBox(kind),
                    out renderer);
                pool.Add(box);
            }
        }

        private void SetDebugBoxesActive(bool active)
        {
            SetPoolActive(_hurtboxes, active);
            SetPoolActive(_hitboxes, active);
            SetPoolActive(_pushboxes, active);
        }

        private static void SetPoolActive(List<Transform> pool, bool active)
        {
            for (int index = 0; index < pool.Count; index++)
            {
                pool[index].gameObject.SetActive(active);
            }
        }

        private static Color ColorForBox(CombatBoxKind kind)
        {
            switch (kind)
            {
                case CombatBoxKind.Hit:
                    return new Color(1f, 0.2f, 0.1f, 0.45f);
                case CombatBoxKind.Push:
                    return new Color(0.25f, 0.45f, 1f, 0.22f);
                case CombatBoxKind.Throw:
                    return new Color(1f, 0.6f, 0.1f, 0.35f);
                case CombatBoxKind.Block:
                    return new Color(0.6f, 0.9f, 1f, 0.3f);
                default:
                    return new Color(0.2f, 0.8f, 1f, 0.35f);
            }
        }

        private static void ResolveAvatar(
            GameObject instance,
            out Transform visualRoot,
            out Animator animator,
            out Quaternion facingRightRotation,
            out Quaternion facingLeftRotation)
        {
            FighterAvatar avatar = instance.GetComponent<FighterAvatar>();

            if (avatar == null)
            {
                avatar = instance.GetComponentInChildren<FighterAvatar>();
            }

            if (avatar != null)
            {
                avatar.ResolveMissingReferences();
                visualRoot = avatar.RequiredVisualRoot;
                animator = avatar.OptionalAnimator;
                facingRightRotation = avatar.FacingRightRotation;
                facingLeftRotation = avatar.FacingLeftRotation;
                return;
            }

            visualRoot = instance.transform.Find("VisualRoot");

            if (visualRoot == null)
            {
                visualRoot = instance.transform;
            }

            animator = instance.GetComponentInChildren<Animator>();
            facingRightRotation = Quaternion.Euler(0f, 90f, 0f);
            facingLeftRotation = Quaternion.Euler(0f, -90f, 0f);
        }

        private static FighterAnimationDriver ResolveAnimationDriver(
            GameObject instance,
            Transform visualRoot,
            Animator animator)
        {
            FighterAnimationDriver driver = instance.GetComponentInChildren<FighterAnimationDriver>();

            if (driver == null && visualRoot != null)
            {
                driver = visualRoot.gameObject.AddComponent<FighterAnimationDriver>();
            }

            if (driver != null)
            {
                driver.BindAnimator(animator);
            }

            return driver;
        }

        private static Transform CreateBox(string name, Transform parent, Color color, out Renderer renderer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);

            Object.Destroy(go.GetComponent<Collider>());
            renderer = go.GetComponent<Renderer>();
            renderer.material = CreateMaterial(color);
            return go.transform;
        }

        private static void SetWorldRect(Transform transform, SimRect rect, float z, float depth)
        {
            transform.position = new Vector3(SimMath.ToUnity(rect.CenterX), SimMath.ToUnity(rect.CenterY), 0f);
            transform.localScale = new Vector3(
                SimMath.ToUnity(rect.HalfWidth * 2),
                SimMath.ToUnity(rect.HalfHeight * 2),
                depth);
            transform.position = new Vector3(transform.position.x, transform.position.y, z);
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.color = color;
            return material;
        }

        private static Color TintForPhase(Color baseColor, FighterPhase phase)
        {
            Color color = baseColor;

            if (phase == FighterPhase.Hitstun)
            {
                color = Color.red;
            }
            else if (phase == FighterPhase.Blockstun || phase == FighterPhase.Guard)
            {
                color = new Color(0.35f, 0.55f, 1f, 1f);
            }
            else if (phase == FighterPhase.KO)
            {
                color = Color.gray;
            }

            color.a = 1f;
            return color;
        }
    }
}
