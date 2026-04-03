using UnityEngine;

public class ChoreographyAnchor : MonoBehaviour
{
	public float GizmoSize = 0.5f;
	public float AnchorOuterLimit = 8f;

	void OnDrawGizmos()
	{
		Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.9f);
		Gizmos.DrawSphere(transform.position, GizmoSize * 0.2f);

		Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.6f);
		Gizmos.DrawLine(transform.position + Vector3.left * GizmoSize, transform.position + Vector3.right * GizmoSize);
		Gizmos.DrawLine(transform.position + Vector3.forward * GizmoSize, transform.position + Vector3.back * GizmoSize);
		Gizmos.DrawLine(transform.position + Vector3.up * GizmoSize, transform.position + Vector3.down * GizmoSize);

	}

	void DrawCircle(Vector3 center, float radius, int segments)
	{
		float step = Mathf.PI * 2f / segments;
		for (int i = 0; i < segments; i++)
		{
			float a = step * i;
			float b = step * (i + 1);
			Vector3 p1 = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
			Vector3 p2 = center + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius;
			Gizmos.DrawLine(p1, p2);
		}
	}
}