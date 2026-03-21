using UnityEngine;

public class EventManager : MonoBehaviour
{
	[Header("References")]
	public SimulationManager SimulationManager;
	public VideoManager VideoManager;

	[Header("Debug")]
	public bool EnableKeyboardDebugStomp = true;
	public KeyCode DebugStompKey = KeyCode.E;
	public bool DebugLogEvents = true;

	[Header("Routing")]
	public bool StompTriggersVideo = true;

	int stomp_count = 0;
	float last_stomp_time = -999f;
	float last_stomp_force = 0f;

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
		if (!EnableKeyboardDebugStomp)
			return;

		if (Input.GetKeyDown(DebugStompKey))
			NotifyStomp(1f, "debug_key");
	}

	public void NotifyStomp(float stomp_force, string source = "io_manager")
	{
		stomp_count++;
		last_stomp_time = Time.unscaledTime;
		last_stomp_force = stomp_force;

		if (DebugLogEvents)
		{
			Debug.Log(
				"[event_manager] stomp | count=" + stomp_count +
				" | source=" + source +
				" | force=" + stomp_force.ToString("F3")
			);
		}

		RouteDefaultStompResponse(stomp_force, source);
	}

	void RouteDefaultStompResponse(float stomp_force, string source)
	{
		if (!StompTriggersVideo)
			return;

		if (VideoManager == null)
		{
			Debug.LogWarning(
				"[event_manager] stomp received but VideoManager is missing | source=" + source
			);
			return;
		}

		VideoManager.Play_video_event();
	}
}