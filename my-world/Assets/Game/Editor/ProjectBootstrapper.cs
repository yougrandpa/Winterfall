using System.Collections.Generic;
using MyAiWorld.App;
using MyAiWorld.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyAiWorld.EditorTools
{
    public static class ProjectBootstrapper
    {
        private const string GameRoot = "Assets/Game";
        private const string ConfigFolder = "Assets/Game/Config";
        private const string ConfigPath = "Assets/Game/Config/WorldConfig.asset";
        private const string SceneFolder = "Assets/Game/Scenes";
        private const string ScenePath = "Assets/Game/Scenes/Bootstrap.unity";

        [MenuItem("Tools/My AI World/Build Bootstrap Scene")]
        public static void BuildAll()
        {
            EnsureFolder(GameRoot);
            EnsureFolder(ConfigFolder);
            EnsureFolder(SceneFolder);

            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            PlayerSettings.companyName = "My AI World";
            PlayerSettings.productName = "My AI World";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;

            WorldConfigAsset config = AssetDatabase.LoadAssetAtPath<WorldConfigAsset>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<WorldConfigAsset>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            config.width = 64;
            config.height = 64;
            config.seed = 20261020;
            config.npcCount = 20;
            config.startTick = MyAiWorld.Sim.World.GameCalendar.WinterStartTick;
            config.challengeDays = MyAiWorld.Sim.World.GameCalendar.DefaultChallengeDays;
            config.ticksPerSecondAt1x = 2;
            config.speedMultipliers = new[] { 1, 2, 4, 8 };
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bootstrapperObject = new GameObject("GameBootstrap");
            var bootstrapper = bootstrapperObject.AddComponent<GameBootstrap>();
            bootstrapper.ConfigAsset = config;
            EditorSceneManager.SaveScene(scene, ScenePath);

            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool found = false;
            for (int i = 0; i < buildScenes.Count; i++)
            {
                if (buildScenes[i].path == ScenePath)
                {
                    buildScenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("My AI World bootstrap complete. Scene=" + ScenePath + ", Config=" + ConfigPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent.Replace('\\', '/'));
            }

            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
