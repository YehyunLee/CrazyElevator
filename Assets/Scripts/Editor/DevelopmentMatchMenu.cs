using CrazyElevator.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace CrazyElevator.EditorTools
{
    // Deliberately lives in an Editor folder: shipped builds have no CPU launch option.
    public static class DevelopmentMatchMenu
    {
        const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/Crazy Elevator/Development/Play Against CPU")]
        static void PlayAgainstCpu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != MainScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }

            MenuManager.RequestDevelopmentCpuMatchOnPlay();
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Crazy Elevator/Development/Play Against CPU", true)]
        static bool CanPlayAgainstCpu() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
