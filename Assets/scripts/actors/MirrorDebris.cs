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

	public void InitializeFromMirror(MirrorActor actor)
	{
		if (actor == null)
			return;

		transform.position = actor.transform.position;
		transform.rotation = actor.transform.rotation;

		if (BrokenMirrorPivotX != null)
		{
			float wrapped_panel_x = Mathf.DeltaAngle(0f, actor.CurrentPanelXAngle);
			BrokenMirrorPivotX.localRotation = Quaternion.AngleAxis(wrapped_panel_x, Vector3.right);
		}
	}

	public void ApplyImpact(Vector3 impactPoint, float force, float radius, float upwardModifier)
	{
		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
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