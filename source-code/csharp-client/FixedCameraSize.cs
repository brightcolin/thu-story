using UnityEngine;

public class FixedCameraSize : MonoBehaviour
{
    [Header("固定视野大小")]
    public float fixedWidth = 9.6f;   // 固定宽度
    public float fixedHeight = 5.0f;  // 固定高度
    
    [Header("窗口适配")]
    public bool adaptToWindow = true;  // 是否适配窗口（true=保持9.6*5，false=完全固定）
    
    private Camera cam;
    private float lastScreenWidth;
    private float lastScreenHeight;
    
    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("找不到Camera组件！");
            return;
        }
        
        UpdateCameraSize();
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
    }
    
    void Update()
    {
        // 检测窗口大小变化
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            UpdateCameraSize();
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
        }
    }
    
    void UpdateCameraSize()
    {
        if (adaptToWindow)
        {
            // 方式1：保持9.6*5的视野
            float targetAspect = fixedWidth / fixedHeight;
            float currentAspect = (float)Screen.width / Screen.height;
            
            if (currentAspect >= targetAspect)
            {
                // 窗口更宽：以高度为准，左右会有黑边
                cam.orthographicSize = fixedHeight / 2f;
            }
            else
            {
                // 窗口更窄：以宽度为准，上下会有黑边
                cam.orthographicSize = (fixedWidth / 2f) / currentAspect;
            }
        }
        else
        {
            // 方式2：完全固定，不管窗口比例
            cam.orthographicSize = fixedHeight / 2f;
        }
        
        Debug.Log($"摄像机大小: {cam.orthographicSize * 2f} (高), 视野: {fixedWidth} x {fixedHeight}");
    }
}