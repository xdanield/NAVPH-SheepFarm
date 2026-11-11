using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Okraje mapy (kamera neukaze nic mimo)")]
    [SerializeField] private bool clampToBounds = false;
    [SerializeField] private Vector2 boundsMin;
    [SerializeField] private Vector2 boundsMax;

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 pos = new Vector3(target.position.x, target.position.y, transform.position.z);

        if (clampToBounds && cam != null && cam.orthographic)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            pos.x = Clamp(pos.x, boundsMin.x, boundsMax.x, halfWidth);
            pos.y = Clamp(pos.y, boundsMin.y, boundsMax.y, halfHeight);
        }

        transform.position = pos;
    }

    // Ak je mapa mensia ako obrazovka, kamera ostane v strede mapy.
    static float Clamp(float value, float min, float max, float half)
    {
        if (max - min < half * 2f) return (min + max) * 0.5f;
        return Mathf.Clamp(value, min + half, max - half);
    }
}
