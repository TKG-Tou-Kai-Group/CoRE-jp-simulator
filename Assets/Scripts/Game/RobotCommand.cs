using UnityEngine;

namespace CoreSim
{
    /// <summary>1 フレームぶんの操縦指令。人もボットもこの形で機体へ渡す。</summary>
    public struct RobotCommand
    {
        /// <summary>並進。x: 右 (+) / 左 (-)、y: 前 (+) / 後 (-)。各 -1..1。</summary>
        public Vector2 Move;
        /// <summary>旋回。右回り (+)。-1..1。</summary>
        public float Turn;
        public bool Fire;
        public bool Turbo;
        /// <summary>ビルダーの昇降。上 (+) / 下 (-)。-1..1。アタッカーは無視する。</summary>
        public float Lift;
        /// <summary>ビルダーのつかむ / 放す (押している間 true。立ち上がりで切り替える)。</summary>
        public bool Grab;

        public static RobotCommand None => default;
    }

    /// <summary>操縦指令の出どころ。人の入力でも CPU でも、将来 ROS 2 を繋ぐときも同じ口。</summary>
    public interface ICommandSource
    {
        RobotCommand Read();
    }
}
