using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;
using QinghuaStory;

// ══════════════════════════════════════════════
// 滚动区宽度同步（防止 TMP 布局循环崩溃）
// ══════════════════════════════════════════════

public class TMPInputFieldScrollSync:MonoBehaviour
{
    public RectTransform scrollContent;
    public TMP_Text textComponent;
    private float _lastWidth = -1f;

    private void LateUpdate()
    {
        if(scrollContent==null||textComponent==null) return;
        float w = Mathf.Max(100f,textComponent.preferredWidth);
        if(_lastWidth<0||Mathf.Abs(w-_lastWidth)>1f)
        {
            _lastWidth=w;
            scrollContent.sizeDelta=new Vector2(w,scrollContent.sizeDelta.y);
        }
    }
}

// ══════════════════════════════════════════════
// NPCManager —— 与 Python 后端通信，管理聊天 UI
// ══════════════════════════════════════════════

/// <summary>
/// 挂在 GameManager GameObject 上。
/// NPCInteraction 调用 ShowChatUI(npcId)，此脚本完成后续所有工作。
/// </summary>
public class NPCManager:MonoBehaviour
{
    public static NPCManager Instance { get; private set; }

    /// <summary>人物对话面板是否显示（用于屏蔽全局快捷键）</summary>
    public bool IsChatOpen => _chatCanvas != null && _chatCanvas.activeSelf;

    /// <summary>对话打开或任意 TMP/Unity 输入框处于焦点时，不处理地图/Y/活动 F 等全局热键</summary>
    public static bool ShouldSuppressGlobalHotkeys()
    {
        if (DailySummaryUI.IsOpen)
            return true;
        if (ActivityPresentationUI.IsOpen)
            return true;
        if (Instance != null && Instance.IsChatOpen)
            return true;
        var es = EventSystem.current;
        if (es == null) return false;
        GameObject sel = es.currentSelectedGameObject;
        if (sel == null) return false;
        return sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<InputField>() != null;
    }

    // ── NPC 配置 ──
    [Header("NPC 配置")]
    public string defaultNpcId = "lin_wanqing";

    [Tooltip("NPC 头像，按顺序对应：林晚晴/陈奕然/沈星辞/李娟/王玉霞/张锟霖/赵晓")]
    public Sprite[] npcAvatars = new Sprite[7];

    // ── 外观 ──
    [Header("对话框外观")]
    public TMP_FontAsset chatFont;
    public Sprite dialogBoxBackground;
    [Tooltip("背景图显示：Simple 拉伸 | Sliced 九宫格(在 Sprite 中设置 Border 可保持边框不变形)")]
    public Image.Type backgroundImageType = Image.Type.Simple;
    [Tooltip("勾选则保持背景图原始宽高比，避免变形")]
    public bool backgroundPreserveAspect;
    [Tooltip("对话框宽度占屏幕比例 (0~1)")]
    public float boxWidthRatio = 0.9f;
    [Tooltip("对话框高度占屏幕比例 (0~1)")]
    public float boxHeightRatio = 0.32f;
    [Tooltip("头像宽度(像素)")]
    public float avatarSize = 80f;
    [Tooltip("头像高度(像素)，0 则与宽度相同(正方形)")]
    public float avatarSizeHeight;
    [Tooltip("头像与文字的间距")]
    public float avatarTextGap = 16f;
    [Tooltip("头像左侧边距")]
    public float avatarOffsetX = 15f;
    [Tooltip("头像垂直偏移(正值上移)")]
    public float avatarOffsetY = 0f;
    [Header("头像/名称/好感度 位置(可独立调整)")]
    [Tooltip("NPC 名称相对面板左上角的 X 偏移")]
    public float nameOffsetX = 15f;
    [Tooltip("NPC 名称相对面板顶部的 Y 偏移(正值=向下)")]
    public float nameOffsetY = 12f;
    [Tooltip("NPC 名称宽度，0 则与头像同宽")]
    public float nameWidth;
    [Tooltip("好感度文字左侧 X 偏移")]
    public float friendshipOffsetX = 111f;
    [Tooltip("好感度文字相对面板顶部的 Y 偏移(正值=向下)")]
    public float friendshipOffsetY = 12f;
    [Tooltip("好感度区域高度")]
    public float friendshipHeight = 22f;
    [Tooltip("关闭按钮相对面板右上角的 X 偏移(负值=向左)")]
    public float closeButtonOffsetX = -5f;
    [Tooltip("关闭按钮相对面板顶部的 Y 偏移(负值=向上)")]
    public float closeButtonOffsetY = -5f;
    [Tooltip("关闭按钮宽高")]
    public float closeButtonSize = 30f;
    [Tooltip("内容区内边距")]
    public float contentPaddingRight = 20f;
    public float contentPaddingTop = 12f;
    public float contentPaddingBottom = 12f;

    [Header("名称/好感度/回复 颜色与字体")]
    [Tooltip("NPC 名称颜色（深色底图可在 Inspector 改为浅色字）")]
    public Color npcNameColor = Color.black;
    [Tooltip("NPC 名称字体，留空用 chatFont")]
    public TMP_FontAsset npcNameFont;
    [Tooltip("NPC 名称字号")]
    public int npcNameFontSize = 16;
    [Tooltip("好感度文字颜色（深色底图可改浅色）")]
    public Color friendshipColor = Color.black;
    [Tooltip("好感度字体，留空用 chatFont")]
    public TMP_FontAsset friendshipFont;
    [Tooltip("好感度字号")]
    public int friendshipFontSize = 14;
    [Tooltip("NPC 回复默认色（等回复时；有情绪则用下方情绪色）")]
    public Color npcResponseColor = Color.black;
    [Tooltip("NPC 回复字体，留空用 chatFont")]
    public TMP_FontAsset npcResponseFont;
    [Tooltip("NPC 回复字号")]
    public int npcResponseFontSize = 18;

    [Header("情绪颜色(回复时的情绪色，若不需要可全设为同一颜色)")]
    [Tooltip("针对黄色对话底：用深暖/深冷色，避免与底图撞色；仍依赖后端 emotion 字段")]
    public Color colorHappy = new Color(0.62f, 0.28f, 0.06f);
    public Color colorSad = new Color(0.12f, 0.30f, 0.72f);
    public Color colorAngry = new Color(0.72f, 0.08f, 0.12f);
    public Color colorSurprised = new Color(0.05f, 0.48f, 0.38f);
    public Color colorNeutral = new Color(0.16f, 0.15f, 0.14f);

    // ── Inspector 可拖入的 UI（留空则自动创建）──
    [Header("UI References（留空自动创建）")]
    public TMP_InputField playerInputField;
    public TMP_Text npcResponseText;
    public TMP_Text friendshipText;
    public TMP_Text npcNameText;
    public Button sendButton;

    // ── 私有 ──
    public GameObject _chatCanvas;
    private RectTransform _panelRt,_avatarRt,_nameRt,_friendshipRt,_responseRt,_inputCanvasRt,_sendBtnRt,_closeBtnRt;
    private Image _panelImage;
    private TMP_Text _playerInputTmpText;
    private TMP_Text _inputPlaceholderTmp;
    private TMP_Text _sendButtonLabelTmp;
    private TMP_Text _closeButtonLabelTmp;
    private string _currentNpcId;
    private int _chatVisibleFrames;
    private bool _isPosting; // 防止 Enter+按钮 同帧触发两次 SendChat

    // 对话历史（显示在回复区上方的滚动列表）
    private readonly List<string> _history = new();
    private const int MaxHistory = 20;
    private const int VisibleHistoryLines = 2;

    // NPC ID → 头像索引
    private static readonly Dictionary<string,int> NpcAvatarIndex = new()
    {
        {"lin_wanqing", 0}, {"chen_yiran", 1}, {"shen_xingci", 2},
        {"li_juan", 3}, {"wang_yuxia", 4}, {"zhang_kunlin", 5}, {"zhao_xiao", 6}
    };

    // ══════════════════════════════════════════
    // Unity 生命周期
    // ══════════════════════════════════════════

    public GameObject player;
    private playercontrol pc;
    private panelctrl pan;

    private void Awake()
    {
        if(Instance!=null&&Instance!=this) { Destroy(gameObject); return; }
        Instance=this;
        DontDestroyOnLoad(gameObject);

        APIManager.EnsureExists();

        if(_chatCanvas==null)
            CreateChatUI();
        _chatCanvas?.SetActive(false);
        pc=FindObjectOfType<playercontrol>();
        pan=FindObjectOfType<panelctrl>();
    }

    private void Update()
    {
        if(_chatCanvas==null||!_chatCanvas.activeSelf) return;
        _chatVisibleFrames++;

        // Enter 键发送
        if(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter))
            SendChat();

        // 右键关闭（同帧保护）
        if(_chatVisibleFrames>2&&Input.GetMouseButtonDown(1))
            HideChatUI();
    }

    private void OnValidate()
    {
        if(Application.isPlaying&&_chatCanvas!=null&&_panelRt!=null)
            ApplyDialogStyle();
    }

    /// <summary>立即将 Inspector 中的布局参数应用到 UI。运行中在 Inspector 改完参数后可调用此方法刷新。</summary>
    public void ApplyLayoutNow() => ApplyDialogStyle();

    // ══════════════════════════════════════════
    // 公共接口
    // ══════════════════════════════════════════

    public void ShowChatUI(string npcId)
    {
        APIManager.EnsureExists();
        // 对话期间不暂停服务端时钟，游戏时间继续流逝（地图/菜单等仍用 ServerPauseCoordinator）
        if (pc != null) pc.canmove = false;
        if (pan != null) pan.talk=true;
        _currentNpcId=string.IsNullOrEmpty(npcId) ? defaultNpcId : npcId;
        _history.Clear();

        if(_chatCanvas==null) CreateChatUI();
        ApplyDialogStyle();
        _chatCanvas?.SetActive(true);
        _chatVisibleFrames=0;

        // 设置 NPC 头像
        SetNpcAvatar(_currentNpcId);

        // 设置 NPC 名称
        if(npcNameText!=null)
            npcNameText.text=GetNpcDisplayName(_currentNpcId);

        if(npcResponseText!=null)
        {
            npcResponseText.text=$"[{GetNpcDisplayName(_currentNpcId)}] 等待对话...";
            npcResponseText.color=npcResponseColor;
        }
        if(friendshipText!=null) friendshipText.text="好感度: --";

        if(playerInputField!=null)
        {
            playerInputField.text="";
            playerInputField.interactable=false;
            StartCoroutine(DeferActivation());
        }
    }

    public void HideChatUI()
    {
        if (pc != null) pc.canmove = true;
        if (pan != null) pan.talk=false;
        _chatCanvas?.SetActive(false);

    }

    public void SendChat(string npcId = null)
    {
        if(_isPosting) return; // 防止 Enter 与按钮同帧触发两次
        if(string.IsNullOrEmpty(npcId)) npcId=_currentNpcId??defaultNpcId;
        if(playerInputField==null) return;

        string message = playerInputField.text?.Trim();
        if(string.IsNullOrEmpty(message)) return;

        _isPosting=true;
        playerInputField.text="";
        StartCoroutine(PostToNPC(npcId,message));
    }

    // ══════════════════════════════════════════
    // HTTP 请求
    // ══════════════════════════════════════════

    private IEnumerator PostToNPC(string npcId,string message)
    {
        SetInputEnabled(false);
        if(npcResponseText!=null)
        {
            npcResponseText.text=AppendHistory($"你：{message}")+"\n[思考中...]";
            npcResponseText.color=npcResponseColor;
            ClearTmpStackingStyle(npcResponseText);
        }

        APIManager.EnsureExists();
        if (APIManager.Instance == null)
        {
            if (npcResponseText != null)
                npcResponseText.text = GetDisplayHistory() + "\n（APIManager 未初始化）";
            _isPosting = false;
            SetInputEnabled(true);
            yield break;
        }

        ChatResponseV21 response = null;
        string reqError = null;
        bool done = false;
        APIManager.Instance.ChatV21(npcId, message,
            r => { response = r; done = true; },
            e => { reqError = e; done = true; });

        while (!done) yield return null;

        if (response == null && !string.IsNullOrEmpty(reqError))
            Debug.LogWarning("[NPCManager] 对话请求失败: " + reqError);

        ApplyResponseV21(npcId, message, response);
        _isPosting=false;
        SetInputEnabled(true);
    }

    private void ApplyResponseV21(string npcId,string playerMsg,ChatResponseV21 res)
    {
        if(res==null||string.IsNullOrEmpty(res.reply))
        {
            if(npcResponseText!=null)
                npcResponseText.text=GetDisplayHistory()+"\n（连接失败，请检查后端是否启动）";
            return;
        }

        // 追加历史（玩家消息已在 PostToNPC 中追加，此处只追加 NPC 回复）
        string npcName = string.IsNullOrEmpty(res.npc_name) ? GetNpcDisplayName(npcId) : res.npc_name;
        string historyText = AppendHistory($"{npcName}：{res.reply}");

        if(npcResponseText!=null)
        {
            npcResponseText.text=historyText;
            npcResponseText.color=GetEmotionColor(res.emotion);
            ClearTmpStackingStyle(npcResponseText);
        }

        // 好感度显示
        if(friendshipText!=null)
        {
            string tier = string.IsNullOrEmpty(res.friendship_tier) ? "" : $"（{res.friendship_tier}）";
            string change = res.friendship_change>0 ? $" <color=#44FF88>+{res.friendship_change}</color>" :
                            res.friendship_change<0 ? $" <color=#FF6666>{res.friendship_change}</color>" : "";
            friendshipText.text=$"好感度: {res.current_friendship}/100{tier}{change}";
            ClearTmpStackingStyle(friendshipText);
        }

        // 同步到 PlayerManager
        if(PlayerManager.Instance!=null)
            PlayerManager.Instance.OnNpcFriendshipChanged(npcId,res.current_friendship);

        // 新解锁通知
        if(res.newly_unlocked!=null&&res.newly_unlocked.Length>0)
        {
            foreach(var flag in res.newly_unlocked)
                ShowUnlockNotification(flag);
        }

        // 对话可能影响全局状态，刷新服务端数据
        PlayerManager.Instance?.RefreshFromServer();
    }

    // ══════════════════════════════════════════
    // UI 辅助
    // ══════════════════════════════════════════

    private string AppendHistory(string line)
    {
        _history.Add(line);
        if(_history.Count>MaxHistory)
            _history.RemoveAt(0);
        return GetDisplayHistory();
    }

    /// <summary>取最近 VisibleHistoryLines 行用于显示，旧对话隐藏</summary>
    private string GetDisplayHistory()
    {
        if(_history.Count==0) return "";
        int take = Mathf.Min(VisibleHistoryLines, _history.Count);
        int start = _history.Count - take;
        var lines = new List<string>();
        for(int i=start;i<_history.Count;i++) lines.Add(_history[i]);
        return string.Join("\n", lines);
    }

    private void SetInputEnabled(bool enabled)
    {
        if(playerInputField!=null) playerInputField.interactable=enabled;
        if(sendButton!=null) sendButton.interactable=enabled;
    }

    private Color GetEmotionColor(string emotion) => emotion switch
    {
        "happy" => colorHappy,
        "sad" => colorSad,
        "angry" => colorAngry,
        "surprised" => colorSurprised,
        _ => colorNeutral
    };

    private IEnumerator DeferActivation()
    {
        yield return null;
        if(playerInputField!=null)
        {
            playerInputField.interactable=true;
            playerInputField.Select();
            playerInputField.ActivateInputField();
        }
    }

    private void SetNpcAvatar(string npcId)
    {
        if(npcAvatars==null) return;
        NpcAvatarIndex.TryGetValue(npcId,out int idx);
        if(idx<npcAvatars.Length&&npcAvatars[idx]!=null)
        {
            var avatarImg = _chatCanvas?.GetComponentInChildren<Image>();
            // 找到名叫 "Avatar" 的 Image
            var imgs = _chatCanvas?.GetComponentsInChildren<Image>(true);
            if(imgs==null) return;
            foreach(var img in imgs)
                if(img.name=="Avatar") { img.sprite=npcAvatars[idx]; img.color=Color.white; return; }
        }
    }

    private void ShowUnlockNotification(string flag)
    {
        string msg = flag switch
        {
            "lab_access" => "🔓 解锁：实验室使用权限！",
            "boyfriend_unlocked" => "💕 解锁：与沈星辞确认恋爱关系！",
            "mentor_close" => "🌟 解锁：与林晚晴建立深厚师生情！",
            "research_project" => "🔬 解锁：科研项目参与资格！",
            _ => $"🔓 解锁新内容：{flag}"
        };
        // 通知 GameHUD
        GameHUD.Instance?.ShowNotification(msg,4f);
        Debug.Log("[NPCManager] "+msg);
    }

    public static string GetNpcDisplayName(string id) => id switch
    {
        "lin_wanqing" => "林晚晴",
        "chen_yiran" => "陈奕然",
        "shen_xingci" => "沈星辞",
        "li_juan" => "李娟",
        "wang_yuxia" => "王玉霞",
        "zhang_kunlin" => "张锟霖",
        "zhao_xiao" => "赵晓",
        _ => id
    };

    // ══════════════════════════════════════════
    // 自动创建 UI（原版逻辑升级版）
    // ══════════════════════════════════════════

    private float AvatarHeight => avatarSizeHeight>0 ? avatarSizeHeight : avatarSize;

    /// <summary>将 Inspector 中的外观参数应用到已创建的 UI（运行中修改即时生效）</summary>
    private void ApplyDialogStyle()
    {
        if(_panelRt==null) return;
        float contentLeft = avatarOffsetX+avatarSize+avatarTextGap;

        _panelRt.anchorMin=new Vector2(0.5f-boxWidthRatio*0.5f,0f);
        _panelRt.anchorMax=new Vector2(0.5f+boxWidthRatio*0.5f,0f);
        _panelRt.sizeDelta=new Vector2(0,600f*boxHeightRatio);

        if(_panelImage!=null){
            _panelImage.type=backgroundImageType;
            _panelImage.preserveAspect=backgroundPreserveAspect;
        }
        if(_avatarRt!=null){
            _avatarRt.anchoredPosition=new Vector2(avatarOffsetX,avatarOffsetY);
            _avatarRt.sizeDelta=new Vector2(avatarSize,AvatarHeight);
        }
        if(_nameRt!=null){
            _nameRt.anchoredPosition=new Vector2(nameOffsetX,-nameOffsetY);
            float nw = nameWidth>0 ? nameWidth : avatarSize;
            _nameRt.sizeDelta=new Vector2(nw,24f);
        }
        if(_friendshipRt!=null){
            _friendshipRt.offsetMin=new Vector2(friendshipOffsetX,-friendshipOffsetY-friendshipHeight);
            _friendshipRt.offsetMax=new Vector2(-contentPaddingRight,-friendshipOffsetY);
        }
        if(_responseRt!=null){
            _responseRt.offsetMin=new Vector2(contentLeft,contentPaddingBottom+46f);
            _responseRt.offsetMax=new Vector2(-contentPaddingRight,-friendshipOffsetY-friendshipHeight-6f);
        }
        if(_inputCanvasRt!=null){
            _inputCanvasRt.offsetMin=new Vector2(contentLeft,contentPaddingBottom);
            _inputCanvasRt.offsetMax=new Vector2(-120f,contentPaddingBottom+36f);
        }
        if(_sendBtnRt!=null){
            _sendBtnRt.anchoredPosition=new Vector2(-contentPaddingRight,contentPaddingBottom);
        }
        if(_closeBtnRt!=null){
            _closeBtnRt.anchoredPosition=new Vector2(closeButtonOffsetX,closeButtonOffsetY);
            _closeBtnRt.sizeDelta=new Vector2(closeButtonSize,closeButtonSize);
        }
        ApplyTextStyle();
    }

    private void ApplyTextStyle()
    {
        if(npcNameText!=null){
            npcNameText.color=npcNameColor;
            npcNameText.fontSize=npcNameFontSize;
            if(npcNameFont!=null) npcNameText.font=npcNameFont;
            else if(chatFont!=null) npcNameText.font=chatFont;
        }
        if(friendshipText!=null){
            friendshipText.color=friendshipColor;
            friendshipText.fontSize=friendshipFontSize;
            if(friendshipFont!=null) friendshipText.font=friendshipFont;
            else if(chatFont!=null) friendshipText.font=chatFont;
        }
        if(npcResponseText!=null){
            npcResponseText.color=npcResponseColor;
            npcResponseText.fontSize=npcResponseFontSize;
            if(npcResponseFont!=null) npcResponseText.font=npcResponseFont;
            else if(chatFont!=null) npcResponseText.font=chatFont;
        }
        ClearTmpStackingStyle(npcNameText);
        ClearTmpStackingStyle(friendshipText);
        ClearTmpStackingStyle(npcResponseText);
        ClearTmpStackingStyle(_playerInputTmpText);
        ClearTmpStackingStyle(_inputPlaceholderTmp);
        ClearTmpStackingStyle(_sendButtonLabelTmp);
        ClearTmpStackingStyle(_closeButtonLabelTmp);
    }

    /// <summary>仅用 Face 色：关闭 TMP 描边与 Underlay，避免叠在字上的第二层效果。</summary>
    private static void ClearTmpStackingStyle(TMP_Text t)
    {
        if(t==null) return;
        ShaderUtilities.GetShaderPropertyIDs();
        t.outlineWidth=0f;
        Material m=t.fontMaterial;
        if(m!=null&&m.HasProperty(ShaderUtilities.ID_UnderlayColor))
            m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
    }

    private void CreateChatUI()
    {
        try
        {
            if(chatFont==null)
                chatFont=ThustoryUIFont.GetDefaultCjkFont();

            var canvasGo = new GameObject("NPCChatCanvas");
            canvasGo.layer=5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=150;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1140,600);  // 与 Fixed1140x600Camera 一致
            scaler.matchWidthOrHeight=0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one;
            rt.offsetMin=rt.offsetMax=Vector2.zero;

            // ── 底部对话面板 ──
            var panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGo.transform,false);
            var pRt = panel.AddComponent<RectTransform>();
            pRt.anchorMin=new Vector2(0.5f-boxWidthRatio*0.5f,0f);
            pRt.anchorMax=new Vector2(0.5f+boxWidthRatio*0.5f,0f);
            pRt.pivot=new Vector2(0.5f,0f);
            pRt.anchoredPosition=Vector2.zero;
            pRt.sizeDelta=new Vector2(0,600f*boxHeightRatio);
            _panelRt=pRt;
            var pImg = panel.AddComponent<Image>();
            if(dialogBoxBackground!=null) {
                pImg.sprite=dialogBoxBackground;
                pImg.color=Color.white;
                pImg.type=backgroundImageType;
                pImg.preserveAspect=backgroundPreserveAspect;
            }
            else pImg.color=new Color(0.12f,0.10f,0.08f,0.96f);
            _panelImage=pImg;

            float contentLeft = avatarOffsetX+avatarSize+avatarTextGap;

            // ── 头像 ──
            var avatarObj = new GameObject("Avatar");
            avatarObj.transform.SetParent(panel.transform,false);
            var aRt = avatarObj.AddComponent<RectTransform>();
            aRt.anchorMin=new Vector2(0f,0.5f); aRt.anchorMax=new Vector2(0f,0.5f);
            aRt.pivot=new Vector2(0f,0.5f);
            aRt.anchoredPosition=new Vector2(avatarOffsetX,avatarOffsetY);
            aRt.sizeDelta=new Vector2(avatarSize,AvatarHeight);
            _avatarRt=aRt;
            var aImg = avatarObj.AddComponent<Image>();
            aImg.color=new Color(0.5f,0.5f,0.5f,0.8f);

            // ── NPC 名称（头像正上方） ──
            var nameObj = new GameObject("NPCNameText");
            nameObj.transform.SetParent(panel.transform,false);
            var nameRt = nameObj.AddComponent<RectTransform>();
            nameRt.anchorMin=new Vector2(0f,1f); nameRt.anchorMax=new Vector2(0f,1f);
            nameRt.pivot=new Vector2(0f,1f);
            nameRt.anchoredPosition=new Vector2(nameOffsetX,-nameOffsetY);
            nameRt.sizeDelta=new Vector2(nameWidth>0 ? nameWidth : avatarSize,24f);
            _nameRt=nameRt;
            npcNameText=nameObj.AddComponent<TextMeshProUGUI>();
            npcNameText.fontSize=npcNameFontSize; npcNameText.color=npcNameColor;
            npcNameText.alignment=TextAlignmentOptions.Center;
            npcNameText.text="";
            if(npcNameFont!=null) npcNameText.font=npcNameFont;
            else if(chatFont!=null) npcNameText.font=chatFont;
            // ── 好感度文字 ──
            var fGo = new GameObject("FriendshipText");
            fGo.transform.SetParent(panel.transform,false);
            var fRt = fGo.AddComponent<RectTransform>();
            fRt.anchorMin=new Vector2(0f,1f); fRt.anchorMax=new Vector2(1f,1f);
            fRt.pivot=new Vector2(0f,1f);
            fRt.offsetMin=new Vector2(friendshipOffsetX,-friendshipOffsetY-friendshipHeight);
            fRt.offsetMax=new Vector2(-contentPaddingRight,-friendshipOffsetY);
            _friendshipRt=fRt;
            friendshipText=fGo.AddComponent<TextMeshProUGUI>();
            friendshipText.fontSize=friendshipFontSize; friendshipText.color=friendshipColor;
            friendshipText.text="好感度: --";
            if(friendshipFont!=null) friendshipText.font=friendshipFont;
            else if(chatFont!=null) friendshipText.font=chatFont;
            // ── NPC 回复文本（历史对话显示区）──
            var respGo = new GameObject("NPCResponseText");
            respGo.transform.SetParent(panel.transform,false);
            var rRt = respGo.AddComponent<RectTransform>();
            rRt.anchorMin=new Vector2(0f,0f); rRt.anchorMax=new Vector2(1f,1f);
            rRt.offsetMin=new Vector2(contentLeft,contentPaddingBottom+46f);
            rRt.offsetMax=new Vector2(-contentPaddingRight,-friendshipOffsetY-friendshipHeight-6f);
            _responseRt=rRt;
            npcResponseText=respGo.AddComponent<TextMeshProUGUI>();
            npcResponseText.fontSize=npcResponseFontSize; npcResponseText.color=npcResponseColor;
            npcResponseText.text="（等待对话...）"; npcResponseText.enableWordWrapping=true;
            npcResponseText.alignment=TextAlignmentOptions.BottomLeft;
            npcResponseText.overflowMode=TextOverflowModes.Overflow;
            if(npcResponseFont!=null) npcResponseText.font=npcResponseFont;
            else if(chatFont!=null) npcResponseText.font=chatFont;
            // ── 输入框子 Canvas（防止布局重建循环崩溃）──
            var inputCanvasGo = new GameObject("InputFieldCanvas");
            inputCanvasGo.transform.SetParent(panel.transform,false);
            var icRt = inputCanvasGo.AddComponent<RectTransform>();
            icRt.anchorMin=new Vector2(0f,0f); icRt.anchorMax=new Vector2(1f,0f);
            icRt.pivot=new Vector2(0f,0f);
            icRt.offsetMin=new Vector2(contentLeft,contentPaddingBottom);
            icRt.offsetMax=new Vector2(-120f,contentPaddingBottom+36f);
            _inputCanvasRt=icRt;
            var ic = inputCanvasGo.AddComponent<Canvas>();
            ic.overrideSorting=true; ic.sortingOrder=151;
            inputCanvasGo.AddComponent<GraphicRaycaster>();

            var inputGo = new GameObject("PlayerInputField");
            inputGo.transform.SetParent(inputCanvasGo.transform,false);
            var iRt = inputGo.AddComponent<RectTransform>();
            iRt.anchorMin=Vector2.zero; iRt.anchorMax=Vector2.one;
            iRt.offsetMin=iRt.offsetMax=Vector2.zero;
            playerInputField=inputGo.AddComponent<TMP_InputField>();

            var inputBg = new GameObject("Background");
            inputBg.transform.SetParent(inputGo.transform,false);
            var ibRt = inputBg.AddComponent<RectTransform>();
            ibRt.anchorMin=Vector2.zero; ibRt.anchorMax=Vector2.one; ibRt.offsetMin=ibRt.offsetMax=Vector2.zero;
            inputBg.AddComponent<Image>().color=new Color(0.18f,0.18f,0.18f,1f);

            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGo.transform,false);
            var taRt = textArea.AddComponent<RectTransform>();
            taRt.anchorMin=Vector2.zero; taRt.anchorMax=Vector2.one;
            taRt.offsetMin=new Vector2(10f,5f); taRt.offsetMax=new Vector2(-10f,-5f);
            textArea.AddComponent<RectMask2D>();

            var tTmp = new GameObject("Text").AddComponent<TextMeshProUGUI>();
            tTmp.transform.SetParent(textArea.transform,false);
            var tRt = tTmp.rectTransform;
            tRt.anchorMin=Vector2.zero; tRt.anchorMax=Vector2.one; tRt.offsetMin=tRt.offsetMax=Vector2.zero;
            tRt.pivot=new Vector2(0f,0.5f);
            tTmp.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
            tTmp.fontSize=17; tTmp.color=Color.white;
            tTmp.overflowMode=TextOverflowModes.Overflow;
            if(chatFont!=null) tTmp.font=chatFont;
            _playerInputTmpText=tTmp;
            playerInputField.textComponent=tTmp;
            playerInputField.textViewport=taRt;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(inputGo.transform,false);
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.anchorMin=Vector2.zero; phRt.anchorMax=Vector2.one;
            phRt.offsetMin=new Vector2(10f,5f); phRt.offsetMax=new Vector2(-10f,-5f);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text="输入消息... （Enter发送）"; phTmp.fontSize=17;
            phTmp.color=new Color(0.5f,0.5f,0.5f,0.6f);
            if(chatFont!=null) phTmp.font=chatFont;
            _inputPlaceholderTmp=phTmp;
            playerInputField.placeholder=phTmp;
            playerInputField.caretBlinkRate=0f;

            // ── 发送按钮 ──
            var btnGo = new GameObject("SendButton");
            btnGo.transform.SetParent(panel.transform,false);
            var bRt = btnGo.AddComponent<RectTransform>();
            bRt.anchorMin=new Vector2(1f,0f); bRt.anchorMax=new Vector2(1f,0f);
            bRt.pivot=new Vector2(1f,0f);
            bRt.anchoredPosition=new Vector2(-contentPaddingRight,contentPaddingBottom);
            _sendBtnRt=bRt;
            bRt.sizeDelta=new Vector2(100f,36f);
            sendButton=btnGo.AddComponent<Button>();
            btnGo.AddComponent<Image>().color=new Color(0.15f,0.45f,0.15f,1f);
            var bTxt = new GameObject("Text").AddComponent<TextMeshProUGUI>();
            bTxt.transform.SetParent(btnGo.transform,false);
            var btRt = bTxt.rectTransform;
            btRt.anchorMin=Vector2.zero; btRt.anchorMax=Vector2.one; btRt.offsetMin=btRt.offsetMax=Vector2.zero;
            bTxt.text="发送"; bTxt.fontSize=17; bTxt.color=Color.white;
            bTxt.alignment=TextAlignmentOptions.Center;
            if(chatFont!=null) bTxt.font=chatFont;
            _sendButtonLabelTmp=bTxt;
            sendButton.targetGraphic=btnGo.GetComponent<Image>();
            sendButton.onClick.AddListener(() => SendChat(_currentNpcId));

            // ── 关闭按钮（右上角） ──
            var clsGo = new GameObject("CloseButton");
            clsGo.transform.SetParent(panel.transform,false);
            var cRt = clsGo.AddComponent<RectTransform>();
            cRt.anchorMin=new Vector2(1f,1f); cRt.anchorMax=new Vector2(1f,1f);
            cRt.pivot=new Vector2(1f,1f);
            cRt.anchoredPosition=new Vector2(closeButtonOffsetX,closeButtonOffsetY);
            cRt.sizeDelta=new Vector2(closeButtonSize,closeButtonSize);
            _closeBtnRt=cRt;
            var clsBtn = clsGo.AddComponent<Button>();
            clsGo.AddComponent<Image>().color=new Color(0.55f,0.18f,0.18f,1f);
            var cTxt = new GameObject("Text").AddComponent<TextMeshProUGUI>();
            cTxt.transform.SetParent(clsGo.transform,false);
            var ctRt = cTxt.rectTransform;
            ctRt.anchorMin=Vector2.zero; ctRt.anchorMax=Vector2.one; ctRt.offsetMin=ctRt.offsetMax=Vector2.zero;
            cTxt.text="X"; cTxt.fontSize=17; cTxt.color=Color.white;
            cTxt.alignment=TextAlignmentOptions.Center;
            if(chatFont!=null) cTxt.font=chatFont;
            _closeButtonLabelTmp=cTxt;
            clsBtn.targetGraphic=clsGo.GetComponent<Image>();
            clsBtn.onClick.AddListener(HideChatUI);

            _chatCanvas=canvasGo;
        }
        catch(Exception ex)
        {
            Debug.LogError("[NPCManager] CreateChatUI 异常: "+ex);
        }
    }
}
