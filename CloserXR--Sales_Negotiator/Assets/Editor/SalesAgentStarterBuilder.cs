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
        private const string PrefabFolder = "Assets/Prefabs";
        private const string BaseModelPath = MixamoFolder + "/Ch01_nonPBR.fbx";
        private const string ControllerPath = AnimationFolder + "/SalesAgent.controller";
        private const string PrefabPath = PrefabFolder + "/SalesAgent.prefab";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

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

        private static void EnsureProjectFolders()
        {
            CreateFolderIfMissing("Assets", "Mixamo");
            CreateFolderIfMissing("Assets", "Animations");
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
