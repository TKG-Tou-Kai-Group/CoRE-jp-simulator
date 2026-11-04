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
            // タブレットでゲームパッドだけで遊ぶと画面に触れないので、自動で暗くならないようにする
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            AcquireMulticastLock();
            Physics.IgnoreLayerCollision(Tuning.LayerDisc, Tuning.LayerDisc, true);
            Physics.IgnoreLayerCollision(Tuning.LayerMinimapOnly, Tuning.LayerMinimapOnly, true);

            var root = new GameObject("CoRE Simulator");
            root.AddComponent<GameDirector>();
        }

        private static AndroidJavaObject s_MulticastLock;

        /// <summary>
        /// Android は Wi-Fi のブロードキャスト (部屋探しの Discover / Announce) を省電力のために捨てることがある。
        /// MulticastLock を取ると受け取れるようになる。ほかの OS では何もしない。
        /// </summary>
        private static void AcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                s_MulticastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "coresim");
                s_MulticastLock.Call("setReferenceCounted", false);
                s_MulticastLock.Call("acquire");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Net] MulticastLock を取れない (部屋の一覧が出ないかもしれない。IP を直接入れれば参加できる): {e.Message}");
            }
#endif
        }
    }
}
