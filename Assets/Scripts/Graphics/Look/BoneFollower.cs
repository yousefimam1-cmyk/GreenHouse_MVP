using UnityEngine;

/// <summary>Keeps an attachment (headwear) glued to an animated bone: same offset as when it was attached. Runs after
/// the animation (LateUpdate), costs one transform write per frame.</summary>
public class BoneFollower : MonoBehaviour
{
    public Transform target;
    public Vector3 localPosition;
    public Quaternion localRotation = Quaternion.identity;

    /// <summary>Attach follower to bone, remembering the current offset between them.</summary>
    public static BoneFollower Attach(GameObject follower, Transform bone)
    {
        if (follower == null || bone == null) return null;
        var f = follower.GetComponent<BoneFollower>();
        if (f == null) f = follower.AddComponent<BoneFollower>();
        f.target = bone;
        f.localPosition = bone.InverseTransformPoint(follower.transform.position);
        f.localRotation = Quaternion.Inverse(bone.rotation) * follower.transform.rotation;
        return f;
    }

    private void LateUpdate()
    {
        if (target == null) return;
        transform.SetPositionAndRotation(target.TransformPoint(localPosition), target.rotation * localRotation);
    }
}
