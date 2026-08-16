using UnityEngine;

namespace GLMFighter.Runtime
{
    [DisallowMultipleComponent]
    public sealed class FighterAvatar : MonoBehaviour
    {
        [Header("Visual")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private Vector3 facingRightEuler = new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 facingLeftEuler = new Vector3(0f, -90f, 0f);

        [Header("Sockets")]
        [SerializeField] private Transform center;
        [SerializeField] private Transform head;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform handLeft;
        [SerializeField] private Transform handRight;
        [SerializeField] private Transform footLeft;
        [SerializeField] private Transform footRight;

        public Transform VisualRoot => visualRoot;
        public Animator Animator => animator;
        public Quaternion FacingRightRotation => Quaternion.Euler(facingRightEuler);
        public Quaternion FacingLeftRotation => Quaternion.Euler(facingLeftEuler);
        public Transform Center => center;
        public Transform Head => head;
        public Transform Chest => chest;
        public Transform HandLeft => handLeft;
        public Transform HandRight => handRight;
        public Transform FootLeft => footLeft;
        public Transform FootRight => footRight;

        public Transform RequiredVisualRoot
        {
            get
            {
                if (visualRoot != null)
                {
                    return visualRoot;
                }

                return transform;
            }
        }

        public Animator OptionalAnimator
        {
            get
            {
                if (animator != null)
                {
                    return animator;
                }

                return GetComponentInChildren<Animator>();
            }
        }

        private void Reset()
        {
            ResolveFacingDefaults();
            ResolveMissingReferences();
        }

        private void OnValidate()
        {
            ResolveFacingDefaults();
            ResolveMissingReferences();
        }

        public void ResolveMissingReferences()
        {
            ResolveFacingDefaults();

            if (visualRoot == null)
            {
                Transform foundVisualRoot = transform.Find("VisualRoot");
                visualRoot = foundVisualRoot != null ? foundVisualRoot : transform;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            center = ResolveSocket(center, "Sockets/Center", "Center");
            head = ResolveSocket(head, "Sockets/Head", "Head");
            chest = ResolveSocket(chest, "Sockets/Chest", "Chest");
            handLeft = ResolveSocket(handLeft, "Sockets/Hand_L", "Hand_L");
            handRight = ResolveSocket(handRight, "Sockets/Hand_R", "Hand_R");
            footLeft = ResolveSocket(footLeft, "Sockets/Foot_L", "Foot_L");
            footRight = ResolveSocket(footRight, "Sockets/Foot_R", "Foot_R");
        }

        private void ResolveFacingDefaults()
        {
            if (facingRightEuler == Vector3.zero && facingLeftEuler == Vector3.zero)
            {
                facingRightEuler = new Vector3(0f, 90f, 0f);
                facingLeftEuler = new Vector3(0f, -90f, 0f);
            }
        }

        private Transform ResolveSocket(Transform current, string path, string fallbackName)
        {
            if (current != null)
            {
                return current;
            }

            Transform socket = transform.Find(path);

            if (socket != null)
            {
                return socket;
            }

            return FindDeepChild(transform, fallbackName);
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name == childName)
                {
                    return child;
                }

                Transform nestedChild = FindDeepChild(child, childName);

                if (nestedChild != null)
                {
                    return nestedChild;
                }
            }

            return null;
        }
    }
}
