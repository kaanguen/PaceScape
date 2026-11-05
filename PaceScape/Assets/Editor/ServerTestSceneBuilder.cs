using System;
using PaceScape.RemoteControl;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaceScape.Editor
{
    /// <summary>Creates the standalone server test scene through Unity's asset and scene APIs.</summary>
    public static class ServerTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/ServerTestScene.unity";
        private const string MaterialPath = "Assets/Scenes/ServerTestGround.mat";

        [MenuItem("PaceScape/Server-Testszene erstellen oder öffnen")]
        private static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ServerTestScene] Leave Play mode before creating or opening the test scene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateScene();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[ServerTestScene] Opened the marker-free scene. Enter Play mode, then spawn the runner from the dashboard.");
        }

        private static void CreateScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PlaceHolders/PlaceHolder.prefab");
            if (prefab == null)
            {
                throw new InvalidOperationException("The existing PlaceHolder runner prefab is missing.");
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("The project's URP Lit shader is unavailable.");
            }

            Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (groundMaterial == null)
            {
                groundMaterial = new Material(shader) { name = "Server test ground" };
                groundMaterial.SetColor("_BaseColor", new Color(0.10f, 0.14f, 0.16f));
                AssetDatabase.CreateAsset(groundMaterial, MaterialPath);
            }

            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Server Test Scene - no marker tracking");
                var target = root.AddComponent<ServerTestScene>();
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.06f, 0.08f, 0.11f);
                camera.fieldOfView = 55f;
                camera.farClipPlane = 500f;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.transform.position = new Vector3(0f, 5f, -9f);
                camera.transform.LookAt(Vector3.up * 0.7f);

                var serialized = new SerializedObject(target);
                serialized.FindProperty("_runnerPrefab").objectReferenceValue = prefab;
                serialized.FindProperty("_previewCamera").objectReferenceValue = camera;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var lightObject = new GameObject("Directional Light");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Test ground";
                ground.transform.position = new Vector3(-40f, -0.06f, 50f);
                ground.transform.localScale = new Vector3(240f, 0.1f, 300f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    throw new InvalidOperationException("Unity could not save the server test scene.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded)
                {
                    SceneManager.SetActiveScene(original);
                }
            }
        }
    }
}
