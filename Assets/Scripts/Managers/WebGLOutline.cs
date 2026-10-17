using UnityEngine;

public class WebGLOutline : MonoBehaviour
{
    public Material outlineMaterial;
    private GameObject outlineObject;

    void Start()
    {
        CreateOutline();
        Invoke(nameof(DisableOutline), 0.1f);
    }

    void DisableOutline()
    {
        EnableOutline(false);
    }

    void CreateOutline()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        MeshRenderer mr = GetComponent<MeshRenderer>();

        if (mf == null || mr == null || mf.sharedMesh == null) return;

        outlineObject = new GameObject("Outline");
        outlineObject.transform.SetParent(transform);
        outlineObject.transform.localPosition = Vector3.zero;
        outlineObject.transform.localRotation = Quaternion.identity;
        outlineObject.transform.localScale = Vector3.one;

        MeshFilter outlineMF = outlineObject.AddComponent<MeshFilter>();
        outlineMF.mesh = mf.sharedMesh;

        MeshRenderer outlineMR = outlineObject.AddComponent<MeshRenderer>();

        // 🔥 FIX: assign outline material for each submesh
        int subMeshCount = mf.sharedMesh.subMeshCount;
        Material[] materials = new Material[subMeshCount];

        for (int i = 0; i < subMeshCount; i++)
        {
            materials[i] = outlineMaterial;
        }

        outlineMR.materials = materials;

        outlineMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outlineMR.receiveShadows = false;
    }

    public void EnableOutline(bool state)
    {
        if (outlineObject != null)
            outlineObject.SetActive(state);
    }
}