using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera cam;

    private void LateUpdate()
    {
        // Follow whichever camera is rendering (the player camera, or a vehicle's chase camera while driving).
        if (cam == null || !cam.isActiveAndEnabled)
        {
            cam = Camera.main != null && Camera.main.isActiveAndEnabled ? Camera.main : null;
            if (cam == null) foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled && c.targetTexture == null) { cam = c; break; }
        }
        if (cam == null) return;
        transform.forward = cam.transform.forward;
    }
}
