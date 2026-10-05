using UnityEngine;

/// <summary>
/// ARPG 俯视角跟随相机：保持固定偏移平滑跟随目标。
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 10f, -7f);
    public float smoothTime = 0.12f;

    Vector3 velocity;

    void Start()
    {
        if (target == null) return;
        transform.position = target.position + offset;
        transform.LookAt(target);
    }

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 desired = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
    }
}
