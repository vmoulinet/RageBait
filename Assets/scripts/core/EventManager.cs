using System.Collections;
using UnityEngine;

public class EventManager : MonoBehaviour
{
	[Header("References")]
	public SimulationManager SimulationManager;
	public VideoManager VideoManager;
	public DaddyLetterProjector DaddyLetterProjector;
	public SoundManager SoundManager;
	public WorldValidation WorldValidation;
	public StompWordStrobe StompWordStrobe;

	[Header("Debug")]
	public bool EnableKeyboardDebugStomp = true;
	public KeyCode DebugStompKey = KeyCode.E;
	public bool DebugLogEvents = true;

	[Header("Routing")]
	public bool StompTriggersVideo = true;
	public float TypingResumeTimeout = 30f;
	[Tooltip("Trigger WorldValidation this many seconds after a stomp starts the video.")]
	public bool StompTriggersWorldValidation = true;
	public float WorldValidationDelay = 1f;

	[Header("Stomp Gate")]
	[Tooltip("Ignore new stomps for a while after a stomp.")]
	public bool BlockStompUntilTimeout = true;
	[Tooltip("How long new stomps are ignored after a stomp, in seconds.")]
	public float StompGateTimeout = 18f;

	int stomp_count = 0;
	float last_stomp_time = -999f;
	float last_stomp_force = 0f;

	bool stomp_gate_closed = false;
	float stomp_gate_open_time = 0f;


	public int StompCount
	{
		get
		{
			return stomp_count;
		}
	}

	public float LastStompTime
	{
		get
		{
			return last_stomp_time;
		}
	}

	public float LastStompForce
	{
		get
		{
			return last_stomp_force;
		}
	}

	public void Initialize(SimulationManager sim)
	{
		SimulationManager = sim;

		if (VideoManager == null && sim != null)
			VideoManager = sim.VideoManager;

		if (SoundManager == null && sim != null)
			SoundManager = sim.SoundManager;

		if (DebugLogEvents)
		{
			Debug.Log(
				"[event_manager] initialize | video_manager=" +
				(VideoManager != null ? VideoManager.name : "null")
			);
		}
	}

	void Update()
	{
		UpdateStompGate();

		if (!EnableKeyboardDebugStomp)
			return;

		if (Input.GetKeyDown(DebugStompKey))
			NotifyStomp(1f, "debug_key");
	}

	void UpdateStompGate()
	{
		if (!stomp_gate_closed)
			return;

		if (Time.unscaledTime - stomp_gate_open_time >= StompGateTimeout)
		{
			if (DebugLogEvents)
				Debug.Log("[event_manager] stomp gate released | reason=timeout");
			stomp_gate_closed = false;
		}
	}

	public void NotifyStomp(float stomp_force, string source = "io_manager")
	{
		// A new stomp is ignored until the gate timeout has elapsed.
		if (BlockStompUntilTimeout && stomp_gate_closed)
		{
			if (DebugLogEvents)
				Debug.Log("[event_manager] stomp ignored | gate closed (timeout not elapsed) | source=" + source);
			return;
		}

		stomp_count++;
		last_stomp_time = Time.unscaledTime;
		last_stomp_force = stomp_force;

		if (BlockStompUntilTimeout)
		{
			stomp_gate_closed = true;
			stomp_gate_open_time = Time.unscaledTime;
		}

		if (DebugLogEvents)
		{
			Debug.Log(
				"[event_manager] stomp | count=" + stomp_count +
				" | source=" + source +
				" | force=" + stomp_force.ToString("F3")
			);
		}

		// Un stomp = le jeu se passe activement : on repousse le meltdown.
		if (SimulationManager != null && SimulationManager.MirrorManager != null)
			SimulationManager.MirrorManager.ResetMeltdownTimer();

		if (DaddyLetterProjector != null)
			DaddyLetterProjector.NotifyStomp();

		if (StompWordStrobe != null)
			StompWordStrobe.Trigger();

		if (SoundManager != null)
			SoundManager.PlayStomp();

		RouteDefaultStompResponse(stomp_force, source);
	}

	void RouteDefaultStompResponse(float stomp_force, string source)
	{
		if (SimulationManager != null && SimulationManager.MirrorManager != null)
			SimulationManager.MirrorManager.BreakAllMirrors();

		if (!StompTriggersVideo)
			return;

		if (VideoManager == null)
		{
			Debug.LogWarning(
				"[event_manager] stomp received but VideoManager is missing | source=" + source
			);
			return;
		}

		// Pause the typing loop for the duration of the video event, then let
		// it resume on its own once the event is over.
		if (SoundManager != null && isActiveAndEnabled)
		{
			SoundManager.SuspendTypingLoopForEvent();
			StartCoroutine(ResumeTypingWhenVideoEnds());
		}

		VideoManager.Play_video_event();

		Debug.Log("[event_manager] WV gate | enabled=" + StompTriggersWorldValidation +
			" ref=" + (WorldValidation != null) + " activeEnabled=" + isActiveAndEnabled);

		if (StompTriggersWorldValidation && WorldValidation != null && isActiveAndEnabled)
			StartCoroutine(TriggerWorldValidationAfterDelay());
	}

	IEnumerator TriggerWorldValidationAfterDelay()
	{
		// Real-time wait so it tracks the video start, not Time.timeScale.
		yield return new WaitForSecondsRealtime(WorldValidationDelay);

		Debug.Log("[event_manager] WV delay elapsed -> Trigger() | IsActive=" + WorldValidation.IsActive);

		// Trigger() self-guards via IsActive, so a TriangleSettled that already
		// fired the event makes this a no-op.
		WorldValidation.Trigger();
	}

	IEnumerator ResumeTypingWhenVideoEnds()
	{
		float elapsed = 0f;

		// Give the video routine a moment to flag itself as playing.
		while (!VideoManager.IsPlayingEvent && elapsed < 1f)
		{
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		elapsed = 0f;
		while (VideoManager.IsPlayingEvent && elapsed < TypingResumeTimeout)
		{
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		if (SoundManager != null)
			SoundManager.ResumeTypingLoopAfterEvent();
	}
}