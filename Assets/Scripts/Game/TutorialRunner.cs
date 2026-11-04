using System.Collections.Generic;
using CoreSim.Rules;
using UnityEngine;
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
            UiKit.Image(m_Root, "bg", new Color(0.05f, 0.1f, 0.2f, 0.85f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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

        private void SetMarker(Vector3 at)
        {
            ClearMarker();
            m_Marker = new GameObject("TutorialMarker");
            Material mat = MaterialLibrary.Unlit(new Color(1f, 0.85f, 0.2f));
            Visuals.Create("ring", PrimitiveType.Cylinder, m_Marker.transform, new Vector3(at.x, 0.02f, at.z), Quaternion.identity, new Vector3(1.2f, 0.01f, 1.2f), mat);
            Visuals.Create("post", PrimitiveType.Cylinder, m_Marker.transform, new Vector3(at.x, 1.2f, at.z), Quaternion.identity, new Vector3(0.06f, 1.2f, 0.06f), mat);
            Visuals.Create("flag", PrimitiveType.Cube, m_Marker.transform, new Vector3(at.x, 2.4f, at.z), Quaternion.identity, new Vector3(0.5f, 0.3f, 0.05f), mat);
        }

        private void ClearMarker()
        {
            if (m_Marker != null) Destroy(m_Marker);
            m_Marker = null;
        }

        private bool NearMarker(float within)
        {
            if (m_Marker == null) return false;
            Vector3 d = m_Marker.transform.GetChild(0).position - m_Self.transform.position;
            d.y = 0f;
            return d.magnitude < within;
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
            // 北側の空き地で始める (スタートゾーンは構造物に近く、印が見えにくい)
            Vector3 start = new Vector3(-4.5f, 0f, 3.5f);
            Teleport(m_Self, start, Vector3.right);
            if (m_Target != null) Teleport(m_Target, new Vector3(2.5f, 0f, 3.5f), Vector3.left);
            TournamentRobot target = m_Target != null ? Round?.Find(m_Target.Index) : null;
            int hitsAtStart = target != null ? target.HitsTaken : 0;

            m_Steps.Add(new Step
            {
                Text = "矢印キー (パッドは左スティック) で前に進み、黄色い印まで行く",
                Hint = "後ろは下矢印、横移動は左右の矢印。Shift (LB) でターボ",
                Enter = () => SetMarker(start + Vector3.right * 4f),
                Done = () => NearMarker(1.3f),
            });
            m_Steps.Add(new Step
            {
                Text = "A / D (パッドは右スティック) で旋回し、相手の機体 (青) を画面の中央に捉える",
                Hint = "視点は機体のカメラだけ。相手は 7 m ほど先に止まっている",
                Enter = ClearMarker,
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
                Text = "自同盟の補給エリア (スタートゾーンの外側の囲い) に入る。入ると 20 秒止まって装填される",
                Hint = "補給エリアの近くまで移動した。黄色い印の先が補給エリア",
                Enter = () =>
                {
                    Vector3 supply = Field.SupplyPoint(MySideName);
                    Vector3 approach = supply + (Field.Spawn(MySideName, 0).position - supply).normalized * 3.5f;
                    Teleport(m_Self, approach, supply - approach);
                    SetMarker(supply);
                },
                Done = () => Me != null && Round != null && Me.IsFrozen(Round.Time),
            });
            m_Steps.Add(new Step
            {
                Text = "装填中 (20 秒)。終わるまで待つ",
                Hint = "ピットインなしで補給エリアに入ると反則 (-50VP)。相手の補給エリアに入っても反則",
                Enter = ClearMarker,
                Done = () => Me != null && Round != null && !Me.IsFrozen(Round.Time),
                MinSeconds = 1f,
            });
            m_Steps.Add(new Step
            {
                Text = "相手の陣地は開始 3 分後から撃てる (ここでは時間を進めた)。陣地の低い位置の板に 1 発当てる",
                Hint = "陣地の前へ移動した。陣地の HP は 200。撃破すると +100VP +100RP",
                Enter = () =>
                {
                    Round?.AdvanceTimeForTutorial(Mathf.Max(0f, m_Director.Tournament.Config.BaseImmuneSeconds + 1f - Round.Time));
                    string enemySide = TournamentController.SideName(TournamentRound.Opposite(MySide));
                    Vector3 front = BaseFront(enemySide, 5f);
                    Vector3 target2 = BaseFront(enemySide, 0f);
                    Teleport(m_Self, front, target2 - front);
                },
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
            FieldProp containerA = null;
            FieldProp block = null;
            m_Steps.Add(new Step
            {
                Text = "矢印キー (左スティック) で、黄色い印 (置き場 A のコンテナ) の前まで行く",
                Hint = "置き場 A は自陣地の横の棚。ハンドは機体の前 0.6 m にある",
                Enter = () =>
                {
                    containerA = NearestProp(p => p.Kind == PropKind.Container && p.Origin == ContainerOrigin.HolderA && p.HomeSide == MySideName && p.AtHome);
                    if (containerA != null) SetMarker(containerA.transform.position);
                },
                Done = () => NearMarker(1.6f),
            });
            m_Steps.Add(new Step
            {
                Text = "ハンドの爪の間にコンテナを入れ、画面下の案内が「…をつかむ」になったら Space (RB) でつかむ",
                Hint = "高さが合わないときは R / F (十字キー上下) でハンドを上下させる",
                Enter = ClearMarker,
                Done = () => m_Self.Lift != null && m_Self.Lift.Held != null && m_Self.Lift.Held.Kind == PropKind.Container,
            });
            m_Steps.Add(new Step
            {
                Text = "スポット (6 列 × 3 段の棚) の 1 段目に置く。黄色い印の前で高さを合わせ、案内が「…に置く」になったら Space",
                Hint = "置くと +10VP +30RP。1 つの段に 4 個並ぶとさらに +50VP +100RP",
                Enter = () =>
                {
                    PropSlot spot = Props.Slot(PlaceKind.Spot, MySideName, 1);
                    if (spot != null) SetMarker(spot.Position);
                },
                Done = () => Mine != null && Mine.Containers >= 1,
            });
            m_Steps.Add(new Step
            {
                Text = "もう 1 個コンテナを運んで、同じ段の隣に置く",
                Hint = "相手の置き場のコンテナや、置いてあるコンテナを取ると反則 (-50VP)。案内に「反則」と出たらやめる",
                Enter = ClearMarker,
                Done = () => Mine != null && Mine.Containers >= 2,
            });
            m_Steps.Add(new Step
            {
                Text = "ブロックゾーン (黄色い印) のブロックをつかむ。ハンドは一番下まで下げる",
                Hint = "ブロックは 300 × 300 × 200 mm。ビルダーだけが持てる",
                Enter = () =>
                {
                    block = NearestProp(p => p.Kind == PropKind.Block && p.HomeSide == MySideName && p.AtHome && p.Slot == null);
                    if (block != null) SetMarker(block.transform.position);
                },
                Done = () => m_Self.Lift != null && m_Self.Lift.Held != null && m_Self.Lift.Held.Kind == PropKind.Block,
            });
            m_Steps.Add(new Step
            {
                Text = "ビルドサイト 1 (黄色い印) に置く。置き場の正面で高さを合わせ、案内が「…に置く」になったら Space",
                Hint = "ビルドサイトは自陣地のそばに 4 つ。サイトから取り出すと反則",
                Enter = () =>
                {
                    PropSlot site = Props.Slot(PlaceKind.Site, MySideName, 1);
                    if (site != null) SetMarker(site.Position);
                },
                Done = () => { PropSlot site = Props.Slot(PlaceKind.Site, MySideName, 1); return site != null && site.Count >= 1; },
            });
            m_Steps.Add(new Step
            {
                Text = "もう 1 個ブロックを運んで、同じサイトに積む。2 個でサイト 1 を獲得する",
                Hint = "サイト 1: 自分の防御 2 倍、2: 陣地の防御 1.5 倍、3 / 4: 置き場 B / C のコンテナが使える",
                Done = () => Mine != null && Mine.SiteAcquired(1),
            });
        }

        // ------------------------------------------------------------------ 進行

        private void Advance()
        {
            ClearMarker();
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
            ClearMarker();
            if (m_Root != null) Destroy(m_Root.gameObject);
        }
    }
}
