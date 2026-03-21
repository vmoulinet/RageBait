using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

public class VideoManager : MonoBehaviour
{
	[Header("References")]
	public VideoPlayer VideoPlayer;
	public TMP_Text SubtitleText;
	public CameraSineShake CameraShake;
	public Transform DebrisFeedbackTarget;

	[Header("Sources")]
	public string VideosFolderRelative = "VIDEOS";
	public string WordListRelative = "scripts/data/wordlist.txt";

	[Header("Playback")]
	public bool PrepareOnAwake = true;
	public bool LoopVideoList = true;
	public bool LoopVideoPlayback = false;
	public float VideoEventDuration = 10f;
	public float SimulationTimeScaleDuringEvent = 0.35f;

	[Header("Subtitles")]
	public float CharactersPerSecond = 42f;
	public bool LoopSentences = true;

	[Header("Screen Shake")]
	public bool ShakeOnPlay = true;
	public float ShakeDuration = 0.55f;
	public float ShakeAmplitude = 0.12f;
	public float ShakeFrequency = 11f;
	public float ShakeDecay = 1.25f;

	[Header("Debris Feedback")]
	public float DebrisFeedbackRaiseAmount = 1.5f;
	public float DebrisFeedbackRaiseDuration = 0.12f;
	public float DebrisFeedbackFallDuration = 0.20f;

	[Header("Future Hooks")]
	public float PendulumSpeedMultiplierAfterVideo = 1.25f;

	readonly List<string> video_paths = new List<string>();
	readonly List<string> sentences = new List<string>();

	int current_video_index = 0;
	int next_sentence_index = 0;
	int prepared_video_index = -1;

	float previous_time_scale = 1f;
	Coroutine active_event_routine = null;
	Coroutine active_subtitle_routine = null;
	Coroutine debris_feedback_routine = null;
	Vector3 debris_feedback_base_local_position = Vector3.zero;
	bool debris_feedback_base_cached = false;

	public bool IsPlayingEvent { get; private set; } = false;
	public string CurrentPreparedVideoPath { get; private set; } = "";
	public string CurrentVideoName
	{
		get
		{
			if (prepared_video_index < 0 || prepared_video_index >= video_paths.Count)
				return "";

			return Path.GetFileNameWithoutExtension(video_paths[prepared_video_index]);
		}
	}

	void Awake()
	{
		Ensure_video_player();
		Load_video_paths();
		Load_sentences();

		if (SubtitleText != null)
		{
			SubtitleText.text = "";
			SubtitleText.maxVisibleCharacters = 0;
		}

		Cache_debris_feedback_base_position();

		if (PrepareOnAwake)
			Prepare_current_video();
	}

	void Cache_debris_feedback_base_position()
	{
		if (DebrisFeedbackTarget == null || debris_feedback_base_cached)
			return;

		debris_feedback_base_local_position = DebrisFeedbackTarget.localPosition;
		debris_feedback_base_cached = true;
	}

	void Ensure_video_player()
	{
		if (VideoPlayer == null)
		{
			VideoPlayer = GetComponent<VideoPlayer>();
			if (VideoPlayer == null)
				VideoPlayer = gameObject.AddComponent<VideoPlayer>();

			VideoPlayer.isLooping = LoopVideoPlayback;
		}
	}

	void Load_video_paths()
	{
		video_paths.Clear();

		string videos_folder = Path.Combine(Application.dataPath, VideosFolderRelative);
		if (!Directory.Exists(videos_folder))
		{
			Debug.LogWarning("[video_manager] videos folder missing: " + videos_folder);
			return;
		}

		string[] valid_extensions = new string[]
		{
			".mp4",
			".mov",
			".m4v",
			".avi",
			".webm"
		};

		string[] files = Directory.GetFiles(videos_folder);
		for (int i = 0; i < files.Length; i++)
		{
			string extension = Path.GetExtension(files[i]).ToLowerInvariant();
			for (int j = 0; j < valid_extensions.Length; j++)
			{
				if (extension == valid_extensions[j])
				{
					video_paths.Add(files[i]);
					break;
				}
			}
		}

		video_paths.Sort(Compare_video_paths);

		Debug.Log("[video_manager] loaded videos=" + video_paths.Count);
	}

	void Load_sentences()
	{
		sentences.Clear();

		string word_list_path = Path.Combine(Application.dataPath, WordListRelative);
		if (!File.Exists(word_list_path))
		{
			Debug.LogWarning("[video_manager] word list missing: " + word_list_path);
			return;
		}

		string[] lines = File.ReadAllLines(word_list_path);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (string.IsNullOrWhiteSpace(line))
				continue;

			sentences.Add(line);
		}

		Debug.Log("[video_manager] loaded sentences=" + sentences.Count);
	}

	int Compare_video_paths(string a, string b)
	{
		string a_name = Path.GetFileNameWithoutExtension(a);
		string b_name = Path.GetFileNameWithoutExtension(b);

		int a_leading_number = Extract_leading_number(a_name);
		int b_leading_number = Extract_leading_number(b_name);

		bool a_has_leading_number = a_leading_number >= 0;
		bool b_has_leading_number = b_leading_number >= 0;

		if (a_has_leading_number && b_has_leading_number && a_leading_number != b_leading_number)
			return a_leading_number.CompareTo(b_leading_number);

		if (a_has_leading_number && !b_has_leading_number)
			return -1;

		if (!a_has_leading_number && b_has_leading_number)
			return 1;

		return Compare_natural(a_name, b_name);
	}

	int Compare_natural(string a, string b)
	{
		int a_index = 0;
		int b_index = 0;

		while (a_index < a.Length && b_index < b.Length)
		{
			bool a_is_digit = char.IsDigit(a[a_index]);
			bool b_is_digit = char.IsDigit(b[b_index]);

			if (a_is_digit && b_is_digit)
			{
				long a_number = Read_number(a, ref a_index);
				long b_number = Read_number(b, ref b_index);

				if (a_number != b_number)
					return a_number.CompareTo(b_number);

				continue;
			}

			char a_char = char.ToLowerInvariant(a[a_index]);
			char b_char = char.ToLowerInvariant(b[b_index]);

			if (a_char != b_char)
				return a_char.CompareTo(b_char);

			a_index++;
			b_index++;
		}

		return a.Length.CompareTo(b.Length);
	}

	long Read_number(string text, ref int index)
	{
		long value = 0;

		while (index < text.Length && char.IsDigit(text[index]))
		{
			value = value * 10 + (text[index] - '0');
			index++;
		}

		return value;
	}

	int Extract_leading_number(string text)
	{
		if (string.IsNullOrEmpty(text))
			return -1;

		int index = 0;
		while (index < text.Length && char.IsDigit(text[index]))
			index++;

		if (index == 0)
			return -1;

		int value;
		if (int.TryParse(text.Substring(0, index), out value))
			return value;

		return -1;
	}

	void Prepare_current_video()
	{
		if (video_paths.Count == 0)
			return;

		current_video_index = Mathf.Clamp(current_video_index, 0, video_paths.Count - 1);
		Prepare_video_at(current_video_index);
	}

	void Prepare_next_video()
	{
		if (video_paths.Count == 0)
			return;

		int next_video_index = current_video_index + 1;
		if (next_video_index >= video_paths.Count)
		{
			if (!LoopVideoList)
				next_video_index = video_paths.Count - 1;
			else
				next_video_index = 0;
		}

		current_video_index = next_video_index;
		Prepare_video_at(current_video_index);
	}

	void Prepare_video_at(int index)
	{
		if (index < 0 || index >= video_paths.Count || VideoPlayer == null)
			return;

		string video_path = video_paths[index];

		prepared_video_index = index;
		CurrentPreparedVideoPath = video_path;
		VideoPlayer.url = video_path;
		VideoPlayer.Prepare();

		Debug.Log("[video_manager] prepare | index=" + index + " | path=" + video_path);
	}

	public void Play_video_event()
	{
		Play_video_event_by_index(current_video_index);
	}

	public void Play_video_event_by_name(string partial_name)
	{
		if (string.IsNullOrWhiteSpace(partial_name))
		{
			Play_video_event();
			return;
		}

		for (int i = 0; i < video_paths.Count; i++)
		{
			string file_name = Path.GetFileNameWithoutExtension(video_paths[i]);
			if (!file_name.ToLowerInvariant().Contains(partial_name.ToLowerInvariant()))
				continue;

			Play_video_event_by_index(i);
			return;
		}

		Debug.LogWarning("[video_manager] no video found for name=" + partial_name);
	}

	public void Play_video_event_by_index(int index)
	{
		if (video_paths.Count == 0)
		{
			Debug.LogWarning("[video_manager] no videos available");
			Trigger_feedback_only();
			return;
		}

		index = Mathf.Clamp(index, 0, video_paths.Count - 1);

		if (IsPlayingEvent)
		{
			Trigger_feedback_only();
			return;
		}

		if (active_event_routine != null)
			StopCoroutine(active_event_routine);

		active_event_routine = StartCoroutine(Video_event_routine(index));
	}

	void Trigger_feedback_only()
	{
		if (ShakeOnPlay && CameraShake != null)
			CameraShake.Trigger_shake(ShakeDuration, ShakeAmplitude, ShakeFrequency, ShakeDecay);

		Trigger_debris_feedback();
	}

	void Trigger_debris_feedback()
	{
		if (DebrisFeedbackTarget == null)
			return;

		Cache_debris_feedback_base_position();

		if (debris_feedback_routine != null)
			StopCoroutine(debris_feedback_routine);

		debris_feedback_routine = StartCoroutine(Debris_feedback_routine());
	}

	IEnumerator Video_event_routine(int index)
	{
		IsPlayingEvent = true;
		Trigger_feedback_only();

		if (SubtitleText != null)
		{
			SubtitleText.text = "";
			SubtitleText.maxVisibleCharacters = 0;
		}

		if (prepared_video_index != index)
			Prepare_video_at(index);

		float prepare_timeout = 3f;
		float prepare_elapsed = 0f;

		while (VideoPlayer != null && !VideoPlayer.isPrepared && prepare_elapsed < prepare_timeout)
		{
			prepare_elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		previous_time_scale = Time.timeScale;
		Time.timeScale = SimulationTimeScaleDuringEvent;

		if (VideoPlayer != null)
		{
			VideoPlayer.isLooping = LoopVideoPlayback;
			VideoPlayer.Play();
		}

		if (active_subtitle_routine != null)
			StopCoroutine(active_subtitle_routine);

		active_subtitle_routine = StartCoroutine(Subtitle_routine());

		float timer = 0f;
		while (timer < VideoEventDuration)
		{
			timer += Time.unscaledDeltaTime;
			yield return null;
		}

		if (VideoPlayer != null && VideoPlayer.isPlaying)
			VideoPlayer.Stop();

		if (active_subtitle_routine != null)
		{
			StopCoroutine(active_subtitle_routine);
			active_subtitle_routine = null;
		}

		if (SubtitleText != null)
		{
			SubtitleText.text = "";
			SubtitleText.maxVisibleCharacters = 0;
		}

		Time.timeScale = previous_time_scale;

		// todo: when pendulum speed control exists, apply PendulumSpeedMultiplierAfterVideo here.
		Debug.Log("[video_manager] event end | pendulum_speed_multiplier_after_video=" + PendulumSpeedMultiplierAfterVideo.ToString("F2"));

		if (DebrisFeedbackTarget != null)
		{
			Cache_debris_feedback_base_position();
			DebrisFeedbackTarget.localPosition = debris_feedback_base_local_position;
		}

		Advance_to_next_sentence();
		current_video_index = index;
		Prepare_next_video();

		IsPlayingEvent = false;
		active_event_routine = null;
	}

	IEnumerator Debris_feedback_routine()
	{
		if (DebrisFeedbackTarget == null)
			yield break;

		Cache_debris_feedback_base_position();

		Vector3 start = debris_feedback_base_local_position;
		Vector3 peak = debris_feedback_base_local_position + Vector3.up * DebrisFeedbackRaiseAmount;

		float raise_elapsed = 0f;
		while (raise_elapsed < DebrisFeedbackRaiseDuration)
		{
			raise_elapsed += Time.unscaledDeltaTime;
			float t = Mathf.Clamp01(raise_elapsed / Mathf.Max(0.0001f, DebrisFeedbackRaiseDuration));
			float eased = 1f - Mathf.Pow(1f - t, 3f);
			DebrisFeedbackTarget.localPosition = Vector3.LerpUnclamped(start, peak, eased);
			yield return null;
		}

		float fall_elapsed = 0f;
		while (fall_elapsed < DebrisFeedbackFallDuration)
		{
			fall_elapsed += Time.unscaledDeltaTime;
			float t = Mathf.Clamp01(fall_elapsed / Mathf.Max(0.0001f, DebrisFeedbackFallDuration));
			float eased = 1f - Mathf.Pow(1f - t, 2f);
			DebrisFeedbackTarget.localPosition = Vector3.LerpUnclamped(peak, debris_feedback_base_local_position, eased);
			yield return null;
		}

		DebrisFeedbackTarget.localPosition = debris_feedback_base_local_position;
		debris_feedback_routine = null;
	}

	IEnumerator Subtitle_routine()
	{
		if (SubtitleText == null || sentences.Count == 0)
			yield break;

		string sentence = Get_current_sentence();
		if (string.IsNullOrWhiteSpace(sentence))
			yield break;

		SubtitleText.text = sentence;
		SubtitleText.maxVisibleCharacters = 0;

		float type_time = Mathf.Max(0.001f, sentence.Length / Mathf.Max(1f, CharactersPerSecond));
		float type_elapsed = 0f;

		while (type_elapsed < type_time)
		{
			type_elapsed += Time.unscaledDeltaTime;

			float progress = Mathf.Clamp01(type_elapsed / type_time);
			SubtitleText.maxVisibleCharacters = Mathf.RoundToInt(sentence.Length * progress);

			yield return null;
		}

		SubtitleText.maxVisibleCharacters = sentence.Length;
	}

	string Get_current_sentence()
	{
		if (sentences.Count == 0)
			return "";

		if (next_sentence_index < 0)
			next_sentence_index = 0;

		if (next_sentence_index >= sentences.Count)
		{
			if (!LoopSentences)
				next_sentence_index = sentences.Count - 1;
			else
				next_sentence_index = 0;
		}

		return sentences[next_sentence_index];
	}

	void Advance_to_next_sentence()
	{
		if (sentences.Count == 0)
			return;

		next_sentence_index++;

		if (next_sentence_index >= sentences.Count)
		{
			if (!LoopSentences)
				next_sentence_index = sentences.Count - 1;
			else
				next_sentence_index = 0;
		}
	}
}