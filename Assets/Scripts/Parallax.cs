using UnityEngine;

/// <summary>
/// Vrstva pozadia, ktora sa vodorovne posuva s kamerou.
/// follow = 1: ide uplne s kamerou (nekonecne daleko), 0: stoji na mieste ako mapa.
/// </summary>
[DefaultExecutionOrder(100)] // az po CameraFollow
public class Parallax : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float follow = 0.5f;
    [SerializeField] private float anchorX; // x kamery, pri ktorom je vrstva na svojej povodnej pozicii

    private Transform cam;
    private float startX;

    void Start()
    {
        if (Camera.main != null) cam = Camera.main.transform;
        startX = transform.position.x;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        Vector3 pos = transform.position;
        pos.x = startX + (cam.position.x - anchorX) * follow;
        transform.position = pos;
    }
}
