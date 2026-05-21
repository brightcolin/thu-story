using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>在活动结算遮罩上检测指针关闭：仅左键，且当 closeOnLeftClick 为 true 时关闭。</summary>
[DisallowMultipleComponent]
public class ActivityPresentationPointerSink : MonoBehaviour, IPointerDownHandler
{
    public bool closeOnLeftClick;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (closeOnLeftClick)
            ActivityPresentationUI.Instance?.RequestCloseFromUi();
    }
}
