using UnityEngine;
using UnityEngine.InputSystem;

namespace CoreSim
{
    /// <summary>
    /// 人の入力。キーボード、ゲームパッド、画面上の仮想パッド (Input System の OnScreen 控件
    /// は仮想 Gamepad として現れる) をまとめて読む。キーとボタンの割り当ては <see cref="InputBindings.Current"/>
    /// (設定画面「操作の割り当て」で変えられる)。スティックとスキルの番号キー (1〜9) は固定。
    /// </summary>
    public sealed class PlayerCommandSource : ICommandSource
    {
        /// <summary>0 = キーボード + 1 台目 (と仮想パッド)、1 = 2 台目のゲームパッド。</summary>
        public int PlayerIndex;
        public float Deadzone = 0.15f;

        private bool m_ViewHeld;
        private bool m_PauseHeld;
        private bool m_SkillHeld;
        private bool m_DpadHeld;
        /// <summary>ゲームパッドで選んでいるスキル (0..8)。</summary>
        public int SelectedSkill;

        private static InputBindings B => InputBindings.Current;
        private Keyboard Kb => PlayerIndex == 0 ? Keyboard.current : null;

        /// <summary>今フレーム発動を指示されたスキルの番号 (0..8)。無ければ -1。描画フレームごとに 1 回だけ呼ぶこと。</summary>
        public int PollSkill()
        {
            int pressed = -1;
            Keyboard kb = Kb;
            if (kb != null)
            {
                if (kb.digit1Key.isPressed) pressed = 0;
                else if (kb.digit2Key.isPressed) pressed = 1;
                else if (kb.digit3Key.isPressed) pressed = 2;
                else if (kb.digit4Key.isPressed) pressed = 3;
                else if (kb.digit5Key.isPressed) pressed = 4;
                else if (kb.digit6Key.isPressed) pressed = 5;
                else if (kb.digit7Key.isPressed) pressed = 6;
                else if (kb.digit8Key.isPressed) pressed = 7;
                else if (kb.digit9Key.isPressed) pressed = 8;
            }
            bool dpad = false, activate = B.KeyHeld(InputAct.SkillUse, kb);
            foreach (Gamepad pad in MyPads())
            {
                if (B.PadHeld(InputAct.SkillPrev, pad)) { dpad = true; if (!m_DpadHeld) SelectedSkill = (SelectedSkill + 8) % 9; }
                else if (B.PadHeld(InputAct.SkillNext, pad)) { dpad = true; if (!m_DpadHeld) SelectedSkill = (SelectedSkill + 1) % 9; }
                activate |= B.PadHeld(InputAct.SkillUse, pad);
            }
            m_DpadHeld = dpad;
            if (activate && pressed < 0) pressed = SelectedSkill;
            bool held = pressed >= 0;
            int result = held && !m_SkillHeld ? pressed : -1;
            m_SkillHeld = held;
            return result;
        }

        /// <summary>視点 (地図) 切替が今フレーム押されたか。描画フレームごとに 1 回だけ呼ぶこと。</summary>
        /// <remarks>
        /// Read() は FixedUpdate から呼ばれ、描画フレームより多く走ることがある。立ち上がりを
        /// そこで作ると Update 側が取りこぼすので、押した瞬間の判定は Update から直接聞く。
        /// </remarks>
        public bool PollToggleView()
        {
            bool held = B.KeyHeld(InputAct.Map, Kb);
            foreach (Gamepad pad in MyPads()) held |= B.PadHeld(InputAct.Map, pad);
            bool edge = held && !m_ViewHeld;
            m_ViewHeld = held;
            return edge;
        }

        /// <summary>一時停止 (Esc / Start) が今フレーム押されたか。描画フレームごとに 1 回だけ呼ぶこと。</summary>
        public bool PollPause()
        {
            bool held = B.KeyHeld(InputAct.Pause, Kb);
            foreach (Gamepad pad in MyPads()) held |= B.PadHeld(InputAct.Pause, pad);
            bool edge = held && !m_PauseHeld;
            m_PauseHeld = held;
            return edge;
        }

        /// <summary>このプレイヤーが読むゲームパッド。プレイヤー 0 は 2 台目以外すべて (仮想パッドを含む)。</summary>
        public System.Collections.Generic.IEnumerable<Gamepad> MyPads()
        {
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
            {
                bool mine = PlayerIndex == 0 ? (i != 1 || pads.Count == 1) : i == PlayerIndex;
                if (mine)
                {
                    yield return pads[i];
                }
            }
        }

        public PlayerCommandSource(int playerIndex)
        {
            PlayerIndex = playerIndex;
        }

        public RobotCommand Read()
        {
            var cmd = new RobotCommand();
            Keyboard kb = Kb;
            if (kb != null)
            {
                float x = 0f, y = 0f, turn = 0f;
                if (B.KeyHeld(InputAct.MoveForward, kb)) y += 1f;
                if (B.KeyHeld(InputAct.MoveBack, kb)) y -= 1f;
                if (B.KeyHeld(InputAct.MoveRight, kb)) x += 1f;
                if (B.KeyHeld(InputAct.MoveLeft, kb)) x -= 1f;
                if (B.KeyHeld(InputAct.TurnRight, kb)) turn += 1f;
                if (B.KeyHeld(InputAct.TurnLeft, kb)) turn -= 1f;
                cmd.Move += new Vector2(x, y);
                cmd.Turn += turn;
                cmd.Fire |= B.KeyHeld(InputAct.Fire, kb);
                cmd.Turbo |= B.KeyHeld(InputAct.Turbo, kb);
                if (B.KeyHeld(InputAct.LiftUp, kb)) cmd.Lift += 1f;
                if (B.KeyHeld(InputAct.LiftDown, kb)) cmd.Lift -= 1f;
            }

            // ゲームパッド。スティックは固定 (左 = 移動、右 = 旋回と、ビルダーのハンド上下)
            foreach (Gamepad pad in MyPads())
            {
                cmd.Move += ApplyDeadzone(pad.leftStick.ReadValue());
                Vector2 right = ApplyDeadzone(pad.rightStick.ReadValue());
                cmd.Turn += right.x;
                cmd.Lift += right.y;
                if (B.PadHeld(InputAct.LiftUp, pad)) cmd.Lift += 1f;
                if (B.PadHeld(InputAct.LiftDown, pad)) cmd.Lift -= 1f;
                cmd.Fire |= B.PadHeld(InputAct.Fire, pad);
                cmd.Turbo |= B.PadHeld(InputAct.Turbo, pad);
            }

            cmd.Move = Vector2.ClampMagnitude(cmd.Move, 1f);
            cmd.Turn = Mathf.Clamp(cmd.Turn, -1f, 1f);
            cmd.Lift = Mathf.Clamp(cmd.Lift, -1f, 1f);
            cmd.Grab = cmd.Fire;   // ビルダーは射撃ボタンでつかむ / 放す
            return cmd;
        }

        private Vector2 ApplyDeadzone(Vector2 v)
        {
            float m = v.magnitude;
            if (m < Deadzone)
            {
                return Vector2.zero;
            }
            return v.normalized * Mathf.Clamp01((m - Deadzone) / (1f - Deadzone));
        }
    }
}
