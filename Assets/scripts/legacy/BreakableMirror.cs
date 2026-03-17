using UnityEngine;

public class BreakableMirror : MonoBehaviour
{
    [Header("References")]
    public GameObject fracturedRoot;

    [Header("Break Settings")]
    public float breakForce = 6f;
    public float breakRadius = 1.2f;

    bool broken = false;

    void OnTriggerEnter(Collider other)
    {
        if (broken) return;

        Debug.Log(
            $"[MIRROR TRIGGER]\n" +
            $"Mirror: {name}\n" +
            $"By object: {other.name}\n" +
            $"Tag: {other.tag}"
        );

        if (!other.CompareTag("Pendulum")) return;

        Break(other.transform.position);
    }

    void Break(Vector3 impactPoint)
    {
        broken = true;

        if (fracturedRoot == null)
        {
            Debug.LogWarning("BreakableMirror: fracturedRoot not assigned");
            return;
        }

        fracturedRoot.transform.position = transform.position;
        fracturedRoot.transform.rotation = transform.rotation;
        fracturedRoot.SetActive(true);

        foreach (Rigidbody rb in fracturedRoot.GetComponentsInChildren<Rigidbody>())
        {
            rb.AddExplosionForce(
                breakForce,
                impactPoint,
                breakRadius,
                0.2f,
                ForceMode.Impulse
            );
        }

        gameObject.SetActive(false);
    }
}
