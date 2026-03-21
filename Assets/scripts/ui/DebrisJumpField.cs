using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DebrisJumpField : MonoBehaviour
{
	[Header("Filtering")]
	public string DebrisTag = "Debris";
	public bool IgnoreMirrorActors = true;

	[Header("Jump")]
	public float UpwardImpulse = 2.5f;
	public float SidewaysJitter = 0.35f;
	public ForceMode ForceMode = ForceMode.Impulse;

	[Header("Timing")]
	public bool ApplyContinuously = true;
	public float ReapplyInterval = 0.35f;

	readonly Dictionary<int, float> next_apply_times = new Dictionary<int, float>();

	void Reset()
	{
		Collider col = GetComponent<Collider>();
		if (col != null)
			col.isTrigger = true;
	}

	void OnTriggerEnter(Collider other)
	{
		Try_apply(other);
	}

	void OnTriggerStay(Collider other)
	{
		if (!ApplyContinuously)
			return;

		Try_apply(other);
	}

	void Try_apply(Collider other)
	{
		if (other == null)
			return;

		if (IgnoreMirrorActors && other.GetComponentInParent<MirrorActor>() != null)
			return;

		bool is_debris = other.CompareTag(DebrisTag);
		if (!is_debris)
			return;

		Rigidbody rb = other.attachedRigidbody;
		if (rb == null)
			rb = other.GetComponentInParent<Rigidbody>();

		if (rb == null)
			return;

		int id = rb.GetInstanceID();
		float now = Time.time;

		float next_apply_time;
		if (next_apply_times.TryGetValue(id, out next_apply_time) && now < next_apply_time)
			return;

		next_apply_times[id] = now + Mathf.Max(0.01f, ReapplyInterval);

		Vector3 impulse = Vector3.up * UpwardImpulse;
		impulse.x += Random.Range(-SidewaysJitter, SidewaysJitter);
		impulse.z += Random.Range(-SidewaysJitter, SidewaysJitter);

		rb.AddForce(impulse, ForceMode);
	}
}