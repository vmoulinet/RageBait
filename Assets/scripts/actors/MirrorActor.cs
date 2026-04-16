using UnityEngine;

public class MirrorActor : MonoBehaviour
{
	[Header("References")]
	public MirrorManager MirrorManager;
	public MirrorSpawnPoint CurrentSpawnPoint;

	[Header("Rig Parts")]
	public Transform FrameRoot;
	public Transform WheelsRoot;
	public Transform MirrorPivotX;
	public GameObject IntactVisual;

	[Header("Movement")]
	public float Mass = 1f;
	public float AttractionStrength = 1.5f;
	public float Damping = 0.98f;
	public float RotationSpeed = 8f;
	public float SteeringAcceleration = 8f;
	public float BrakingAcceleration = 10f;
	public float MaxGroundSpeed = 3.5f;
	public float SpeedRandomRange = 0.3f;
	public float WallSlideDamping = 0.97f;
	public float DirectionResponsiveness = 4f;
	public float VelocityResponsiveness = 3f;
	public float MinRotationSpeed = 0.2f;

	[Header("Avoidance")]
	public float AvoidanceLookAhead = 1.4f;
	public float AvoidanceProbeRadius = 0.35f;
	public float AvoidanceStrength = 3f;
	public float AvoidanceSideBias = 0.25f;
	public LayerMask AvoidanceMask = ~0;

	[Header("Debris Bulldozer")]
	public float DebrisPushForce = 18f;
	public float DebrisPushMaxMass = 0.35f;
	public float DebrisPushUpward = 0.05f;
	public float DebrisPushRadius = 0.6f;

	[Header("Noise")]
	public float NoiseStrength = 0.1f;
	public float NoiseFrequency = 1f;

	[Header("Physics")]
	public bool UseGravity = true;
	public float Downforce = 2f;
	public float GroundStickVelocity = -0.5f;

	[Header("Panel X")]
	public float PanelXAngle = 0f;
	public float PanelXSpeed = 180f;
	public bool PanelSpin = false;
	public float PanelSpinSpeed = 360f;
	public float PanelXVelocityTilt = 25f;

	[Header("Break")]
	public string PendulumTag = "Pendulum";
	public float BreakBoostHopForce = 2f;
	public float BreakBoostHopGravity = 15f;

	[Header("Debug")]
	public bool DebugDraw = true;
	public bool DebugPanel = false;

	[HideInInspector] public Vector3 CircleTarget;

	Vector3 last_break_impact_velocity = Vector3.zero;
	Vector3 last_break_impact_direction = Vector3.zero;
	float last_break_impact_speed = 0f;
	bool is_broken = false;
	bool facing_override_active = false;
	Vector3 facing_override_direction = Vector3.forward;
	float panel_x_target = 0f;
	float panel_x_current = 0f;
	Quaternion panel_base_local_rotation = Quaternion.identity;
	bool panel_base_rotation_cached = false;
	Rigidbody rb;
	Vector3 last_desired_planar_velocity = Vector3.zero;
	Vector3 smoothed_desired_planar_velocity = Vector3.zero;
	Vector3 smoothed_facing_direction = Vector3.forward;
	Collider[] debris_push_hits = new Collider[12];
	Collider[] own_colliders;
	float base_max_ground_speed;
	float speed_boost_multiplier = 1f;
	float speed_boost_timer = 0f;
	float speed_boost_initial_multiplier = 1f;
	float speed_boost_duration = 0f;
	bool is_hopping = false;
	RigidbodyConstraints pre_hop_constraints;
	Quaternion hop_frozen_rotation;

	public bool IsBroken
	{
		get
		{
			return is_broken;
		}
	}

	public Vector3 Velocity
	{
		get
		{
			return rb != null ? rb.linearVelocity : Vector3.zero;
		}
	}

	public Vector3 PlanarVelocity
	{
		get
		{
			Vector3 value = Velocity;
			value.y = 0f;
			return value;
		}
	}

	public Vector3 WorldPosition
	{
		get
		{
			return rb != null ? rb.position : transform.position;
		}
	}

	public Quaternion WorldRotation
	{
		get
		{
			return rb != null ? rb.rotation : transform.rotation;
		}
	}

	public Vector3 LastBreakImpactVelocity
	{
		get
		{
			return last_break_impact_velocity;
		}
	}

	public Vector3 LastBreakImpactDirection
	{
		get
		{
			return last_break_impact_direction;
		}
	}

	public float LastBreakImpactSpeed
	{
		get
		{
			return last_break_impact_speed;
		}
	}

	public float CurrentPanelXAngle
	{
		get
		{
			return panel_x_current;
		}
	}

	public bool AtCircleTarget(float tolerance)
	{
		Vector3 a = WorldPosition;
		Vector3 b = CircleTarget;
		a.y = 0f;
		b.y = 0f;
		return Vector3.Distance(a, b) <= tolerance;
	}

	public bool IsOrientedToPoint(Vector3 point, float toleranceDegrees)
	{
		Vector3 to_point = point - WorldPosition;
		to_point.y = 0f;

		if (to_point.sqrMagnitude <= 0.0001f)
			return true;

		Vector3 forward = WorldRotation * Vector3.forward;
		forward.y = 0f;

		if (forward.sqrMagnitude <= 0.0001f)
			return true;

		return Vector3.Angle(forward.normalized, to_point.normalized) <= toleranceDegrees;
	}

	void Awake()
	{
		base_max_ground_speed = MaxGroundSpeed;
		rb = GetComponent<Rigidbody>();
		if (rb == null)
		{
			Debug.LogError(name + " | MirrorActor requires a Rigidbody.");
			enabled = false;
			return;
		}

		rb.mass = Mathf.Max(0.01f, Mass);
		rb.useGravity = UseGravity;
		rb.isKinematic = false;
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
		rb.constraints =
			RigidbodyConstraints.FreezeRotationX |
			RigidbodyConstraints.FreezeRotationZ;

		own_colliders = GetComponentsInChildren<Collider>(true);
		smoothed_desired_planar_velocity = Vector3.zero;
		smoothed_facing_direction = transform.forward;
		smoothed_facing_direction.y = 0f;
		if (smoothed_facing_direction.sqrMagnitude <= 0.0001f)
			smoothed_facing_direction = Vector3.forward;
		else
			smoothed_facing_direction.Normalize();

		CachePanelBaseRotation();
		panel_x_target = PanelXAngle;
		panel_x_current = PanelXAngle;
		ApplyPanelPoseImmediate();
	}

	void Update()
	{
		if (is_broken)
			return;

		UpdatePanelX();
	}

	void FixedUpdate()
	{
		if (is_broken)
			return;

		if (rb == null)
			return;

		if (MirrorManager == null || MirrorManager.ChoreographyManager == null)
			return;

		rb.mass = Mathf.Max(0.01f, Mass);
		rb.useGravity = UseGravity;

		if (speed_boost_timer > 0f)
		{
			speed_boost_timer -= Time.fixedDeltaTime;
			speed_boost_multiplier = 1f + (speed_boost_initial_multiplier - 1f) * Mathf.Clamp01(speed_boost_timer / speed_boost_duration);
		}
		else
		{
			speed_boost_multiplier = 1f;
		}

		if (is_hopping)
		{
			rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
			rb.angularVelocity = Vector3.zero;
			rb.rotation = hop_frozen_rotation;
			ApplyGrounding();
			return;
		}

		Vector3 desired_planar_velocity = ComputeDesiredPlanarVelocity();
		desired_planar_velocity = ApplyObstacleAvoidance(desired_planar_velocity);

		smoothed_desired_planar_velocity = Vector3.Lerp(
			smoothed_desired_planar_velocity,
			desired_planar_velocity,
			1f - Mathf.Exp(-VelocityResponsiveness * Time.fixedDeltaTime)
		);
		last_desired_planar_velocity = smoothed_desired_planar_velocity;

		ApplyPlanarSteering(smoothed_desired_planar_velocity);
		ApplyGrounding();
		ClampPlanarSpeed();
		PushNearbyDebris();
		UpdateBodyRotation();
	}

	Vector3 ComputeDesiredPlanarVelocity()
	{
		ChoreographyManager choreography = MirrorManager.ChoreographyManager;

		switch (choreography.CurrentState)
		{
			case ChoreographyState.Triangle:
				return ComputeTriangleVelocity(choreography);

			case ChoreographyState.Spiral:
				return ComputeSpiralVelocity(choreography);

			case ChoreographyState.Circle:
				return ComputeCircleVelocity(choreography);

			case ChoreographyState.Chaos:
				return ComputeChaosVelocity(choreography);

			case ChoreographyState.Scatter:
				return ComputeScatterVelocity(choreography);

			case ChoreographyState.Line:
				return ComputeLineVelocity(choreography);

			case ChoreographyState.Pause:
				return Vector3.zero;
		}

		return Vector3.zero;
	}

	Vector3 ComputeTriangleVelocity(ChoreographyManager choreography)
	{
		Vector3 target = choreography.GetTriangleTargetFor(this);
		return BuildDesiredVelocity(target, AttractionStrength, true, choreography);
	}

	Vector3 ComputeSpiralVelocity(ChoreographyManager choreography)
	{
		Vector3 center = choreography.GetResolvedAnchorPoint();
		Vector3 to_center = WorldPosition - center;
		to_center.y = 0f;

		if (to_center.sqrMagnitude <= 0.0001f)
			return Vector3.zero;

		Vector3 tangent = new Vector3(-to_center.z, 0f, to_center.x).normalized;
		Vector3 desired = tangent * choreography.SpiralStrength;
		desired += ComputeAnchorIntent(choreography);
		return ClampDesiredVelocity(desired + NoiseVelocity());
	}

	Vector3 ComputeCircleVelocity(ChoreographyManager choreography)
	{
		if (AtCircleTarget(0.05f))
			return Vector3.zero;

		Vector3 to_target = CircleTarget - WorldPosition;
		to_target.y = 0f;

		if (to_target.sqrMagnitude <= 0.0001f)
			return Vector3.zero;

		float distance = to_target.magnitude;
		float speed = Mathf.Max(MaxGroundSpeed * 0.3f, Mathf.Min(MaxGroundSpeed, distance * AttractionStrength));
		Vector3 desired = to_target.normalized * speed;
		desired += NoiseVelocity();
		desired += ComputeAnchorIntent(choreography);
		return ClampDesiredVelocity(desired);
	}

	Vector3 ComputeChaosVelocity(ChoreographyManager choreography)
	{
		Vector3 center = choreography.GetResolvedAnchorPoint();
		Vector3 to_center = center - WorldPosition;
		to_center.y = 0f;

		Vector3 tangent = Vector3.zero;
		if (to_center.sqrMagnitude > 0.0001f)
			tangent = new Vector3(-to_center.z, 0f, to_center.x).normalized;

		Vector3 desired = Vector3.zero;
		if (to_center.sqrMagnitude > 0.0001f)
			desired += to_center.normalized * choreography.ChaosStrength;

		desired += tangent * choreography.ChaosOrbitStrength;
		desired += NoiseVelocity();
		return ClampDesiredVelocity(desired);
	}

	Vector3 ComputeScatterVelocity(ChoreographyManager choreography)
	{
		Vector3 center = choreography.GetResolvedAnchorPoint();
		Vector3 away = WorldPosition - center;
		away.y = 0f;

		Vector3 desired = Vector3.zero;
		if (away.sqrMagnitude > 0.0001f)
			desired += away.normalized * choreography.ScatterStrength;

		desired += NoiseVelocity();
		desired += ComputeAnchorIntent(choreography);
		return ClampDesiredVelocity(desired);
	}

	Vector3 ComputeLineVelocity(ChoreographyManager choreography)
	{
		Vector3 target = choreography.GetLineTargetFor(this);
		return BuildDesiredVelocity(target, AttractionStrength, true, choreography);
	}

	Vector3 BuildDesiredVelocity(Vector3 target, float strength, bool include_anchor, ChoreographyManager choreography)
	{
		Vector3 to_target = target - WorldPosition;
		to_target.y = 0f;

		if (to_target.sqrMagnitude <= 0.0001f)
			return Vector3.zero;

		Vector3 desired = to_target.normalized * Mathf.Min(MaxGroundSpeed, to_target.magnitude * Mathf.Max(0.01f, strength));
		desired += NoiseVelocity();

		if (include_anchor)
			desired += ComputeAnchorIntent(choreography);

		return ClampDesiredVelocity(desired);
	}

	Vector3 ComputeAnchorIntent(ChoreographyManager choreography)
	{
		Vector3 anchor = choreography.GetResolvedAnchorPoint();
		Vector3 to_anchor = anchor - WorldPosition;
		to_anchor.y = 0f;

		if (to_anchor.sqrMagnitude <= 0.0001f)
			return Vector3.zero;

		return to_anchor.normalized * choreography.AnchorPullStrength;
	}

	Vector3 NoiseVelocity()
	{
		float seed = Mathf.Abs(GetInstanceID()) * 0.00137f;
		float x = Mathf.PerlinNoise(seed, Time.time * NoiseFrequency) - 0.5f;
		float z = Mathf.PerlinNoise(seed + 31.73f, Time.time * NoiseFrequency) - 0.5f;
		return new Vector3(x, 0f, z) * NoiseStrength;
	}

	Vector3 ClampDesiredVelocity(Vector3 desired)
	{
		desired.y = 0f;
		return Vector3.ClampMagnitude(desired, MaxGroundSpeed * speed_boost_multiplier);
	}

	Vector3 ApplyObstacleAvoidance(Vector3 desired_planar_velocity)
	{
		Vector3 desired_direction = desired_planar_velocity;
		desired_direction.y = 0f;
		if (desired_direction.sqrMagnitude <= 0.0001f)
			return desired_planar_velocity;

		desired_direction.Normalize();

		Vector3 probe_origin = WorldPosition + Vector3.up * 0.35f;
		float probe_distance = Mathf.Max(AvoidanceLookAhead, DebrisPushRadius);

		if (!Physics.SphereCast(probe_origin, AvoidanceProbeRadius, desired_direction, out RaycastHit hit, probe_distance, AvoidanceMask, QueryTriggerInteraction.Ignore))
			return desired_planar_velocity;

		if (hit.collider == null)
			return desired_planar_velocity;

		if (hit.collider.transform.IsChildOf(transform))
			return desired_planar_velocity;

		if (hit.collider.CompareTag(PendulumTag))
			return desired_planar_velocity;

		if (IsIgnorableDebris(hit.collider))
			return desired_planar_velocity;

		Vector3 normal = hit.normal;
		normal.y = 0f;
		if (normal.sqrMagnitude <= 0.0001f)
			return desired_planar_velocity;

		normal.Normalize();

		Vector3 slide_direction = Vector3.ProjectOnPlane(desired_direction, normal);
		slide_direction.y = 0f;
		if (slide_direction.sqrMagnitude <= 0.0001f)
		{
			Vector3 side = Vector3.Cross(Vector3.up, normal);
			slide_direction = side * GetAvoidanceSideSign(desired_direction, normal);
		}

		slide_direction.y = 0f;
		if (slide_direction.sqrMagnitude <= 0.0001f)
			return desired_planar_velocity;

		slide_direction.Normalize();

		float speed = desired_planar_velocity.magnitude;
		Vector3 avoided_velocity = Vector3.Lerp(
			desired_planar_velocity,
			slide_direction * speed,
			Mathf.Clamp01(AvoidanceStrength / Mathf.Max(0.0001f, speed + AvoidanceStrength))
		);

		return ClampDesiredVelocity(avoided_velocity);
	}

	float GetAvoidanceSideSign(Vector3 desired_direction, Vector3 normal)
	{
		Vector3 left = Vector3.Cross(Vector3.up, desired_direction);
		float side = Vector3.Dot(left, normal);
		if (Mathf.Abs(side) <= 0.0001f)
			return AvoidanceSideBias >= 0f ? 1f : -1f;
		return side >= 0f ? -1f : 1f;
	}

	void ApplyPlanarSteering(Vector3 desired_planar_velocity)
	{
		Vector3 current_planar_velocity = PlanarVelocity;
		Vector3 desired_delta = desired_planar_velocity - current_planar_velocity;

		float acceleration = desired_planar_velocity.sqrMagnitude > 0.0001f ? SteeringAcceleration : BrakingAcceleration;
		acceleration *= speed_boost_multiplier;
		Vector3 steering = desired_delta * acceleration;
		steering.y = 0f;

		rb.AddForce(steering, ForceMode.Acceleration);

		if (desired_planar_velocity.sqrMagnitude <= 0.0001f)
		{
			Vector3 damped = Vector3.Lerp(current_planar_velocity, Vector3.zero, 1f - Mathf.Exp(-BrakingAcceleration * Time.fixedDeltaTime));
			Vector3 full_velocity = rb.linearVelocity;
			full_velocity.x = damped.x;
			full_velocity.z = damped.z;
			rb.linearVelocity = full_velocity;
		}
	}

	void ApplyGrounding()
	{
		if (rb == null)
			return;

		if (is_hopping)
		{
			rb.AddForce(Vector3.down * BreakBoostHopGravity, ForceMode.Acceleration);
			if (rb.linearVelocity.y <= 0f)
			{
				is_hopping = false;
				rb.constraints = pre_hop_constraints;
				speed_boost_multiplier = speed_boost_initial_multiplier;
			}
		}
		else if (UseGravity)
		{
			rb.AddForce(Vector3.down * Downforce, ForceMode.Acceleration);
		}

		Vector3 full_velocity = rb.linearVelocity;
		if (!is_hopping && full_velocity.y < GroundStickVelocity)
			full_velocity.y = GroundStickVelocity;
		rb.linearVelocity = full_velocity;
	}

	void ClampPlanarSpeed()
	{
		Vector3 full_velocity = rb.linearVelocity;
		Vector3 planar = new Vector3(full_velocity.x, 0f, full_velocity.z);
		float effective_max = MaxGroundSpeed * speed_boost_multiplier;

		if (planar.sqrMagnitude > effective_max * effective_max)
		{
			planar = planar.normalized * effective_max;
			full_velocity.x = planar.x;
			full_velocity.z = planar.z;
			rb.linearVelocity = full_velocity;
		}
	}

	void PushNearbyDebris()
	{
		if (rb == null || DebrisPushForce <= 0f || DebrisPushRadius <= 0f)
			return;

		Vector3 push_direction = PlanarVelocity;
		if (push_direction.sqrMagnitude <= 0.0001f)
			push_direction = last_desired_planar_velocity;

		push_direction.y = 0f;
		if (push_direction.sqrMagnitude <= 0.0001f)
			return;

		push_direction.Normalize();

		Vector3 overlap_center = WorldPosition + push_direction * (DebrisPushRadius * 0.6f) + Vector3.up * 0.25f;
		int hit_count = Physics.OverlapSphereNonAlloc(overlap_center, DebrisPushRadius, debris_push_hits, AvoidanceMask, QueryTriggerInteraction.Ignore);

		for (int i = 0; i < hit_count; i++)
		{
			Collider hit = debris_push_hits[i];
			if (hit == null)
				continue;

			if (hit.transform.IsChildOf(transform))
				continue;

			if (hit.CompareTag(PendulumTag))
				continue;

			Rigidbody hit_body = hit.attachedRigidbody;
			if (hit_body == null || hit_body == rb || hit_body.isKinematic)
				continue;

			if (hit_body.mass > DebrisPushMaxMass)
				continue;

			IgnoreDebrisCollision(hit);
			Vector3 impulse = push_direction;
			impulse.y = DebrisPushUpward;
			impulse.Normalize();

			hit_body.AddForce(impulse * DebrisPushForce, ForceMode.Acceleration);
		}
	}

	public void SetFacingOverride(Vector3 world_direction)
	{
		world_direction.y = 0f;
		if (world_direction.sqrMagnitude < 0.0001f)
			return;
		facing_override_active = true;
		facing_override_direction = world_direction.normalized;
	}

	public void ClearFacingOverride()
	{
		facing_override_active = false;
	}

	void UpdateBodyRotation()
	{
		if (rb == null)
			return;

		Vector3 target_direction;

		if (facing_override_active)
		{
			target_direction = facing_override_direction;
		}
		else
		{
			target_direction = PlanarVelocity;
			if (target_direction.sqrMagnitude < MinRotationSpeed * MinRotationSpeed)
				target_direction = last_desired_planar_velocity;

			target_direction.y = 0f;
			if (target_direction.sqrMagnitude < 0.0001f)
				return;
		}

		target_direction.Normalize();
		smoothed_facing_direction = Vector3.Slerp(
			smoothed_facing_direction,
			target_direction,
			1f - Mathf.Exp(-DirectionResponsiveness * Time.fixedDeltaTime)
		);

		smoothed_facing_direction.y = 0f;
		if (smoothed_facing_direction.sqrMagnitude < 0.0001f)
			return;

		smoothed_facing_direction.Normalize();

		Quaternion target_rotation = Quaternion.LookRotation(smoothed_facing_direction, Vector3.up);
		Quaternion next_rotation = Quaternion.Slerp(rb.rotation, target_rotation, 1f - Mathf.Exp(-RotationSpeed * Time.fixedDeltaTime));
		rb.MoveRotation(next_rotation);
	}

	void UpdatePanelX()
	{
		if (MirrorPivotX == null)
			return;

		CachePanelBaseRotation();

		if (PanelSpin)
		{
			panel_x_current += PanelSpinSpeed * Time.deltaTime;
		}
		else
		{
			if (is_hopping)
			{
				panel_x_target = 0f;
			}
			else
			{
				float speed_ratio = Mathf.Clamp01(PlanarVelocity.magnitude / Mathf.Max(0.01f, MaxGroundSpeed * speed_boost_multiplier));
				float velocity_tilt = PanelXAngle + speed_ratio * PanelXVelocityTilt;
				panel_x_target = velocity_tilt;
			}
			panel_x_current = Mathf.MoveTowards(panel_x_current, panel_x_target, PanelXSpeed * Time.deltaTime);
		}

		MirrorPivotX.localRotation = panel_base_local_rotation * Quaternion.AngleAxis(panel_x_current, Vector3.right);

		if (DebugPanel && Time.frameCount % 20 == 0)
		{
			Debug.Log(
				name +
				" | panel_spin=" + PanelSpin +
				" | panel_x_current=" + panel_x_current.ToString("F2") +
				" | panel_x_target=" + panel_x_target.ToString("F2") +
				" | pivot=" + MirrorPivotX.name
			);
		}
	}

	void Snap(Vector3 position_to_snap)
	{
		position_to_snap.y = WorldPosition.y;

		if (rb != null)
		{
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
			rb.position = position_to_snap;
			transform.position = position_to_snap;
		}
		else
		{
			transform.position = position_to_snap;
		}
	}

	public void Initialize(MirrorManager mirror_manager)
	{
		MirrorManager = mirror_manager;
	}

	public void ApplySpawnOffset(Vector3 offset)
	{
		if (rb != null)
		{
			Vector3 next_position = rb.position + offset;
			rb.position = next_position;
			transform.position = next_position;
		}
		else
		{
			transform.position += offset;
		}
	}

	public void ResetToSpawn(MirrorSpawnPoint spawn_point)
	{
		CurrentSpawnPoint = spawn_point;
		CurrentSpawnPoint.CurrentMirror = this;

		is_broken = false;

		Vector3 spawn_position = spawn_point.transform.position;
		Quaternion spawn_rotation = spawn_point.transform.rotation;

		if (rb != null)
		{
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
			rb.rotation = spawn_rotation;
			rb.position = spawn_position;
			transform.rotation = spawn_rotation;
			transform.position = spawn_position;
		}
		else
		{
			transform.rotation = spawn_rotation;
			transform.position = spawn_position;
		}

		MaxGroundSpeed = base_max_ground_speed * Random.Range(1f - SpeedRandomRange, 1f + SpeedRandomRange);

		last_desired_planar_velocity = Vector3.zero;
		smoothed_desired_planar_velocity = Vector3.zero;
		smoothed_facing_direction = spawn_rotation * Vector3.forward;
		smoothed_facing_direction.y = 0f;
		if (smoothed_facing_direction.sqrMagnitude <= 0.0001f)
			smoothed_facing_direction = Vector3.forward;
		else
			smoothed_facing_direction.Normalize();

		last_break_impact_velocity = Vector3.zero;
		last_break_impact_direction = Vector3.zero;
		last_break_impact_speed = 0f;
		CachePanelBaseRotation();
		panel_x_target = 0f;
		panel_x_current = 0f;
		PanelSpin = false;
		ApplyPanelPoseImmediate();

		if (IntactVisual != null)
			IntactVisual.SetActive(true);

		gameObject.SetActive(true);
	}

	void ApplyPanelPoseImmediate()
	{
		if (MirrorPivotX == null)
			return;

		CachePanelBaseRotation();
		MirrorPivotX.localRotation = panel_base_local_rotation * Quaternion.AngleAxis(panel_x_current, Vector3.right);
	}

	void CachePanelBaseRotation()
	{
		if (MirrorPivotX == null || panel_base_rotation_cached)
			return;

		panel_base_local_rotation = MirrorPivotX.localRotation;
		panel_base_rotation_cached = true;
	}

	public void SetPanelXTarget(float angle_degrees)
	{
		panel_x_target = angle_degrees;
		PanelSpin = false;
	}

	public void TriggerPanelBeat(float angle_degrees)
	{
		panel_x_target = angle_degrees;
		PanelSpin = false;
	}

	public void SetPanelSpin(bool enabled, float spin_speed = -1f)
	{
		PanelSpin = enabled;

		if (spin_speed >= 0f)
			PanelSpinSpeed = spin_speed;
	}

	void OnCollisionEnter(Collision collision)
	{
		if (collision.collider != null && IsIgnorableDebris(collision.collider))
		{
			IgnoreDebrisCollision(collision.collider);
			return;
		}

		if (collision.contacts.Length > 0)
		{
			TryBreak(collision.collider, collision.contacts[0].point);
			ApplyWallSlide(collision);
		}
	}

	void OnCollisionStay(Collision collision)
	{
		if (collision.collider != null && IsIgnorableDebris(collision.collider))
		{
			IgnoreDebrisCollision(collision.collider);
			return;
		}

		ApplyWallSlide(collision);
	}

	void ApplyWallSlide(Collision collision)
	{
		if (rb == null)
			return;

		if (collision.collider != null && collision.collider.CompareTag(PendulumTag))
			return;

		if (collision.contacts == null || collision.contacts.Length == 0)
			return;

		Vector3 planar_velocity = PlanarVelocity;
		if (planar_velocity.sqrMagnitude <= 0.0001f)
			return;

		for (int i = 0; i < collision.contacts.Length; i++)
		{
			Vector3 normal = collision.contacts[i].normal;
			normal.y = 0f;

			if (normal.sqrMagnitude <= 0.0001f)
				continue;

			normal.Normalize();
			float push_into_wall = Vector3.Dot(planar_velocity, -normal);
			if (push_into_wall <= 0f)
				continue;

			planar_velocity = Vector3.ProjectOnPlane(planar_velocity, normal) * WallSlideDamping;
			planar_velocity += Vector3.ProjectOnPlane(last_desired_planar_velocity, normal) * (1f - WallSlideDamping);
		}

		Vector3 full_velocity = rb.linearVelocity;
		full_velocity.x = planar_velocity.x;
		full_velocity.z = planar_velocity.z;
		rb.linearVelocity = full_velocity;
	}

	bool IsIgnorableDebris(Collider other)
	{
		if (other == null)
			return false;

		if (other.transform.IsChildOf(transform))
			return false;

		if (other.CompareTag(PendulumTag))
			return false;

		MirrorDebris debris = other.GetComponentInParent<MirrorDebris>();
		if (debris == null)
			return false;

		Rigidbody other_body = other.attachedRigidbody;
		if (other_body == null || other_body == rb || other_body.isKinematic)
			return false;

		return other_body.mass <= DebrisPushMaxMass;
	}

	void IgnoreDebrisCollision(Collider other)
	{
		if (!IsIgnorableDebris(other) || own_colliders == null)
			return;

		for (int i = 0; i < own_colliders.Length; i++)
		{
			Collider own = own_colliders[i];
			if (own == null || own == other)
				continue;

			Physics.IgnoreCollision(own, other, true);
		}
	}

	void OnTriggerEnter(Collider other)
	{
		TryBreak(other, other.ClosestPoint(WorldPosition));
	}

	void TryBreak(Collider other, Vector3 impact_point)
	{
		if (is_broken || !other.CompareTag(PendulumTag))
			return;

		PendulumManager pendulum = other.GetComponentInParent<PendulumManager>();
		if (pendulum != null)
		{
			last_break_impact_velocity = pendulum.CurrentWorldVelocity;

			Vector3 flat_direction = last_break_impact_velocity;
			flat_direction.y = 0f;

			if (flat_direction.sqrMagnitude > 0.0001f)
				last_break_impact_direction = flat_direction.normalized;
			else
				last_break_impact_direction = pendulum.GetImpactDirection();

			last_break_impact_speed = last_break_impact_velocity.magnitude;
		}
		else
		{
			last_break_impact_velocity = Vector3.zero;
			last_break_impact_direction = Vector3.zero;
			last_break_impact_speed = 0f;
		}

		Break(impact_point);
	}

	public void ApplySpeedBoost(float multiplier, float duration)
	{
		speed_boost_initial_multiplier = multiplier;
		speed_boost_multiplier = 1f;
		speed_boost_duration = duration;
		speed_boost_timer = duration;

		if (rb != null && !is_hopping)
		{
			pre_hop_constraints = rb.constraints;
			rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
			smoothed_desired_planar_velocity = Vector3.zero;
			hop_frozen_rotation = rb.rotation;
			rb.AddForce(Vector3.up * BreakBoostHopForce, ForceMode.VelocityChange);
			is_hopping = true;
		}
	}

	public void ForceBreak()
	{
		if (is_broken)
			return;

		last_break_impact_velocity = Vector3.zero;
		last_break_impact_direction = Vector3.down;
		last_break_impact_speed = 0f;
		Break(WorldPosition);
	}

	void Break(Vector3 impact_point)
	{
		is_broken = true;

		if (rb != null)
		{
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
		}

		if (IntactVisual != null)
			IntactVisual.SetActive(false);

		if (DebugDraw)
		{
			Debug.Log(
				name +
				" | break | impact_point=" + impact_point.ToString("F2") +
				" | impact_velocity=" + last_break_impact_velocity.ToString("F2") +
				" | impact_direction=" + last_break_impact_direction.ToString("F2") +
				" | impact_speed=" + last_break_impact_speed.ToString("F2")
			);
		}

		if (MirrorManager != null)
			MirrorManager.OnMirrorBroken(this, impact_point);

		gameObject.SetActive(false);
	}

	void OnDrawGizmosSelected()
	{
		if (!DebugDraw)
			return;

		Gizmos.color = Color.cyan;
		Gizmos.DrawLine(WorldPosition, WorldPosition + PlanarVelocity);

		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(WorldPosition, WorldPosition + last_desired_planar_velocity);

		if (MirrorPivotX != null)
		{
			Gizmos.color = Color.magenta;
			Gizmos.DrawLine(MirrorPivotX.position, MirrorPivotX.position + MirrorPivotX.right * 0.75f);
		}
	}
}