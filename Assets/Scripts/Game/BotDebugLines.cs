using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;

namespace CoreSim
{
    /// <summary>F9 のデバッグ表示: CPU 全機の経路 (自機 → 経路の角 → 目標) を床の少し上に線で描く。</summary>
    public static class BotDebugLines
    {
        private static readonly Dictionary<RobotAgent, LineRenderer> s_Lines = new Dictionary<RobotAgent, LineRenderer>();
        private static readonly List<Vector3> s_Points = new List<Vector3>();
        private static readonly List<RobotAgent> s_Dead = new List<RobotAgent>();
        private static Transform s_Root;
        private static int s_Frame = -1;

        public static void Refresh(IReadOnlyList<RobotAgent> robots, bool show)
        {
            if (s_Frame == Time.frameCount) return;   // 画面分割でも 1 回
            s_Frame = Time.frameCount;
            if (!show)
            {
                if (s_Lines.Count > 0) Clear();
                return;
            }
            if (s_Root == null) s_Root = new GameObject("BotDebugLines").transform;
            s_Dead.Clear();
            foreach (RobotAgent r in s_Lines.Keys) if (r == null) s_Dead.Add(r);
            foreach (RobotAgent r in s_Dead) s_Lines.Remove(r);
            foreach (RobotAgent r in robots)
            {
                if (r == null || !(r.Source is IBotStatus bot)) continue;
                if (!s_Lines.TryGetValue(r, out LineRenderer line) || line == null)
                {
                    line = Create(r);
                    s_Lines[r] = line;
                }
                bot.DebugPath(s_Points);
                line.positionCount = s_Points.Count;
                for (int i = 0; i < s_Points.Count; i++) line.SetPosition(i, s_Points[i] + Vector3.up * 0.3f);
            }
        }

        private static LineRenderer Create(RobotAgent r)
        {
            var go = new GameObject("path_" + r.name);
            go.transform.SetParent(s_Root, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.06f;
            Color c = r.Team == Team.Blue ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.4f, 0.4f);
            if (r.IsBuilder) c = Color.Lerp(c, Color.yellow, 0.5f);
            line.sharedMaterial = MaterialLibrary.Unlit(c);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        public static void Clear()
        {
            foreach (LineRenderer l in s_Lines.Values) if (l != null) Object.Destroy(l.gameObject);
            s_Lines.Clear();
        }
    }
}
