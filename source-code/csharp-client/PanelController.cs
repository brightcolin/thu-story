using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using QinghuaStory;

public class panelctrl : MonoBehaviour
{
    public GameObject scenectrl;
    private scenetrans tr;
    public GameObject player;
    private playercontrol pc;
    public Button[] but;
    private bool h = false, m = false;
    public GameObject map;
    public GameObject TimeUI;
    public GameObject panel;
    public GameObject head;
    public Button enter;
    public Button exit;
    public bool talk=false;

    void Start()
    {
        tr=FindObjectOfType<scenetrans>();
        pc=FindObjectOfType<playercontrol>();
        enter.onClick.AddListener(OnButtonClickEnter);
        exit.onClick.AddListener(OnButtonClickExit);
        map.SetActive(false);
        for(int i = 0;i<14;i++)
        {
            int index = i;
            but[i].onClick.AddListener(() => OnButtonClick(index));
        }
        head.SetActive(true);
        panel.SetActive(false);
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.M)&&!m&&!h&&!talk&&!NPCManager.ShouldSuppressGlobalHotkeys())
        {
            TimeUI.SetActive(false);
            map.SetActive(true);
            pc.canmove=false;
            head.SetActive(false);
            m=true;
            ServerPauseCoordinator.Acquire(this);
        }
        else if(Input.GetKeyDown(KeyCode.M)&&m)
        {
            TimeUI.SetActive(true);
            map.SetActive(false);
            pc.canmove=true;
            head.SetActive(true);
            m=false;
            ServerPauseCoordinator.Release(this);
        }
    }

    void OnButtonClickEnter()
    {
        if(!h&&!m)
        {
            TimeUI.SetActive(false);
            head.SetActive(false);
            panel.SetActive(true);
            pc.canmove=false;
            h=true;
            ServerPauseCoordinator.Acquire(this);
        }
    }

    void OnButtonClickExit()
    {
        if(h)
        {
            TimeUI.SetActive(true);
            head.SetActive(true);
            panel.SetActive(false);
            pc.canmove=true;
            h=false;
            ServerPauseCoordinator.Release(this);
        }
    }

    void OnButtonClick(int i)
    {
        if (i == 11 && LibraryHoursV21.TryIsLibraryClosedFromPlayerCache(out bool closed) && closed)
        {
            ActivityPresentationUI.EnsureExists();
            ActivityPresentationUI.Instance.ShowFailure("图书馆", "闭馆中，开馆时间为每日 8:00–22:00。",
                ActivityUnlockHints.LibraryClosedContext);
            return;
        }
        tr.scene=i;
        map.SetActive(false);
        TimeUI.SetActive(true);
        head.SetActive(true);
        pc.canmove=true;
        if (m) ServerPauseCoordinator.Release(this);
        m=false;
    }

    /// <summary>宵禁等强制传送：若大地图打开则关闭并恢复移动，与连按 M 关闭一致。</summary>
    public void CloseMapIfOpenForTeleport()
    {
        if (!m) return;
        if (TimeUI != null) TimeUI.SetActive(true);
        if (map != null) map.SetActive(false);
        if (pc != null) pc.canmove = true;
        if (head != null) head.SetActive(true);
        m = false;
        ServerPauseCoordinator.Release(this);
    }
}
