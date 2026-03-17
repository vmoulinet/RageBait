using UnityEngine;

public class SimulationManager : MonoBehaviour
{
	public MirrorManager MirrorManager;
	public ChoreographyManager ChoreographyManager;

	void Awake()
	{
		if (MirrorManager == null)
			MirrorManager = GetComponent<MirrorManager>();

		if (ChoreographyManager == null)
			ChoreographyManager = GetComponent<ChoreographyManager>();
	}

	void Start()
	{
		if (MirrorManager == null)
		{
			Debug.LogError("SimulationManager: MirrorManager reference is missing.");
			return;
		}

		if (ChoreographyManager == null)
		{
			Debug.LogError("SimulationManager: ChoreographyManager reference is missing.");
			return;
		}

		MirrorManager.Initialize(this);
		ChoreographyManager.Initialize(this);

		MirrorManager.BootstrapMirrors();
		ChoreographyManager.RefreshTargets();
	}
}