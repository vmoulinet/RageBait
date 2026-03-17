using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class PendulumTrail : MonoBehaviour
{
    public Transform target;          // la boule du pendule
    public int maxPoints = 2000;
    public float minDistance = 0.02f; // seuil anti-jitter
    public Color lineColor = Color.magenta;
    public float lineWidth = 0.02f;

    LineRenderer line;
    List<Vector3> points = new List<Vector3>();

    void Awake()
    {
    line = GetComponent<LineRenderer>();

    line.useWorldSpace = true;

    // Shader neutre valide (OBLIGATOIRE)
    line.material = new Material(Shader.Find("Unlit/Color"));

    line.startWidth = lineWidth;
    line.endWidth = lineWidth;

    line.startColor = lineColor;
    line.endColor = lineColor;

    line.positionCount = 0;
    }


    void LateUpdate()
    {
        if (target == null) return;

        Vector3 p = target.position;

        // Anti-jitter : ignore micro-mouvements
        if (points.Count > 0 &&
            Vector3.Distance(points[points.Count - 1], p) < minDistance)
            return;

        points.Add(p);

        if (points.Count > maxPoints)
            points.RemoveAt(0);

        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());
        line.SetPositions(points.ToArray());
        Bounds b = new Bounds(Vector3.zero, new Vector3(500f, 1f, 500f));
        line.localBounds = b;


    }
}
