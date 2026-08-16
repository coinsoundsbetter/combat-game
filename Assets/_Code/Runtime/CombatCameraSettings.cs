using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Shared fixed side-view camera settings for Runtime and editor previews.
    /// </summary>
    public static class CombatCameraSettings
    {
        public const float CenterX = 0f;
        public const float CenterY = 2.1f;
        public const float Depth = -10f;
        public const float OrthographicSize = 3.2f;

        public static Vector3 Position
        {
            get { return new Vector3(CenterX, CenterY, Depth); }
        }

        public static void Apply(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.orthographic = true;
            camera.orthographicSize = OrthographicSize;
            camera.transform.position = Position;
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.1f, 1f);
        }
    }
}
