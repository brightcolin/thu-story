using UnityEngine;

/// <summary>
/// 固定 1140x600 (11.4:6)，Build 后正确适配。解决：UI 缩小、人物移动异常（SetResolution 异步 + player 边界未刷新）。
/// </summary>
[DefaultExecutionOrder(-1000)]
public class Fixed1100x600Camera : MonoBehaviour
{
    public const int TargetWidth = 1140;
    public const int TargetHeight = 600;
    private const float TargetAspect = (float)TargetWidth / TargetHeight;
    private Camera _cam;
    private int _lastWidth;
    private int _lastHeight;
    private int _resolutionRetryCount;
    private GameObject _letterboxCamGo;

    void Awake()
    {
        TrySetResolution();
    }

    void Start()
    {
        _cam = GetComponent<Camera>();
        _lastWidth = 0;
        _lastHeight = 0;
        _resolutionRetryCount = 0;
        EnsureLetterboxCamera();
        ApplyRect();
    }

    void Update()
    {
        // SetResolution 异步：多帧内持续尝试直至生效
        if (_resolutionRetryCount < 30 && (Screen.width != TargetWidth || Screen.height != TargetHeight))
        {
            _resolutionRetryCount++;
            TrySetResolution();
        }

        if (Screen.width != _lastWidth || Screen.height != _lastHeight)
        {
            EnsureLetterboxCamera();
            ApplyRect();
        }
    }

    private void TrySetResolution()
    {
        var refreshRate = new RefreshRate { numerator = 60, denominator = 1 };
        Screen.SetResolution(TargetWidth, TargetHeight, Screen.fullScreenMode, refreshRate);
    }

    private void EnsureLetterboxCamera()
    {
        if (_letterboxCamGo != null) return;

        _letterboxCamGo = new GameObject("LetterboxBackground");
        _letterboxCamGo.transform.SetParent(transform);

        var bgCam = _letterboxCamGo.AddComponent<Camera>();
        bgCam.clearFlags = CameraClearFlags.SolidColor;
        bgCam.backgroundColor = Color.black;
        bgCam.rect = new Rect(0, 0, 1, 1);
        bgCam.depth = _cam != null ? _cam.depth - 1 : -2;
        bgCam.cullingMask = 0;
        bgCam.orthographic = true;
        bgCam.orthographicSize = 5f;
        bgCam.nearClipPlane = 0.3f;
        bgCam.farClipPlane = 1000f;
        bgCam.useOcclusionCulling = false;
    }

    private void ApplyRect()
    {
        if (_cam == null) return;

        int w = Screen.width;
        int h = Screen.height;
        if (w <= 0 || h <= 0) return;

        _lastWidth = w;
        _lastHeight = h;

        // 分辨率完全匹配时全屏，避免 rect 影响 Camera.aspect 和 player 边界计算
        if (w == TargetWidth && h == TargetHeight)
        {
            _cam.rect = new Rect(0, 0, 1, 1);
            if (_letterboxCamGo != null) _letterboxCamGo.SetActive(false);
            return;
        }

        if (_letterboxCamGo != null) _letterboxCamGo.SetActive(true);

        float windowAspect = (float)w / h;
        float scaleHeight = windowAspect / TargetAspect;
        Rect rect;

        if (scaleHeight < 1f)
            rect = new Rect(0f, (1f - scaleHeight) / 2f, 1f, scaleHeight);
        else
        {
            float scaleWidth = 1f / scaleHeight;
            rect = new Rect((1f - scaleWidth) / 2f, 0f, scaleWidth, 1f);
        }

        _cam.rect = rect;

        if (_letterboxCamGo != null)
        {
            var bgCam = _letterboxCamGo.GetComponent<Camera>();
            if (bgCam != null) bgCam.depth = _cam.depth - 1;
        }
    }
}
