using UnityEngine;

public class MirrorDebris : MonoBehaviour
{
	[Header("Broken Rig")]
	public Transform BrokenMirrorPivotX;

	[Header("Debug")]
	public bool DebugDebris = false;
	public float PendulumIgnoreDuration = 0.2f;

	[Header("Impact")]
	public float ImpactForce = 6f;
	public float ImpactForceRandom = 1f;
	public float ImpactUpwardForce = 2f;
	public float ImpactUpwardForceRandom = 1f;
	public float ImpactTorque = 4f;
	public float ImpactTorqueRandom = 1f;

	[Header("Sink")]
	public float SinkForceLight = 1f;
	public float SinkForceFast = 20f;
	public float SinkFastBelowY = -1f;
	public float SinkDestroyBelowY = -10f;

	SoundManager sound_manager;
	MirrorActor source_actor;
	Vector3 source_impact_direction = Vector3.zero;

	Rigidbody[] cached_bodies;
	Vector3[] initial_local_positions;
	Quaternion[] initial_local_rotations;
	bool is_sinking = false;
	bool snapshot_taken = false;
	float activate_time = 0f;

	public bool IsSinking => is_sinking;
	public float ActivateTime => activate_time;

	void Awake()
	{
		CacheAndSnapshot();
	}

	void CacheAndSnapshot()
	{
		if (snapshot_taken)
			return;

		cached_bodies = GetComponentsInChildren<Rigidbody>(true);
		initial_local_positions = new Vector3[cached_bodies.Length];
		initial_local_rotations = new Quaternion[cached_bodies.Length];

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] != null)
			{
				initial_local_positions[i] = cached_bodies[i].transform.localPosition;
				initial_local_rotations[i] = cached_bodies[i].transform.localRotation;
				// Pas d'interpolation : ces Rigidbody vivent sous un parent au scale != 1
				// (BrokenFrameRoot ~0.35), ou l'interpolation est instable et teleporte les
				// fragments. La fluidite pendant l'event ralenti est assuree autrement :
				// VideoManager scale Time.fixedDeltaTime avec le timeScale (cf. Apply_time_scale),
				// donc la physique reste fluide sans interpolation.
				cached_bodies[i].interpolation = RigidbodyInterpolation.None;
			}
		}

		snapshot_taken = true;
	}

	public void ResetForReuse()
	{
		CacheAndSnapshot();

		is_sinking = false;
		activate_time = Time.time;
		source_actor = null;
		source_impact_direction = Vector3.zero;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] == null)
				continue;

			cached_bodies[i].transform.localPosition = initial_local_positions[i];
			cached_bodies[i].transform.localRotation = initial_local_rotations[i];
			cached_bodies[i].isKinematic = false;
			cached_bodies[i].linearVelocity = Vector3.zero;
			cached_bodies[i].angularVelocity = Vector3.zero;
			cached_bodies[i].useGravity = true;
			cached_bodies[i].detectCollisions = true;
		}

		gameObject.SetActive(true);
	}

	public void ReturnToPool()
	{
		gameObject.SetActive(false);
	}

	float sink_y_offset = 0f;

	public void StartSinking()
	{
		is_sinking = true;
		sink_y_offset = 0f;

		if (cached_bodies == null || cached_bodies.Length == 0)
			cached_bodies = GetComponentsInChildren<Rigidbody>(true);

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] != null)
			{
				cached_bodies[i].isKinematic = true;
				cached_bodies[i].detectCollisions = false;
			}
		}
	}

	void Update()
	{
		if (!is_sinking)
			return;

		float speed = sink_y_offset < SinkFastBelowY ? SinkForceFast : SinkForceLight;
		sink_y_offset -= speed * Time.deltaTime;

		if (sink_y_offset < SinkDestroyBelowY)
		{
			ReturnToPool();
			return;
		}

		transform.position += Vector3.down * speed * Time.deltaTime;
	}

	public void InitializeFromMirror(MirrorActor actor)
	{
		if (actor == null)
			return;

		activate_time = Time.time;
		source_actor = actor;
		source_impact_direction = actor.LastBreakImpactDirection;

		if (actor.MirrorManager != null)
			sound_manager = actor.MirrorManager.SoundManager;

		if (sound_manager != null)
			sound_manager.PlayMirrorBreak(actor.transform.position);

		cached_bodies = GetComponentsInChildren<Rigidbody>(true);
		transform.position = actor.transform.position;
		transform.rotation = actor.transform.rotation;

		if (BrokenMirrorPivotX != null)
		{
			float wrapped_panel_x = Mathf.DeltaAngle(0f, actor.CurrentPanelXAngle);
			BrokenMirrorPivotX.localRotation = Quaternion.AngleAxis(wrapped_panel_x, Vector3.right);
		}
	}

	public void ApplyImpact()
	{
		if (cached_bodies == null || cached_bodies.Length == 0)
			cached_bodies = GetComponentsInChildren<Rigidbody>(true);

		Vector3 horizontal_dir = source_impact_direction;
		horizontal_dir.y = 0f;

		if (horizontal_dir.sqrMagnitude > 0.0001f)
			horizontal_dir = horizontal_dir.normalized;
		else
			horizontal_dir = Vector3.zero;

		// Bris "explosion en l'air" (MirrorMeltdown) : poussee verticale supplementaire.
		float explode_upward = source_actor != null ? source_actor.ExplodeUpwardBoost : 0f;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			float force = ImpactForce + Random.Range(-ImpactForceRandom, ImpactForceRandom);
			float upward = ImpactUpwardForce + explode_upward + Random.Range(-ImpactUpwardForceRandom, ImpactUpwardForceRandom);
			float torque = ImpactTorque + Random.Range(-ImpactTorqueRandom, ImpactTorqueRandom);

			Vector3 impulse = horizontal_dir * force + Vector3.up * upward;

			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.AddForce(impulse, ForceMode.VelocityChange);

			if (torque > 0f)
				body.AddTorque(Random.insideUnitSphere * torque, ForceMode.VelocityChange);
		}

		if (DebugDebris)
		{
			Debug.Log(
				name +
				" | debris impact | source_actor=" + (source_actor != null ? source_actor.name : "null") +
				" | direction=" + horizontal_dir.ToString("F2") +
				" | base_force=" + ImpactForce.ToString("F2") +
				" | bodies=" + cached_bodies.Length
			);
		}
	}
}
