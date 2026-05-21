using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// NPC 右键交互组件
/// 挂在 NPC 物体上，右键点击时触发交互
/// 左键推进对话框
/// </summary>
public class NPCInteraction:MonoBehaviour
{
    [Header("距离限制")]
    [Tooltip("Player 与 NPC 的最近距离需在此范围内才能交互（世界单位）")]
    public float interactionDistance = 10f;

    [Tooltip("鼠标世界位置与 NPC 的最近距离需在此范围内才能交互（世界单位）")]
    public float mouseProximityRadius = 3f;

    [Header("可交互提示")]
    [Tooltip("可交互时在鼠标上方显示提示")]
    public bool showChatPromptIcon = true;

    [Tooltip("自定义提示图（Sprite），留空则显示红色感叹号")]
    public Sprite chatPromptIconSprite;

    [Tooltip("提示距离鼠标的像素偏移（正值=在鼠标上方）")]
    public float promptIconOffsetY = 25f;

    [Tooltip("Player 物体，留空则自动查找名为 player 的对象")]
    public GameObject play;

    [Header("AI 聊天（优先）")]
    [Tooltip("勾选后右键交互时显示 AI 聊天框，与 Python 后端通信")]
    public bool useAIChat = true;
    [Tooltip("该 NPC 的 ID，发送给 Python 时使用")]
    public string npcId = "lin_wanqing";

    [Header("静态对话（useAIChat 为 false 时使用）")]
    [Tooltip("对话条目（右键开始，左键推进）")]
    public List<DialogueBox.DialogueEntry> dialogueEntries = new List<DialogueBox.DialogueEntry>();

    [Header("交互设置")]
    [Tooltip("可自定义交互界面的 GameObject，例如对话气泡、选项菜单")]
    public GameObject interactionUI;

    [Tooltip("是否每次点击都切换显示/隐藏")]
    public bool toggleOnClick = true;

    [Header("内置交互提示")]
    [Tooltip("勾选后会在屏幕上显示「已与 XXX 交互！」的提示文字")]
    public bool showDefaultPrompt = true;

    [Tooltip("提示文字显示时长（秒）")]
    public float promptDuration = 2f;

    [Header("可选：调试用")]
    [Tooltip("勾选后会在 Console 输出交互信息")]
    public bool debugLog = true;

    [Header("事件")]
    public UnityEvent onInteract;

    private Camera _mainCamera;
    private bool _isUIVisible;
    private GameObject _promptCanvas;
    private float _promptHideTime;
    private GameObject _chatPromptIcon;
    private RectTransform _chatPromptRect;
    private Transform player;
    private playercontrol pc;

    private void Awake()
    {
        _mainCamera=Camera.main;
        if(_mainCamera==null)
            _mainCamera=FindObjectOfType<Camera>();
        player=play.transform;
        pc=FindObjectOfType<playercontrol>();
        interactionDistance = 10f;
        mouseProximityRadius = 2f;
        npcId=this.name;
    }

    private void Update()
    {
        // 隐藏到期的交互成功提示
        if(_promptCanvas!=null&&Time.time>=_promptHideTime)
        {
            _promptCanvas.SetActive(false);
        }

        bool canInteract = IsPlayerNearby()&&IsMouseNearNPC();

        // 检查聊天是否打开
        bool isChatOpen = NPCManager.Instance!=null&&
                          NPCManager.Instance._chatCanvas!=null&&
                          NPCManager.Instance._chatCanvas.activeSelf;

        if(showChatPromptIcon)
        {
            // 只有在可交互且聊天未打开时才显示
            if(canInteract&&!isChatOpen)
            {
                if(_chatPromptIcon==null) CreateChatPromptIcon();
                _chatPromptIcon.SetActive(true);
                if(_chatPromptRect!=null)
                {
                    _chatPromptRect.position=new Vector2(Input.mousePosition.x,Input.mousePosition.y+promptIconOffsetY);
                }
            }
            else if(_chatPromptIcon!=null)
            {
                _chatPromptIcon.SetActive(false);
            }
        }

        // 检测右键点击：玩家靠近时，任意右键即触发 AI 对话
        if(!Input.GetMouseButtonDown(1)) return;
        if(!canInteract) return;

        OnNPCClicked();
    }

    /// <summary>
    /// 检查 Player 是否在交互距离内（若有 Collider2D 则用碰撞体最近点计算）
    /// </summary>
    private bool IsPlayerNearby()
    {
        if(player==null) return false;

        Vector2 npcPoint = GetNpcReferencePoint(player.position);
        float dist = Vector2.Distance(player.position,npcPoint);
        return dist<=interactionDistance;
    }

    /// <summary>
    /// 检查鼠标是否在 NPC 较近距离内（若有 Collider2D 则用碰撞体最近点计算）
    /// </summary>
    private bool IsMouseNearNPC()
    {
        if(_mainCamera==null) return false;
        Vector2 mouseWorld = GetMouseWorldPosition();
        Vector2 npcPoint = GetNpcReferencePoint(mouseWorld);
        float dist = Vector2.Distance(mouseWorld,npcPoint);
        return dist<=mouseProximityRadius;
    }

    /// <summary>
    /// 获取 NPC 的参考点（用于距离计算）。若有 Collider2D 则返回碰撞体上离目标最近的点，否则返回 transform 位置
    /// </summary>
    private Vector2 GetNpcReferencePoint(Vector2 fromPoint)
    {
        var col = GetComponent<Collider2D>();
        if(col!=null)
            return col.ClosestPoint(fromPoint);
        return transform.position;
    }

    private Vector2 GetMouseWorldPosition()
    {
        if(_mainCamera==null) return default;

        // 使用 NPC 所在平面的深度，适配 2D 正交相机
        float depth = Mathf.Abs(_mainCamera.transform.position.z-transform.position.z);
        Vector3 worldPos = _mainCamera.ScreenToWorldPoint(
            new Vector3(Input.mousePosition.x,Input.mousePosition.y,depth));
        return worldPos;
    }

    private void OnNPCClicked()
    {
        if(debugLog)
            Debug.Log($"[NPCInteraction] 与 {gameObject.name} 交互");

        onInteract?.Invoke();
        

        // 优先：AI 聊天（与 Python 后端通信）
        if(useAIChat)
        {
            var mgr = NPCManager.Instance!=null ? NPCManager.Instance : FindObjectOfType<NPCManager>();
            if(mgr==null)
            {
                var go = new GameObject("GameManager");
                mgr=go.AddComponent<NPCManager>();
            }
            if(mgr!=null)
            {
                if(DialogueBox.Instance!=null)
                    DialogueBox.Instance.Hide();
                // 不再禁用 DialogueCanvas，避免其上组件被禁用导致请求中断
                mgr.ShowChatUI(string.IsNullOrEmpty(npcId) ? gameObject.name : npcId);
                return;
            }
        }

        // 静态对话（useAIChat 为 false 或 NPCManager 不存在时）
        if(dialogueEntries!=null&&dialogueEntries.Count>0)
        {
            var db = DialogueBox.Instance;
            if(db==null)
            {
                var go = new GameObject("DialogueBox");
                go.AddComponent<DialogueBox>();
                db=DialogueBox.Instance;
            }
            db.Show(dialogueEntries);
            return;
        }

        // 显示内置交互提示
        if(showDefaultPrompt)
            ShowInteractionPrompt();

        if(interactionUI!=null)
        {
            if(toggleOnClick)
            {
                _isUIVisible=!_isUIVisible;
                interactionUI.SetActive(_isUIVisible);
            }
            else
                interactionUI.SetActive(true);
        }
    }

    /// <summary>
    /// 在屏幕中央显示交互成功提示
    /// </summary>
    private void ShowInteractionPrompt()
    {
        if(_promptCanvas==null)
        {
            CreatePromptUI();
        }

        var text = _promptCanvas.GetComponentInChildren<TextMeshProUGUI>();
        if(text!=null)
            text.text=$"已与 {gameObject.name} 交互！";

        _promptCanvas.SetActive(true);
        _promptHideTime=Time.time+promptDuration;
    }

    private void CreatePromptUI()
    {
        _promptCanvas=new GameObject("NPC交互提示");
        var canvas = _promptCanvas.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=200;

        var scaler = _promptCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1140,600);  // 与 Fixed1140x600Camera 一致
        scaler.matchWidthOrHeight=0.5f;

        _promptCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(_promptCanvas.transform,false);
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin=new Vector2(0.5f,0.5f);
        rect.anchorMax=new Vector2(0.5f,0.5f);
        rect.sizeDelta=new Vector2(400,80);
        rect.anchoredPosition=Vector2.zero;

        var image = panel.AddComponent<UnityEngine.UI.Image>();
        image.color=new Color(0,0,0,0.85f);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(panel.transform,false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin=Vector2.zero;
        textRect.anchorMax=Vector2.one;
        textRect.offsetMin=new Vector2(20,10);
        textRect.offsetMax=new Vector2(-20,-10);

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text=$"已与 {gameObject.name} 交互！";
        tmp.fontSize=28;
        tmp.color=Color.white;
        tmp.alignment=TextAlignmentOptions.Center;
        tmp.fontStyle=FontStyles.Bold;
        ThustoryUIFont.Apply(tmp);

        _promptCanvas.SetActive(false);
    }

    /// <summary>
    /// 创建可交互时的聊天提示（自定义图或红色感叹号）
    /// </summary>
    private void CreateChatPromptIcon()
    {
        _chatPromptIcon=new GameObject("NPC可交互提示");
        _chatPromptIcon.transform.SetParent(this.transform, false);
        var canvas = _chatPromptIcon.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=199;
        canvas.pixelPerfect=false;

        var scaler = _chatPromptIcon.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor=1f;
        scaler.referencePixelsPerUnit=100f;

        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(_chatPromptIcon.transform,false);
        _chatPromptRect=iconObj.AddComponent<RectTransform>();
        float size = chatPromptIconSprite!=null ? 48f : 40f;
        _chatPromptRect.sizeDelta=new Vector2(size,size);
        _chatPromptRect.anchorMin=new Vector2(0,0);
        _chatPromptRect.anchorMax=new Vector2(0,0);
        _chatPromptRect.pivot=new Vector2(0.5f,0.5f);

        if(chatPromptIconSprite!=null)
        {
            var img = iconObj.AddComponent<UnityEngine.UI.Image>();
            img.sprite=chatPromptIconSprite;
            img.color=Color.white;
            img.raycastTarget=false;
        }
        else
        {
            var tmp = iconObj.AddComponent<TextMeshProUGUI>();
            tmp.text="!";
            tmp.fontSize=36;
            tmp.color=Color.red;
            tmp.fontStyle=FontStyles.Bold;
            tmp.alignment=TextAlignmentOptions.Center;
            tmp.raycastTarget=false;
            ThustoryUIFont.Apply(tmp);
        }

        _chatPromptIcon.SetActive(false);
    }

    /// <summary>
    /// 关闭交互 UI（可从其他脚本或按钮调用）
    /// </summary>
    public void CloseInteractionUI()
    {
        _isUIVisible=false;
        if(interactionUI!=null)
            interactionUI.SetActive(false);
        //pc.canmove=true;
    }
}
