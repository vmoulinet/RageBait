using UnityEngine;

public class MirrorSpawnPoint : MonoBehaviour
{
	[HideInInspector] public MirrorActor CurrentMirror;

	public bool IsOccupied
	{
		get
		{
			return CurrentMirror != null;
		}
	}

	void OnDrawGizmos()
	{
		Gizmos.color = IsOccupied ? Color.red : Color.green;
		Gizmos.DrawWireSphere(transform.position, 0.2f);
		Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
	}
}