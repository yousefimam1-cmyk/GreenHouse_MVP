using UnityEngine;

/// <summary>
/// Marks a collider (or everything under this object) as ground you walk and drive on - a ramp, a deck, a paved pad -
/// so the vehicle obstacle sweep (VehicleObstacles.Blocks) and the vehicle's contact bounce never treat it as a wall.
/// </summary>
public class Walkable : MonoBehaviour { }
