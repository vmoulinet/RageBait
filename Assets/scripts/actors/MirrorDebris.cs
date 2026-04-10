using UnityEngine;

public class MirrorDebris : MonoBehaviour
{
	[Header("Broken Rig")]
	public Transform BrokenMirrorPivotX;

	[Header("Debug")]
	public bool DebugDebris = false;
	public float PendulumIgnoreDuration = 0.2f;
	public float InheritedVelocityMultiplier = 0.65f;
	public float DirectionalImpulseMultiplier = 0.45f;
	public float MaxDirectionalImpulse = 8f;

	[Header("Ground Impact")]
	public LayerMask GroundLayers = ~0;
	public float GroundImpactSpeedThreshold = 1.5f;
	public float GroundImpactCooldown = 0.08f;

	[Header("Debris Loop")]
	public float DebrisLoopSpeedForMax = 6f;
	public float DebrisLoopLerpSpeed = 10f;

	[Header("Sink")]
	public float SinkForceLight = 1f;
	public float SinkForceFast = 20f;
	public float SinkFastBelowY = -1f;
	public float SinkDestroyBelowY = -10f;

	SoundManager sound_manager;

	Rigidbody[] cached_bodies;
	Vector3[] initial_local_positions;
	Quaternion[] initial_local_rotations;
	float last_ground_impact_time = -999f;
	float current_loop_amount = 0f;
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
			}
		}

		snapshot_taken = true;
	}

	public void ResetForReuse()
	{
		CacheAndSnapshot();

		is_sinking = false;
		last_ground_impact_time = -999f;
		current_loop_amount = 0f;
		activate_time = Time.time;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] == null)
				continue;

			cached_bodies[i].transform.localPosition = initial_local_positions[i];
			cached_bodies[i].transform.localRotation = initial_local_rotations[i];
			cached_bodies[i].linearVelocity = Vector3.zero;
			cached_bodies[i].angularVelocity = Vector3.zero;
			cached_bodies[i].isKinematic = false;
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
		{
			UpdateSound();
			return;
		}

		float speed = sink_y_offset < SinkFastBelowY ? SinkForceFast : SinkForceLight;
		sink_y_offset -= speed * Time.deltaTime;

		if (sink_y_offset < SinkDestroyBelowY)
		{
			ReturnToPool();
			return;
		}

		transform.position += Vector3.down * speed * Time.deltaTime;
	}

	void UpdateSound()
	{
		if (sound_manager == null)
			return;

		if (cached_bodies == null || cached_bodies.Length == 0)
			cached_bodies = GetComponentsInChildren<Rigidbody>(true);

		float max_horizontal_speed = 0f;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			Vector3 horizontal_velocity = body.linearVelocity;
			horizontal_velocity.y = 0f;
			float horizontal_speed = horizontal_velocity.magnitude;
			if (horizontal_speed > max_horizontal_speed)
				max_horizontal_speed = horizontal_speed;
		}

		float target_loop_amount = Mathf.Clamp01(max_horizontal_speed / Mathf.Max(0.0001f, DebrisLoopSpeedForMax));
		current_loop_amount = Mathf.MoveTowards(current_loop_amount, target_loop_amount, DebrisLoopLerpSpeed * Time.deltaTime);
		sound_manager.SetDebrisAmount(current_loop_amount);
	}

	void HandleBodyCollision(Collision collision, Rigidbody body)
	{
		if (collision == null || body == null)
			return;

		int other_layer_mask = 1 << collision.gameObject.layer;
		if ((GroundLayers.value & other_layer_mask) == 0)
			return;

		if (Time.time - last_ground_impact_time < GroundImpactCooldown)
			return;

		float impact_speed = collision.relativeVelocity.magnitude;
		if (impact_speed < GroundImpactSpeedThreshold)
			return;

		last_ground_impact_time = Time.time;

		Vector3 impact_point = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
		if (sound_manager != null)
			sound_manager.PlayDebrisImpact(impact_point);
	}

	class MirrorDebrisBodyNotifier : MonoBehaviour
	{
		public MirrorDebris Owner;
		Rigidbody cached_body;

		void Awake()
		{
			cached_body = GetComponent<Rigidbody>();
		}

		void OnCollisionEnter(Collision collision)
		{
			if (Owner == null)
				return;

			if (cached_body == null)
				cached_body = GetComponent<Rigidbody>();

			Owner.HandleBodyCollision(collision, cached_body);
		}
	}

	public void InitializeFromMirror(MirrorActor actor)
	{
		if (actor == null)
			return;

		activate_time = Time.time;

		if (actor.MirrorManager != null)
			sound_manager = actor.MirrorManager.SoundManager;

		cached_bodies = GetComponentsInChildren<Rigidbody>(true);
		transform.position = actor.transform.position;
		transform.rotation = actor.transform.rotation;

		if (BrokenMirrorPivotX != null)
		{
			float wrapped_panel_x = Mathf.DeltaAngle(0f, actor.CurrentPanelXAngle);
			BrokenMirrorPivotX.localRotation = Quaternion.AngleAxis(wrapped_panel_x, Vector3.right);
		}

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			MirrorDebrisBodyNotifier notifier = body.GetComponent<MirrorDebrisBodyNotifier>();
			if (notifier == null)
				notifier = body.gameObject.AddComponent<MirrorDebrisBodyNotifier>();

			notifier.Owner = this;
		}
	}

	public void ApplyImpact(Vector3 impactPoint, float force, float radius, float upwardModifier)
	{
		if (cached_bodies == null || cached_bodies.Length == 0)
			cached_bodies = GetComponentsInChildren<Rigidbody>(true);

		Rigidbody[] bodies = cached_bodies;
		MirrorActor source_actor = null;
		MirrorActor[] actors = FindObjectsByType<MirrorActor>(FindObjectsSortMode.None);

		float nearest_distance = float.MaxValue;
		for (int i = 0; i < actors.Length; i++)
		{
			MirrorActor actor = actors[i];
			if (actor == null)
				continue;

			float distance = Vector3.Distance(actor.transform.position, transform.position);
			if (distance < nearest_distance)
			{
				nearest_distance = distance;
				source_actor = actor;
			}
		}

		Vector3 inherited_velocity = Vector3.zero;
		Vector3 directional_impulse = Vector3.zero;

		if (source_actor != null)
		{
			inherited_velocity = source_actor.Velocity * InheritedVelocityMultiplier;

			if (source_actor.LastBreakImpactDirection.sqrMagnitude > 0.0001f)
			{
				float impulse_strength = Mathf.Min(source_actor.LastBreakImpactSpeed * DirectionalImpulseMultiplier, MaxDirectionalImpulse);
				directional_impulse = source_actor.LastBreakImpactDirection.normalized * impulse_strength;
			}
		}

		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody body = bodies[i];
			body.linearVelocity = inherited_velocity;
			body.AddExplosionForce(force, impactPoint, radius, upwardModifier, ForceMode.Impulse);

			if (directional_impulse.sqrMagnitude > 0.0001f)
				body.AddForce(directional_impulse, ForceMode.Impulse);
		}

		if (DebugDebris)
		{
			Debug.Log(
				name +
				" | debris impact | force=" + force.ToString("F2") +
				" | radius=" + radius.ToString("F2") +
				" | inherited_velocity=" + inherited_velocity.ToString("F2") +
				" | directional_impulse=" + directional_impulse.ToString("F2") +
				" | bodies=" + bodies.Length
			);
		}
	}
}