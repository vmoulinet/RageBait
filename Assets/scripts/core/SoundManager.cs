using UnityEngine;

public class SoundManager : MonoBehaviour
{
	[Header("Mirror Shatter")]
	public AudioClip[] MirrorBreakClips;
	public float MirrorBreakVolume = 1f;
	public float MirrorBreakPitchRandom = 0.06f;
	public float MirrorBreakSpatialBlend = 1f;
	public Transform RuntimeRoot;

	[Header("Mirror Shatter Reverb")]
	public bool MirrorBreakReverbEnabled = false;
	public AudioReverbPreset MirrorBreakReverbPreset = AudioReverbPreset.Hallway;
	[Tooltip("Only applied when MirrorBreakReverbPreset is set to User.")]
	public float MirrorBreakReverbDryLevel = 0f;
	public float MirrorBreakReverbRoom = 0f;
	public float MirrorBreakReverbDecayTime = 1f;
	public float MirrorBreakReverbLevel = 0f;

	[Header("Stomp")]
	public AudioClip[] StompClips;
	[Range(0f, 1f)]
	public float StompVolume = 1f;
	public float StompPitchRandom = 0.04f;
	public float StompSpatialBlend = 0f;
	[Header("Pendulum Loop")]
	public AudioSource[] PendulumLoopSources = new AudioSource[3];
	public AudioClip[] PendulumLoopClips = new AudioClip[3];
	public Transform PendulumEmitter;
	[Range(0f, 1f)]
	public float PendulumLoopVolume = 1f;

	[Header("Pendulum Pan")]
	public Transform PendulumPanTarget;
	public float PendulumPanLeftX = -19.7f;
	public float PendulumPanRightX = 19.7f;
	public float PendulumPanMax = 0.85f;
	public float PendulumVolumeFade = 1f;

	[Header("Typing Loop")]
	public AudioSource TypingLoopSource;
	public AudioClip TypingLoopClip;
	public float TypingLoopVolume = 1f;

	[Header("Typing Loop Reverb")]
	public bool TypingReverbEnabled = false;
	public AudioReverbPreset TypingReverbPreset = AudioReverbPreset.Hallway;
	[Tooltip("Only applied when TypingReverbPreset is set to User.")]
	public float TypingReverbDryLevel = 0f;
	public float TypingReverbRoom = 0f;
	public float TypingReverbDecayTime = 1f;
	public float TypingReverbLevel = 0f;
	AudioReverbFilter typing_reverb_filter;

	[Header("Daddy Loves You Loop")]
	public AudioSource DaddyLoopSource;
	public AudioClip DaddyLoopClip;
	public float DaddyLoopVolume = 1f;

	[Header("Debug")]
	public bool DebugSound = false;

	// Live mix multipliers driven by the debug-menu sliders. The preset volumes
	// above stay untouched; each slider scales its source: 1 = preset as-is,
	// 2 = twice as loud, 0 = silent. Effective volume = preset * multiplier.
	// Persisted to PlayerPrefs so a build remembers them between launches.
	[HideInInspector] public float MirrorBreakVolumeMult = 1f;
	[HideInInspector] public float StompVolumeMult = 1f;
	[HideInInspector] public float PendulumLoopVolumeMult = 1f;
	[HideInInspector] public float TypingLoopVolumeMult = 1f;
	[HideInInspector] public float DaddyLoopVolumeMult = 1f;

	const string PREF_MIRROR_MULT = "mix.mirror_shatter";
	const string PREF_STOMP_MULT = "mix.stomp";
	const string PREF_PENDULUM_MULT = "mix.pendulum";
	const string PREF_TYPING_MULT = "mix.typing";
	const string PREF_SOUNDTRACK_MULT = "mix.soundtrack";

	// Loads saved multipliers (falling back to the current values). Called from
	// Awake so the loops start at the persisted mix even when the debug menu is
	// inactive at launch.
	public void LoadVolumeMultipliers()
	{
		MirrorBreakVolumeMult = PlayerPrefs.GetFloat(PREF_MIRROR_MULT, MirrorBreakVolumeMult);
		StompVolumeMult = PlayerPrefs.GetFloat(PREF_STOMP_MULT, StompVolumeMult);
		PendulumLoopVolumeMult = PlayerPrefs.GetFloat(PREF_PENDULUM_MULT, PendulumLoopVolumeMult);
		TypingLoopVolumeMult = PlayerPrefs.GetFloat(PREF_TYPING_MULT, TypingLoopVolumeMult);
		DaddyLoopVolumeMult = PlayerPrefs.GetFloat(PREF_SOUNDTRACK_MULT, DaddyLoopVolumeMult);
	}

	// Writes the current multipliers and flushes to disk.
	public void SaveVolumeMultipliers()
	{
		PlayerPrefs.SetFloat(PREF_MIRROR_MULT, MirrorBreakVolumeMult);
		PlayerPrefs.SetFloat(PREF_STOMP_MULT, StompVolumeMult);
		PlayerPrefs.SetFloat(PREF_PENDULUM_MULT, PendulumLoopVolumeMult);
		PlayerPrefs.SetFloat(PREF_TYPING_MULT, TypingLoopVolumeMult);
		PlayerPrefs.SetFloat(PREF_SOUNDTRACK_MULT, DaddyLoopVolumeMult);
		PlayerPrefs.Save();
	}

	// AudioSource.volume tolerates values above 1; this is just a sane safety cap
	// (loudest preset ~5 * a 2x slider).
	public const float MaxVolume = 10f;

	static float Clamp_volume(float value)
	{
		return Mathf.Clamp(value, 0f, MaxVolume);
	}

	float Mirror_break_mix_volume { get { return Clamp_volume(MirrorBreakVolume * MirrorBreakVolumeMult); } }
	float Stomp_mix_volume { get { return Clamp_volume(StompVolume * StompVolumeMult); } }
	float Pendulum_mix_volume { get { return Clamp_volume(PendulumLoopVolume * PendulumLoopVolumeMult); } }
	float Typing_mix_volume { get { return Clamp_volume(TypingLoopVolume * TypingLoopVolumeMult); } }
	float Daddy_mix_volume { get { return Clamp_volume(DaddyLoopVolume * DaddyLoopVolumeMult); } }

	bool typing_loop_running = false;
	int typing_loop_suspend_count = 0;

	float current_pendulum_pan = 0f;
	float current_pendulum_volume = 1f;
	const float internal_pendulum_pan_smooth_speed = 8f;
	const float internal_pendulum_volume_smooth_speed = 8f;

	public void Initialize(SimulationManager sim)
	{
		Ensure_audio_sources();
		Start_loops_if_needed();

		if (DebugSound)
		{
			Debug.Log(
				"[sound_manager] initialize | mirror_break_clips=" + (MirrorBreakClips != null ? MirrorBreakClips.Length : 0) +
				" | pendulum_sources=" + (PendulumLoopSources != null ? PendulumLoopSources.Length : 0) +
				" | typing_source=" + (TypingLoopSource != null ? TypingLoopSource.name : "null")
			);
		}
	}

	void Awake()
	{
		LoadVolumeMultipliers();
		Ensure_audio_sources();
	}

	// Safety net: persist the mix on quit/pause even if the debug menu object was
	// inactive (e.g. closed) when the app exits. SoundManager is always active.
	void OnApplicationQuit()
	{
		SaveVolumeMultipliers();
	}

	void OnApplicationPause(bool paused)
	{
		if (paused)
			SaveVolumeMultipliers();
	}

	Transform Get_runtime_root()
	{
		if (RuntimeRoot != null)
			return RuntimeRoot;

		GameObject runtime_object = GameObject.Find("Runtime");
		if (runtime_object == null)
			runtime_object = new GameObject("Runtime");

		RuntimeRoot = runtime_object.transform;
		return RuntimeRoot;
	}

	void Update()
	{
		Keep_loops_alive();
		Update_pendulum_pan();
	}

	void Ensure_audio_sources()
	{
		Ensure_pendulum_arrays();

		Transform pendulum_parent = PendulumEmitter != null ? PendulumEmitter : transform;
		if (PendulumPanTarget == null)
			PendulumPanTarget = PendulumEmitter;

		for (int i = 0; i < PendulumLoopSources.Length; i++)
		{
			if (PendulumLoopSources[i] == null)
				PendulumLoopSources[i] = Create_loop_source("pendulum_loop_audio_source_" + i, pendulum_parent);

			if (PendulumLoopSources[i] != null && PendulumLoopClips != null && i < PendulumLoopClips.Length && PendulumLoopClips[i] != null)
				PendulumLoopSources[i].clip = PendulumLoopClips[i];
		}

		if (TypingLoopSource == null)
			TypingLoopSource = Create_loop_source("typing_loop_audio_source", transform);

		if (TypingLoopSource != null && TypingLoopClip != null)
			TypingLoopSource.clip = TypingLoopClip;

		if (TypingLoopSource != null)
			TypingLoopSource.volume = Typing_mix_volume;

		Ensure_typing_reverb();

		if (DaddyLoopSource == null)
		{
			DaddyLoopSource = Create_loop_source("daddy_loves_you_loop_audio_source", transform);
			DaddyLoopSource.spatialBlend = 0f;
		}

		if (DaddyLoopSource != null && DaddyLoopClip != null)
			DaddyLoopSource.clip = DaddyLoopClip;

		if (DaddyLoopSource != null)
			DaddyLoopSource.volume = Daddy_mix_volume;
	}

	void Ensure_typing_reverb()
	{
		if (TypingLoopSource == null)
			return;

		if (typing_reverb_filter == null)
		{
			typing_reverb_filter = TypingLoopSource.GetComponent<AudioReverbFilter>();
			if (typing_reverb_filter == null)
				typing_reverb_filter = TypingLoopSource.gameObject.AddComponent<AudioReverbFilter>();
		}

		Apply_typing_reverb();
	}

	void Apply_typing_reverb()
	{
		if (typing_reverb_filter == null)
			return;

		typing_reverb_filter.enabled = TypingReverbEnabled;

		if (!TypingReverbEnabled)
			return;

		typing_reverb_filter.reverbPreset = TypingReverbPreset;

		// reverbPreset == User lets us drive the individual parameters by hand.
		if (TypingReverbPreset == AudioReverbPreset.User)
		{
			typing_reverb_filter.dryLevel = TypingReverbDryLevel;
			typing_reverb_filter.room = TypingReverbRoom;
			typing_reverb_filter.decayTime = Mathf.Max(0.1f, TypingReverbDecayTime);
			typing_reverb_filter.reverbLevel = TypingReverbLevel;
		}
	}

	void Ensure_pendulum_arrays()
	{
		if (PendulumLoopSources == null || PendulumLoopSources.Length != 3)
		{
			AudioSource[] new_sources = new AudioSource[3];
			if (PendulumLoopSources != null)
			{
				for (int i = 0; i < Mathf.Min(3, PendulumLoopSources.Length); i++)
					new_sources[i] = PendulumLoopSources[i];
			}
			PendulumLoopSources = new_sources;
		}

		if (PendulumLoopClips == null || PendulumLoopClips.Length != 3)
		{
			AudioClip[] new_clips = new AudioClip[3];
			if (PendulumLoopClips != null)
			{
				for (int i = 0; i < Mathf.Min(3, PendulumLoopClips.Length); i++)
					new_clips[i] = PendulumLoopClips[i];
			}
			PendulumLoopClips = new_clips;
		}
	}

	AudioSource Create_loop_source(string source_name, Transform emitter)
	{
		GameObject source_object = new GameObject(source_name);
		if (emitter != null)
		{
			source_object.transform.SetParent(emitter, false);
			source_object.transform.localPosition = Vector3.zero;
		}
		else
		{
			source_object.transform.SetParent(transform, false);
		}

		AudioSource source = source_object.AddComponent<AudioSource>();
		source.playOnAwake = false;
		source.loop = true;
		source.volume = 0f;
		source.pitch = 1f;
		source.spatialBlend = 1f;
		return source;
	}

	void Start_loops_if_needed()
	{
		for (int i = 0; i < PendulumLoopSources.Length; i++)
			Start_loop_if_needed(PendulumLoopSources[i], PendulumLoopClips[i]);

		typing_loop_running = true;
		ApplyTypingLoopState();

		Start_loop_if_needed(DaddyLoopSource, DaddyLoopClip);
		if (DaddyLoopSource != null)
			DaddyLoopSource.volume = Daddy_mix_volume;
	}

	void Start_loop_if_needed(AudioSource source, AudioClip clip)
	{
		if (source == null || clip == null)
			return;

		if (source.clip != clip)
			source.clip = clip;

		source.loop = true;
		source.pitch = 1f;

		if (!source.isPlaying)
			source.Play();
	}

	public void PlayMirrorBreak(Vector3 world_position)
	{
		Play_one_shot(MirrorBreakClips, world_position, Mirror_break_mix_volume, MirrorBreakPitchRandom, MirrorBreakSpatialBlend, "mirror_break", MirrorBreakReverbEnabled);
	}

	public void PlayStomp()
	{
		PlayStomp(transform.position);
	}

	public void PlayStomp(Vector3 world_position)
	{
		Play_one_shot(StompClips, world_position, Stomp_mix_volume, StompPitchRandom, StompSpatialBlend, "stomp");
	}

	void Play_one_shot(AudioClip[] clips, Vector3 world_position, float volume, float pitch_random, float spatial_blend, string label, bool apply_reverb = false)
	{
		if (clips == null || clips.Length == 0)
			return;

		AudioClip clip = clips[Random.Range(0, clips.Length)];
		if (clip == null)
			return;

		float pitch = 1f + Random.Range(-pitch_random, pitch_random);

		GameObject one_shot_object = new GameObject("one_shot_" + label);
		one_shot_object.transform.SetParent(Get_runtime_root(), true);

		AudioSource source = one_shot_object.AddComponent<AudioSource>();
		source.clip = clip;
		source.volume = 1f;
		source.pitch = pitch;
		source.spatialBlend = Mathf.Clamp01(spatial_blend);
		source.playOnAwake = false;

		if (apply_reverb)
		{
			AudioReverbFilter reverb = one_shot_object.AddComponent<AudioReverbFilter>();
			Configure_mirror_break_reverb(reverb);
		}

		source.PlayOneShot(clip, Clamp_volume(volume));

		Destroy(one_shot_object, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch)) + 0.1f);

		if (DebugSound)
		{
			Debug.Log(
				"[sound_manager] one_shot | label=" + label +
				" | clip=" + clip.name +
				" | position=" + world_position.ToString("F2") +
				" | volume=" + volume.ToString("F2") +
				" | pitch=" + pitch.ToString("F2")
			);
		}
	}

	void Configure_mirror_break_reverb(AudioReverbFilter reverb)
	{
		if (reverb == null)
			return;

		reverb.reverbPreset = MirrorBreakReverbPreset;

		// reverbPreset == User lets us drive the individual parameters by hand.
		if (MirrorBreakReverbPreset == AudioReverbPreset.User)
		{
			reverb.dryLevel = MirrorBreakReverbDryLevel;
			reverb.room = MirrorBreakReverbRoom;
			reverb.decayTime = Mathf.Max(0.1f, MirrorBreakReverbDecayTime);
			reverb.reverbLevel = MirrorBreakReverbLevel;
		}
	}

	public void SetPendulumDroneAmountRaw(float raw_amount)
	{
		// Compatibility hook: pendulum audio behavior is now controlled on the AudioSources directly.
	}

	public void SetPendulumLoopAmount(float normalized_amount)
	{
		// Compatibility hook: pendulum audio behavior is now controlled on the AudioSources directly.
	}

	// Marks whether the typing loop should be playing at all. The loop is
	// automatically silenced while one or more events are suspending it.
	public void SetTypingLoopActive(bool active)
	{
		typing_loop_running = active;
		ApplyTypingLoopState();
	}

	// Call when an event (world validation, stomp, ...) begins. The typing
	// loop pauses and resumes on its own once every event has ended.
	public void SuspendTypingLoopForEvent()
	{
		typing_loop_suspend_count++;
		ApplyTypingLoopState();
	}

	public void ResumeTypingLoopAfterEvent()
	{
		if (typing_loop_suspend_count > 0)
			typing_loop_suspend_count--;
		ApplyTypingLoopState();
	}

	void ApplyTypingLoopState()
	{
		if (TypingLoopSource == null)
			return;

		bool should_play = typing_loop_running && typing_loop_suspend_count <= 0;

		if (should_play)
		{
			if (TypingLoopSource.clip == null && TypingLoopClip != null)
				TypingLoopSource.clip = TypingLoopClip;

			TypingLoopSource.volume = Typing_mix_volume;
			TypingLoopSource.loop = true;

			if (TypingLoopSource.clip != null && !TypingLoopSource.isPlaying)
				TypingLoopSource.Play();
		}
		else
		{
			if (TypingLoopSource.isPlaying)
				TypingLoopSource.Stop();
		}
	}

	public void SetTypingLoopAmount(float normalized_amount)
	{
		SetTypingLoopActive(normalized_amount > 0f);
	}

	public void SetDaddyLoopActive(bool active)
	{
		if (DaddyLoopSource == null)
			return;

		if (active)
		{
			if (DaddyLoopSource.clip == null && DaddyLoopClip != null)
				DaddyLoopSource.clip = DaddyLoopClip;

			DaddyLoopSource.volume = Daddy_mix_volume;

			DaddyLoopSource.loop = true;

			if (DaddyLoopSource.clip != null && !DaddyLoopSource.isPlaying)
				DaddyLoopSource.Play();
		}
		else
		{
			if (DaddyLoopSource.isPlaying)
				DaddyLoopSource.Stop();
		}
	}

	void Keep_loops_alive()
	{
		if (PendulumLoopSources != null)
		{
			for (int i = 0; i < PendulumLoopSources.Length; i++)
			{
				AudioSource source = PendulumLoopSources[i];
				if (source == null || source.clip == null)
					continue;

				source.loop = true;

				if (!source.isPlaying)
					source.Play();
			}
		}

		if (TypingLoopSource != null && TypingLoopSource.clip != null)
		{
			TypingLoopSource.loop = true;
			TypingLoopSource.volume = Typing_mix_volume;
		}

		Apply_typing_reverb();

		if (DaddyLoopSource != null && DaddyLoopSource.clip != null)
		{
			DaddyLoopSource.loop = true;
			DaddyLoopSource.volume = Daddy_mix_volume;

			if (!DaddyLoopSource.isPlaying)
				DaddyLoopSource.Play();
		}
	}

	void Update_pendulum_pan()
	{
		Transform pan_target = PendulumPanTarget != null ? PendulumPanTarget : PendulumEmitter;
		if (pan_target == null || PendulumLoopSources == null)
			return;

		float x = pan_target.position.x;
		float left = Mathf.Min(PendulumPanLeftX, PendulumPanRightX);
		float right = Mathf.Max(PendulumPanLeftX, PendulumPanRightX);
		float normalized = Mathf.InverseLerp(left, right, x);
		float target_pan = Mathf.Lerp(-PendulumPanMax, PendulumPanMax, normalized);

		current_pendulum_pan = Mathf.MoveTowards(
			current_pendulum_pan,
			target_pan,
			internal_pendulum_pan_smooth_speed * Time.unscaledDeltaTime
		);

		float center_x = (left + right) * 0.5f;
		float half_range = Mathf.Max(0.0001f, (right - left) * 0.5f);
		float distance_from_center = Mathf.Abs(x - center_x);
		float edge_amount = Mathf.Clamp01(distance_from_center / half_range);
		float target_volume = Mathf.Lerp(1f, 0f, Mathf.Clamp01(edge_amount * PendulumVolumeFade));

		current_pendulum_volume = Mathf.MoveTowards(
			current_pendulum_volume,
			target_volume,
			internal_pendulum_volume_smooth_speed * Time.unscaledDeltaTime
		);

		for (int i = 0; i < PendulumLoopSources.Length; i++)
		{
			AudioSource source = PendulumLoopSources[i];
			if (source == null)
				continue;

			source.panStereo = current_pendulum_pan;
			source.volume = current_pendulum_volume * Pendulum_mix_volume;
		}
	}
}