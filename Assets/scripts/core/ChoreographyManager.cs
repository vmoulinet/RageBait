using System.Collections.Generic;
using UnityEngine;

public enum ChoreographyState
{
	Triangle,
	Spiral,
	Circle,
	Chaos,
	Scatter,
	Line,
	Pause
}

public class ChoreographyManager : MonoBehaviour
{
	[Header("References")]
	public MirrorManager MirrorManager;
	public ChoreographyAnchor ChoreographyAnchor;

	[Header("Mode")]
	public ChoreographyState CurrentState = ChoreographyState.Triangle;
	public bool AutoCycle = true;

	[Header("Debug")]
	public bool DebugChoreography = true;
	public bool DebugPartners = true;
	public bool DebugCircleTargets = true;
	public float DebugLogInterval = 1f;

	[Header("Triangle")]
	public float TriangleMinDistance = 1f;
	public float TriangleMaxDistance = 2.5f;

	[Header("Global Motion")]
	public float MaxSpeed = 2f;
	public float ToleranceRadius = 0.05f;

	[Header("Anchor Coupling")]
	public float AnchorPullStrength = 0.35f;
	public float AnchorOuterLimit = 8f;
	public float AnchorOuterPullStrength = 1.5f;

	[Header("Spiral")]
	public float SpiralInterval = 30f;
	public float SpiralDuration = 6f;
	public float SpiralStrength = 2f;

	[Header("Circle")]
	public float CircleRadius = 4f;
	public float CircleHoldDuration = 2f;

	[Header("Chaos")]
	public float ChaosStrength = 1.5f;
	public float ChaosOrbitStrength = 0.75f;

	[Header("Scatter")]
	public float ScatterStrength = 2.5f;

	[Header("Line")]
	public float LineSpacing = 2f;
	public Vector3 LineDirection = Vector3.right;

	[HideInInspector] public Vector3 Center;

	public enum CirclePhase
	{
		Move,
		Orient,
		Hold
	}

	[HideInInspector] public CirclePhase CurrentCirclePhase = CirclePhase.Move;

	float intervalTimer;
	float spiralTimer;
	float circleHoldTimer;
	float debugLogTimer;

	public void Initialize(SimulationManager sim)
	{
		if (MirrorManager == null)
			MirrorManager = sim.MirrorManager;
	}

	void Start()
	{
		AssignTrianglePartners();
	}

	void Update()
	{
		if (MirrorManager == null)
			return;

		if (AutoCycle)
			UpdateAutoCycle();

		UpdateDebugLogging();
	}

	void UpdateAutoCycle()
	{
		intervalTimer += Time.deltaTime;

		if (CurrentState == ChoreographyState.Triangle && intervalTimer >= SpiralInterval)
			StartSpiral();

		if (CurrentState == ChoreographyState.Spiral)
		{
			spiralTimer += Time.deltaTime;

			if (spiralTimer >= SpiralDuration)
				StartCircle();
		}

		if (CurrentState == ChoreographyState.Circle)
			UpdateCircleState();
	}

	public void RefreshTargets()
	{
		if (CurrentState == ChoreographyState.Triangle)
			AssignTrianglePartners();

		if (CurrentState == ChoreographyState.Circle)
			ComputeCircleTargets();

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] refresh | state=" + CurrentState +
				" | active=" + GetActiveActors().Count +
				" | center=" + GetMirrorCenter().ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2")
			);
		}
	}

	public void SetState(ChoreographyState newState)
	{
		CurrentState = newState;

		if (newState == ChoreographyState.Triangle)
		{
			AssignTrianglePartners();
			return;
		}

		if (newState == ChoreographyState.Circle)
		{
			StartCircle();
			return;
		}

		if (newState == ChoreographyState.Spiral)
		{
			StartSpiral();
			return;
		}
	}

	public Vector3 GetResolvedAnchorPoint()
	{
		if (ChoreographyAnchor != null)
			return ChoreographyAnchor.transform.position;

		return GetMirrorCenter();
	}

	public Vector3 GetLineTargetFor(MirrorActor actor)
	{
		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count == 0)
			return actor.transform.position;

		int index = actors.IndexOf(actor);
		if (index < 0)
			return actor.transform.position;

		Vector3 anchorPoint = GetResolvedAnchorPoint();
		Vector3 direction = LineDirection.normalized;
		Vector3 start = anchorPoint - direction * ((actors.Count - 1) * 0.5f * LineSpacing);

		return start + direction * (index * LineSpacing);
	}

	public Vector3 ApplyAnchorCoupling(Vector3 position)
	{
		Vector3 anchorPoint = GetResolvedAnchorPoint();

		Vector3 flatPosition = new Vector3(position.x, 0f, position.z);
		Vector3 flatAnchor = new Vector3(anchorPoint.x, 0f, anchorPoint.z);

		Vector3 delta = flatPosition - flatAnchor;
		float distance = delta.magnitude;

		if (distance > AnchorOuterLimit && distance > 0.0001f)
		{
			Vector3 pulled = flatAnchor + delta.normalized * AnchorOuterLimit;
			position.x = Mathf.Lerp(position.x, pulled.x, AnchorOuterPullStrength * Time.deltaTime);
			position.z = Mathf.Lerp(position.z, pulled.z, AnchorOuterPullStrength * Time.deltaTime);
			return position;
		}

		position.x = Mathf.Lerp(position.x, anchorPoint.x, AnchorPullStrength * Time.deltaTime);
		position.z = Mathf.Lerp(position.z, anchorPoint.z, AnchorPullStrength * Time.deltaTime);
		return position;
	}

	void StartSpiral()
	{
		intervalTimer = 0f;
		spiralTimer = 0f;
		CurrentState = ChoreographyState.Spiral;

		ComputeCenter();
		if (DebugChoreography)
			Debug.Log("[choreography] start spiral | center=" + Center.ToString("F2") + " | anchor=" + GetResolvedAnchorPoint().ToString("F2"));
	}

	void StartCircle()
	{
		CurrentState = ChoreographyState.Circle;
		CurrentCirclePhase = CirclePhase.Move;
		circleHoldTimer = 0f;

		ComputeCenter();
		ComputeCircleTargets();
		if (DebugChoreography)
			Debug.Log("[choreography] start circle | center=" + Center.ToString("F2") + " | anchor=" + GetResolvedAnchorPoint().ToString("F2"));
	}

	void StopCircle()
	{
		CurrentState = ChoreographyState.Triangle;
		AssignTrianglePartners();
		if (DebugChoreography)
			Debug.Log("[choreography] stop circle -> triangle");
	}

	void UpdateCircleState()
	{
		if (CurrentCirclePhase == CirclePhase.Move)
		{
			if (AllAtTargets())
				CurrentCirclePhase = CirclePhase.Orient;
		}
		else if (CurrentCirclePhase == CirclePhase.Orient)
		{
			if (AllOriented())
				CurrentCirclePhase = CirclePhase.Hold;
		}
		else if (CurrentCirclePhase == CirclePhase.Hold)
		{
			circleHoldTimer += Time.deltaTime;

			if (circleHoldTimer >= CircleHoldDuration)
				StopCircle();
		}
	}

	void ComputeCenter()
	{
		Center = GetMirrorCenter();
	}

	Vector3 GetMirrorCenter()
	{
		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count == 0)
			return transform.position;

		Vector3 center = Vector3.zero;

		for (int i = 0; i < actors.Count; i++)
			center += actors[i].transform.position;

		return center / actors.Count;
	}

	void ComputeCircleTargets()
	{
		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count == 0)
			return;

		Vector3 anchorPoint = GetResolvedAnchorPoint();
		List<Vector3> slots = new List<Vector3>();

		for (int i = 0; i < actors.Count; i++)
		{
			float angle = Mathf.PI * 2f * i / actors.Count;
			slots.Add(anchorPoint + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * CircleRadius);
		}

		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor actor = actors[i];

			Vector3 best = slots[0];
			float bestDistance = Vector3.Distance(actor.transform.position, best);

			for (int j = 0; j < slots.Count; j++)
			{
				float distance = Vector3.Distance(actor.transform.position, slots[j]);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = slots[j];
				}
			}

			actor.CircleTarget = best;
			if (DebugCircleTargets)
			{
				Debug.Log(
					"[choreography] circle target | actor=" + actor.name +
					" | pos=" + actor.transform.position.ToString("F2") +
					" | target=" + best.ToString("F2") +
					" | anchor=" + anchorPoint.ToString("F2")
				);
			}
			slots.Remove(best);
		}
	}

	bool AllAtTargets()
	{
		List<MirrorActor> actors = GetActiveActors();

		for (int i = 0; i < actors.Count; i++)
		{
			if (!actors[i].AtCircleTarget(ToleranceRadius))
				return false;
		}

		return true;
	}

	bool AllOriented()
	{
		List<MirrorActor> actors = GetActiveActors();
		Vector3 anchorPoint = GetResolvedAnchorPoint();

		for (int i = 0; i < actors.Count; i++)
		{
			if (!actors[i].IsOrientedToPoint(anchorPoint, 1f))
				return false;
		}

		return true;
	}

	void AssignTrianglePartners()
	{
		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count == 0)
			return;

		for (int i = 0; i < actors.Count; i++)
		{
			List<MirrorActor> others = new List<MirrorActor>(actors);
			others.Remove(actors[i]);

			if (others.Count < 2)
			{
				if (DebugPartners)
					Debug.Log("[choreography] triangle partners | actor=" + actors[i].name + " | not enough partners");
				actors[i].AgentA = null;
				actors[i].AgentB = null;
				continue;
			}

			MirrorActor a = others[Random.Range(0, others.Count)];
			others.Remove(a);
			MirrorActor b = others[Random.Range(0, others.Count)];

			actors[i].AgentA = a.transform;
			actors[i].AgentB = b.transform;
			if (DebugPartners)
			{
				Debug.Log(
					"[choreography] triangle partners | actor=" + actors[i].name +
					" | A=" + a.name +
					" | B=" + b.name +
					" | anchor=" + GetResolvedAnchorPoint().ToString("F2")
				);
			}
		}
	}

	void UpdateDebugLogging()
	{
		if (!DebugChoreography)
			return;

		debugLogTimer += Time.deltaTime;
		if (debugLogTimer < DebugLogInterval)
			return;

		debugLogTimer = 0f;

		Vector3 center = GetMirrorCenter();
		Vector3 anchor = GetResolvedAnchorPoint();
		List<MirrorActor> actors = GetActiveActors();

		Debug.Log(
			"[choreography] tick | state=" + CurrentState +
			" | phase=" + CurrentCirclePhase +
			" | active=" + actors.Count +
			" | center=" + center.ToString("F2") +
			" | anchor=" + anchor.ToString("F2") +
			" | anchor_delta=" + (center - anchor).ToString("F2")
		);
	}

	public void LogActorSnapshot()
	{
		List<MirrorActor> actors = GetActiveActors();
		Vector3 anchor = GetResolvedAnchorPoint();

		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor actor = actors[i];
			Vector3 toAnchor = anchor - actor.transform.position;
			toAnchor.y = 0f;

			Debug.Log(
				"[choreography] actor | name=" + actor.name +
				" | pos=" + actor.transform.position.ToString("F2") +
				" | dist_to_anchor=" + toAnchor.magnitude.ToString("F2") +
				" | A=" + (actor.AgentA != null ? actor.AgentA.name : "null") +
				" | B=" + (actor.AgentB != null ? actor.AgentB.name : "null")
			);
		}
	}

	List<MirrorActor> GetActiveActors()
	{
		List<MirrorActor> result = new List<MirrorActor>();

		if (MirrorManager == null || MirrorManager.ActiveMirrors == null)
			return result;

		for (int i = 0; i < MirrorManager.ActiveMirrors.Count; i++)
		{
			MirrorActor actor = MirrorManager.ActiveMirrors[i];

			if (actor == null || actor.IsBroken || !actor.gameObject.activeInHierarchy)
				continue;

			result.Add(actor);
		}

		return result;
	}
}