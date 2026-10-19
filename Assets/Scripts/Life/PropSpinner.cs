using UnityEngine;

/// <summary>Spins a prop around its local up axis (misting fans).</summary>
public class PropSpinner : MonoBehaviour
{
    public float speed = 360f;
    private void Update() => transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
}
