using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace CoreSim
{
    public enum TutorialKind { None, Attacker, Builder }

    /// <summary>
    /// 機体ごとのチュートリアル (docs/UX_PLAN.md 5 章)。大会ルールの試合 (相手は止まったまま) の上に
    /// 「やること 1 行 + 達成したら次へ」を重ねる。達成はゲーム内の状態で判定する。とばすことも、やめることもできる。
    /// </summary>
    public sealed class TutorialRunner : MonoBehaviour
    {
        private sealed class Step
        {
            public string Text;
            public string Hint;
            public System.Func<bool> Done;
            public System.Action Enter;
            public float MinSeconds;   // 入ってからこの秒数は達成にしない (説明を読む時間)
        }

        private GameDirector m_Director;
        private TutorialKind m_Kind;
        private RobotAgent m_Self;
        private RobotAgent m_Target;
        private HudView m_Hud;
        private readonly List<Step> m_Steps = new List<Step>();
        private int m_Index = -1;
        private float m_EnteredAt;
        private bool m_Finished;
        private GameObject m_Marker;
        private Text m_Title, m_Text, m_Hint;
        private RectTransform m_Root, m_Buttons, m_DoneButtons;

        public static TutorialRunner Create(GameDirector director, TutorialKind kind, RobotAgent self, RobotAgent target, HudView hud, Transform canvas)
        {
            var go = new GameObject("Tutorial");
            var t = go.AddComponent<TutorialRunner>();
            t.m_Director = director;
            t.m_Kind = kind;
            t.m_Self = self;
            t.m_Target = target;
            t.m_Hud = hud;
            t.BuildUi(canvas);
            if (kind == TutorialKind.Builder) t.DefineBuilder(); else t.DefineAttacker();
            t.Advance();
            return t;
        }

        private void BuildUi(Transform canvas)
        {
            m_Root = UiKit.Rect(canvas, "TutorialPanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 150f));
            UiKit.Round(UiKit.Image(m_Root, "bg", new Color(0.05f, 0.1f, 0.2f, 0.85f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero));
            m_Title = UiKit.Label(UiKit.Rect(m_Root, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -22f), new Vector2(0f, 34f)), "Text", "", 18, new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleLeft);
            m_Title.rectTransform.offsetMin = new Vector2(20f, m_Title.rectTransform.offsetMin.y);
            m_Text = UiKit.Label(UiKit.Rect(m_Root, "Text", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -72f), new Vector2(0f, 64f)), "Text", "", 22, Color.white, TextAnchor.UpperLeft);
            m_Text.rectTransform.offsetMin = new Vector2(20f, m_Text.rectTransform.offsetMin.y);
            m_Text.rectTransform.offsetMax = new Vector2(-320f, m_Text.rectTransform.offsetMax.y);
            m_Text.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_Hint = UiKit.Label(UiKit.Rect(m_Root, "Hint", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 20f), new Vector2(0f, 30f)), "Text", "", 15, new Color(0.8f, 0.85f, 0.95f), TextAnchor.MiddleLeft);
            m_Hint.rectTransform.offsetMin = new Vector2(20f, m_Hint.rectTransform.offsetMin.y);
            m_Hint.rectTransform.offsetMax = new Vector2(-320f, m_Hint.rectTransform.offsetMax.y);
            m_Hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            // 見出しの行の右側 (ボタンの列にかからない範囲) に「次の印まで N m」
            RectTransform routeRow = UiKit.Rect(m_Root, "Route", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -22f), new Vector2(0f, 34f));
            routeRow.offsetMin = new Vector2(20f, routeRow.offsetMin.y);
            routeRow.offsetMax = new Vector2(-320f, routeRow.offsetMax.y);
            m_RouteInfo = UiKit.Label(routeRow, "Text", "", 16, new Color(1f, 0.9f, 0.5f), TextAnchor.MiddleRight);

            m_Buttons = UiKit.Rect(m_Root, "Buttons", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(280f, 120f));
            var bl = m_Buttons.gameObject.AddComponent<VerticalLayoutGroup>();
            bl.spacing = 8f;
            bl.childControlWidth = false;
            bl.childControlHeight = false;
            bl.childForceExpandWidth = false;
            bl.childAlignment = TextAnchor.MiddleCenter;
            UiKit.Button(m_Buttons, "この手順をとばす", new Vector2(260f, 48f), () => Advance(), new Color(0.3f, 0.35f, 0.45f, 0.95f), 18);
            UiKit.Button(m_Buttons, "チュートリアルをやめる", new Vector2(260f, 48f), () => m_Director.BackToMenu(), new Color(0.4f, 0.25f, 0.25f, 0.95f), 18);

            m_DoneButtons = UiKit.Rect(m_Root, "Done", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(280f, 120f));
            var dl = m_DoneButtons.gameObject.AddComponent<VerticalLayoutGroup>();
            dl.spacing = 8f;
            dl.childControlWidth = false;
            dl.childControlHeight = false;
            dl.childForceExpandWidth = false;
            dl.childAlignment = TextAnchor.MiddleCenter;
            UiKit.Button(m_DoneButtons, "この機体で試合へ", new Vector2(260f, 48f), () => m_Director.FinishTutorialAndPlay(), new Color(0.85f, 0.35f, 0.2f, 0.95f), 18);
            UiKit.Button(m_DoneButtons, "メニューへ", new Vector2(260f, 48f), () => m_Director.BackToMenu(), new Color(0.3f, 0.35f, 0.45f, 0.95f), 18);
            m_DoneButtons.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ 共通の部品

        private TournamentRound Round => m_Director.Tournament != null ? m_Director.Tournament.Round : null;
        private Side MySide => TournamentController.SideOf(m_Self.Team);
        private string MySideName => TournamentController.SideName(MySide);
        private TournamentRobot Me => Round?.Find(m_Self.Index);
        private TournamentAlliance Mine => Round?.Of(MySide);
        private TournamentAlliance Enemy => Round?.OpponentOf(MySide);
        private Field2027Data Field => ArenaBuilder.Field2027;
        private PropRegistry Props => Field?.Props;

        // ------------------------------------------------------------------ 黄色い印での誘導
        // 目的地までの道を NavMesh で引き、角と 5 m ごとの中継点に印を立てる。手前の印から順に消していく

        private readonly List<Vector3> m_Route = new List<Vector3>();
        private readonly List<GameObject> m_RouteMarkers = new List<GameObject>();
        private int m_RouteIndex;
        private float m_ReachWithin = 1.3f;
        private LineRenderer m_RouteLine;     // 道そのものを床の上の線で示す (一人称では先の印が構造物に隠れるため)
        private Text m_RouteInfo;             // 「次の印まで N m ↑」

        private void StartRoute(Vector3 destination, float reachWithin = 1.3f, bool allowSupply = false)
        {
            ClearRoute();
            m_ReachWithin = reachWithin;
            Vector3 from = m_Self.transform.position;
            int mask = allowSupply ? NavMesh.AllAreas : NavMesh.AllAreas & ~(1 << NavigationBuilder.SupplyArea);
            var path = new NavMeshPath();
            bool ok = NavMesh.SamplePosition(from, out NavMeshHit a, 2f, NavMesh.AllAreas) &&
                      NavMesh.SamplePosition(destination, out NavMeshHit b, 2.5f, mask) &&
                      NavMesh.CalculatePath(a.position, b.position, mask, path) && path.corners.Length >= 2;
            Vector3 prev = from;
            if (ok)
            {
                for (int i = 1; i < path.corners.Length; i++)
                {
                    Vector3 c = path.corners[i];
                    float d = Vector3.Distance(prev, c);
                    // 長い直線は 5 m ごとに区切る (次の印が見える距離に)
                    int pieces = Mathf.Max(1, Mathf.CeilToInt(d / 5f));
                    for (int k = 1; k <= pieces; k++)
                    {
                        Vector3 pnt = Vector3.Lerp(prev, c, (float)k / pieces);
                        if (i == path.corners.Length - 1 && k == pieces) break;   // 最後は目的地そのもの
                        if (Vector3.Distance(pnt, m_Route.Count > 0 ? m_Route[m_Route.Count - 1] : from) > 1.5f) m_Route.Add(pnt);
                    }
                    prev = c;
                }
            }
            Vector3 dest = destination;
            dest.y = 0f;
            if (m_Route.Count == 0 || Vector3.Distance(m_Route[m_Route.Count - 1], dest) > 1.5f) m_Route.Add(dest);
            m_RouteIndex = 0;
            for (int i = 0; i < m_Route.Count; i++) m_RouteMarkers.Add(MakeMarker(m_Route[i], i == 0, i == m_Route.Count - 1));
            RedrawRouteLine();
        }

        private void RedrawRouteLine()
        {
            if (m_RouteLine == null)
            {
                var go = new GameObject("TutorialRouteLine");
                m_RouteLine = go.AddComponent<LineRenderer>();
                m_RouteLine.useWorldSpace = true;
                m_RouteLine.widthMultiplier = 0.16f;
                m_RouteLine.sharedMaterial = MaterialLibrary.Unlit(new Color(1f, 0.85f, 0.2f));
                m_RouteLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_RouteLine.receiveShadows = false;
            }
            int n = m_Route.Count - m_RouteIndex;
            if (n <= 0) { m_RouteLine.positionCount = 0; return; }
            m_RouteLine.positionCount = n + 1;
            Vector3 p0 = m_Self.transform.position;
            m_RouteLine.SetPosition(0, new Vector3(p0.x, 0.12f, p0.z));
            for (int i = 0; i < n; i++)
            {
                Vector3 p = m_Route[m_RouteIndex + i];
                m_RouteLine.SetPosition(i + 1, new Vector3(p.x, 0.12f, p.z));
            }
        }

        private GameObject MakeMarker(Vector3 at, bool current, bool goal)
        {
            // 一人称 (カメラの高さ 0.3 m) から 5〜10 m 先でも見えるよう、どの印も背を高くし、今の印は太く明るくする
            var go = new GameObject("TutorialMarker");
            Material mat = MaterialLibrary.Unlit(current ? new Color(1f, 0.85f, 0.2f) : new Color(0.85f, 0.7f, 0.2f));
            float size = current || goal ? 1.2f : 0.9f;
            Visuals.Create("ring", PrimitiveType.Cylinder, go.transform, new Vector3(at.x, 0.02f, at.z), Quaternion.identity, new Vector3(size, 0.01f, size), mat);
            Visuals.Create("post", PrimitiveType.Cylinder, go.transform, new Vector3(at.x, 1.4f, at.z), Quaternion.identity, new Vector3(current ? 0.12f : 0.06f, 1.4f, current ? 0.12f : 0.06f), mat);
            Visuals.Create("flag", PrimitiveType.Cube, go.transform, new Vector3(at.x, 2.9f, at.z), Quaternion.identity,
                           current || goal ? new Vector3(0.7f, 0.4f, 0.07f) : new Vector3(0.4f, 0.25f, 0.05f), mat);
            return go;
        }

        private void ClearRoute()
        {
            foreach (GameObject g in m_RouteMarkers) if (g != null) Destroy(g);
            m_RouteMarkers.Clear();
            m_Route.Clear();
            m_RouteIndex = 0;
            if (m_RouteLine != null) m_RouteLine.positionCount = 0;
            if (m_RouteInfo != null) m_RouteInfo.text = "";
        }

        private bool RouteDone => m_Route.Count > 0 && m_RouteIndex >= m_Route.Count;

        /// <summary>今の印に着いたら消して次の印を目立たせる。線と「次の印まで N m」も更新する。</summary>
        private void UpdateRoute()
        {
            if (m_RouteIndex >= m_Route.Count) return;
            Vector3 d = m_Route[m_RouteIndex] - m_Self.transform.position;
            d.y = 0f;
            RedrawRouteLine();
            if (m_RouteInfo != null)
            {
                float ang = Vector3.SignedAngle(m_Self.transform.forward, d, Vector3.up);
                string dir = Mathf.Abs(ang) < 20f ? "↑ 正面" : ang > 0f ? (ang > 110f ? "↓ 右後ろ" : "→ 右") : (ang < -110f ? "↓ 左後ろ" : "← 左");
                m_RouteInfo.text = $"次の印まで {d.magnitude:F0} m  {dir}   (残り {m_Route.Count - m_RouteIndex} 個)";
            }
            if (d.magnitude > m_ReachWithin) return;
            if (m_RouteMarkers[m_RouteIndex] != null) Destroy(m_RouteMarkers[m_RouteIndex]);
            m_RouteMarkers[m_RouteIndex] = null;
            m_RouteIndex++;
            if (m_RouteIndex < m_Route.Count)
            {
                if (m_RouteMarkers[m_RouteIndex] != null) Destroy(m_RouteMarkers[m_RouteIndex]);
                m_RouteMarkers[m_RouteIndex] = MakeMarker(m_Route[m_RouteIndex], true, m_RouteIndex == m_Route.Count - 1);
                GameDirector.Instance?.Audio.PlayClick();
            }
        }

        /// <summary>機体を別の場所へ置き直す (遠い場所の手順のため)。</summary>
        private void Teleport(RobotAgent agent, Vector3 pos, Vector3 forward)
        {
            agent.SetStartPose(new Pose(pos, Quaternion.LookRotation(forward.normalized, Vector3.up)));
            agent.ResetToStart();
        }

        private Vector3 BaseFront(string side, float distance)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (BasePanel p in Field.PanelsOf(side)) { if (!p.High) { sum += p.transform.position; n++; } }
            if (n == 0) return Vector3.zero;
            Vector3 c = sum / n;
            c.y = 0f;
            Vector3 toCenter = -c;
            toCenter.y = 0f;
            return c + toCenter.normalized * distance;
        }

        private FieldProp NearestProp(System.Func<FieldProp, bool> pick)
        {
            FieldProp best = null;
            float bestD = float.MaxValue;
            foreach (FieldProp p in Props.Props)
            {
                if (!pick(p)) continue;
                float d = (p.transform.position - m_Self.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        // ------------------------------------------------------------------ アタッカー

        private void DefineAttacker()
        {
            // スタートゾーンから始め、黄色い印をたどって相手の見える場所へ。戻るときも印をたどる (瞬間移動はしない)
            float sign = MySide == Side.Red ? 1f : -1f;
            Vector3 engage = new Vector3(-4.5f * sign, 0f, 3.5f * sign);
            if (m_Target != null) Teleport(m_Target, new Vector3(2.5f * sign, 0f, 3.5f * sign), new Vector3(-sign, 0f, 0f));
            TournamentRobot target = m_Target != null ? Round?.Find(m_Target.Index) : null;
            int hitsAtStart = target != null ? target.HitsTaken : 0;

            m_Steps.Add(new Step
            {
                Text = "矢印キー (パッドは左スティック) で、黄色い印を順にたどって相手の見える場所まで進む",
                Hint = "後ろは下矢印、横移動は左右の矢印。Shift (LB) でターボ。印に着くと次の印が明るくなる",
                Enter = () => StartRoute(engage),
                Done = () => RouteDone,
            });
            m_Steps.Add(new Step
            {
                Text = "A / D (パッドは右スティック) で旋回し、相手の機体 (青) を画面の中央に捉える",
                Hint = "視点は機体のカメラだけ。相手は 7 m ほど先に止まっている。見失ったら V の地図で確かめる",
                Done = () => m_Target != null && Bearing(m_Target.transform.position) < 8f,
            });
            m_Steps.Add(new Step
            {
                Text = "Space (RB) で撃つ。相手の装甲板 (機体の四隅の板) に 1 発当てる",
                Hint = "装甲板に当たると相手の HP が 10 減る。ほかの場所に当たっても減らない",
                Done = () => target != null && target.HitsTaken >= hitsAtStart + 1,
            });
            m_Steps.Add(new Step
            {
                Text = "相手の横や後ろに回り込んで、さらに 3 発当てる",
                Hint = "正面の板は細く見えて当てにくい。横や後ろから撃つと当たりやすい",
                Done = () => target != null && target.HitsTaken >= hitsAtStart + 4,
            });
            m_Steps.Add(new Step
            {
                Text = "V (パッドは Y) で地図を出し、自分と相手の位置を見る",
                Hint = "地図は左下。もう一度押すと消える",
                Enter = () => m_Hud.SetMinimapVisible(false),
                Done = () => m_Hud.MinimapVisible,
                MinSeconds = 1f,
            });
            m_Steps.Add(new Step
            {
                Text = "弾が尽きたら補給が要る。1 キー (パッドは十字キーで 1 を選んで X) でピットインを発動する",
                Hint = "ピットインは同盟の RP を 20 使う。右下に残弾、左下に RP が出ている",
                Done = () => Me != null && Me.PitInGranted,
            });
            m_Steps.Add(new Step
            {
                Text = "黄色い印をたどってスタートゾーンへ戻り、その外側の補給エリア (囲い) に入る。入ると 20 秒止まって装填される",
                Hint = "ピットインなしで入ると反則 (-50VP)。相手の補給エリアに入っても反則",
                Enter = () => StartRoute(Field.SupplyPoint(MySideName), 1.3f, allowSupply: true),
                Done = () => Me != null && Round != null && Me.IsFrozen(Round.Time),
            });
            m_Steps.Add(new Step
            {
                Text = "装填中 (20 秒)。終わるまで待つ",
                Hint = "試合では補給の間も時間が進む。弾が尽きる前に戻るか、尽きてから戻るかは作戦次第",
                Enter = ClearRoute,
                Done = () => Me != null && Round != null && !Me.IsFrozen(Round.Time),
                MinSeconds = 1f,
            });
            m_Steps.Add(new Step
            {
                Text = "相手の陣地は開始 3 分後から撃てる (ここでは時間を進めた)。黄色い印をたどって相手の陣地の前まで行く",
                Hint = "陣地はフィールドの反対側の角。途中の進入禁止ゾーン (赤い帯) には入らないように印が引いてある",
                Enter = () =>
                {
                    Round?.AdvanceTimeForTutorial(Mathf.Max(0f, m_Director.Tournament.Config.BaseImmuneSeconds + 1f - Round.Time));
                    string enemySide = TournamentController.SideName(TournamentRound.Opposite(MySide));
                    StartRoute(BaseFront(enemySide, 5f), 1.8f);
                },
                Done = () => RouteDone,
            });
            m_Steps.Add(new Step
            {
                Text = "相手の陣地の低い位置の板に 1 発当てる",
                Hint = "陣地の HP は 200。撃破すると +100VP +100RP。高い板は当てても減りが少ない",
                Done = () => Enemy != null && Enemy.BaseHp < m_Director.Tournament.Config.BaseHp,
            });
            m_Steps.Add(new Step
            {
                Text = "スキルを使う: 5 キー (パッドは十字キーで選んで X) でブースト Lv.1 を発動する",
                Hint = "RP はコンテナの運搬や陣地の撃破で増える。スキルは同盟の人なら誰でも発動できる",
                Done = () => Mine != null && Round != null && Mine.BoostActive(Round.Time),
            });
        }

        private float Bearing(Vector3 worldPos)
        {
            Vector3 to = worldPos - m_Self.transform.position;
            to.y = 0f;
            return Vector3.Angle(m_Self.transform.forward, to);
        }

        // ------------------------------------------------------------------ ビルダー

        private void DefineBuilder()
        {
            System.Func<FieldProp> nextContainer = () => NearestProp(p => p.Kind == PropKind.Container && p.Origin == ContainerOrigin.HolderA && p.HomeSide == MySideName && p.AtHome);
            System.Func<FieldProp> nextBlock = () => NearestProp(p => p.Kind == PropKind.Block && p.HomeSide == MySideName && p.AtHome && p.Slot == null);
            System.Func<PropSlot> spot1 = () => Props.Slot(PlaceKind.Spot, MySideName, 1);
            System.Func<PropSlot> site1 = () => Props.Slot(PlaceKind.Site, MySideName, 1);
            bool heldContainer() => m_Self.Lift != null && m_Self.Lift.Held != null && m_Self.Lift.Held.Kind == PropKind.Container;
            bool heldBlock() => m_Self.Lift != null && m_Self.Lift.Held != null && m_Self.Lift.Held.Kind == PropKind.Block;

            m_Steps.Add(new Step
            {
                Text = "矢印キー (左スティック) で、黄色い印をたどって置き場 A のコンテナの前まで行く",
                Hint = "置き場 A は自陣地の横の棚。印に着くと次の印が明るくなる",
                Enter = () => { FieldProp c = nextContainer(); if (c != null) StartRoute(c.transform.position, 1.6f); },
                Done = () => RouteDone,
            });
            m_Steps.Add(new Step
            {
                Text = "ハンドの爪の間にコンテナを入れ、画面下の案内が「…をつかむ」になったら Space (RB) でつかむ",
                Hint = "高さが合わないときは R / F (十字キー上下) でハンドを上下させる。ハンドは機体の前 0.6 m",
                Enter = ClearRoute,
                Done = heldContainer,
            });
            m_Steps.Add(new Step
            {
                Text = "黄色い印をたどってスポット (6 列 × 3 段の棚) へ。1 段目の前で高さを合わせ、案内が「…に置く」になったら Space",
                Hint = "置くと +10VP +30RP。1 つの段に 4 個並ぶとさらに +50VP +100RP",
                Enter = () => { PropSlot sp = spot1(); if (sp != null) StartRoute(sp.Position, 1.6f); },
                Done = () => Mine != null && Mine.Containers >= 1,
            });
            m_Steps.Add(new Step
            {
                Text = "もう 1 個。印をたどって置き場 A へ戻り、コンテナをつかむ",
                Hint = "相手の置き場のコンテナや、置いてあるコンテナを取ると反則 (-50VP)。案内に「反則」と出たらやめる",
                Enter = () => { FieldProp c = nextContainer(); if (c != null) StartRoute(c.transform.position, 1.6f); },
                Done = heldContainer,
            });
            m_Steps.Add(new Step
            {
                Text = "スポットへ運び、同じ段の隣に置く",
                Hint = "置き場所は自動で揃う。案内に「…に置く」と出る位置で Space",
                Enter = () => { PropSlot sp = spot1(); if (sp != null) StartRoute(sp.Position, 1.6f); },
                Done = () => Mine != null && Mine.Containers >= 2,
            });
            m_Steps.Add(new Step
            {
                Text = "印をたどってブロックゾーンへ行き、ブロックをつかむ。ハンドは一番下まで下げる",
                Hint = "ブロックは 300 × 300 × 200 mm。ビルダーだけが持てる",
                Enter = () => { FieldProp b = nextBlock(); if (b != null) StartRoute(b.transform.position, 1.6f); },
                Done = heldBlock,
            });
            m_Steps.Add(new Step
            {
                Text = "印をたどってビルドサイト 1 へ。置き場の正面で高さを合わせ、案内が「…に置く」になったら Space",
                Hint = "ビルドサイトは自陣地のそばに 4 つ。サイトから取り出すと反則",
                Enter = () => { PropSlot st = site1(); if (st != null) StartRoute(st.Position, 1.6f); },
                Done = () => { PropSlot st = site1(); return st != null && st.Count >= 1; },
            });
            m_Steps.Add(new Step
            {
                Text = "もう 1 個。印をたどってブロックゾーンへ戻り、ブロックをつかむ",
                Hint = "2 個積むとサイトを獲得。サイト 1: 自分の防御 2 倍、2: 陣地の防御 1.5 倍、3 / 4: 置き場 B / C が使える",
                Enter = () => { FieldProp b = nextBlock(); if (b != null) StartRoute(b.transform.position, 1.6f); },
                Done = heldBlock,
            });
            m_Steps.Add(new Step
            {
                Text = "ビルドサイト 1 へ運んで、上に積む",
                Hint = "獲得すると HUD のサイトの印が ■ になる",
                Enter = () => { PropSlot st = site1(); if (st != null) StartRoute(st.Position, 1.6f); },
                Done = () => Mine != null && Mine.SiteAcquired(1),
            });
        }

        // ------------------------------------------------------------------ 進行

        private void Advance()
        {
            ClearRoute();
            m_Index++;
            if (m_Index >= m_Steps.Count)
            {
                Finish();
                return;
            }
            m_EnteredAt = Time.time;
            Step s = m_Steps[m_Index];
            s.Enter?.Invoke();
            m_Title.text = $"チュートリアル ({(m_Kind == TutorialKind.Builder ? "ビルダー" : "アタッカー")})   {m_Index + 1} / {m_Steps.Count}";
            m_Text.text = s.Text;
            m_Hint.text = s.Hint ?? "";
        }

        private void Finish()
        {
            m_Finished = true;
            PlayerPrefs.SetInt("tut_" + m_Kind, 1);
            m_Title.text = "チュートリアル 完了";
            m_Text.text = m_Kind == TutorialKind.Builder
                ? "ビルダーの基本はこれで全部。試合では相手の邪魔を避けながら、コンテナとブロックを運び続ける"
                : "アタッカーの基本はこれで全部。試合では撃破 +10VP、3 分後からは陣地 +100VP を狙う";
            m_Hint.text = "";
            m_Buttons.gameObject.SetActive(false);
            m_DoneButtons.gameObject.SetActive(true);
            GameDirector.Instance?.Audio.PlayEnd();
        }

        private void Update()
        {
            if (m_Finished || m_Index < 0 || m_Index >= m_Steps.Count) return;
            if (!m_Director.RobotsMayMove) return;
            UpdateRoute();
            Step s = m_Steps[m_Index];
            if (Time.time - m_EnteredAt < s.MinSeconds) return;
            bool done = false;
            try { done = s.Done != null && s.Done(); }
            catch (System.Exception) { done = false; }
            if (done)
            {
                GameDirector.Instance?.Audio.PlaySkill();
                Advance();
            }
        }

        private void OnDestroy()
        {
            ClearRoute();
            if (m_RouteLine != null) Destroy(m_RouteLine.gameObject);
            if (m_Root != null) Destroy(m_Root.gameObject);
        }
    }
}
