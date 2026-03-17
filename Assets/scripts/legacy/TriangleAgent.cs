using UnityEngine;

public class TriangleAgent : MonoBehaviour
{
    public Transform agentA;
    public Transform agentB;
    public TriangleManager manager;

    [Header("Movement")]
    public float mass = 1f;
    public float attractionStrength = 1.5f;
    public float damping = 0.98f;

    [Header("Noise")]
    public float noiseStrength = 0.1f;
    public float noiseFrequency = 1f;

    [Header("Rotation")]
    public float rotationSpeed = 8f;

    [Header("Lines")]
    public float lineWidth = 0.05f;

    [HideInInspector] public Vector3 circleTarget;

    Vector3 velocity;

    LineRenderer lineA;
    LineRenderer lineB;

    void Start()
    {
        velocity = Random.insideUnitSphere * 0.3f;
        velocity.y = 0f;

        lineA = CreateLine("LineA");
        lineB = CreateLine("LineB");
    }

    void Update()
    {
        if (!manager) return;

        if (manager.currentMode == TriangleManager.GlobalMode.Triangle)
            UpdateTriangle();
        else if (manager.currentMode == TriangleManager.GlobalMode.Spiral)
            UpdateSpiral();
        else
            UpdateCircle();

        velocity *= damping;
        transform.position += velocity * Time.deltaTime;

        UpdateLines();
    }

    // -------- TRIANGLE

    void UpdateTriangle()
    {
        Vector3 target = TriangleTarget();
        Vector3 force = (target - transform.position) * attractionStrength;
        force += Noise();
        ApplyForce(force);

        UpdateTriangleRotation();
    }

    Vector3 TriangleTarget()
    {
        Vector3 a = agentA.position;
        Vector3 b = agentB.position;
        a.y = b.y = transform.position.y;

        Vector3 mid = (a + b) * 0.5f;
        Vector3 axis = b - a;

        Vector3 normal = new Vector3(-axis.z, 0f, axis.x).normalized;
        float s = Vector3.Dot(transform.position - mid, normal);
        float d = Mathf.Clamp(Mathf.Abs(s), manager.minDistance, manager.maxDistance);

        return mid + normal * (s < 0 ? -d : d);
    }

    void UpdateTriangleRotation()
    {
        Vector3 dir = velocity;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion r = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, r, rotationSpeed * Time.deltaTime);
    }

    // -------- SPIRAL

    void UpdateSpiral()
    {
        Vector3 toCenter = transform.position - manager.center;
        toCenter.y = 0f;

        Vector3 tangent = new Vector3(-toCenter.z, 0f, toCenter.x).normalized;
        ApplyForce(tangent * manager.spiralStrength);
    }

    // -------- CIRCLE

    void UpdateCircle()
    {
        if (manager.circlePhase == TriangleManager.CirclePhase.Move)
        {
            float d = Vector3.Distance(transform.position, circleTarget);

            if (d <= manager.toleranceRadius)
                Snap(circleTarget);
            else
                ApplyForce((circleTarget - transform.position) * attractionStrength);
        }
        else if (manager.circlePhase == TriangleManager.CirclePhase.Orient)
        {
            velocity = Vector3.zero;

            Vector3 toCenter = manager.center - transform.position;
            toCenter.y = 0f;

            Quaternion r = Quaternion.LookRotation(-toCenter.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, r, rotationSpeed * Time.deltaTime);
        }
    }

    public bool AtCircleTarget()
    {
        return Vector3.Distance(transform.position, circleTarget) <= manager.toleranceRadius;
    }

    public bool IsOrientedToCenter()
    {
        Vector3 toCenter = manager.center - transform.position;
        toCenter.y = 0f;
        return Vector3.Angle(-transform.forward, toCenter) < 1f;
    }

    // -------- UTIL

    void Snap(Vector3 p)
    {
        transform.position = p;
        velocity = Vector3.zero;
    }

    void ApplyForce(Vector3 force)
    {
        velocity += (force / Mathf.Max(0.0001f, mass)) * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, manager.maxSpeed);
    }

    Vector3 Noise()
    {
        float n = Mathf.PerlinNoise(Time.time * noiseFrequency, GetInstanceID()) - 0.5f;
        return new Vector3(n, 0f, -n) * noiseStrength;
    }

    // -------- LINES

    LineRenderer CreateLine(string name)
    {
        GameObject g = new GameObject(name);
        g.transform.parent = transform;

        LineRenderer lr = g.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));

        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        lr.colorGradient = grad;
        return lr;
    }

    void UpdateLines()
    {
        lineA.SetPosition(0, transform.position);
        lineA.SetPosition(1, agentA.position);

        lineB.SetPosition(0, transform.position);
        lineB.SetPosition(1, agentB.position);
    }
}
