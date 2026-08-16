using GLMFighter.Core;
using System.Collections.Generic;
using UnityEngine;

namespace GLMFighter.Runtime
{
    public sealed class LogicWorldDebugView
    {
        private readonly Transform _root;
        private readonly Transform _originMarker;
        private readonly Transform _facingMarker;
        private readonly List<Transform> _hurtboxes = new List<Transform>();
        private readonly List<Transform> _pushboxes = new List<Transform>();
        private readonly List<Transform> _hitboxes = new List<Transform>();
        private readonly Renderer _originRenderer;
        private readonly Renderer _facingRenderer;
        private readonly Color _baseColor;

        public LogicWorldDebugView(string name, Transform parent, Color color)
        {
            _baseColor = color;
            GameObject root = new GameObject(name + " Logic Entity");
            _root = root.transform;
            _root.SetParent(parent, false);

            _originMarker = CreateBox(name + " Logic Root", _root, color, out _originRenderer);
            _facingMarker = CreateBox(name + " Logic Facing", _root, new Color(1f, 1f, 1f, 0.85f), out _facingRenderer);
        }

        public void Apply(FighterState state, BattleSimulation simulation, bool showBoxes)
        {
            _root.position = Vector3.zero;
            _root.rotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            SimVector2 entityCenter = simulation.GetEntityCenter(state);
            Vector3 origin = new Vector3(SimMath.ToUnity(entityCenter.X), SimMath.ToUnity(entityCenter.Y), -0.9f);
            _originMarker.position = origin;
            _originMarker.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            _originRenderer.material.color = state.IsKO ? Color.gray : _baseColor;

            _facingMarker.position = origin + new Vector3(0.28f * state.Facing, 0.14f, 0f);
            _facingMarker.localScale = new Vector3(0.36f, 0.04f, 0.04f);
            _facingMarker.localRotation = Quaternion.identity;
            _facingRenderer.material.color = _baseColor;

            if (showBoxes)
            {
                SetDebugRects(_hurtboxes, simulation.GetHurtboxes(state), CombatBoxKind.Hurt, -0.92f, 0.08f);
                SetDebugRects(_pushboxes, simulation.GetPushboxes(state), CombatBoxKind.Push, -0.88f, 0.06f);

                SimRect[] hitboxes;
                if (simulation.TryGetAttackHitboxes(state, out hitboxes))
                {
                    SetDebugRects(_hitboxes, hitboxes, CombatBoxKind.Hit, -0.96f, 0.1f);
                }
                else
                {
                    SetDebugRects(_hitboxes, new SimRect[0], CombatBoxKind.Hit, -0.96f, 0.1f);
                }
            }
            else
            {
                SetPoolActive(_hurtboxes, false);
                SetPoolActive(_pushboxes, false);
                SetPoolActive(_hitboxes, false);
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }
        }

        private void SetDebugRects(List<Transform> pool, SimRect[] rects, CombatBoxKind kind, float z, float depth)
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

        private static void SetPoolActive(List<Transform> pool, bool active)
        {
            for (int index = 0; index < pool.Count; index++)
            {
                pool[index].gameObject.SetActive(active);
            }
        }

        private static Transform CreateBox(string name, Transform parent, Color color, out Renderer renderer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);

            Object.Destroy(go.GetComponent<Collider>());
            renderer = go.GetComponent<Renderer>();
            renderer.material = CreateMaterial(color);
            return go.transform;
        }

        private static void SetWorldRect(Transform transform, SimRect rect, float z, float depth)
        {
            transform.position = new Vector3(SimMath.ToUnity(rect.CenterX), SimMath.ToUnity(rect.CenterY), z);
            transform.localScale = new Vector3(
                SimMath.ToUnity(rect.HalfWidth * 2),
                SimMath.ToUnity(rect.HalfHeight * 2),
                depth);
            transform.localRotation = Quaternion.identity;
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

        private static Color ColorForBox(CombatBoxKind kind)
        {
            switch (kind)
            {
                case CombatBoxKind.Hit:
                    return new Color(1f, 0.1f, 0.05f, 0.7f);
                case CombatBoxKind.Push:
                    return new Color(0.25f, 0.45f, 1f, 0.35f);
                case CombatBoxKind.Throw:
                    return new Color(1f, 0.6f, 0.1f, 0.55f);
                case CombatBoxKind.Block:
                    return new Color(0.6f, 0.9f, 1f, 0.45f);
                default:
                    return new Color(0.1f, 0.9f, 1f, 0.55f);
            }
        }
    }
}
