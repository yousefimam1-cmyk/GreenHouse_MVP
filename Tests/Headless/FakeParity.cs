// Fake UnityEngine members used by the Farming-Simulator parity features (GpsRoute). Every member mirrors a real
// Unity 6 API with the same name and signature (LineRenderer: positionCount, SetPositions, SetPosition, GetPosition,
// widthMultiplier, useWorldSpace, numCornerVertices, numCapVertices). Rendering is inert.
namespace UnityEngine
{
    public class LineRenderer : Renderer
    {
        private Vector3[] pts = new Vector3[0];
        public bool useWorldSpace = true;
        public float widthMultiplier = 1f, startWidth = 1f, endWidth = 1f;
        public int numCornerVertices, numCapVertices;
        public Color startColor = Color.white, endColor = Color.white;
        public int positionCount
        {
            get => pts.Length;
            set { var n = new Vector3[System.Math.Max(0, value)]; for (int i = 0; i < n.Length && i < pts.Length; i++) n[i] = pts[i]; pts = n; }
        }
        public void SetPositions(Vector3[] positions) { for (int i = 0; i < positions.Length && i < pts.Length; i++) pts[i] = positions[i]; }
        public void SetPosition(int index, Vector3 position) => pts[index] = position;
        public Vector3 GetPosition(int index) => pts[index];
    }
}
