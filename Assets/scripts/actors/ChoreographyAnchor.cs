using UnityEngine;

public class ChoreographyAnchor : MonoBehaviour
{
	public float GizmoSize = 0.5f;

	void OnDrawGizmos()
	{
		Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.9f);
		Gizmos.DrawSphere(transform.position, GizmoSize * 0.2f);

		Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.6f);
		Gizmos.DrawLine(transform.position + Vector3.left * GizmoSize, transform.position + Vector3.right * GizmoSize);
		Gizmos.DrawLine(transform.position + Vector3.forward * GizmoSize, transform.position + Vector3.back * GizmoSize);
		Gizmos.DrawLine(transform.position + Vector3.up * GizmoSize, transform.position + Vector3.down * GizmoSize);
	}
}