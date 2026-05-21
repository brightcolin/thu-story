using UnityEngine;

public class methods : MonoBehaviour
{
    public static float dis(float x1,float y1,float x2,float y2)
    {
        return Mathf.Sqrt((x2-x1)*(x2-x1)+(y2-y1)*(y2-y1));
    }
}