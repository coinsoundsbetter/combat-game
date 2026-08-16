using UnityEngine;
using Object = UnityEngine.Object;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Creates the temporary prototype scene pieces. It is not part of battle logic.
    /// </summary>
    public static class BattleSceneBootstrap
    {
        public static void EnsureDefaultScene()
        {
            EnsureSceneView();
            CreateGround();
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.08f, 0.2f);
            ground.transform.localScale = new Vector3(12f, 0.16f, 1.2f);

            Renderer renderer = ground.GetComponent<Renderer>();
            renderer.material = new Material(FindDefaultShader());
            renderer.material.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            Object.Destroy(ground.GetComponent<Collider>());
        }

        private static void EnsureSceneView()
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            CombatCameraSettings.Apply(camera);

            if (Object.FindAnyObjectByType<Light>() == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            }
        }

        private static Shader FindDefaultShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            return shader == null ? Shader.Find("Standard") : shader;
        }
    }
}
