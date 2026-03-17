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

	[Header("Triangle Links")]
	public Transform AgentA;
	public Transform AgentB;

	[Header("Movement")]
	public float Mass = 1f;
	public float AttractionStrength = 1.5f;
	public float Damping = 0.98f;
	public float RotationSpeed = 8f;

	[Header("Noise")]
	public float NoiseStrength = 0.1f;
	public float NoiseFrequency = 1f;

	[Header("Panel X")]
	public float PanelXAngle = 0f;
	public float PanelXSpeed = 180f;
	public bool PanelSpin = false;
	public float PanelSpinSpeed = 360f;

	[Header("Break")]
	public string PendulumTag = "Pendulum";

	[Header("Debug")]
	public bool DebugDraw = true;

	[HideInInspector] public Vector3 CircleTarget;

	Vector3 velocity;
	bool is_broken = false;
	float panel_x_target = 0f;

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
			return velocity;
		}
	}

	public bool AtCircleTarget(float tolerance)
	{
		Vector3 a = transform.position;
		Vector3 b = CircleTarget;
		a.y = 0f;
		b.y = 0f;
		return Vector3.Distance(a, b) <= tolerance;
	}

	public bool IsOrientedToPoint(Vector3 point, float toleranceDegrees)
	{
		Vector3 to_point = point - transform.position;
		to_point.y = 0f;

		if (to_point.sqrMagnitude <= 0.0001f)
			return true;

		return Vector3.Angle(transform.forward, to_point.normalized) <= toleranceDegrees;
	}

	public float CurrentPanelXAngle
	{
		get
		{
			if (MirrorPivotX == null)
				return 0f;

			float angle = MirrorPivotX.localEulerAngles.x;
			if (angle > 180f)
				angle -= 360f;

			return angle;
		}
	}

	void Start()
	{
		velocity = Random.insideUnitSphere * 0.3f;
		velocity.y = 0f;

		panel_x_target = PanelXAngle;
		ApplyPanelPoseImmediate();
	}

	void Update()
	{
		if (is_broken)
			return;

		if (MirrorManager == null || MirrorManager.ChoreographyManager == null)
			return;

		UpdateMovement();
		UpdateBodyRotation();
		UpdatePanelX();

		velocity *= Damping;
		transform.position += velocity * Time.deltaTime;
	}

	void UpdateMovement()
	{
		ChoreographyManager choreography = MirrorManager.ChoreographyManager;

		switch (choreography.CurrentState)
		{
			case ChoreographyState.Triangle:
				UpdateTriangle(choreography);
				break;

			case ChoreographyState.Spiral:
				UpdateSpiral(choreography);
				break;

			case ChoreographyState.Circle:
				UpdateCircle(choreography);
				break;

			case ChoreographyState.Chaos:
				UpdateChaos(choreography);
				break;

			case ChoreographyState.Scatter:
				UpdateScatter(choreography);
				break;

			case ChoreographyState.Line:
				UpdateLine(choreography);
				break;

			case ChoreographyState.Pause:
				UpdatePause();
				break;
		}
	}

	void UpdateTriangle(ChoreographyManager choreography)
	{
		Vector3 target = TriangleTarget(choreography);
		Vector3 force = (target - transform.position) * AttractionStrength;
		force += Noise();
		force += AnchorCouplingForce(choreography);
		ApplyForce(force);
	}

	Vector3 TriangleTarget(ChoreographyManager choreography)
	{
		if (AgentA == null || AgentB == null)
			return transform.position;

		Vector3 a = AgentA.position;
		Vector3 b = AgentB.position;
		a.y = b.y = transform.position.y;

		Vector3 mid = (a + b) * 0.5f;
		Vector3 axis = b - a;

		if (axis.sqrMagnitude <= 0.0001f)
			return transform.position;

		Vector3 normal = new Vector3(-axis.z, 0f, axis.x).normalized;
		float signed_distance = Vector3.Dot(transform.position - mid, normal);
		float target_distance = Mathf.Clamp(Mathf.Abs(signed_distance), choreography.TriangleMinDistance, choreography.TriangleMaxDistance);

		return mid + normal * (signed_distance < 0f ? -target_distance : target_distance);
	}

	void UpdateSpiral(ChoreographyManager choreography)
	{
		Vector3 center = choreography.GetResolvedAnchorPoint();
		Vector3 to_center = transform.position - center;
		to_center.y = 0f;

		if (to_center.sqrMagnitude <= 0.0001f)
			return;

		Vector3 tangent = new Vector3(-to_center.z, 0f, to_center.x).normalized;
		Vector3 force = tangent * choreography.SpiralStrength;
		force += AnchorCouplingForce(choreography);
		ApplyForce(force);
	}

	void UpdateCircle(ChoreographyManager choreography)
	{
		float distance = Vector3.Distance(transform.position, CircleTarget);

		if (distance <= choreography.ToleranceRadius)
		{
			Snap(CircleTarget);
			return;
		}

		Vector3 force = (CircleTarget - transform.position) * AttractionStrength;
		force += AnchorCouplingForce(choreography);
		ApplyForce(force);
	}

	void UpdateChaos(ChoreographyManager choreography)
	{
		Vector3 center = choreography.GetResolvedAnchorPoint();
		Vector3 to_center = center - transform.position;
		to_center.y = 0f;

		Vector3 tangent = Vector3.zero;
		if (to_center.sqrMagnitude > 0.0001f)
			tangent = new Vector3(-to_center.z, 0f, to_center.x).normalized;

		Vector3 force = to_center.sqrMagnitude > 0.0001f ? to_center.normalized * choreography.ChaosStrength : Vector3.zero;
		force += tangent * choreography.ChaosOrbitStrength;
		force += Noise();
		ApplyForce(force);
	}

	void UpdateScatter(ChoreographyManager choreography)
	{
		Vector3 center = choreography.GetResolvedAnchorPoint();
		Vector3 away = transform.position - center;
		away.y = 0f;

		Vector3 force = Vector3.zero;
		if (away.sqrMagnitude > 0.0001f)
			force += away.normalized * choreography.ScatterStrength;

		force += Noise();
		force += AnchorCouplingForce(choreography);
		ApplyForce(force);
	}

	void UpdateLine(ChoreographyManager choreography)
	{
		Vector3 target = choreography.GetLineTargetFor(this);
		Vector3 force = (target - transform.position) * AttractionStrength;
		force += AnchorCouplingForce(choreography);
		ApplyForce(force);
	}

	void UpdatePause()
	{
		velocity *= 0.9f;
	}

	void UpdateBodyRotation()
	{
		Vector3 direction = velocity;
		direction.y = 0f;

		if (direction.sqrMagnitude < 0.0001f)
			return;

		Quaternion target_rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
		if (FrameRoot != null)
			FrameRoot.rotation = Quaternion.Slerp(FrameRoot.rotation, target_rotation, RotationSpeed * Time.deltaTime);
		else
			transform.rotation = Quaternion.Slerp(transform.rotation, target_rotation, RotationSpeed * Time.deltaTime);
	}

	void UpdatePanelX()
	{
		if (MirrorPivotX == null)
			return;

		if (PanelSpin)
		{
			MirrorPivotX.Rotate(Vector3.right, PanelSpinSpeed * Time.deltaTime, Space.Self);
			return;
		}

		float current_x = CurrentPanelXAngle;
		float next_x = Mathf.MoveTowards(current_x, panel_x_target, PanelXSpeed * Time.deltaTime);

		Vector3 euler = MirrorPivotX.localEulerAngles;
		MirrorPivotX.localRotation = Quaternion.Euler(next_x, euler.y, euler.z);
	}

	Vector3 AnchorCouplingForce(ChoreographyManager choreography)
	{
		Vector3 anchor = choreography.GetResolvedAnchorPoint();
		Vector3 to_anchor = anchor - transform.position;
		to_anchor.y = 0f;

		if (to_anchor.sqrMagnitude <= 0.0001f)
			return Vector3.zero;

		return to_anchor.normalized * choreography.AnchorPullStrength;
	}

	void ApplyForce(Vector3 force)
	{
		force.y = 0f;

		velocity += (force / Mathf.Max(0.0001f, Mass)) * Time.deltaTime;
		velocity = Vector3.ClampMagnitude(velocity, MirrorManager != null ? MirrorManager.MaxSpeed : 2f);
	}

	Vector3 Noise()
	{
		float n = Mathf.PerlinNoise(Time.time * NoiseFrequency, GetInstanceID()) - 0.5f;
		return new Vector3(n, 0f, -n) * NoiseStrength;
	}

	void Snap(Vector3 position_to_snap)
	{
		transform.position = position_to_snap;
		velocity = Vector3.zero;
	}

	public void Initialize(MirrorManager mirror_manager)
	{
		MirrorManager = mirror_manager;
	}

	public void ResetToSpawn(MirrorSpawnPoint spawn_point)
	{
		CurrentSpawnPoint = spawn_point;
		CurrentSpawnPoint.CurrentMirror = this;

		is_broken = false;

		if (FrameRoot != null)
			FrameRoot.rotation = spawn_point.transform.rotation;
		else
			transform.rotation = spawn_point.transform.rotation;

		transform.position = spawn_point.transform.position;

		velocity = Vector3.zero;
		panel_x_target = 0f;
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

		Vector3 euler = MirrorPivotX.localEulerAngles;
		MirrorPivotX.localRotation = Quaternion.Euler(panel_x_target, euler.y, euler.z);
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
		TryBreak(collision.collider, collision.GetContact(0).point);
	}

	void OnTriggerEnter(Collider other)
	{
		TryBreak(other, other.ClosestPoint(transform.position));
	}

	void TryBreak(Collider other, Vector3 impact_point)
	{
		if (is_broken || !other.CompareTag(PendulumTag))
			return;

		Break(impact_point);
	}

	void Break(Vector3 impact_point)
	{
		is_broken = true;

		if (IntactVisual != null)
			IntactVisual.SetActive(false);

		if (MirrorManager != null)
			MirrorManager.OnMirrorBroken(this, impact_point);

		gameObject.SetActive(false);
	}

	void OnDrawGizmosSelected()
	{
		if (!DebugDraw)
			return;

		Gizmos.color = Color.cyan;
		Gizmos.DrawLine(transform.position, transform.position + velocity);

		if (MirrorPivotX != null)
		{
			Gizmos.color = Color.magenta;
			Gizmos.DrawLine(MirrorPivotX.position, MirrorPivotX.position + MirrorPivotX.right * 0.75f);
		}
	}
}