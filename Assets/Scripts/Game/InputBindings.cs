using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;

namespace CoreSim
{
    /// <summary>割り当てを変えられる操作。移動と旋回のスティック、スキルの番号キー (1〜9)、ストライダーの Q W O P は固定。</summary>
    public enum InputAct
    {
        MoveForward, MoveBack, MoveLeft, MoveRight,
        TurnLeft, TurnRight,
        Fire,        // 発射 / ビルダーはつかむ・放す
        Turbo,
        Map,         // 地図 (俯瞰) の表示切替
        LiftUp, LiftDown,
        SkillUse,    // 選んでいるスキルを発動
        SkillPrev, SkillNext,
        Pause,
        Guide,       // プレイ中のガイドの ON / OFF
    }

    public enum PadButton
    {
        None, South, East, West, North, LB, RB, LT, RT, DpadUp, DpadDown, DpadLeft, DpadRight, Start, Select, L3, R3,
    }

    /// <summary>
    /// キーボードとゲームパッドの割り当て。操作ごとにキー 2 つ・ボタン 2 つまで。設定に文字列で保存する。
    /// 既定: 矢印 / WASD 移動、A D 旋回、Space 発射、Shift ターボ、V 地図、R F ハンド、H ガイド、Esc メニュー。
    /// パッド: R1 / R2 発射、L2 ターボ、L1 地図、十字↑↓ ハンド、□ スキル発動、十字← → スキル選択、Select ガイド、Start メニュー。
    /// </summary>
    public sealed class InputBindings
    {
        public const int Slots = 2;
        public static InputBindings Current = Defaults();

        private readonly Dictionary<InputAct, Key[]> m_Keys = new Dictionary<InputAct, Key[]>();
        private readonly Dictionary<InputAct, PadButton[]> m_Buttons = new Dictionary<InputAct, PadButton[]>();

        public static readonly InputAct[] All = (InputAct[])Enum.GetValues(typeof(InputAct));

        public static InputBindings Defaults()
        {
            var b = new InputBindings();
            foreach (InputAct a in All) { b.m_Keys[a] = new[] { Key.None, Key.None }; b.m_Buttons[a] = new[] { PadButton.None, PadButton.None }; }
            b.SetKeys(InputAct.MoveForward, Key.UpArrow, Key.W);
            b.SetKeys(InputAct.MoveBack, Key.DownArrow, Key.S);
            b.SetKeys(InputAct.MoveLeft, Key.LeftArrow);
            b.SetKeys(InputAct.MoveRight, Key.RightArrow);
            b.SetKeys(InputAct.TurnLeft, Key.A, Key.Q);
            b.SetKeys(InputAct.TurnRight, Key.D, Key.E);
            b.SetKeys(InputAct.Fire, Key.Space, Key.Enter);
            b.SetKeys(InputAct.Turbo, Key.LeftShift, Key.RightShift);
            b.SetKeys(InputAct.Map, Key.V);
            b.SetKeys(InputAct.LiftUp, Key.R, Key.PageUp);
            b.SetKeys(InputAct.LiftDown, Key.F, Key.PageDown);
            b.SetKeys(InputAct.Pause, Key.Escape);
            b.SetKeys(InputAct.Guide, Key.H);
            b.SetButtons(InputAct.Fire, PadButton.RB, PadButton.RT);
            b.SetButtons(InputAct.Turbo, PadButton.LT);
            b.SetButtons(InputAct.Map, PadButton.LB);
            b.SetButtons(InputAct.LiftUp, PadButton.DpadUp);
            b.SetButtons(InputAct.LiftDown, PadButton.DpadDown);
            b.SetButtons(InputAct.SkillUse, PadButton.West);
            b.SetButtons(InputAct.SkillPrev, PadButton.DpadLeft);
            b.SetButtons(InputAct.SkillNext, PadButton.DpadRight);
            b.SetButtons(InputAct.Pause, PadButton.Start);
            b.SetButtons(InputAct.Guide, PadButton.Select);
            return b;
        }

        public void SetKeys(InputAct a, Key k1, Key k2 = Key.None) => m_Keys[a] = new[] { k1, k2 };
        public void SetButtons(InputAct a, PadButton b1, PadButton b2 = PadButton.None) => m_Buttons[a] = new[] { b1, b2 };
        public Key GetKey(InputAct a, int slot) => m_Keys[a][slot];
        public PadButton GetButton(InputAct a, int slot) => m_Buttons[a][slot];
        public void SetKey(InputAct a, int slot, Key k) { m_Keys[a][slot] = k; }
        public void SetButton(InputAct a, int slot, PadButton b) { m_Buttons[a][slot] = b; }

        /// <summary>キーボードでその操作が押されているか (キーボードが無ければ false)。</summary>
        public bool KeyHeld(InputAct a, Keyboard kb)
        {
            if (kb == null) return false;
            foreach (Key k in m_Keys[a]) if (k != Key.None && kb[k].isPressed) return true;
            return false;
        }

        public bool PadHeld(InputAct a, Gamepad pad)
        {
            if (pad == null) return false;
            foreach (PadButton b in m_Buttons[a]) if (b != PadButton.None && Pressed(b, pad)) return true;
            return false;
        }

        public static bool Pressed(PadButton b, Gamepad pad) => b switch
        {
            PadButton.South => pad.buttonSouth.isPressed,
            PadButton.East => pad.buttonEast.isPressed,
            PadButton.West => pad.buttonWest.isPressed,
            PadButton.North => pad.buttonNorth.isPressed,
            PadButton.LB => pad.leftShoulder.isPressed,
            PadButton.RB => pad.rightShoulder.isPressed,
            PadButton.LT => pad.leftTrigger.ReadValue() > 0.5f,
            PadButton.RT => pad.rightTrigger.ReadValue() > 0.5f,
            PadButton.DpadUp => pad.dpad.up.isPressed,
            PadButton.DpadDown => pad.dpad.down.isPressed,
            PadButton.DpadLeft => pad.dpad.left.isPressed,
            PadButton.DpadRight => pad.dpad.right.isPressed,
            PadButton.Start => pad.startButton.isPressed,
            PadButton.Select => pad.selectButton.isPressed,
            PadButton.L3 => pad.leftStickButton.isPressed,
            PadButton.R3 => pad.rightStickButton.isPressed,
            _ => false,
        };

        /// <summary>今フレーム押されたパッドのボタン (割り当て画面で拾う)。無ければ None。</summary>
        public static PadButton AnyPressed(Gamepad pad)
        {
            if (pad == null) return PadButton.None;
            foreach (PadButton b in (PadButton[])Enum.GetValues(typeof(PadButton)))
            {
                if (b != PadButton.None && Pressed(b, pad)) return b;
            }
            return PadButton.None;
        }

        public static string ActName(InputAct a) => a switch
        {
            InputAct.MoveForward => "前進", InputAct.MoveBack => "後退", InputAct.MoveLeft => "左へ移動", InputAct.MoveRight => "右へ移動",
            InputAct.TurnLeft => "左旋回", InputAct.TurnRight => "右旋回",
            InputAct.Fire => "発射 / つかむ・放す", InputAct.Turbo => "ターボ", InputAct.Map => "地図 (俯瞰) の表示切替",
            InputAct.LiftUp => "ハンドを上げる", InputAct.LiftDown => "ハンドを下げる",
            InputAct.SkillUse => "スキルを発動", InputAct.SkillPrev => "スキルを 1 つ前へ", InputAct.SkillNext => "スキルを 1 つ次へ",
            InputAct.Pause => "一時停止 / メニュー", _ => "ガイドの ON / OFF",
        };

        /// <summary>移動と旋回はパッドではスティック固定なので、ボタンの割り当ては無い。</summary>
        public static bool PadAssignable(InputAct a) => a != InputAct.MoveForward && a != InputAct.MoveBack && a != InputAct.MoveLeft && a != InputAct.MoveRight &&
                                                        a != InputAct.TurnLeft && a != InputAct.TurnRight;

        public static string KeyName(Key k) => k switch
        {
            Key.None => "-", Key.Space => "Space", Key.Enter => "Enter", Key.Escape => "Esc", Key.LeftShift => "左 Shift", Key.RightShift => "右 Shift",
            Key.LeftCtrl => "左 Ctrl", Key.RightCtrl => "右 Ctrl", Key.LeftAlt => "左 Alt", Key.RightAlt => "右 Alt", Key.Tab => "Tab", Key.Backspace => "BS",
            Key.UpArrow => "↑", Key.DownArrow => "↓", Key.LeftArrow => "←", Key.RightArrow => "→", Key.PageUp => "PgUp", Key.PageDown => "PgDn",
            Key.Home => "Home", Key.End => "End", Key.Insert => "Ins", Key.Delete => "Del", Key.CapsLock => "CapsLock",
            _ => k.ToString().Replace("Digit", "").Replace("Numpad", "テンキー "),
        };

        /// <summary>PlayStation 風の名前で表示する (× ○ □ △、L1 R1 L2 R2)。</summary>
        public static string ButtonName(PadButton b) => b switch
        {
            PadButton.None => "-", PadButton.South => "× (A)", PadButton.East => "○ (B)", PadButton.West => "□ (X)", PadButton.North => "△ (Y)",
            PadButton.LB => "L1", PadButton.RB => "R1", PadButton.LT => "L2", PadButton.RT => "R2",
            PadButton.DpadUp => "十字↑", PadButton.DpadDown => "十字↓", PadButton.DpadLeft => "十字←", PadButton.DpadRight => "十字→",
            PadButton.Start => "Start", PadButton.Select => "Select", PadButton.L3 => "L3", _ => "R3",
        };

        /// <summary>割り当ての一覧を短い文字列に (設定に保存する)。</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (InputAct a in All)
            {
                sb.Append(a).Append('=').Append(m_Keys[a][0]).Append(',').Append(m_Keys[a][1]).Append('|').Append(m_Buttons[a][0]).Append(',').Append(m_Buttons[a][1]).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>保存した文字列から戻す。読めない部分は既定のまま。</summary>
        public static InputBindings Parse(string text)
        {
            InputBindings b = Defaults();
            if (string.IsNullOrEmpty(text)) return b;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                if (!Enum.TryParse(entry.Substring(0, eq), out InputAct act)) continue;
                string[] parts = entry.Substring(eq + 1).Split('|');
                if (parts.Length != 2) continue;
                string[] keys = parts[0].Split(',');
                string[] buttons = parts[1].Split(',');
                for (int i = 0; i < Slots && i < keys.Length; i++) if (Enum.TryParse(keys[i], out Key k)) b.m_Keys[act][i] = k;
                for (int i = 0; i < Slots && i < buttons.Length; i++) if (Enum.TryParse(buttons[i], out PadButton pb)) b.m_Buttons[act][i] = pb;
            }
            return b;
        }
    }
}
