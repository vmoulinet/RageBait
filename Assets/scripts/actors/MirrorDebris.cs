using UnityEngine;

public class MirrorDebris : MonoBehaviour
{
	public void ApplyImpact(Vector3 impactPoint, float force, float radius, float upwardModifier)
	{
		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>();

		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody body = bodies[i];
			body.AddExplosionForce(force, impactPoint, radius, upwardModifier, ForceMode.Impulse);
		}
	}
}