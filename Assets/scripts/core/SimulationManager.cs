using UnityEngine;

public class SimulationManager : MonoBehaviour
{
	[Header("Managers")]
	public MirrorManager MirrorManager;
	public ChoreographyManager ChoreographyManager;
	public WordManager WordManager;
	public IOManager IOManager;
	public EventManager EventManager;
	public VideoManager VideoManager;


	void Awake()
	{
		if (MirrorManager == null)
			MirrorManager = GetComponent<MirrorManager>();

		if (ChoreographyManager == null)
			ChoreographyManager = GetComponent<ChoreographyManager>();

		if (WordManager == null)
			WordManager = GetComponent<WordManager>();

		if (IOManager == null)
			IOManager = GetComponent<IOManager>();

		if (EventManager == null)
			EventManager = GetComponent<EventManager>();

		if (VideoManager == null)
			VideoManager = GetComponent<VideoManager>();
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

		if (WordManager == null)
		{
			Debug.LogError("SimulationManager: WordManager reference is missing.");
			return;
		}

		if (VideoManager == null)
		{
			Debug.LogError("SimulationManager: VideoManager reference is missing.");
			return;
		}

		if (EventManager == null)
		{
			Debug.LogError("SimulationManager: EventManager reference is missing.");
			return;
		}

		if (IOManager == null)
			Debug.LogWarning("SimulationManager: IOManager reference is missing. OSC input will be disabled.");

		MirrorManager.Initialize(this);
		ChoreographyManager.Initialize(this);
		WordManager.Initialize(this);
		EventManager.Initialize(this);
		VideoManager.Initialize(this);

		if (IOManager != null)
			IOManager.Initialize(this);

		MirrorManager.BootstrapMirrors();
		ChoreographyManager.RefreshTargets();
	}
}