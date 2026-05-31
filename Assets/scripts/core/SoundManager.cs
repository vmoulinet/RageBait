using UnityEngine;

public class SoundManager : MonoBehaviour
{
	[Header("Mirror Shatter")]
	public AudioClip[] MirrorBreakClips;
	public float MirrorBreakVolume = 1f;
	public float MirrorBreakPitchRandom = 0.06f;
	public float MirrorBreakSpatialBlend = 1f;
	public Transform RuntimeRoot;

	[Header("Pendulum Loop")]
	public AudioSource[] PendulumLoopSources = new AudioSource[3];
	public AudioClip[] PendulumLoopClips = new AudioClip[3];
	public Transform PendulumEmitter;

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

	[Header("Daddy Loves You Loop")]
	public AudioSource DaddyLoopSource;
	public AudioClip DaddyLoopClip;
	public float DaddyLoopVolume = 1f;

	[Header("Debug")]
	public bool DebugSound = false;

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
		Ensure_audio_sources();
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
			TypingLoopSource.volume = Mathf.Clamp01(TypingLoopVolume);

		if (DaddyLoopSource == null)
		{
			DaddyLoopSource = Create_loop_source("daddy_loves_you_loop_audio_source", transform);
			DaddyLoopSource.spatialBlend = 0f;
		}

		if (DaddyLoopSource != null && DaddyLoopClip != null)
			DaddyLoopSource.clip = DaddyLoopClip;

		if (DaddyLoopSource != null)
			DaddyLoopSource.volume = Mathf.Clamp01(DaddyLoopVolume);
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

		Start_loop_if_needed(TypingLoopSource, TypingLoopClip);

		Start_loop_if_needed(DaddyLoopSource, DaddyLoopClip);
		if (DaddyLoopSource != null)
			DaddyLoopSource.volume = Mathf.Clamp01(DaddyLoopVolume);
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
		Play_one_shot(MirrorBreakClips, world_position, MirrorBreakVolume, MirrorBreakPitchRandom, MirrorBreakSpatialBlend, "mirror_break");
	}

	void Play_one_shot(AudioClip[] clips, Vector3 world_position, float volume, float pitch_random, float spatial_blend, string label)
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
		source.PlayOneShot(clip, Mathf.Max(0f, volume));

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

	public void SetPendulumDroneAmountRaw(float raw_amount)
	{
		// Compatibility hook: pendulum audio behavior is now controlled on the AudioSources directly.
	}

	public void SetPendulumLoopAmount(float normalized_amount)
	{
		// Compatibility hook: pendulum audio behavior is now controlled on the AudioSources directly.
	}

	public void SetTypingLoopActive(bool active)
	{
		if (TypingLoopSource == null)
			return;

		if (active)
		{
			if (TypingLoopSource.clip == null && TypingLoopClip != null)
				TypingLoopSource.clip = TypingLoopClip;

			TypingLoopSource.volume = Mathf.Clamp01(TypingLoopVolume);

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

			DaddyLoopSource.volume = Mathf.Clamp01(DaddyLoopVolume);

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
			TypingLoopSource.volume = Mathf.Clamp01(TypingLoopVolume);
		}

		if (DaddyLoopSource != null && DaddyLoopSource.clip != null)
		{
			DaddyLoopSource.loop = true;
			DaddyLoopSource.volume = Mathf.Clamp01(DaddyLoopVolume);

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
			source.volume = current_pendulum_volume;
		}
	}
}