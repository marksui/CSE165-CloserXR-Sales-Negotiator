using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CloserXR.SalesNegotiator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CloserXR.SalesNegotiator.Editor
{
    public static class SalesAgentStarterBuilder
    {
        private const string MixamoFolder = "Assets/Mixamo";
        private const string AnimationFolder = "Assets/Animations";
        private const string MaterialFolder = "Assets/Materials";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string BaseModelPath = MixamoFolder + "/Ch01_nonPBR.fbx";
        private const string ControllerPath = AnimationFolder + "/SalesAgent.controller";
        private const string PrefabPath = PrefabFolder + "/SalesAgent.prefab";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SkinMaterialPath = MaterialFolder + "/CloserXR_Skin.mat";
        private const string ShirtMaterialPath = MaterialFolder + "/CloserXR_Shirt.mat";
        private const string PantsMaterialPath = MaterialFolder + "/CloserXR_Pants.mat";
        private const string SneakersMaterialPath = MaterialFolder + "/CloserXR_Sneakers.mat";
        private const string EyelashesMaterialPath = MaterialFolder + "/CloserXR_Eyelashes.mat";

        private const string TalkingParameter = "IsTalking";
        private const string WalkingParameter = "IsWalking";
        private const string PointParameter = "Point";
        private const string ArgueParameter = "Argue";
        private const string DismissParameter = "Dismiss";
        private const string CelebrateParameter = "Celebrate";
        private const string SadParameter = "Sad";
        private const string ResetParameter = "Reset";

        [MenuItem("CloserXR/Build Sales Agent Starter")]
        public static void BuildStarter()
        {
            EnsureProjectFolders();
            AssetDatabase.Refresh();

            ConfigureMixamoImporters();
            Dictionary<string, AnimationClip> clips = LoadMixamoClips();
            AnimatorController controller = CreateAnimatorController(clips);
            GameObject prefab = CreatePrefab(controller);
            PlacePrefabInSampleScene(prefab);

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog(
                "CloserXR Sales Agent",
                "Created the SalesAgent animator controller and prefab, then placed it in SampleScene.\n\nPress Play and use the debug keys to test the Mixamo gestures.",
                "OK");
        }

        [MenuItem("CloserXR/Refresh Sales Agent Colors")]
        public static void RefreshSalesAgentColors()
        {
            EnsureProjectFolders();
            Dictionary<string, Material> materials = CreateFallbackMaterials();

            ApplyMaterialsToPrefabAsset(materials);
            ApplyMaterialsToSampleScene(materials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureProjectFolders()
        {
            CreateFolderIfMissing("Assets", "Mixamo");
            CreateFolderIfMissing("Assets", "Animations");
            CreateFolderIfMissing("Assets", "Materials");
            CreateFolderIfMissing("Assets", "Prefabs");
            CreateFolderIfMissing("Assets", "Scripts");
            CreateFolderIfMissing("Assets/Scripts", "SalesAgent");
            CreateFolderIfMissing("Assets", "Editor");
        }

        private static void CreateFolderIfMissing(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void ConfigureMixamoImporters()
        {
            Avatar sourceAvatar = ConfigureBaseModel();

            foreach (string path in FindMixamoFbxPaths())
            {
                if (path.Equals(BaseModelPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ConfigureAnimationModel(path, sourceAvatar);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Avatar ConfigureBaseModel()
        {
            ModelImporter importer = AssetImporter.GetAtPath(BaseModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"Missing base Mixamo character at {BaseModelPath}.");
                return null;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(BaseModelPath)
                .OfType<Avatar>()
                .FirstOrDefault(avatar => avatar.isHuman);
        }

        private static void ConfigureAnimationModel(string path, Avatar sourceAvatar)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            string clipName = GetClipNameFromPath(path);
            ModelImporterClipAnimation[] clipAnimations = importer.defaultClipAnimations;

            if (clipAnimations.Length == 0)
            {
                clipAnimations = importer.clipAnimations;
            }

            for (int i = 0; i < clipAnimations.Length; i++)
            {
                clipAnimations[i].name = clipName;
                clipAnimations[i].loopTime = ShouldLoop(clipName);
                clipAnimations[i].loopPose = ShouldLoop(clipName);
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = sourceAvatar != null
                ? ModelImporterAvatarSetup.CopyFromOther
                : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = sourceAvatar;
            importer.importAnimation = true;
            importer.clipAnimations = clipAnimations;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, AnimationClip> LoadMixamoClips()
        {
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in FindMixamoFbxPaths())
            {
                if (!Path.GetFileNameWithoutExtension(path).Contains("@"))
                {
                    continue;
                }

                string clipName = GetClipNameFromPath(path);
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(IsUsableAnimationClip);

                if (clip != null)
                {
                    clips[clipName] = clip;
                }
            }

            return clips;
        }

        private static AnimatorController CreateAnimatorController(Dictionary<string, AnimationClip> clips)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AddParameterIfMissing(controller, TalkingParameter, AnimatorControllerParameterType.Bool);
            AddParameterIfMissing(controller, WalkingParameter, AnimatorControllerParameterType.Bool);
            AddParameterIfMissing(controller, PointParameter, AnimatorControllerParameterType.Trigger);
            AddParameterIfMissing(controller, ArgueParameter, AnimatorControllerParameterType.Trigger);
            AddParameterIfMissing(controller, DismissParameter, AnimatorControllerParameterType.Trigger);
            AddParameterIfMissing(controller, CelebrateParameter, AnimatorControllerParameterType.Trigger);
            AddParameterIfMissing(controller, SadParameter, AnimatorControllerParameterType.Trigger);
            AddParameterIfMissing(controller, ResetParameter, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimationClip idleClip = PickClip(clips, "Breathing Idle");
            AnimationClip talkingClip = PickClip(clips, "Talking");
            AnimationClip walkingClip = PickClip(clips, "Walking Left Turn");
            AnimationClip pointingClip = PickClip(clips, "Pointing");
            AnimationClip arguingClip = PickClip(clips, "Standing Arguing");
            AnimationClip dismissClip = PickClip(clips, "Dismissing Gesture");
            AnimationClip celebrateClip = PickClip(clips, "Dancing");
            AnimationClip sadClip = PickClip(clips, "Sad Idle");

            AnimatorState idle = AddState(stateMachine, "Idle", idleClip, new Vector3(260f, 40f, 0f));
            AnimatorState talking = AddState(stateMachine, "Talking", talkingClip, new Vector3(540f, 40f, 0f));
            AnimatorState walking = AddState(stateMachine, "Walking / Pacing", walkingClip, new Vector3(540f, 180f, 0f));
            AnimatorState pointing = AddState(stateMachine, "Pointing Close", pointingClip, new Vector3(260f, 300f, 0f));
            AnimatorState arguing = AddState(stateMachine, "Defensive Argument", arguingClip, new Vector3(520f, 300f, 0f));
            AnimatorState dismissing = AddState(stateMachine, "Dismiss Pushback", dismissClip, new Vector3(780f, 300f, 0f));
            AnimatorState celebrating = AddState(stateMachine, "Celebrate Sale", celebrateClip, new Vector3(1040f, 300f, 0f));
            AnimatorState sad = AddState(stateMachine, "Losing The Deal", sadClip, new Vector3(260f, 480f, 0f));

            stateMachine.defaultState = idle;

            AddBoolTransition(idle, talking, TalkingParameter, true);
            AddBoolTransition(talking, idle, TalkingParameter, false);
            AddBoolTransition(idle, walking, WalkingParameter, true);
            AddBoolTransition(talking, walking, WalkingParameter, true);
            AddBoolTransition(walking, idle, WalkingParameter, false);
            AddBoolTransition(sad, talking, TalkingParameter, true);
            AddBoolTransition(sad, walking, WalkingParameter, true);

            AddAnyTriggerTransition(stateMachine, pointing, PointParameter);
            AddAnyTriggerTransition(stateMachine, arguing, ArgueParameter);
            AddAnyTriggerTransition(stateMachine, dismissing, DismissParameter);
            AddAnyTriggerTransition(stateMachine, celebrating, CelebrateParameter);
            AddAnyTriggerTransition(stateMachine, sad, SadParameter);

            AddTimedReturn(pointing, idle, talking);
            AddTimedReturn(arguing, idle, talking);
            AddTimedReturn(dismissing, idle, talking);
            AddTimedReturn(celebrating, idle, talking);
            AddTriggerTransition(sad, idle, ResetParameter);

            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject CreatePrefab(AnimatorController controller)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BaseModelPath);
            if (model == null)
            {
                Debug.LogError($"Could not load Mixamo base model at {BaseModelPath}.");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(model);
            }

            instance.name = "SalesAgent";

            if (PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            SalesAgentAnimator driver = instance.GetComponent<SalesAgentAnimator>();
            if (driver == null)
            {
                driver = instance.AddComponent<SalesAgentAnimator>();
            }

            driver.AssignAnimator(animator);

            SalesDialogueGestureRouter router = instance.GetComponent<SalesDialogueGestureRouter>();
            if (router == null)
            {
                router = instance.AddComponent<SalesDialogueGestureRouter>();
            }

            router.AssignTarget(driver);

            SalesAgentDebugControls debugControls = instance.GetComponent<SalesAgentDebugControls>();
            if (debugControls == null)
            {
                debugControls = instance.AddComponent<SalesAgentDebugControls>();
            }

            debugControls.AssignTarget(driver);

            SalesAgentMaterialStyler materialStyler = instance.GetComponent<SalesAgentMaterialStyler>();
            if (materialStyler == null)
            {
                materialStyler = instance.AddComponent<SalesAgentMaterialStyler>();
            }

            SalesAgentFaceFeatures faceFeatures = instance.GetComponent<SalesAgentFaceFeatures>();
            if (faceFeatures == null)
            {
                faceFeatures = instance.AddComponent<SalesAgentFaceFeatures>();
            }

            ApplyFallbackMaterials(instance, CreateFallbackMaterials());

            if (instance.GetComponent<SpatialRoomMapDemo>() == null)
            {
                instance.AddComponent<SpatialRoomMapDemo>();
            }

            if (instance.GetComponent<SalesAgentVRStatusPanel>() == null)
            {
                instance.AddComponent<SalesAgentVRStatusPanel>();
            }

            if (instance.GetComponent<CloserXRDemoRuntime>() == null)
            {
                instance.AddComponent<CloserXRDemoRuntime>();
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void PlacePrefabInSampleScene(GameObject prefab)
        {
            if (prefab == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) == null)
            {
                return;
            }

            EditorSceneManager.OpenScene(SampleScenePath);

            GameObject existingAgent = GameObject.Find("SalesAgent");
            if (existingAgent != null)
            {
                UnityEngine.Object.DestroyImmediate(existingAgent);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                return;
            }

            instance.name = "SalesAgent";
            instance.transform.position = new Vector3(0f, 0f, 2.5f);
            instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            instance.transform.localScale = Vector3.one;

            EditorSceneManager.MarkSceneDirty(instance.scene);
            EditorSceneManager.SaveScene(instance.scene);
        }

        private static Dictionary<string, Material> CreateFallbackMaterials()
        {
            return new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase)
            {
                ["Body"] = CreateOrUpdateMaterial(SkinMaterialPath, "CloserXR_Skin", new Color(0.78f, 0.58f, 0.44f, 1f), 0.42f),
                ["Shirt"] = CreateOrUpdateMaterial(ShirtMaterialPath, "CloserXR_Shirt", new Color(0.08f, 0.42f, 0.48f, 1f), 0.48f),
                ["Pants"] = CreateOrUpdateMaterial(PantsMaterialPath, "CloserXR_Pants", new Color(0.12f, 0.16f, 0.18f, 1f), 0.35f),
                ["Sneakers"] = CreateOrUpdateMaterial(SneakersMaterialPath, "CloserXR_Sneakers", new Color(0.88f, 0.89f, 0.84f, 1f), 0.55f),
                ["Eyelashes"] = CreateOrUpdateMaterial(EyelashesMaterialPath, "CloserXR_Eyelashes", new Color(0.08f, 0.07f, 0.06f, 1f), 0.2f)
            };
        }

        private static Material CreateOrUpdateMaterial(string path, string materialName, Color color, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Diffuse");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            material.name = materialName;
            material.color = color;
            SetMaterialColor(material, "_BaseColor", color);
            SetMaterialColor(material, "_Color", color);
            SetMaterialColor(material, "_EmissionColor", color);
            SetMaterialFloat(material, "_Smoothness", smoothness);
            SetMaterialFloat(material, "_Glossiness", smoothness);
            SetMaterialFloat(material, "_Metallic", 0f);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ApplyMaterialsToPrefabAsset(Dictionary<string, Material> materials)
        {
            if (!File.Exists(PrefabPath))
            {
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            EnsureMaterialStyler(prefabRoot);
            EnsureFaceFeatures(prefabRoot);
            ApplyFallbackMaterials(prefabRoot, materials);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        private static void ApplyMaterialsToSampleScene(Dictionary<string, Material> materials)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) == null)
            {
                return;
            }

            EditorSceneManager.OpenScene(SampleScenePath);
            GameObject existingAgent = GameObject.Find("SalesAgent");
            if (existingAgent == null)
            {
                return;
            }

            EnsureMaterialStyler(existingAgent);
            EnsureFaceFeatures(existingAgent);
            ApplyFallbackMaterials(existingAgent, materials);
            EditorSceneManager.MarkSceneDirty(existingAgent.scene);
            EditorSceneManager.SaveScene(existingAgent.scene);
        }

        private static void EnsureMaterialStyler(GameObject root)
        {
            if (root != null && root.GetComponent<SalesAgentMaterialStyler>() == null)
            {
                root.AddComponent<SalesAgentMaterialStyler>();
            }
        }

        private static void EnsureFaceFeatures(GameObject root)
        {
            if (root != null && root.GetComponent<SalesAgentFaceFeatures>() == null)
            {
                root.AddComponent<SalesAgentFaceFeatures>();
            }
        }

        private static void ApplyFallbackMaterials(GameObject root, Dictionary<string, Material> materials)
        {
            if (root == null || materials == null)
            {
                return;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material material = PickMaterialForRenderer(renderer.gameObject.name, materials);
                if (material == null)
                {
                    continue;
                }

                Material[] rendererMaterials = renderer.sharedMaterials;
                if (rendererMaterials == null || rendererMaterials.Length == 0)
                {
                    rendererMaterials = new[] { material };
                }

                for (int i = 0; i < rendererMaterials.Length; i++)
                {
                    rendererMaterials[i] = material;
                }

                renderer.sharedMaterials = rendererMaterials;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static Material PickMaterialForRenderer(string rendererName, Dictionary<string, Material> materials)
        {
            if (rendererName.IndexOf("Eyelashes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials["Eyelashes"];
            }

            if (rendererName.IndexOf("Shirt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials["Shirt"];
            }

            if (rendererName.IndexOf("Pants", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials["Pants"];
            }

            if (rendererName.IndexOf("Sneakers", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials["Sneakers"];
            }

            if (rendererName.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return materials["Body"];
            }

            return null;
        }

        private static void SetMaterialColor(Material material, string propertyName, Color color)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static IEnumerable<string> FindMixamoFbxPaths()
        {
            if (!AssetDatabase.IsValidFolder(MixamoFolder))
            {
                return Enumerable.Empty<string>();
            }

            return AssetDatabase.FindAssets("t:Model", new[] { MixamoFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetClipNameFromPath(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            int separatorIndex = fileName.IndexOf('@');
            return separatorIndex >= 0 ? fileName.Substring(separatorIndex + 1) : fileName;
        }

        private static bool ShouldLoop(string clipName)
        {
            return clipName.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0
                || clipName.IndexOf("Talking", StringComparison.OrdinalIgnoreCase) >= 0
                || clipName.IndexOf("Walking", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUsableAnimationClip(AnimationClip clip)
        {
            return clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase);
        }

        private static AnimationClip PickClip(Dictionary<string, AnimationClip> clips, string clipName)
        {
            if (clips.TryGetValue(clipName, out AnimationClip clip))
            {
                return clip;
            }

            Debug.LogWarning($"Missing Mixamo clip '{clipName}'. The Animator state will be created without a motion.");
            return null;
        }

        private static AnimatorState AddState(
            AnimatorStateMachine stateMachine,
            string stateName,
            Motion motion,
            Vector3 position)
        {
            AnimatorState state = stateMachine.AddState(stateName, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void AddParameterIfMissing(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            if (controller.parameters.Any(parameter => parameter.name == parameterName))
            {
                return;
            }

            controller.AddParameter(parameterName, parameterType);
        }

        private static void AddBoolTransition(
            AnimatorState from,
            AnimatorState to,
            string parameterName,
            bool expectedValue)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.12f;
            transition.AddCondition(
                expectedValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f,
                parameterName);
        }

        private static void AddAnyTriggerTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState to,
            string triggerName)
        {
            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.08f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
        }

        private static void AddTriggerTransition(
            AnimatorState from,
            AnimatorState to,
            string triggerName)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.12f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
        }

        private static void AddTimedReturn(AnimatorState from, AnimatorState idle, AnimatorState talking)
        {
            AnimatorStateTransition toTalking = from.AddTransition(talking);
            toTalking.hasExitTime = true;
            toTalking.exitTime = 0.9f;
            toTalking.duration = 0.16f;
            toTalking.AddCondition(AnimatorConditionMode.If, 0f, TalkingParameter);

            AnimatorStateTransition toIdle = from.AddTransition(idle);
            toIdle.hasExitTime = true;
            toIdle.exitTime = 0.92f;
            toIdle.duration = 0.16f;
        }
    }
}
