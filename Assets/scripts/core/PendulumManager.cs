using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class PendulumManager : MonoBehaviour
{
	[Header("References")]
	public Transform root;

	[Header("Pendulum Physics")]
	[Tooltip("maximum angle in degrees")]
	public float amplitude = 12f;

	[Tooltip("cable length in meters")]
	public float cable_length = 48.5f;

	[Header("Cable Visual")]
	public bool enable_cable = true;
	public Material cable_material;
	public Color cable_color = Color.white;
	public float cable_width = 0.02f;

	const float gravity = 9.81f;

	LineRenderer line_renderer;
	Rigidbody body;

	float phase;
	float omega;
	Vector3 last_world_position;
	Vector3 current_world_velocity = Vector3.zero;

	public Vector3 CurrentWorldVelocity
	{
		get
		{
			return current_world_velocity;
		}
	}

	void Awake()
	{
		line_renderer = GetComponent<LineRenderer>();
		body = GetComponent<Rigidbody>();

		body.useGravity = false;
		body.isKinematic = true;

		line_renderer.useWorldSpace = true;
		line_renderer.positionCount = 2;
		ApplyCableVisuals();

		omega = Mathf.Sqrt(gravity / Mathf.Max(0.01f, cable_length));
		last_world_position = transform.position;
	}

	void Update()
	{
		phase += omega * Time.deltaTime;

		float angle = Mathf.Sin(phase) * Mathf.Deg2Rad * amplitude;
		float x = Mathf.Sin(angle) * cable_length;
		float y = -Mathf.Cos(angle) * cable_length;

		transform.localPosition = new Vector3(x, y, 0f);

		Vector3 world_delta = transform.position - last_world_position;
		current_world_velocity = world_delta / Mathf.Max(Time.deltaTime, 0.0001f);
		last_world_position = transform.position;
	}

	void LateUpdate()
	{
		if (!Application.isPlaying)
		{
			line_renderer.enabled = false;
			return;
		}

		line_renderer.enabled = enable_cable;
		if (!enable_cable)
			return;

		if (root == null)
			return;

		line_renderer.SetPosition(0, root.position);
		line_renderer.SetPosition(1, transform.position);
	}

	public Vector3 GetImpactDirection()
	{
		Vector3 flat_velocity = current_world_velocity;
		flat_velocity.y = 0f;

		if (flat_velocity.sqrMagnitude > 0.0001f)
			return flat_velocity.normalized;

		return Vector3.right;
	}

	void ApplyCableVisuals()
	{
		if (cable_material != null)
			line_renderer.material = cable_material;

		line_renderer.startColor = cable_color;
		line_renderer.endColor = cable_color;
		line_renderer.startWidth = cable_width;
		line_renderer.endWidth = cable_width;
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		if (line_renderer == null)
			line_renderer = GetComponent<LineRenderer>();

		ApplyCableVisuals();
		omega = Mathf.Sqrt(gravity / Mathf.Max(0.01f, cable_length));
	}
#endif
}