using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using QinghuaStory;
using static methods;

public class scenetrans : MonoBehaviour
{
    public GameObject[] back = new GameObject[14];
    public GameObject prompt;
    private float x0 = 0f, y0 = 0f;
    public int scene = 0;
    public Rigidbody2D playerRb;
    private bool changeable = false;
    private int targetscene = 0;

    void Start()
    {
        prompt.SetActive(false);
        scene = 0;
    }

    void Update()
    {
        SpriteRenderer sprite = back[scene].GetComponent<SpriteRenderer>();
        Bounds bounds = sprite.bounds;
        x0=bounds.min.x;
        y0=bounds.min.y;
        switch(scene)
        {
            case 0:
                if(dis(x0+10.23f,y0+1.03f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=3;
                    changeable=true;
                }
                else if(dis(x0+1.5f,y0+4.27f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=7;
                    changeable=true;
                }
                else if(dis(x0+1.5f,y0+27.17f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=8;
                    changeable=true;
                }
                else if(dis(x0+1.5f,y0+33.97f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=6;
                    changeable=true;
                }
                else if(dis(x0+10.23f,y0+36.59f,playerRb.position.x,playerRb.position.y)<2f)
                {
                    targetscene=1;
                    changeable=true;
                }
                else if(dis(x0+19.5f,y0+33.97f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=5;
                    changeable=true;
                }
                else if(dis(x0+19.5f,y0+27.17f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=2;
                    changeable=true;
                }
                else if(dis(x0+19.5f,y0+4.27f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=4;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 1:
                if(dis(x0+23.8f,y0+7.34f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=13;
                    changeable=true;
                }
                else if(dis(x0+23.8f,y0+1.2f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 2:
                if(dis(x0+1.37f,y0+1.36f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 3:
                if(dis(x0+27.2f,y0+28.34f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=12;
                    changeable=true;
                }
                else if(dis(x0+27.2f,y0+32.38f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 4:
                if(dis(x0+23.12f,y0+7.93f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=10;
                    changeable=true;
                }
                else if(dis(x0+1.31f,y0+11.9f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 5:
                if(dis(x0+22.33f,y0+6.32f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=9;
                    changeable=true;
                }
                else if(dis(x0+22.33f,y0+0.69f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 6:
                if(dis(x0+18.38f,y0+10.61f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=11;
                    changeable=true;
                }
                else if(dis(x0+45.37f,y0+3.59f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 7:
                if(dis(x0+37.48f,y0+7.48f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 8:
                if(dis(x0+19.53f,y0+1.12f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=0;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 9:
                if(dis(x0+10.58f,y0+2.11f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=5;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 10:
                if(dis(x0+22.73f,y0+12.9f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=4;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 11:
                if(dis(x0+14.48f,y0+0.96f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=6;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 12:
                if(dis(x0+15.81f,y0+12.02f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=3;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
            case 13:
                if(dis(x0+12.56f,y0+0.76f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=1;
                    changeable=true;
                }
                else if(dis(x0+8.34f,y0+0.76f,playerRb.position.x,playerRb.position.y)<1.5f)
                {
                    targetscene=1;
                    changeable=true;
                }
                else
                    changeable=false;
                break;
        }
        prompt.SetActive(changeable);
        if(changeable&&Input.GetKeyDown(KeyCode.F)&&!NPCManager.ShouldSuppressGlobalHotkeys()&&
            !Input.GetKey(KeyCode.W)&&!Input.GetKey(KeyCode.A)&&!Input.GetKey(KeyCode.S)&&!Input.GetKey(KeyCode.D))
        {
            if (scene == 6 && targetscene == 11 &&
                LibraryHoursV21.TryIsLibraryClosedFromPlayerCache(out bool libClosed) && libClosed)
            {
                ActivityPresentationUI.EnsureExists();
                ActivityPresentationUI.Instance.ShowFailure("图书馆", "闭馆中，开馆时间为每日 8:00–22:00。",
                    ActivityUnlockHints.LibraryClosedContext);
            }
            else
                scene = targetscene;
        }
    }
}
