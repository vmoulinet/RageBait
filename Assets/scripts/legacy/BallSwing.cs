using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class FoucaultPendulumBall : MonoBehaviour
{
    [Header("References")]
    public Transform root;

    [Header("Pendulum Physics")]
    [Tooltip("Angle max en degrés (petits angles recommandés)")]
    public float amplitude = 12f;

    [Tooltip("Longueur du câble en mètres")]
    public float cableLength = 48.5f;

    [Header("Cable Visual")]
    public bool enableCable = true;
    public Material cableMaterial;
    public Color cableColor = Color.white;
    public float cableWidth = 0.02f;

    const float g = 9.81f;

    LineRenderer lr;
    Rigidbody rb;

    float phase;     // angle d’oscillation
    float omega;     // pulsation réelle (rad/s)

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        rb = GetComponent<Rigidbody>();

        // Rigidbody = collisions only
        rb.useGravity = false;
        rb.isKinematic = true;

        lr.useWorldSpace = true;
        lr.positionCount = 2;
        ApplyCableVisuals();

        // pulsation réelle du pendule
        omega = Mathf.Sqrt(g / cableLength);
    }

    void Update()
    {
        phase += omega * Time.deltaTime;

        float angle = Mathf.Sin(phase) * Mathf.Deg2Rad * amplitude;

        float x = Mathf.Sin(angle) * cableLength;
        float y = -Mathf.Cos(angle) * cableLength;

        // LOCAL SPACE → précession conservée
        transform.localPosition = new Vector3(x, y, 0f);
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            lr.enabled = false;
            return;
        }

        lr.enabled = enableCable;
        if (!enableCable) return;

        lr.SetPosition(0, root.position);
        lr.SetPosition(1, transform.position);
    }

    void ApplyCableVisuals()
    {
        if (cableMaterial)
            lr.material = cableMaterial;

        lr.startColor = cableColor;
        lr.endColor = cableColor;
        lr.startWidth = cableWidth;
        lr.endWidth = cableWidth;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!lr) lr = GetComponent<LineRenderer>();
        ApplyCableVisuals();

        // recalcul si la longueur change en editor
        omega = Mathf.Sqrt(g / Mathf.Max(0.01f, cableLength));
    }
#endif
}
