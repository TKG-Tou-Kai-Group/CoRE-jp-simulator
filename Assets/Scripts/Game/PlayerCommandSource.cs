using UnityEngine;
using UnityEngine.InputSystem;

namespace CoreSim
{
    /// <summary>
    /// 人の入力。キーボード、ゲームパッド、画面上の仮想パッド (Input System の OnScreen 控件
    /// は仮想 Gamepad として現れる) をまとめて読む。
    /// </summary>
    /// <remarks>
    /// 割り当ては CoRE-jp-Unity-ROS2-packages の tools/control.html に合わせてある:
    /// 矢印 = 移動、A/D = 旋回、Space = 射撃、Shift = ターボ、V = 視点、Esc = メニュー。
    /// それに加えて WASD でも移動できる (W/S 前後、A/D は旋回のまま)。
    /// ゲームパッドは左スティック = 移動、右スティック = 旋回、RB/RT = 射撃、
    /// LB = ターボ、Y = 視点、Start = メニュー。
    /// </remarks>
    public sealed class PlayerCommandSource : ICommandSource
    {
        /// <summary>0 = キーボード + 1 台目 (と仮想パッド)、1 = 2 台目のゲームパッド。</summary>
        public int PlayerIndex;
        public float Deadzone = 0.15f;

        private bool m_ViewHeld;
        private bool m_PauseHeld;
        private bool m_SkillHeld;
        private bool m_DpadHeld;
        /// <summary>ゲームパッドの十字キーで選んでいるスキル (0..8)。</summary>
        public int SelectedSkill;

        /// <summary>今フレーム発動を指示されたスキルの番号 (0..8)。無ければ -1。描画フレームごとに 1 回だけ呼ぶこと。</summary>
        public int PollSkill()
        {
            int pressed = -1;
            Keyboard kb = Keyboard.current;
            if (kb != null && PlayerIndex == 0)
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
            bool dpad = false, activate = false;
            foreach (Gamepad pad in MyPads())
            {
                if (pad.dpad.left.isPressed) { dpad = true; if (!m_DpadHeld) SelectedSkill = (SelectedSkill + 8) % 9; }
                else if (pad.dpad.right.isPressed) { dpad = true; if (!m_DpadHeld) SelectedSkill = (SelectedSkill + 1) % 9; }
                activate |= pad.buttonWest.isPressed;
            }
            m_DpadHeld = dpad;
            if (activate && pressed < 0) pressed = SelectedSkill;
            bool held = pressed >= 0;
            int result = held && !m_SkillHeld ? pressed : -1;
            m_SkillHeld = held;
            return result;
        }

        /// <summary>視点切替ボタンが今フレーム押されたか。描画フレームごとに 1 回だけ呼ぶこと。</summary>
        /// <remarks>
        /// Read() は FixedUpdate から呼ばれ、描画フレームより多く走ることがある。立ち上がりを
        /// そこで作ると Update 側が取りこぼすので、押した瞬間の判定は Update から直接聞く。
        /// </remarks>
        public bool PollToggleView()
        {
            bool held = Keyboard.current != null && PlayerIndex == 0 && Keyboard.current.vKey.isPressed;
            foreach (Gamepad pad in MyPads())
            {
                held |= pad.buttonNorth.isPressed;
            }
            bool edge = held && !m_ViewHeld;
            m_ViewHeld = held;
            return edge;
        }

        /// <summary>一時停止 (Esc / Start) が今フレーム押されたか。描画フレームごとに 1 回だけ呼ぶこと。</summary>
        public bool PollPause()
        {
            bool held = Keyboard.current != null && PlayerIndex == 0 && Keyboard.current.escapeKey.isPressed;
            foreach (Gamepad pad in MyPads())
            {
                held |= pad.startButton.isPressed;
            }
            bool edge = held && !m_PauseHeld;
            m_PauseHeld = held;
            return edge;
        }

        /// <summary>このプレイヤーが読むゲームパッド。プレイヤー 0 は 2 台目以外すべて (仮想パッドを含む)。</summary>
        private System.Collections.Generic.IEnumerable<Gamepad> MyPads()
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

            if (PlayerIndex == 0)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    float x = 0f, y = 0f, turn = 0f;
                    if (kb.upArrowKey.isPressed || kb.wKey.isPressed) y += 1f;
                    if (kb.downArrowKey.isPressed || kb.sKey.isPressed) y -= 1f;
                    if (kb.rightArrowKey.isPressed) x += 1f;
                    if (kb.leftArrowKey.isPressed) x -= 1f;
                    if (kb.dKey.isPressed || kb.eKey.isPressed) turn += 1f;
                    if (kb.aKey.isPressed || kb.qKey.isPressed) turn -= 1f;
                    cmd.Move += new Vector2(x, y);
                    cmd.Turn += turn;
                    cmd.Fire |= kb.spaceKey.isPressed || kb.enterKey.isPressed;
                    cmd.Turbo |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                }
            }

            // ゲームパッド。プレイヤー 0 は 1 台目以外 (仮想パッドを含む) も足し合わせる。
            foreach (Gamepad pad in MyPads())
            {
                cmd.Move += ApplyDeadzone(pad.leftStick.ReadValue());
                cmd.Turn += ApplyDeadzone(pad.rightStick.ReadValue()).x;
                cmd.Fire |= pad.rightShoulder.isPressed || pad.rightTrigger.ReadValue() > 0.5f || pad.buttonSouth.isPressed;
                cmd.Turbo |= pad.leftShoulder.isPressed || pad.leftTrigger.ReadValue() > 0.5f;
            }

            cmd.Move = Vector2.ClampMagnitude(cmd.Move, 1f);
            cmd.Turn = Mathf.Clamp(cmd.Turn, -1f, 1f);
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
