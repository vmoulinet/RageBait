using UnityEngine;

public class WorldValidation : MonoBehaviour
{
	public enum Phase
	{
		Idle,
		Attract,
		Propel,
		Done
	}

	[Header("References")]
	public ChoreographyManager ChoreographyManager;
	public Transform DebrisRoot;
	public Transform AttractCenter;

	[Header("Timing")]
	public float AttractDuration = 2.0f;
	public float PropelDelay = 0.3f;

	[Header("Attract")]
	public float AttractForce = 12f;
	public float AttractMaxSpeed = 10f;
	public float AttractDamping = 3f;
	public float AttractSpinTorque = 2f;

	[Header("Repulsion")]
	public float RepulsionForce = 8f;
	public float RepulsionRadius = 1.5f;

	[Header("Initial Spin")]
	public float InitialSpinMin = 3f;
	public float InitialSpinMax = 10f;

	[Header("Propel")]
	public Vector3 PropelDirection = Vector3.forward;
	public float PropelForce = 40f;

	[Header("Debug")]
	public bool DebugLog = true;
	public bool EnableKeyboardTrigger = true;
	public KeyCode TriggerKey = KeyCode.T;

	Phase current_phase = Phase.Idle;
	float phase_timer = 0f;
	Rigidbody[] cached_bodies;
	bool[] cached_use_gravity;
	float[] cached_damping;
	int debris_layer = -1;
	bool debris_collision_was_enabled = true;

	public Phase CurrentPhase => current_phase;
	public bool IsActive => current_phase != Phase.Idle && current_phase != Phase.Done;

	void OnEnable()
	{
		if (ChoreographyManager != null)
			ChoreographyManager.TriangleSettled += OnTriangleSettled;
	}

	void OnDisable()
	{
		if (ChoreographyManager != null)
			ChoreographyManager.TriangleSettled -= OnTriangleSettled;
	}

	void OnTriangleSettled()
	{
		Trigger();
	}

	public void Trigger()
	{
		if (IsActive)
			return;

		CacheBodies();

		if (cached_bodies == null || cached_bodies.Length == 0)
		{
			if (DebugLog)
				Debug.Log("[world_validation] trigger skipped | no debris found");
			return;
		}

		SaveState();
		SetGravity(false);
		DisableDebrisCollisions();
		ApplyInitialSpin();

		current_phase = Phase.Attract;
		phase_timer = 0f;

		if (DebugLog)
			Debug.Log("[world_validation] trigger | bodies=" + cached_bodies.Length);
	}

	void Update()
	{
		if (EnableKeyboardTrigger && Input.GetKeyDown(TriggerKey))
			Trigger();
	}

	void FixedUpdate()
	{
		if (current_phase == Phase.Idle || current_phase == Phase.Done)
			return;

		phase_timer += Time.fixedDeltaTime;

		switch (current_phase)
		{
			case Phase.Attract:
				ApplyAttract();
				if (phase_timer >= AttractDuration)
					EnterPhase(Phase.Propel);
				break;

			case Phase.Propel:
				if (phase_timer >= PropelDelay)
				{
					ApplyPropel();
					EnterPhase(Phase.Done);
				}
				break;
		}
	}

	void EnterPhase(Phase next)
	{
		if (DebugLog)
			Debug.Log("[world_validation] phase " + current_phase + " -> " + next);

		current_phase = next;
		phase_timer = 0f;

		if (next == Phase.Done)
		{
			RestoreState();
			RestoreDebrisCollisions();
		}
	}

	void ApplyInitialSpin()
	{
		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			float spin_strength = Random.Range(InitialSpinMin, InitialSpinMax);
			body.angularVelocity = Random.onUnitSphere * spin_strength;
		}
	}

	void ApplyAttract()
	{
		Vector3 center = AttractCenter != null ? AttractCenter.position : transform.position;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			body.linearDamping = AttractDamping;

			Vector3 to_center = center - body.worldCenterOfMass;
			float distance = to_center.magnitude;

			if (distance > 0.01f)
			{
				body.AddForce(to_center.normalized * AttractForce, ForceMode.Acceleration);

				if (body.linearVelocity.magnitude > AttractMaxSpeed)
					body.linearVelocity = body.linearVelocity.normalized * AttractMaxSpeed;
			}

			body.AddTorque(Random.insideUnitSphere * AttractSpinTorque, ForceMode.Acceleration);
		}

		ApplyRepulsion();
	}

	void ApplyRepulsion()
	{
		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody a = cached_bodies[i];
			if (a == null)
				continue;

			for (int j = i + 1; j < cached_bodies.Length; j++)
			{
				Rigidbody b = cached_bodies[j];
				if (b == null)
					continue;

				Vector3 delta = a.worldCenterOfMass - b.worldCenterOfMass;
				float distance = delta.magnitude;

				if (distance < 0.001f || distance > RepulsionRadius)
					continue;

				float strength = RepulsionForce * (1f - distance / RepulsionRadius);
				Vector3 push = delta.normalized * strength;

				a.AddForce(push, ForceMode.Acceleration);
				b.AddForce(-push, ForceMode.Acceleration);
			}
		}
	}

	void ApplyPropel()
	{
		Vector3 direction = PropelDirection.normalized;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			Rigidbody body = cached_bodies[i];
			if (body == null)
				continue;

			body.linearDamping = 0f;
			body.AddForce(direction * PropelForce, ForceMode.VelocityChange);
		}

		if (DebugLog)
			Debug.Log("[world_validation] propel | direction=" + direction.ToString("F2") + " | force=" + PropelForce);
	}

	void CacheBodies()
	{
		if (DebrisRoot == null)
		{
			cached_bodies = null;
			return;
		}

		cached_bodies = DebrisRoot.GetComponentsInChildren<Rigidbody>(true);
	}

	void SaveState()
	{
		cached_use_gravity = new bool[cached_bodies.Length];
		cached_damping = new float[cached_bodies.Length];

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] != null)
			{
				cached_use_gravity[i] = cached_bodies[i].useGravity;
				cached_damping[i] = cached_bodies[i].linearDamping;
			}
		}
	}

	void RestoreState()
	{
		if (cached_bodies == null)
			return;

		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] == null)
				continue;

			if (cached_use_gravity != null && i < cached_use_gravity.Length)
				cached_bodies[i].useGravity = cached_use_gravity[i];

			if (cached_damping != null && i < cached_damping.Length)
				cached_bodies[i].linearDamping = cached_damping[i];
		}

		cached_bodies = null;
		cached_use_gravity = null;
		cached_damping = null;
	}

	void SetGravity(bool enabled)
	{
		for (int i = 0; i < cached_bodies.Length; i++)
		{
			if (cached_bodies[i] != null)
				cached_bodies[i].useGravity = enabled;
		}
	}

	void DisableDebrisCollisions()
	{
		debris_layer = LayerMask.NameToLayer("Debris");
		if (debris_layer < 0)
		{
			if (DebugLog)
				Debug.LogWarning("[world_validation] layer 'Debris' not found, skipping collision disable");
			return;
		}

		debris_collision_was_enabled = !Physics.GetIgnoreLayerCollision(debris_layer, debris_layer);
		Physics.IgnoreLayerCollision(debris_layer, debris_layer, true);

		if (DebugLog)
			Debug.Log("[world_validation] debris-debris collisions disabled");
	}

	void RestoreDebrisCollisions()
	{
		if (debris_layer < 0)
			return;

		Physics.IgnoreLayerCollision(debris_layer, debris_layer, !debris_collision_was_enabled);

		if (DebugLog)
			Debug.Log("[world_validation] debris-debris collisions restored");
	}
}
