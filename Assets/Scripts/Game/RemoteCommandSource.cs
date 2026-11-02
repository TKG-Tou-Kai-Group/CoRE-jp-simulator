using CoreSim.Net;
using UnityEngine;

namespace CoreSim
{
    /// <summary>ホスト側で、ネット越しのプレイヤーの操作を機体へ渡す。</summary>
    public sealed class RemoteCommandSource : ICommandSource
    {
        public const double StaleSeconds = 0.5;

        private readonly HostSession m_Host;
        private readonly byte m_Slot;

        public RemoteCommandSource(HostSession host, byte slot)
        {
            m_Host = host;
            m_Slot = slot;
        }

        public RobotCommand Read()
        {
            if (m_Host == null ||
                !m_Host.TryGetInput(m_Slot, Time.realtimeSinceStartupAsDouble, out InputMessage input, out double age) ||
                age > StaleSeconds)
            {
                return RobotCommand.None;   // 届かなくなった機体はその場で止まる
            }
            return new RobotCommand
            {
                Move = Vector2.ClampMagnitude(new Vector2(input.MoveX, input.MoveY), 1f),
                Turn = Mathf.Clamp(input.Turn, -1f, 1f),
                Fire = input.Fire,
                Turbo = input.Turbo,
                Lift = Mathf.Clamp(input.Lift, -1f, 1f),
                Grab = input.Grab,
            };
        }
    }
}
