using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 游戏菜单：我的课表、社工组织、结局、导出存档、游戏提示。默认 Y 开关，打开时暂停服务端时钟。
/// </summary>
public class BackendGameplayMenu : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.Y;

    private GameObject _root;
    private TMP_Text _log;
    private Transform _joinHostRoot;
    private RectTransform _scrollPanelRt;
    private ScrollRect _menuScrollRect;
    private RectTransform _menuContentRt;
    private LayoutElement _socialJoinHostLe;
    private LayoutElement _contentTopSpacerLe;
    private bool _open;
    private readonly List<GameObject> _dynamicRows = new();

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey) && !NPCManager.ShouldSuppressGlobalHotkeys())
            Toggle();
    }

    private void Toggle()
    {
        if (_root == null)
            BuildUi();
        if (_root == null) return;

        _open = !_root.activeSelf;
        _root.SetActive(_open);
        if (_open)
        {
            ServerPauseCoordinator.Acquire(this);
            LogLine("《清华园物语》— 请选择上方功能");
            ScrollMenuToTop();
        }
        else
            ServerPauseCoordinator.Release(this);
    }

    /// <summary>关闭本菜单并释放时间暂停（用于关闭按钮、打开课表等）。</summary>
    void CloseMenuUiOnly()
    {
        _open = false;
        if (_root != null) _root.SetActive(false);
        ServerPauseCoordinator.Release(this);
    }

    const float ScrollPanelTopInset = 132f;

    private void BuildUi()
    {
        if (_root != null)
        {
            Destroy(_root);
            _root = null;
        }
        _log = null;
        _joinHostRoot = null;
        _scrollPanelRt = null;
        _menuScrollRect = null;
        _menuContentRt = null;
        _socialJoinHostLe = null;
        _contentTopSpacerLe = null;

        APIManager.EnsureExists();
        var canvasGo = new GameObject("BackendGameplayMenuCanvas");
        DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 280;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 600);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.08f, 0.1f);
        prt.anchorMax = new Vector2(0.92f, 0.92f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.12f, 0.97f);

        // ① Scroll 最先创建，保证绘制在底层，避免盖住按钮与社工条（后创建的同级 UI 在上层）
        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(panel.transform, false);
        var srt = scrollGo.AddComponent<RectTransform>();
        _scrollPanelRt = srt;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(12f, 12f);
        srt.offsetMax = new Vector2(-12f, -ScrollPanelTopInset);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        _menuScrollRect = scroll;
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0, 1200f);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12;
        vlg.padding = new RectOffset(10, 10, 24, 12);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        scroll.viewport = vrt;
        scroll.content = crt;
        scroll.vertical = true;
        _menuContentRt = crt;

        var topSpacer = new GameObject("ContentTopSpacer");
        topSpacer.transform.SetParent(content.transform, false);
        topSpacer.AddComponent<RectTransform>();
        var tsLe = topSpacer.AddComponent<LayoutElement>();
        tsLe.minHeight = 0;
        tsLe.flexibleHeight = 0;
        _contentTopSpacerLe = tsLe;

        _log = UiUtil.Label(content.transform, "", 13, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var le = _log.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 80f;
        le.flexibleHeight = 1;
        le.flexibleWidth = 1;

        // 社工加入条挂在 Panel 上、叠在滚动区之上，不参与 Scroll 内容高度分配，避免被算成 0 高。
        var stripRoot = new GameObject("SocialJoinStrip");
        stripRoot.transform.SetParent(panel.transform, false);
        var stripRt = stripRoot.AddComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0f, 1f);
        stripRt.anchorMax = new Vector2(1f, 1f);
        stripRt.pivot = new Vector2(0.5f, 1f);
        stripRt.anchoredPosition = new Vector2(0f, -(ScrollPanelTopInset - 6f));
        stripRt.sizeDelta = new Vector2(-24f, 0f);
        var stripLe = stripRoot.AddComponent<LayoutElement>();
        stripLe.minHeight = 0;
        stripLe.flexibleHeight = 0;
        _socialJoinHostLe = stripLe;
        var stripFitter = stripRoot.AddComponent<ContentSizeFitter>();
        stripFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        stripFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var joinInner = new GameObject("SocialJoinInner");
        joinInner.transform.SetParent(stripRoot.transform, false);
        var jInRt = joinInner.AddComponent<RectTransform>();
        jInRt.anchorMin = new Vector2(0f, 1f);
        jInRt.anchorMax = new Vector2(1f, 1f);
        jInRt.pivot = new Vector2(0.5f, 1f);
        jInRt.anchoredPosition = Vector2.zero;
        jInRt.sizeDelta = Vector2.zero;
        _joinHostRoot = joinInner.transform;
        var jHostVlg = joinInner.AddComponent<VerticalLayoutGroup>();
        jHostVlg.spacing = 8;
        jHostVlg.padding = new RectOffset(10, 10, 0, 0);
        jHostVlg.childControlHeight = true;
        jHostVlg.childControlWidth = true;
        jHostVlg.childForceExpandWidth = false;
        jHostVlg.childForceExpandHeight = false;
        jHostVlg.childAlignment = TextAnchor.UpperLeft;

        // ② 功能按钮栏
        var buttonBar = new GameObject("ButtonBar");
        buttonBar.transform.SetParent(panel.transform, false);
        var barRt = buttonBar.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 1f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(0.5f, 1f);
        barRt.anchoredPosition = new Vector2(0f, -44f);
        barRt.sizeDelta = new Vector2(-24f, 76f);
        var grid = buttonBar.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(168f, 32f);
        grid.spacing = new Vector2(5f, 8f);
        grid.padding = new RectOffset(8, 8, 0, 0);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;

        void AddBarButton(string label, Action a) => UiUtil.GridBarButton(buttonBar.transform, label, a);

        AddBarButton("我的课表", OpenMySchedule);
        AddBarButton("课程掌握度", LoadCourseMastery);
        AddBarButton("社工组织", LoadSocialOrgs);
        AddBarButton("结局", LoadEndings);
        AddBarButton("导出存档", ExportSaveClick);
        AddBarButton("游戏提示", ShowGameTips);

        // ③ 标题与关闭（最上层，保证可点）
        _ = UiUtil.Label(panel.transform, "游戏菜单", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16f), new Vector2(400, 36));
        UiUtil.Button(panel.transform, $"关闭 ({toggleKey})", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(100, 32), CloseMenuUiOnly);

        _root = canvasGo;
        _root.SetActive(false);
    }

    private void LogLine(string msg)
    {
        if (_log != null)
            _log.text = msg;
        Debug.Log("[BackendGameplayMenu] " + msg);
    }

    private void ClearDynamicRows()
    {
        ClearJoinHost();
        foreach (var go in _dynamicRows)
            if (go != null) Destroy(go);
        _dynamicRows.Clear();
    }

    void ClearJoinHost()
    {
        if (_socialJoinHostLe != null)
            _socialJoinHostLe.minHeight = 0;
        if (_contentTopSpacerLe != null)
            _contentTopSpacerLe.minHeight = 0;
        if (_joinHostRoot == null) return;
        for (int i = _joinHostRoot.childCount - 1; i >= 0; i--)
            Destroy(_joinHostRoot.GetChild(i).gameObject);
    }

    void ScrollMenuToTop()
    {
        if (_menuScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        _menuScrollRect.verticalNormalizedPosition = 1f;
    }

    private void OpenMySchedule()
    {
        ClearDynamicRows();
        var sv = UnityEngine.Object.FindObjectOfType<ScheduleViewUI>(true);
        if (sv != null)
        {
            CloseMenuUiOnly();
            var canvas = sv.GetComponentInParent<Canvas>();
            if (canvas != null) canvas.sortingOrder = 290;
            sv.OpenPanel();
        }
        else
            LoadScheduleFallback();
    }

    private void LoadCourseMastery()
    {
        ClearDynamicRows();
        if (APIManager.Instance == null) return;
        APIManager.Instance.GetMyCoursesV21(data =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("════════ 课程掌握度（本学期已选） ════════");
            var pm = PlayerManager.Instance;
            if (pm != null)
            {
                if (!string.IsNullOrEmpty(pm.stats.server_week_name))
                    sb.AppendLine($"当前学期：{pm.stats.server_week_name}");
                else if (pm.stats.semester_index >= 0)
                    sb.AppendLine($"当前学期序号：{pm.stats.semester_index}");
            }

            if (data?.courses == null || data.courses.Length == 0)
            {
                sb.AppendLine("暂无已选课程。请在新学期通过选课界面选课。");
                LogLine(sb.ToString());
                ScrollMenuToTop();
                return;
            }

            var list = new List<PlayerCourseV21>(data.courses.Length);
            foreach (var c in data.courses)
                if (c != null) list.Add(c);
            list.Sort((a, b) => string.CompareOrdinal(a.course_name ?? "", b.course_name ?? ""));

            int idx = 0;
            foreach (var c in list)
            {
                idx++;
                string title = string.IsNullOrEmpty(c.course_name) ? c.course_id : c.course_name;
                int m = Mathf.Clamp(Mathf.RoundToInt(c.mastery), 0, 100);
                sb.AppendLine($"{idx}. {title}");
                sb.AppendLine($"   掌握度 {m}/100　学分 {Mathf.Max(0, c.credits)}　出勤 {Mathf.Max(0, c.attendance_count)}　缺勤 {Mathf.Max(0, c.absence_count)}");
            }

            LogLine(sb.ToString());
            ScrollMenuToTop();
        }, err => LogLine("课程掌握度加载失败：" + err));
    }

    private void LoadScheduleFallback()
    {
        if (APIManager.Instance == null) return;
        APIManager.Instance.GetScheduleV21(data =>
        {
            var sb = new StringBuilder();
            if (data?.schedule == null || data.schedule.Length == 0)
            {
                LogLine("课表为空，请在新学期通过选课界面添加课程。");
                return;
            }
            string[] wds = { "一", "二", "三", "四", "五", "六", "日" };
            foreach (var s in data.schedule)
            {
                string wd = s.day_of_week >= 0 && s.day_of_week < wds.Length ? wds[s.day_of_week] : $"{s.day_of_week}";
                sb.AppendLine($"星期{wd} 第{s.period}节 · {s.course_name} ({s.course_id})");
            }
            LogLine(sb.ToString());
        }, e => LogLine("课表加载失败：" + e));
    }

    private void LoadSocialOrgs()
    {
        ClearDynamicRows();
        if (APIManager.Instance == null) return;
        APIManager.Instance.GetSocialOrgsV21(data =>
        {
            var sb = new StringBuilder();
            if (data?.orgs == null || data.orgs.Length == 0)
            {
                LogLine("暂无社工组织数据");
                return;
            }

            sb.AppendLine("请点击上方面板中的蓝色「加入」按钮；下方为各组织说明。加入后若社工能力达标将自动晋升。");
            var orgCount = 0;
            foreach (var o in data.orgs)
            {
                if (o == null) continue;
                orgCount++;
                string name = string.IsNullOrEmpty(o.name) ? o.id : o.name;
                sb.AppendLine($"· {name}（{o.id}）— {o.bonus}");
                if (_joinHostRoot != null)
                    UiUtil.AddOrgJoinRow(_joinHostRoot, name, o.id, oid => StartCoroutine(JoinOrgRoutine(oid)));
            }
            // 与 CSF 并行：条带在 Panel 上也保留 minHeight，避免个别环境下条带高度为 0。
            if (_socialJoinHostLe != null && orgCount > 0)
                _socialJoinHostLe.minHeight = 34f * orgCount + 8f * Mathf.Max(0, orgCount - 1) + 4f;
            if (_contentTopSpacerLe != null && orgCount > 0 && _socialJoinHostLe != null)
                _contentTopSpacerLe.minHeight = _socialJoinHostLe.minHeight + 24f;
            LogLine(sb.ToString());
            if (_menuContentRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_menuContentRt);
            ScrollMenuToTop();
        }, e => LogLine("社工组织加载失败：" + e));
    }

    private IEnumerator JoinOrgRoutine(string orgId)
    {
        JoinOrgResponseV21 res = null;
        string err = null;
        bool done = false;
        APIManager.Instance.JoinSocialOrgV21(orgId,
            r => { res = r; done = true; },
            e => { err = e; done = true; });
        while (!done) yield return null;
        LogLine(!string.IsNullOrEmpty(err) ? err : (res != null && res.success ? $"已加入 {res.org}" : "加入失败"));
        PlayerManager.Instance?.RefreshFromServer();
        if (string.IsNullOrEmpty(err) && res != null && res.success)
            yield return AutoPromoteWhileEligible();
    }

    /// <summary>连续调用 /social/promote，直到服务端返回未成功（由后端根据社工能力判定）。</summary>
    static IEnumerator AutoPromoteWhileEligible()
    {
        if (APIManager.Instance == null) yield break;
        const int maxAttempts = 8;
        for (int n = 0; n < maxAttempts; n++)
        {
            bool done = false;
            PromoteResponseV21 resp = null;
            string terr = null;
            APIManager.Instance.TryPromoteV21(
                r => { resp = r; done = true; },
                e => { terr = e; done = true; });
            while (!done) yield return null;

            if (!string.IsNullOrEmpty(terr))
                yield break;
            if (resp == null || !resp.success)
                yield break;

            string msg = string.IsNullOrEmpty(resp.message) ? "社工职位已晋升" : resp.message;
            GameHUD.Instance?.ShowNotification(msg, 5f);
            PlayerManager.Instance?.RefreshFromServer();
            yield return null;
        }
    }

    private void LoadEndings()
    {
        ClearDynamicRows();
        if (APIManager.Instance == null) return;
        APIManager.Instance.GetEndingsV21(data =>
        {
            var sb = new StringBuilder();
            if (data?.endings == null) { LogLine("暂无结局数据"); return; }
            foreach (var e in data.endings)
            {
                if (e == null) continue;
                string mark = e.forced ? "[强制] " : "";
                sb.AppendLine($"{mark}{e.name}（{e.id}）可达成:{e.available}");
            }
            LogLine(sb.ToString());
            ScrollMenuToTop();

            foreach (var e in data.endings)
            {
                if (e == null || !e.forced) continue;
                GameHUD.Instance?.ShowNotification($"注意强制结局：{e.name}", 8f);
            }

            if (PlayerManager.Instance != null && PlayerManager.Instance.stats.is_game_over_server)
                GameHUD.Instance?.ShowNotification("学业已结束，请查看结局列表。", 6f);
        }, err => LogLine("结局加载失败：" + err));
    }

    private void ExportSaveClick()
    {
        if (APIManager.Instance == null) return;
        APIManager.Instance.ExportSaveRawV21(json =>
        {
            PlayerPrefs.SetString("thustory_save_export_v21", json);
            PlayerPrefs.Save();
            LogLine($"已导出至 PlayerPrefs 键「thustory_save_export_v21」（{json.Length} 字符）。可用于备份。");
        }, e => LogLine("导出失败：" + e));
    }

    private void ShowGameTips()
    {
        ClearDynamicRows();
        LogLine(GameTipsBody);
        ScrollMenuToTop();
    }

    static readonly string GameTipsBody =
        "════════ 快捷键 ════════\n" +
        "· Y：打开/关闭本游戏菜单（可在 BackendGameplayMenu 上修改；打开时服务端时间暂停）\n" +
        "· U：打开/关闭「我的课表」网格（场景需挂 ScheduleViewUI，键位可在该组件上改）\n" +
        "· M：打开/关闭地图。打开地图时角色不可移动，时间会暂停同步\n" +
        "· WASD：角色移动（仅横向或纵向一格方向）\n" +
        "· F：①出现场景切换提示时，在松开 WASD 的情况下切换场景；②靠近活动触发点时执行活动（与 ActivityTrigger 一致）\n" +
        "· E：场景互动——宿舍睡觉/休息、图书馆自习、操场锻炼、教室上课、实验室、社团、食堂等（进入对应场景后在可互动状态下按 E）\n" +
        "· R：① 宿舍：夜间互动（室友聊天等）；② 图书馆（开馆、自习面板未开）：社团活动确认面板；③ 操场：约会确认面板；④ 教室：社工组会确认面板；⑤ 实验室开馆时段：导师面谈确认（若场景配置）\n" +
        "· Enter：NPC 对话中发送输入内容\n" +
        "说明：对话、大地图、本菜单等打开时，部分全局快捷键会被屏蔽（避免误触）。\n\n" +
        "════════ 时间与行程 ════════\n" +
        "· 时钟运行时约现实 0.9 秒 ≈ 游戏内 1 分钟；游戏日约 6:30 至次日 1:00，睡觉可睡到次日 6:30。菜单/对话是否暂停时间以客户端为准。\n" +
        "· 游戏内日期与钟点由后端统一管理，前端通过 GET /time 同步并显示在 HUD。\n" +
        "· 每天的有效时段、阶段名称等以后端数据为准。\n" +
        "· 活动列表来自 GET /activities：仅列表中出现的活动可在当前时刻执行，成功后会消耗时间并可能影响精力、健康等属性。\n\n" +
        "════════ 四条主线（玩什么） ════════\n" +
        "· 学习：选课排周课表，按星期+节次上课，图书馆自习（可选科目）拉高掌握度，学期末 GPA 结算；准时到课掌握度加得多。科协成员上课与自习掌握度收益略快。\n" +
        "· 社工：三选一加入学生会/团委/科协，涨社工能力，做组会、帮助游客、社团活动等；职位可随能力自动晋升。团委：活动社工成长约×1.15；学生会：对话 NPC 正向好感略快；科协：偏学习协同。\n" +
        "· 科研：提升科研能力，解锁实验室与导师（林晚晴）相关玩法；常与 GPA、学期进度（如 SRT）交织。\n" +
        "· 恋爱（可选）：与沈星辞提升好感，解锁后可约会等，注意为晚间/午间留时间槽。\n\n" +
        "════════ 课程 ════════\n" +
        "· 新学期开始或课表为空时会弹出选课界面；可多次选课，课表由服务端保存。\n" +
        "· 每周学时与学分挂钩：第1节 8:00–9:30（2 学时）、第2节 9:50–12:10（3 学时）、第3节 13:30–15:00（2 学时）、第4节 19:20–21:00（2 学时）；周一～周五排课。\n" +
        "· 上课：在课表对应时段进入教室场景，按 E 触发；到课时间与出勤判定（准时/迟到/缺勤）由服务端结合当前时刻与课表节次计算，成功后时间可推进到本节结束。\n" +
        "· 组会：同一教室场景下按 R 打开社工组会（须已加入组织且出现在 /activities；常见晚间时段）。\n" +
        "· 掌握度与出勤统计可通过菜单「课程掌握度」或「我的课表」查看（数据来自 GET /courses/mine）。\n\n" +
        "════════ 社工组织 ════════\n" +
        "· 在「社工组织」中可加入学生会/团委/科协等；蓝色「加入」在功能按钮下方、说明文字的上方条带中。\n" +
        "· 加入组织后，若社工能力达到后端要求，会自动连续尝试晋升，无需手点晋升。\n" +
        "· 组会、社工线活动是否开放仍以 GET /activities 为准。\n\n" +
        "════════ 解锁速查（达成后多为自动检测，活动无法进行时可看结算里的开放条件提示） ════════\n" +
        "· boyfriend_unlocked（恋爱/约会）：沈星辞好感约 ≥60。\n" +
        "· lab_access（实验室/做实验）：科研约 ≥30 且林晚晴好感约 ≥40。\n" +
        "· club_joined（科协/社团活动）：社工约 ≥20 且张锟霖好感约 ≥30。\n" +
        "· social_org_joined（组会等）：成功加入任一社工组织。\n" +
        "· mentor_close（导师亲近）：林晚晴好感约 ≥80。\n" +
        "· srt_unlocked：GPA ≥3.0 且学期序号 ≥2（大二上及以后）。\n" +
        "· internship_unlocked：学期序号 ≥4（大三上及以后）。\n" +
        "· 时段、每日次数、最低精力、NPC 关联等详见仓库内《活动与结局参考》活动表；数值以实际存档为准。\n\n" +
        "════════ 身体与风险 ════════\n" +
        "· 学习、科研、社交多耗精力；吃饭、休息、睡觉、运动与健康类活动可回补。\n" +
        "· 精力或健康过低可能触发晕倒、住院等强制事件；挂科学分过高可能强制退学结局。请以「结局」列表与 HUD 警告为准。\n\n" +
        "════════ 结局与存档 ════════\n" +
        "· 「结局」列出当前可达成去向（保研、出国、考研、创业、灵活就业等）；退学等为强制优先。具体阈值以后端接口为准。\n" +
        "· 「导出存档」将完整存档 JSON 写入本地 PlayerPrefs（键名见导出成功提示），便于备份。\n\n" +
        "════════ 其它 ════════\n" +
        "· 属性、好友度、通知等可在画面 HUD 与人物属性面板中查看。\n" +
        "· 图书馆建筑开馆约 8:00–22:00；闭馆无法入内。\n" +
        "· 若按键无反应，请先关闭正在打开的 UI 面板或对话窗口。\n";

    private static class UiUtil
    {
        public static TMP_Text Label(Transform parent, string text, int size, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            ThustoryUIFont.Apply(tmp);
            return tmp;
        }

        public static GameObject Button(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.35f, 0.55f, 1f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4, 2);
            trt.offsetMax = new Vector2(-4, -2);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 13;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            ThustoryUIFont.Apply(tmp);
            return go;
        }

        /// <summary>社工条：左文右窄按钮，固定在菜单中部，避免卷入 ScrollView 被拉成满屏宽条。</summary>
        public static void AddOrgJoinRow(Transform stripRoot, string displayName, string orgId, Action<string> onJoin)
        {
            var row = new GameObject("OrgRow_" + (orgId ?? "x"));
            row.transform.SetParent(stripRoot, false);
            row.AddComponent<RectTransform>();
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.padding = new RectOffset(4, 4, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            var leRow = row.AddComponent<LayoutElement>();
            leRow.minHeight = 34;
            leRow.preferredHeight = 34;

            var txtGo = new GameObject("Info");
            txtGo.transform.SetParent(row.transform, false);
            txtGo.AddComponent<RectTransform>();
            var info = txtGo.AddComponent<TextMeshProUGUI>();
            info.text = displayName ?? orgId;
            info.fontSize = 12;
            info.color = new Color(0.93f, 0.94f, 0.98f, 1f);
            info.enableWordWrapping = true;
            info.alignment = TextAlignmentOptions.MidlineLeft;
            ThustoryUIFont.Apply(info);
            var leTxt = txtGo.AddComponent<LayoutElement>();
            leTxt.minWidth = 100;
            leTxt.flexibleWidth = 1f;
            leTxt.preferredHeight = 32;

            string oid = orgId ?? "";
            var btnGo = CompactJoinButton(row.transform, "加入", () => onJoin?.Invoke(oid));
        }

        static GameObject CompactJoinButton(Transform parent, string label, Action onClick)
        {
            var go = new GameObject("JoinBtn");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 72;
            le.preferredWidth = 72;
            le.flexibleWidth = 0f;
            le.minHeight = 30;
            le.preferredHeight = 30;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.28f, 0.52f, 0.82f, 1f);
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(2, 2);
            trt.offsetMax = new Vector2(-2, -2);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 13;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            ThustoryUIFont.Apply(tmp);
            return go;
        }

        /// <summary>挂到带 GridLayoutGroup 的条上，由网格统一分配尺寸，避免横向溢出。</summary>
        public static GameObject GridBarButton(Transform barParent, string label, Action onClick)
        {
            var go = new GameObject("Btn_" + label.GetHashCode());
            go.transform.SetParent(barParent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 32f;
            le.preferredHeight = 32f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.35f, 0.55f, 1f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4, 2);
            trt.offsetMax = new Vector2(-4, -2);
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 13;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            ThustoryUIFont.Apply(tmp);
            return go;
        }
    }
}
