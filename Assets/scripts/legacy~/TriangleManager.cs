using UnityEngine;
using System.Collections.Generic;

public class TriangleManager : MonoBehaviour
{
    public TriangleAgent[] agents;

    public enum GlobalMode { Triangle, Spiral, Circle }
    public enum CirclePhase { Move, Orient, Hold }

    public GlobalMode currentMode = GlobalMode.Triangle;
    public CirclePhase circlePhase;

    [Header("Triangle")]
    public float minDistance = 1f;
    public float maxDistance = 2f;
    public float maxSpeed = 2f;
    public float toleranceRadius = 0.05f;

    [Header("Spiral")]
    public float spiralInterval = 30f;
    public float spiralDuration = 6f;
    public float spiralStrength = 2f;

    [Header("Circle")]
    public float circleRadius = 4f;
    public float circleHoldDuration = 2f;

    [HideInInspector] public Vector3 center;

    float intervalTimer;
    float spiralTimer;
    float circleHoldTimer;

    void Start()
    {
        AssignPairs();
    }

    void Update()
    {
        intervalTimer += Time.deltaTime;

        if (currentMode == GlobalMode.Triangle && intervalTimer >= spiralInterval)
            StartSpiral();

        if (currentMode == GlobalMode.Spiral)
        {
            spiralTimer += Time.deltaTime;
            if (spiralTimer >= spiralDuration)
                StartCircle();
        }

        if (currentMode == GlobalMode.Circle)
            UpdateCircle();
    }

    void StartSpiral()
    {
        Debug.Log("=== SPIRAL MODE ===");

        intervalTimer = 0f;
        spiralTimer = 0f;
        currentMode = GlobalMode.Spiral;

        ComputeCenter();
    }

    void StartCircle()
    {
        Debug.Log("=== CIRCLE MODE ===");

        currentMode = GlobalMode.Circle;
        circlePhase = CirclePhase.Move;
        circleHoldTimer = 0f;

        ComputeCenter();
        ComputeCircleTargets();
    }

    void StopCircle()
    {
        Debug.Log("=== TRIANGLE MODE ===");
        currentMode = GlobalMode.Triangle;
    }

    void UpdateCircle()
    {
        if (circlePhase == CirclePhase.Move)
        {
            if (AllAtTargets())
                circlePhase = CirclePhase.Orient;
        }
        else if (circlePhase == CirclePhase.Orient)
        {
            if (AllOriented())
                circlePhase = CirclePhase.Hold;
        }
        else if (circlePhase == CirclePhase.Hold)
        {
            circleHoldTimer += Time.deltaTime;
            if (circleHoldTimer >= circleHoldDuration)
                StopCircle();
        }
    }

    void ComputeCenter()
    {
        center = Vector3.zero;
        foreach (var a in agents)
            center += a.transform.position;
        center /= agents.Length;
    }

    void ComputeCircleTargets()
    {
        List<Vector3> slots = new List<Vector3>();

        for (int i = 0; i < agents.Length; i++)
        {
            float a = Mathf.PI * 2f * i / agents.Length;
            slots.Add(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * circleRadius);
        }

        foreach (var agent in agents)
        {
            Vector3 best = slots[0];
            float bestDist = Vector3.Distance(agent.transform.position, best);

            foreach (var s in slots)
            {
                float d = Vector3.Distance(agent.transform.position, s);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }

            agent.circleTarget = best;
            slots.Remove(best);
        }
    }

    bool AllAtTargets()
    {
        foreach (var a in agents)
            if (!a.AtCircleTarget())
                return false;
        return true;
    }

    bool AllOriented()
    {
        foreach (var a in agents)
            if (!a.IsOrientedToCenter())
                return false;
        return true;
    }

    void AssignPairs()
    {
        foreach (TriangleAgent agent in agents)
        {
            List<TriangleAgent> others = new List<TriangleAgent>(agents);
            others.Remove(agent);

            TriangleAgent a = others[Random.Range(0, others.Count)];
            others.Remove(a);
            TriangleAgent b = others[Random.Range(0, others.Count)];

            agent.agentA = a.transform;
            agent.agentB = b.transform;
            agent.manager = this;
        }
    }
}
