using UnityEngine;

public class CameraSineShake : MonoBehaviour
{
	public Transform CamTransform;

	Vector3 original_local_position = Vector3.zero;
	float shake_duration = 0f;
	float shake_amplitude = 0f;
	float shake_frequency = 0f;
	float shake_decay = 1f;

	void Awake()
	{
		if (CamTransform == null)
			CamTransform = transform;
	}

	void OnEnable()
	{
		if (CamTransform != null)
			original_local_position = CamTransform.localPosition;
	}

	void LateUpdate()
	{
		if (CamTransform == null)
			return;

		if (shake_duration <= 0f)
		{
			original_local_position = CamTransform.localPosition;
			return;
		}

		float t = Time.unscaledTime * shake_frequency;

		float x =
			Mathf.Sin(t * 1.31f + 0.17f) +
			0.55f * Mathf.Sin(t * 2.17f + 1.73f) +
			0.25f * Mathf.Sin(t * 3.91f + 0.61f);

		float y =
			Mathf.Sin(t * 1.73f + 2.11f) +
			0.45f * Mathf.Sin(t * 2.83f + 0.93f) +
			0.20f * Mathf.Sin(t * 4.37f + 1.41f);

		float z =
			Mathf.Sin(t * 1.19f + 0.49f) +
			0.35f * Mathf.Sin(t * 2.41f + 1.27f);

		Vector3 offset = new Vector3(x, y, z) * shake_amplitude;
		CamTransform.localPosition = original_local_position + offset;

		shake_duration -= Time.unscaledDeltaTime * shake_decay;
		if (shake_duration <= 0f)
		{
			shake_duration = 0f;
			CamTransform.localPosition = original_local_position;
		}
	}

	public void Trigger_shake(float duration, float amplitude, float frequency, float decay = 1f)
	{
		if (CamTransform == null)
			CamTransform = transform;

		original_local_position = CamTransform.localPosition;
		shake_duration = Mathf.Max(shake_duration, duration);
		shake_amplitude = Mathf.Max(shake_amplitude, amplitude);
		shake_frequency = Mathf.Max(0.01f, frequency);
		shake_decay = Mathf.Max(0.01f, decay);
	}
}