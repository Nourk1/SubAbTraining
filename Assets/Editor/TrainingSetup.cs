using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RealtimePatient.Editor
{
    public static class TrainingSetup
    {
        [MenuItem("Capstone/Add Starter Training Flow")]
        private static void AddFlow()
        {
            if (Object.FindFirstObjectByType<TrainingFlow>() != null) return;
            if (Object.FindFirstObjectByType<OpenAIRealtimeClient>() == null)
            {
                EditorUtility.DisplayDialog("Open PatientScene first",
                    "Open Assets/Scenes/PatientScene.unity, then run this command again.", "OK");
                return;
            }
            var root = new GameObject("Starter Training Flow");
            Undo.RegisterCreatedObjectUndo(root, "Add Starter Training Flow");
            Undo.AddComponent<TrainingFlow>(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
        }
    }
}
