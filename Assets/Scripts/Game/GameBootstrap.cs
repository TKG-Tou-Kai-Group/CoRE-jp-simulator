using UnityEngine;

namespace CoreSim
{
    /// <summary>
    /// シーンに何も置かずに済ませるための入口。空のシーンが読まれたあとに
    /// <see cref="GameDirector"/> を生やし、以降はすべてコードから組み立てる。
    /// </summary>
    /// <remarks>
    /// シーン YAML を手で編集するのは事故りやすいので、Unity_ROS2_Robot_Simulator の
    /// PhysicsRateController と同じ方針でコードから作る。Editor でどのシーンを開いて
    /// Play しても同じゲームが立ち上がる。
    /// </remarks>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindFirstObjectByType<GameDirector>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            Physics.IgnoreLayerCollision(Tuning.LayerDisc, Tuning.LayerDisc, true);
            Physics.IgnoreLayerCollision(Tuning.LayerMinimapOnly, Tuning.LayerMinimapOnly, true);

            var root = new GameObject("CoRE Simulator");
            root.AddComponent<GameDirector>();
        }
    }
}
