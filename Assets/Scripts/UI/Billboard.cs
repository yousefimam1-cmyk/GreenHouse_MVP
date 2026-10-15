using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera cam;

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        transform.forward = cam.transform.forward;
    }
}
