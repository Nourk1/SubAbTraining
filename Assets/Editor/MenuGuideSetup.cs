using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RealtimePatient.Editor
{
    public static class MenuGuideSetup
    {
        private const string PrefabPath = "Assets/Resources/MenuGuide.prefab";
        private const string ControllerPath = "Assets/Resources/MenuGuide.controller";

        [InitializeOnLoadMethod]
        private static void ScheduleSetup()
        {
            EditorApplication.delayCall += EnsureAssets;
            EditorApplication.playModeStateChanged -= OnPlayStateChanged;
            EditorApplication.playModeStateChanged += OnPlayStateChanged;
        }
        private static void OnPlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += EnsureAssets;
        }

        [MenuItem("Capstone/Create Menu Guide Assets")]
        public static void EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Preserve a replacement prefab or controller supplied by the team later.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/nurse.fbx");
            var idle = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/Animations/Movement/Idle.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == "Idle");
            var wave = AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/Animations/common_people@wave.FBX")
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == "wave");
            if (model == null || idle == null || wave == null)
            {
                Debug.LogWarning("Menu guide assets are still importing. After imports finish, choose Capstone > Create Menu Guide Assets.");
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                controller.AddParameter("Wave", AnimatorControllerParameterType.Trigger);
                var machine = controller.layers[0].stateMachine;
                var resting = machine.AddState("Idle"); resting.motion = idle; resting.speed = 0.8f;
                machine.defaultState = resting;
                var greeting = machine.AddState("Wave"); greeting.motion = wave;
                var enter = resting.AddTransition(greeting); enter.hasExitTime = false; enter.duration = 0.2f;
                enter.AddCondition(AnimatorConditionMode.If, 0, "Wave");
                var leave = greeting.AddTransition(resting); leave.hasExitTime = true; leave.exitTime = 1; leave.duration = 0.2f;
            }
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("MenuGuide");
                SceneManager.MoveGameObjectToScene(root, preview);
                var character = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                character.transform.SetParent(root.transform, false);
                character.transform.localPosition = Vector3.zero;
                character.transform.localRotation = Quaternion.identity;
                var animator = character.GetComponentInChildren<Animator>();
                if (animator == null) animator = character.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
