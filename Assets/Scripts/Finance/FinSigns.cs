using UnityEngine;

/// <summary>Placeholder signboards for the land office, bank and greenhouse vent switches.</summary>
public static class FinSigns
{
    public static GameObject Sign(string objName, Vector3 pos, Quaternion rot, Color board, string labelKey)
    {
        var root = new GameObject(objName);
        root.layer = 7;
        root.transform.SetPositionAndRotation(Build.OnGround(pos), rot);
        var col = root.AddComponent<BoxCollider>(); col.center = new Vector3(0, 1.3f, 0); col.size = new Vector3(2.2f, 2.6f, 0.6f);
        var wood = Build.Mat(new Color(0.5f, 0.34f, 0.2f), 0.15f);
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-0.9f, 0.9f, 0), new Vector3(0.12f, 1.8f, 0.12f), wood, "Post");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0.9f, 0.9f, 0), new Vector3(0.12f, 1.8f, 0.12f), wood, "Post");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 1.9f, 0), new Vector3(2.1f, 1.0f, 0.1f), Build.Mat(board, 0.3f), "Board");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        Build.Label(root.transform, new Vector3(0, 2.9f, 0), labelKey, 3f, Color.white);
        return root;
    }
}
