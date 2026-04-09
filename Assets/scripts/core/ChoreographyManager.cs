using System;
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

	[Header("Debug")]
	public bool DebugChoreography = true;
	public bool DebugCircleTargets = true;
	public float DebugLogInterval = 1f;
	public bool DebugTriangleLinks = false;
	public Material DebugTriangleLineMaterial;
	public float DebugTriangleLineWidth = 0.03f;
	public float DebugTriangleLineHeight = 0.15f;

	[Header("Triangle")]
	public float TriangleMinDistance = 1f;
	public float TriangleMaxDistance = 2.5f;
	public float ToleranceRadius = 0.05f;
	public float TriangleStableDistanceTolerance = 0.34f;
	public float TriangleStableDistanceHysteresis = 0.6f;
	public float TriangleStableSpeedThreshold = 0.45f;
	public float TriangleStableHoldDuration = 1.2f;
	public float TriangleStableAverageSpeedThreshold = 0.25f;
	public float TriangleStableCenterDistanceTolerance = 0.75f;
	public float PendulumExclusionHalfWidth = 0.1f;
	public Transform DaddyTransform;
	public float DaddyGazeHoldDuration = 2f;

	[Header("Anchor Coupling")]
	public float AnchorPullStrength = 0.35f;
	public float AnchorOuterLimit = 8f;
	public float AnchorOuterPullStrength = 1.5f;

	[Header("Pattern Cycle")]
	public float PatternDurationMin = 4f;
	public float PatternDurationMax = 8f;
	public bool IncludeSpiral = true;
	public bool IncludeCircle = true;
	public bool IncludeChaos = true;
	public bool IncludeScatter = true;
	public bool IncludeLine = true;

	[Header("Spiral")]
	public float SpiralInterval = 30f;
	public float SpiralDuration = 6f;
	public float SpiralStrength = 2f;

	[Header("Circle")]
	public float CircleRadius = 4f;
	public float CircleHoldDuration = 2f;
	public float CircleMoveTimeout = 6f;
	public float CircleOrientTimeout = 3f;
	public float CircleOrientationTolerance = 8f;

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

	public event Action<ChoreographyState> ChoreographyStateEntered;
	public event Action<ChoreographyState> ChoreographyStateExited;
	public event Action<ChoreographyState> ChoreographyPatternStarted;
	public event Action<ChoreographyState> ChoreographyPatternCompleted;
	public event Action TriangleSettled;

	float circleHoldTimer;
	float debugLogTimer;
	float circlePhaseTimer;
	float triangleStableTimer;
	float activePatternTimer;
	float activePatternDuration;
	string lastTriangleUnstableReason = "";
	float lastTriangleAverageDistanceToTarget = 0f;
	float lastTriangleMaxDistanceToTarget = 0f;
	float lastTriangleAverageDistanceToAnchor = 0f;
	float lastTriangleMaxSpeed = 0f;
	bool triangleSettledThisCycle;
	float daddyGazeTimer = 0f;
	bool daddyGazeActive = false;
	ChoreographyState activeRandomPattern = ChoreographyState.Triangle;
	ChoreographyState lastRandomPattern = ChoreographyState.Triangle;
	readonly List<LineRenderer> debugTriangleLines = new List<LineRenderer>();
	Transform debugTriangleLinesRoot;
	readonly Dictionary<MirrorActor, MirrorActor[]> trianglePartners = new Dictionary<MirrorActor, MirrorActor[]>();
	readonly HashSet<MirrorActor> mirrorsAtPosition = new HashSet<MirrorActor>();
	readonly Dictionary<MirrorActor, int> triangleSide = new Dictionary<MirrorActor, int>();

	public void Initialize(SimulationManager sim)
	{
		if (MirrorManager == null)
			MirrorManager = sim.MirrorManager;
	}

	void Update()
	{
		if (MirrorManager == null)
			return;

		UpdateAutoCycle();

		UpdateTriangleDebugLines();

		UpdateDebugLogging();
	}
	void UpdateTriangleDebugLines()
	{
		if (!DebugTriangleLinks || CurrentState != ChoreographyState.Triangle)
		{
			ClearTriangleDebugLines();
			return;
		}

		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count < 3)
		{
			ClearTriangleDebugLines();
			return;
		}

		EnsureTrianglePartners();
		EnsureTriangleDebugLinesRoot();
		EnsureTriangleDebugLineCount(actors.Count * 2);

		int line_index = 0;
		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor actor = actors[i];
			if (!trianglePartners.TryGetValue(actor, out MirrorActor[] partners) || partners == null)
				continue;

			Vector3 start = actor.WorldPosition + Vector3.up * DebugTriangleLineHeight;

			for (int partner_index = 0; partner_index < partners.Length; partner_index++)
			{
				MirrorActor partner = partners[partner_index];
				if (partner == null)
					continue;

				Vector3 end = partner.WorldPosition + Vector3.up * DebugTriangleLineHeight;
				ConfigureTriangleDebugLine(debugTriangleLines[line_index], actor.name + "_to_" + partner.name, start, end);
				line_index++;
			}
		}

		for (int i = line_index; i < debugTriangleLines.Count; i++)
		{
			if (debugTriangleLines[i] != null)
				debugTriangleLines[i].enabled = false;
		}
	}
	void EnsureTrianglePartners()
	{
		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count < 3)
		{
			trianglePartners.Clear();
			return;
		}

		bool needs_rebuild = trianglePartners.Count != actors.Count;
		if (!needs_rebuild)
		{
			for (int i = 0; i < actors.Count; i++)
			{
				if (!trianglePartners.ContainsKey(actors[i]))
				{
					needs_rebuild = true;
					break;
				}
			}
		}

		if (needs_rebuild)
			RebuildTrianglePartners();
	}

	void RebuildTrianglePartners()
	{
		trianglePartners.Clear();
		triangleSide.Clear();
		mirrorsAtPosition.Clear();

		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count < 3)
			return;

		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor actor = actors[i];
			List<MirrorActor> others = new List<MirrorActor>(actors);
			others.Remove(actor);

			MirrorActor partner_a = others[UnityEngine.Random.Range(0, others.Count)];
			others.Remove(partner_a);
			MirrorActor partner_b = others[UnityEngine.Random.Range(0, others.Count)];

			trianglePartners[actor] = new MirrorActor[] { partner_a, partner_b };

			if (DebugChoreography)
				Debug.Log("[choreography] partners | " + actor.name + " -> " + partner_a.name + " + " + partner_b.name);
		}
	}

	void EnsureTriangleDebugLinesRoot()
	{
		if (debugTriangleLinesRoot != null)
			return;

		GameObject root = new GameObject("triangle_debug_lines");
		root.hideFlags = HideFlags.DontSave;
		root.transform.SetParent(transform, false);
		debugTriangleLinesRoot = root.transform;
	}

	void EnsureTriangleDebugLineCount(int required_count)
	{
		while (debugTriangleLines.Count < required_count)
		{
			GameObject line_object = new GameObject("triangle_debug_line_" + debugTriangleLines.Count);
			line_object.hideFlags = HideFlags.DontSave;
			line_object.transform.SetParent(debugTriangleLinesRoot, false);

			LineRenderer line = line_object.AddComponent<LineRenderer>();
			line.positionCount = 2;
			line.useWorldSpace = true;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			line.textureMode = LineTextureMode.Stretch;
			line.alignment = LineAlignment.View;
			line.numCapVertices = 0;
			line.numCornerVertices = 0;
			line.material = DebugTriangleLineMaterial != null ? DebugTriangleLineMaterial : new Material(Shader.Find("Sprites/Default"));
			debugTriangleLines.Add(line);
		}
	}

	void ConfigureTriangleDebugLine(LineRenderer line, string line_name, Vector3 start, Vector3 end)
	{
		if (line == null)
			return;

		line.name = line_name;
		line.enabled = true;
		line.startWidth = DebugTriangleLineWidth;
		line.endWidth = DebugTriangleLineWidth;
		line.SetPosition(0, start);
		line.SetPosition(1, end);
	}

	void ClearTriangleDebugLines()
	{
		for (int i = 0; i < debugTriangleLines.Count; i++)
		{
			if (debugTriangleLines[i] != null)
				debugTriangleLines[i].enabled = false;
		}
	}

	void UpdateAutoCycle()
	{
		if (CurrentState == ChoreographyState.Triangle)
		{
			UpdateTriangleAutoCycle();
			return;
		}

		UpdateRandomPatternAutoCycle();
	}

	void UpdateTriangleAutoCycle()
	{
		bool is_stable = IsTriangleStable();

		if (daddyGazeActive)
		{
			daddyGazeTimer += Time.deltaTime;
			if (daddyGazeTimer >= DaddyGazeHoldDuration)
			{
				daddyGazeActive = false;
				StartRandomPattern();
			}
			return;
		}

		if (is_stable)
		{
			triangleStableTimer += Time.deltaTime;

			if (!triangleSettledThisCycle && triangleStableTimer >= TriangleStableHoldDuration)
			{
				triangleSettledThisCycle = true;
				if (DebugChoreography)
				{
					Debug.Log(
						"[choreography] triangle settled | hold=" + triangleStableTimer.ToString("F2") +
						" | avg_partner_dist=" + lastTriangleAverageDistanceToTarget.ToString("F3") +
						" | max_partner_dist=" + lastTriangleMaxDistanceToTarget.ToString("F3") +
						" | max_speed=" + lastTriangleMaxSpeed.ToString("F3")
					);
				}
				EmitTriangleSettled();
				daddyGazeActive = true;
				daddyGazeTimer = 0f;
			}
		}
		else
		{
			triangleStableTimer = 0f;
			triangleSettledThisCycle = false;
		}
	}

	void UpdateRandomPatternAutoCycle()
	{
		if (CurrentState == ChoreographyState.Circle)
		{
			UpdateCircleState();
			return;
		}

		activePatternTimer += Time.deltaTime;
		if (activePatternTimer >= activePatternDuration)
			ReturnToTriangleFromPattern();
	}

	bool IsTriangleStable()
	{
		lastTriangleUnstableReason = "";
		lastTriangleAverageDistanceToTarget = 0f;
		lastTriangleMaxDistanceToTarget = 0f;
		lastTriangleAverageDistanceToAnchor = 0f;
		lastTriangleMaxSpeed = 0f;

		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count < 3)
		{
			lastTriangleUnstableReason = "not_enough_actors";
			return false;
		}

		Vector3 anchor = GetResolvedAnchorPoint();
		anchor.y = 0f;

		Vector3 center = Vector3.zero;
		float total_speed = 0f;
		float max_speed = 0f;
		float total_distance_to_anchor = 0f;

		for (int i = 0; i < actors.Count; i++)
		{
			Vector3 actor_pos = actors[i].WorldPosition;
			actor_pos.y = 0f;
			center += actor_pos;

			float speed = actors[i].PlanarVelocity.magnitude;
			total_speed += speed;
			if (speed > max_speed)
				max_speed = speed;

			total_distance_to_anchor += Vector3.Distance(actor_pos, anchor);
		}

		center /= actors.Count;

		float center_distance_to_anchor = Vector3.Distance(center, anchor);
		float average_speed = total_speed / actors.Count;
		float average_distance_to_anchor = total_distance_to_anchor / actors.Count;

		lastTriangleAverageDistanceToAnchor = average_distance_to_anchor;
		lastTriangleMaxSpeed = max_speed;
		lastTriangleAverageDistanceToTarget = 0f;
		lastTriangleMaxDistanceToTarget = 0f;

if (average_speed > TriangleStableAverageSpeedThreshold)
		{
			lastTriangleUnstableReason = "average_speed:" + average_speed.ToString("F3");
			return false;
		}

		if (max_speed > TriangleStableSpeedThreshold)
		{
			lastTriangleUnstableReason = "max_speed:" + max_speed.ToString("F3");
			return false;
		}

		if (PendulumExclusionHalfWidth > 0f)
		{
			for (int i = 0; i < actors.Count; i++)
			{
				float z = actors[i].WorldPosition.z;
				if (Mathf.Abs(z) < PendulumExclusionHalfWidth)
				{
					lastTriangleUnstableReason = "pendulum_zone:" + actors[i].name + ":z=" + z.ToString("F3");
					return false;
				}
			}
		}

		for (int i = 0; i < actors.Count; i++)
		{
			if (!IsMirrorAtTrianglePosition(actors[i]))
			{
				lastTriangleUnstableReason = "mirror_not_at_position:" + actors[i].name;
				return false;
			}
		}

		lastTriangleUnstableReason = "stable";
		return true;
	}

	void StartRandomPattern()
	{
		List<ChoreographyState> choices = BuildRandomPatternChoices();
		if (choices.Count == 0)
			return;

		if (choices.Count > 1)
			choices.Remove(lastRandomPattern);

		if (choices.Count == 0)
			choices = BuildRandomPatternChoices();

		ClearAllMirrorFacingOverrides();
		ChoreographyState next_pattern = choices[UnityEngine.Random.Range(0, choices.Count)];
		activeRandomPattern = next_pattern;
		lastRandomPattern = next_pattern;
		activePatternTimer = 0f;
		activePatternDuration = UnityEngine.Random.Range(PatternDurationMin, PatternDurationMax);

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] pattern start | from=Triangle" +
				" | to=" + next_pattern +
				" | duration=" + activePatternDuration.ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2")
			);
		}

		EmitPatternStarted(next_pattern);
		SetState(next_pattern);
	}
	List<ChoreographyState> BuildRandomPatternChoices()
	{
		List<ChoreographyState> choices = new List<ChoreographyState>();

		if (IncludeSpiral)
			choices.Add(ChoreographyState.Spiral);
		if (IncludeCircle)
			choices.Add(ChoreographyState.Circle);
		if (IncludeChaos)
			choices.Add(ChoreographyState.Chaos);
		if (IncludeScatter)
			choices.Add(ChoreographyState.Scatter);
		if (IncludeLine)
			choices.Add(ChoreographyState.Line);

		return choices;
	}

	void ReturnToTriangleFromPattern()
	{
		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] pattern complete | state=" + activeRandomPattern +
				" | elapsed=" + activePatternTimer.ToString("F2") +
				" | returning=Triangle"
			);
		}

		ClearAllMirrorFacingOverrides();
		EmitPatternCompleted(activeRandomPattern);
		RefreshTargets();
		activeRandomPattern = ChoreographyState.Triangle;
		activePatternTimer = 0f;
		activePatternDuration = 0f;
		triangleStableTimer = 0f;
		triangleSettledThisCycle = false;
		daddyGazeActive = false;
		daddyGazeTimer = 0f;
		SetState(ChoreographyState.Triangle);
	}

	public void RefreshTargets()
	{
		if (CurrentState == ChoreographyState.Triangle)
			RebuildTrianglePartners();
		if (CurrentState == ChoreographyState.Circle)
			ComputeCircleTargets();

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] refresh | state=" + CurrentState +
				" | active=" + GetActiveActors().Count +
				" | center=" + GetMirrorCenter().ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2") +
				" | anchor_pull=" + AnchorPullStrength.ToString("F2")
			);
		}
	}

	public void SetState(ChoreographyState newState)
	{
		if (CurrentState == newState)
			return;

		EmitStateExited(CurrentState);
		CurrentState = newState;
		EmitStateEntered(CurrentState);

		if (newState == ChoreographyState.Triangle)
		{
			RebuildTrianglePartners();
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

		if (newState == ChoreographyState.Chaos)
		{
			StartChaos();
			return;
		}

		if (newState == ChoreographyState.Scatter)
		{
			StartScatter();
			return;
		}

		if (newState == ChoreographyState.Line)
		{
			StartLine();
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
			return actor.WorldPosition;

		int index = actors.IndexOf(actor);
		if (index < 0)
			return actor.WorldPosition;

		Vector3 anchorPoint = GetResolvedAnchorPoint();
		Vector3 direction = LineDirection.normalized;
		Vector3 start = anchorPoint - direction * ((actors.Count - 1) * 0.5f * LineSpacing);

		return start + direction * (index * LineSpacing);
	}

	public bool IsMirrorAtTrianglePosition(MirrorActor actor)
	{
		if (actor == null || actor.IsBroken)
		{
			mirrorsAtPosition.Remove(actor);
			return false;
		}

		if (actor.PlanarVelocity.magnitude > TriangleStableSpeedThreshold)
		{
			mirrorsAtPosition.Remove(actor);
			return false;
		}

		Vector3 target = GetTriangleTargetFor(actor);
		Vector3 pos = actor.WorldPosition;
		target.y = 0f;
		pos.y = 0f;

		float distance_to_target = Vector3.Distance(pos, target);
		bool was_stable = mirrorsAtPosition.Contains(actor);
		float threshold = was_stable ? TriangleStableDistanceHysteresis : TriangleStableDistanceTolerance;

		if (distance_to_target <= threshold)
		{
			mirrorsAtPosition.Add(actor);
			return true;
		}

		mirrorsAtPosition.Remove(actor);
		return false;
	}

	public MirrorActor[] GetTrianglePartnersFor(MirrorActor actor)
	{
		EnsureTrianglePartners();
		if (trianglePartners.TryGetValue(actor, out MirrorActor[] partners))
			return partners;
		return null;
	}

	public Vector3 GetTriangleTargetFor(MirrorActor actor)
	{
		List<MirrorActor> actors = GetActiveActors();
		if (actors.Count < 3)
			return ApplyAnchorCoupling(actor.WorldPosition);

		EnsureTrianglePartners();
		if (!trianglePartners.TryGetValue(actor, out MirrorActor[] partners) || partners == null || partners.Length < 2)
			return ApplyAnchorCoupling(actor.WorldPosition);

		MirrorActor agent_a = partners[0];
		MirrorActor agent_b = partners[1];
		if (agent_a == null || agent_b == null)
			return ApplyAnchorCoupling(actor.WorldPosition);

		Vector3 current_position = actor.WorldPosition;
		Vector3 anchor = GetResolvedAnchorPoint();
		Vector3 a = agent_a.WorldPosition;
		Vector3 b = agent_b.WorldPosition;
		a.y = current_position.y;
		b.y = current_position.y;
		anchor.y = current_position.y;

		Vector3 mid = (a + b) * 0.5f;
		Vector3 axis = b - a;
		if (axis.sqrMagnitude <= 0.0001f)
			return ApplyAnchorCoupling(current_position);

		Vector3 normal = new Vector3(-axis.z, 0f, axis.x).normalized;
		float signed_distance = Vector3.Dot(current_position - mid, normal);
		float target_distance = Mathf.Clamp(Mathf.Abs(signed_distance), TriangleMinDistance, TriangleMaxDistance);
		if (target_distance <= 0.0001f)
			target_distance = TriangleMinDistance;

		Vector3 target_positive = mid + normal * target_distance;
		Vector3 target_negative = mid - normal * target_distance;

		Vector3 flat_pos = current_position;
		flat_pos.y = 0f;
		float dist_to_anchor = Vector3.Distance(flat_pos, anchor);

		int side;
		if (triangleSide.TryGetValue(actor, out int cached_side))
		{
			side = cached_side;
		}
		else
		{
			if (dist_to_anchor > AnchorOuterLimit)
				side = Vector3.Distance(target_positive, anchor) <= Vector3.Distance(target_negative, anchor) ? 1 : -1;
			else
				side = signed_distance >= 0f ? 1 : -1;

			triangleSide[actor] = side;
		}

		Vector3 target = side > 0 ? target_positive : target_negative;

		target = PushOutOfPendulumZone(target, current_position);
		target = ClampToStageBounds(target);
		return ApplyAnchorCoupling(target);
	}

	static readonly Vector2[] stageBounds = new Vector2[]
	{
		new Vector2(-5.96f, 4.34f),   // haut gauche  (x, z)
		new Vector2(6.03f, 4.28f),    // haut droite
		new Vector2(7.37f, -6f),      // bas droite
		new Vector2(-7.33f, -6f),     // bas gauche
	};

	Vector3 ClampToStageBounds(Vector3 target)
	{
		Vector2 point = new Vector2(target.x, target.z);

		if (IsInsidePolygon(point, stageBounds))
			return target;

		Vector2 closest = ClosestPointOnPolygon(point, stageBounds);
		target.x = closest.x;
		target.z = closest.y;
		return target;
	}

	static bool IsInsidePolygon(Vector2 point, Vector2[] polygon)
	{
		bool inside = false;
		int count = polygon.Length;

		for (int i = 0, j = count - 1; i < count; j = i++)
		{
			if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
				point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
			{
				inside = !inside;
			}
		}

		return inside;
	}

	static Vector2 ClosestPointOnPolygon(Vector2 point, Vector2[] polygon)
	{
		float bestDist = float.MaxValue;
		Vector2 bestPoint = point;
		int count = polygon.Length;

		for (int i = 0, j = count - 1; i < count; j = i++)
		{
			Vector2 a = polygon[j];
			Vector2 b = polygon[i];
			Vector2 ab = b - a;
			float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
			Vector2 projected = a + ab * t;
			float dist = (point - projected).sqrMagnitude;

			if (dist < bestDist)
			{
				bestDist = dist;
				bestPoint = projected;
			}
		}

		return bestPoint;
	}

	Vector3 PushOutOfPendulumZone(Vector3 target, Vector3 current_position)
	{
		if (PendulumExclusionHalfWidth <= 0f)
			return target;

		if (Mathf.Abs(target.z) >= PendulumExclusionHalfWidth)
			return target;

		// La cible est dans la zone interdite — pousse vers le côté où se trouve le miroir
		// Marge pour que le miroir se stabilise bien en dehors de la zone
		float push_z = PendulumExclusionHalfWidth + TriangleStableCenterDistanceTolerance;
		float mirror_z = current_position.z;
		if (mirror_z >= 0f)
			target.z = push_z;
		else
			target.z = -push_z;

		return target;
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
		CurrentState = ChoreographyState.Spiral;
		activePatternTimer = 0f;
		ComputeCenter();

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] start spiral | center=" + Center.ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2") +
				" | spiral_strength=" + SpiralStrength.ToString("F2")
			);
		}
	}
	void StartChaos()
	{
		CurrentState = ChoreographyState.Chaos;
		activePatternTimer = 0f;
		ComputeCenter();

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] start chaos | center=" + Center.ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2") +
				" | chaos_strength=" + ChaosStrength.ToString("F2") +
				" | orbit_strength=" + ChaosOrbitStrength.ToString("F2")
			);
		}
	}

	void StartScatter()
	{
		CurrentState = ChoreographyState.Scatter;
		activePatternTimer = 0f;
		ComputeCenter();

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] start scatter | center=" + Center.ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2") +
				" | scatter_strength=" + ScatterStrength.ToString("F2")
			);
		}
	}

	void StartLine()
	{
		CurrentState = ChoreographyState.Line;
		activePatternTimer = 0f;
		ComputeCenter();

		if (DebugChoreography)
		{
			Debug.Log(
				"[choreography] start line | center=" + Center.ToString("F2") +
				" | anchor=" + GetResolvedAnchorPoint().ToString("F2") +
				" | spacing=" + LineSpacing.ToString("F2") +
				" | direction=" + LineDirection.ToString("F2")
			);
		}
	}

	void StartCircle()
	{
		CurrentState = ChoreographyState.Circle;
		CurrentCirclePhase = CirclePhase.Move;
		circleHoldTimer = 0f;
		circlePhaseTimer = 0f;
		activePatternTimer = 0f;

		ComputeCenter();
		ComputeCircleTargets();
		if (DebugChoreography)
			Debug.Log("[choreography] start circle | center=" + Center.ToString("F2") + " | anchor=" + GetResolvedAnchorPoint().ToString("F2"));
	}

	void StopCircle()
	{
		ReturnToTriangleFromPattern();
		if (DebugChoreography)
			Debug.Log("[choreography] stop circle -> triangle");
	}

	void UpdateCircleState()
	{
		circlePhaseTimer += Time.deltaTime;

		if (CurrentCirclePhase == CirclePhase.Move)
		{
			if (AllAtTargets())
			{
				CurrentCirclePhase = CirclePhase.Orient;
				circlePhaseTimer = 0f;

				if (DebugChoreography)
					Debug.Log("[choreography] circle phase move -> orient");
			}
			else if (circlePhaseTimer >= CircleMoveTimeout)
			{
				CurrentCirclePhase = CirclePhase.Orient;
				circlePhaseTimer = 0f;

				if (DebugChoreography)
					Debug.Log("[choreography] circle move timeout -> orient");
			}
		}
		else if (CurrentCirclePhase == CirclePhase.Orient)
		{
			if (AllOriented())
			{
				CurrentCirclePhase = CirclePhase.Hold;
				circlePhaseTimer = 0f;
				circleHoldTimer = 0f;

				if (DebugChoreography)
					Debug.Log("[choreography] circle phase orient -> hold");
			}
			else if (circlePhaseTimer >= CircleOrientTimeout)
			{
				CurrentCirclePhase = CirclePhase.Hold;
				circlePhaseTimer = 0f;
				circleHoldTimer = 0f;

				if (DebugChoreography)
					Debug.Log("[choreography] circle orient timeout -> hold");
			}
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
			center += actors[i].WorldPosition;

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
			float bestDistance = Vector3.Distance(actor.WorldPosition, best);

			for (int j = 0; j < slots.Count; j++)
			{
				float distance = Vector3.Distance(actor.WorldPosition, slots[j]);
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
					" | pos=" + actor.WorldPosition.ToString("F2") +
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
			if (!actors[i].IsOrientedToPoint(anchorPoint, CircleOrientationTolerance))
				return false;
		}

		return true;
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

		float average_speed = 0f;
		for (int i = 0; i < actors.Count; i++)
			average_speed += actors[i].PlanarVelocity.magnitude;
		average_speed = actors.Count > 0 ? average_speed / actors.Count : 0f;

		Debug.Log(
			"[choreography] tick | state=" + CurrentState +
			" | phase=" + CurrentCirclePhase +
			" | active=" + actors.Count +
			" | center=" + center.ToString("F2") +
			" | anchor=" + anchor.ToString("F2") +
			" | anchor_delta=" + (center - anchor).ToString("F2") +
			" | triangle_stable_timer=" + triangleStableTimer.ToString("F2") +
			" | avg_speed=" + average_speed.ToString("F3") +
			" | avg_partner_dist=" + lastTriangleAverageDistanceToTarget.ToString("F3") +
			" | max_partner_dist=" + lastTriangleMaxDistanceToTarget.ToString("F3") +
			" | avg_anchor_dist=" + lastTriangleAverageDistanceToAnchor.ToString("F3") +
			" | max_speed=" + lastTriangleMaxSpeed.ToString("F3") +
			" | stable_reason=" + lastTriangleUnstableReason +
			" | active_pattern_timer=" + activePatternTimer.ToString("F2") +
			" | active_pattern_duration=" + activePatternDuration.ToString("F2") +
				" | partners=" + BuildPartnersDebugString(actors)
		);
	}

	string BuildPartnersDebugString(List<MirrorActor> actors)
	{
		System.Text.StringBuilder sb = new System.Text.StringBuilder();

		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor actor = actors[i];
			if (i > 0) sb.Append(" ");

			string label = actor.name.Replace("NeuMirrorActor(Clone)", "M" + i);
			bool stable = IsMirrorAtTrianglePosition(actor);
			sb.Append(stable ? "[" : "(");
			sb.Append(label);

			if (trianglePartners.TryGetValue(actor, out MirrorActor[] partners) && partners != null)
			{
				sb.Append("->");
				for (int p = 0; p < partners.Length; p++)
				{
					if (p > 0) sb.Append(",");
					int partner_index = actors.IndexOf(partners[p]);
					sb.Append("M" + partner_index);
				}

				if (!stable)
				{
					Vector3 pos = actor.WorldPosition; pos.y = 0f;
					Vector3 tgt = GetTriangleTargetFor(actor); tgt.y = 0f;
					float dist = Vector3.Distance(pos, tgt);
					float spd = actor.PlanarVelocity.magnitude;
					sb.Append(" dist=" + dist.ToString("F2") + "/tol=" + TriangleStableDistanceTolerance.ToString("F2"));
					sb.Append(" spd=" + spd.ToString("F3") + "/max=" + TriangleStableSpeedThreshold.ToString("F3"));
				}
			}

			sb.Append(stable ? "]" : ")");
		}

		return sb.ToString();
	}

	public void LogActorSnapshot()
	{
		List<MirrorActor> actors = GetActiveActors();
		Vector3 anchor = GetResolvedAnchorPoint();

		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor actor = actors[i];
			Vector3 toAnchor = anchor - actor.WorldPosition;
			toAnchor.y = 0f;

			Debug.Log(
				"[choreography] actor | name=" + actor.name +
				" | pos=" + actor.WorldPosition.ToString("F2") +
				" | dist_to_anchor=" + toAnchor.magnitude.ToString("F2")
			);
		}
	}

	void EmitStateEntered(ChoreographyState state)
	{
		ChoreographyStateEntered?.Invoke(state);
	}

	void EmitStateExited(ChoreographyState state)
	{
		ChoreographyStateExited?.Invoke(state);
	}

	void EmitPatternStarted(ChoreographyState state)
	{
		ChoreographyPatternStarted?.Invoke(state);
	}

	void EmitPatternCompleted(ChoreographyState state)
	{
		ChoreographyPatternCompleted?.Invoke(state);
	}

	void EmitTriangleSettled()
	{
		TriangleSettled?.Invoke();
		FaceAllMirrorsToDaddy();
	}

	void FaceAllMirrorsToDaddy()
	{
		if (MirrorManager == null)
			return;

		List<MirrorActor> actors = MirrorManager.ActiveMirrors;
		for (int i = 0; i < actors.Count; i++)
		{
			MirrorActor mirror = actors[i];
			if (mirror == null || mirror.IsBroken)
				continue;

			Vector3 daddy_pos = DaddyTransform != null ? DaddyTransform.position : Vector3.zero;
			Vector3 to_daddy = daddy_pos - mirror.WorldPosition;
			to_daddy.y = 0f;
			if (to_daddy.sqrMagnitude < 0.0001f)
				continue;

			mirror.SetFacingOverride(-to_daddy.normalized);
		}
	}

	void ClearAllMirrorFacingOverrides()
	{
		if (MirrorManager == null)
			return;

		List<MirrorActor> actors = MirrorManager.ActiveMirrors;
		for (int i = 0; i < actors.Count; i++)
		{
			if (actors[i] != null)
				actors[i].ClearFacingOverride();
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
	void OnDisable()
	{
		trianglePartners.Clear();
		ClearTriangleDebugLines();
	}
}
