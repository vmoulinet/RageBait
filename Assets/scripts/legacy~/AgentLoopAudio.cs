using UnityEngine;
using FMODUnity;
using FMOD.Studio;

[RequireComponent(typeof(TriangleAgent))]
public class AgentLoopAudio : MonoBehaviour
{
    [Header("FMOD")]
    public EventReference noteEvent;          // Assign in Inspector
    public string distanceParam = "Distance"; // FMOD parameter (0..1)

    [Header("Distance Settings")]
    public float minDistance = 0.5f;          // 0.0 in FMOD
    public float maxDistance = 3.0f;          // 1.0 in FMOD
    public float triggerRadius = 3.0f;         // must be <= maxDistance
    public float cooldown = 0.2f;

    [Header("Reference Point (optional)")]
    public Transform centerTransform;          // If null, uses TriangleManager.center

    private TriangleAgent agent;
    private float cooldownTimer;

    void Start()
    {
        agent = GetComponent<TriangleAgent>();
    }

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        if (agent == null || agent.manager == null)
            return;

        Vector3 center = GetCenterPoint();

        Vector3 delta = transform.position - center;
        delta.y = 0f;

        float distance = delta.magnitude;

        if (distance > triggerRadius)
            return;

        if (cooldownTimer > 0f)
            return;

        PlayOneShot(distance);
        cooldownTimer = cooldown;
    }

    Vector3 GetCenterPoint()
    {
        if (centerTransform != null)
            return centerTransform.position;

        return agent.manager.center;
    }

    void PlayOneShot(float distance)
    {
        if (noteEvent.IsNull)
        {
            Debug.LogError($"{nameof(AgentLoopAudio)} on '{gameObject.name}': noteEvent not assigned.");
            return;
        }

        // Normalize distance to 0..1
        float normalizedDistance = Mathf.InverseLerp(minDistance, maxDistance, distance);

        EventInstance e = RuntimeManager.CreateInstance(noteEvent);
        RuntimeManager.AttachInstanceToGameObject(e, transform);

        e.setParameterByName(distanceParam, normalizedDistance);

        e.start();
        e.release(); // important for one-shots
    }
}
