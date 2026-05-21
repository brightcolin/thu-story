using UnityEngine;

public class GetSpriteCorners:MonoBehaviour
{
    void Start()
    {
        GetSpriteCornerPoints();
    }

    void GetSpriteCornerPoints()
    {
        // 获取SpriteRenderer组件
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();

        if(spriteRenderer!=null)
        {
            // 获取精灵的边界框
            Bounds bounds = spriteRenderer.bounds;

            // 获取四个角点坐标
            Vector3 topLeft = new Vector3(bounds.min.x,bounds.max.y,0);
            Vector3 topRight = new Vector3(bounds.max.x,bounds.max.y,0);
            Vector3 bottomLeft = new Vector3(bounds.min.x,bounds.min.y,0);
            Vector3 bottomRight = new Vector3(bounds.max.x,bounds.min.y,0);

            // 输出坐标
            Debug.Log($"左上角: {topLeft}");
            Debug.Log($"右上角: {topRight}");
            Debug.Log($"左下角: {bottomLeft}");
            Debug.Log($"右下角: {bottomRight}");

        }
    }
}