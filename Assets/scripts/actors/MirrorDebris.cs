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

	[Header("Interpolation")]
	[Tooltip("Delai (s) apres le bris avant d'activer l'interpolation des fragments. " +
		"Pendant ce delai les fragments restent en None pour eviter la teleportation due au scale parent ; " +
		"ensuite on passe en Interpolate pour le lissage pendant l'event ralenti.")]
	public float EnableInterpolationDelay = 0.5f;

	SoundManager sound_manager;
	MirrorActor source_actor;
	Vector3 source_impact_direction = Vector3.zero;

	Rigidbody[] cached_bodies;
	Vector3[] initial_local_positions;
	Quaternion[] initial_local_rotations;
	bool is_sinking = false;
	bool snapshot_taken = false;
	float activate_time = 0f;
	bool interpolation_enabled = false;
	float interpolation_enable_at = -1f;

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
				// Pas d'interpolation au depart : ces Rigidbody vivent sous un parent au scale != 1,
				// et l'interpolation/extrapolation y est instable pendant les premieres frames apres
				// le bris (conversion monde<->local degeneree -> les fragments se teleportent).
				// On l'active seulement une fois le debris stabilise (cf. EnableInterpolationDelay),
				// pour retrouver le lissage anti-saccade pendant l'event VideoManager ralenti.
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
		interpolation_enabled = false;
		interpolation_enable_at = -1f;

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
			cached_bodies[i].interpolation = RigidbodyInterpolation.None;
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
		UpdateInterpolationActivation();

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

	// Passe les fragments en Interpolate une fois le debris stabilise (apres EnableInterpolationDelay).
	// Avant ce delai ils restent en None, sinon le scale du parent rend l'interpolation instable
	// et teleporte les fragments dans les premieres frames apres le bris.
	void UpdateInterpolationActivation()
	{
		if (interpolation_enabled || interpolation_enable_at < 0f)
			return;

		if (Time.time < interpolation_enable_at)
			return;

		SetFragmentsInterpolation(RigidbodyInterpolation.Interpolate);
		interpolation_enabled = true;
	}

	void SetFragmentsInterpolation(RigidbodyInterpolation mode)
	{
		if (cached_bodies == null)
			return;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] != null)
				cached_bodies[i].interpolation = mode;
		}
	}

	void ArmInterpolationActivation()
	{
		SetFragmentsInterpolation(RigidbodyInterpolation.None);
		interpolation_enabled = false;
		interpolation_enable_at = Time.time + Mathf.Max(0f, EnableInterpolationDelay);
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

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			float force = ImpactForce + Random.Range(-ImpactForceRandom, ImpactForceRandom);
			float upward = ImpactUpwardForce + Random.Range(-ImpactUpwardForceRandom, ImpactUpwardForceRandom);
			float torque = ImpactTorque + Random.Range(-ImpactTorqueRandom, ImpactTorqueRandom);

			Vector3 impulse = horizontal_dir * force + Vector3.up * upward;

			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.AddForce(impulse, ForceMode.VelocityChange);

			if (torque > 0f)
				body.AddTorque(Random.insideUnitSphere * torque, ForceMode.VelocityChange);
		}

		// Bris : on (re)part en None, et on programme l'activation de l'interpolation
		// une fois les fragments stabilises.
		ArmInterpolationActivation();

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
