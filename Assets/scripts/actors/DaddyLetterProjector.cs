using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

public class DaddyLetterProjector : MonoBehaviour
{
	[Header("Source")]
	public TextAsset LetterAsset;
	public TextAsset LetterAssetJP;

	[Header("Display")]
	public TextMeshPro TargetText;
	public TextMeshPro TargetTextJP;

	[Header("Cookie Output")]
	public Light ProjectorLight;
	public Camera CookieCamera;
	public RenderTexture CookieTexture;
	public int CookieResolution = 1024;
	public bool AssignCookieAtStart = true;

	[Header("Typing (EN)")]
	public float BaseCharDelay = 0.06f;
	public float CharDelayJitter = 0.2f;
	public float PunctuationDelay = 0.35f;
	public float NewlineDelay = 0.7f;
	public float ParagraphDelay = 1.2f;

	[Header("Typing (JP)")]
	public bool UseSeparateJPTiming = true;
	public float BaseCharDelayJP = 0.10f;
	public float CharDelayJitterJP = 0.2f;
	public float PunctuationDelayJP = 0.45f;
	public float NewlineDelayJP = 0.7f;
	public float ParagraphDelayJP = 1.2f;

	[Header("Erase")]
	public float EraseCharDelay = 0.025f;
	public float EraseCharJitter = 0.1f;
	public int EraseCharsPerStep = 1;
	public float EraseTotalDuration = 0f;

	[Header("Lifecycle")]
	public float RestartDelay = 1.0f;
	public float HoldAfterFinishedDelay = 4.0f;
	public VideoManager VideoManager;
	public WorldValidation WorldValidation;
	public float RestartWaitTimeout = 30f;

	[Header("Audio")]
	public SoundManager SoundManager;

	[Header("Redaction")]
	public string RedactionMarkOpen = "<mark=#FFFFFFFF padding=\"6,6,2,2\">";
	public string RedactionMarkClose = "</mark>";

	[Header("Cursor")]
	public bool ShowCursor = true;
	public string CursorCharacter = "|";
	public float CursorBlinkInterval = 0.5f;

	[Header("Debug")]
	public bool DebugLog = false;

	enum State
	{
		Idle,
		Typing,
		Holding,
		Erasing,
		Restarting
	}

	struct WordSpan
	{
		public int start;
		public int end;
		public bool redacted;
	}

	enum EraseSource
	{
		Natural,
		Stomp,
		WorldValidation
	}

	string source_text = "";
	int typed_count = 0;
	State current_state = State.Idle;
	Coroutine state_coroutine = null;
	bool is_japanese_cycle = false;
	EraseSource last_erase_source = EraseSource.Natural;

	// Raised when the EN letter has just finished typing (entering Holding).
	// BreakupLetterService listens to collect redacted words and kick off the
	// Nephila move. Not raised on the JP cycle.
	public event Action OnLetterFinishedTyping;

	// When set, the next EN write cycle displays this text instead of the
	// TextAsset (the AI-generated breakup letter). Consumed once.
	string generated_letter_override = null;

	readonly List<WordSpan> word_spans = new List<WordSpan>();
	int current_word_index = -1;

	readonly StringBuilder builder = new StringBuilder(2048);

	bool cursor_armed = false;
	float cursor_phase_start = 0f;

	void Start()
	{
		LoadLetter();
		BuildWordSpans();
		EnsureCookieTexture();
		AssignCookieToLight();
		ApplyActiveTargetForCycle();
		ApplyTextToTarget();
		BeginTyping();
	}

	TextMeshPro ActiveText
	{
		get { return is_japanese_cycle ? TargetTextJP : TargetText; }
	}

	void ApplyActiveTargetForCycle()
	{
		if (TargetText != null)
		{
			TargetText.gameObject.SetActive(!is_japanese_cycle);
			if (!is_japanese_cycle)
				TargetText.text = "";
		}

		if (TargetTextJP != null)
		{
			TargetTextJP.gameObject.SetActive(is_japanese_cycle);
			if (is_japanese_cycle)
				TargetTextJP.text = "";
		}
	}

	void Update()
	{
		if (!ShowCursor)
			return;

		if (cursor_armed)
			ApplyTextToTarget();
	}

	void LoadLetter()
	{
		TextAsset asset = is_japanese_cycle ? LetterAssetJP : LetterAsset;

		if (asset == null)
		{
			Debug.LogError("[daddy_letter] LetterAsset is missing (japanese=" + is_japanese_cycle + ")");
			source_text = "";
			return;
		}

		source_text = asset.text.Replace("\r\n", "\n").Replace("\r", "\n");

		// On the EN cycle, a pending AI-generated breakup letter replaces the
		// whole letter for this one write. Consumed once, then back to normal.
		if (!is_japanese_cycle && !string.IsNullOrEmpty(generated_letter_override))
		{
			source_text = generated_letter_override.Replace("\r\n", "\n").Replace("\r", "\n");
			generated_letter_override = null;

			if (DebugLog)
				Debug.Log("[daddy_letter] using AI-generated breakup letter | chars=" + source_text.Length);
		}
	}
	void BuildWordSpans()
	{
		word_spans.Clear();

		int i = 0;
		while (i < source_text.Length)
		{
			while (i < source_text.Length && !IsWordChar(source_text[i]))
				i++;

			if (i >= source_text.Length)
				break;

			int start = i;
			while (i < source_text.Length && IsWordChar(source_text[i]))
				i++;

			WordSpan span;
			span.start = start;
			span.end = i;
			span.redacted = false;
			word_spans.Add(span);
		}
	}

	static bool IsWordChar(char c)
	{
		return char.IsLetterOrDigit(c) || c == '\'' || c == '-';
	}

	void EnsureCookieTexture()
	{
		if (CookieTexture != null)
			return;

		if (!AssignCookieAtStart)
			return;

		int size = Mathf.Max(64, CookieResolution);
		CookieTexture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
		{
			name = "DaddyLetter_Cookie",
			useMipMap = false,
			autoGenerateMips = false,
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Point,
			anisoLevel = 0
		};
		CookieTexture.Create();

		if (CookieCamera != null)
			CookieCamera.targetTexture = CookieTexture;
	}

	void AssignCookieToLight()
	{
		if (!AssignCookieAtStart || ProjectorLight == null || CookieTexture == null)
			return;

		ProjectorLight.cookie = CookieTexture;
	}

	void BeginTyping()
	{
		StopActiveCoroutine();
		typed_count = 0;
		current_word_index = -1;
		ResetRedactions();
		ApplyTextToTarget();

		current_state = State.Typing;
		SetTypingSound(true);
		state_coroutine = StartCoroutine(TypeRoutine());
	}

	void SetTypingSound(bool active)
	{
		// The typing loop is now owned by SoundManager and driven by event
		// suspend/resume (world validation, stomp). The projector no longer
		// starts or stops the audio so both scene variants stay consistent
		// whether or not this projector object is present.
	}

	void StopActiveCoroutine()
	{
		if (state_coroutine != null)
		{
			StopCoroutine(state_coroutine);
			state_coroutine = null;
		}
	}

	IEnumerator TypeRoutine()
	{
		while (typed_count < source_text.Length)
		{
			char next_char = source_text[typed_count];
			typed_count++;
			SetCursorArmed(false);
			ApplyTextToTarget();

			float delay = ComputeTypingDelay(next_char);
			if (delay > 0f)
			{
				SetCursorArmed(true);
				yield return new WaitForSeconds(delay);
				SetCursorArmed(false);
			}
		}

		current_state = State.Holding;
		SetCursorArmed(true);
		SetTypingSound(false);
		ApplyTextToTarget();

		// Letter fully written: on the EN cycle, notify listeners so the
		// breakup-letter generation can start while we hold/erase.
		if (!is_japanese_cycle && OnLetterFinishedTyping != null)
			OnLetterFinishedTyping.Invoke();

		if (HoldAfterFinishedDelay > 0f)
			yield return new WaitForSeconds(HoldAfterFinishedDelay);

		SetCursorArmed(false);
		last_erase_source = EraseSource.Natural;
		BeginErase();
	}

	float ComputeTypingDelay(char c)
	{
		bool jp = is_japanese_cycle && UseSeparateJPTiming;

		float base_delay = jp ? BaseCharDelayJP : BaseCharDelay;
		float punctuation_delay = jp ? PunctuationDelayJP : PunctuationDelay;
		float newline_delay = jp ? NewlineDelayJP : NewlineDelay;
		float paragraph_delay = jp ? ParagraphDelayJP : ParagraphDelay;
		float jitter = jp ? CharDelayJitterJP : CharDelayJitter;

		float delay = base_delay;

		if (c == '\n')
		{
			bool is_paragraph = typed_count < source_text.Length && source_text[typed_count] == '\n';
			delay = is_paragraph ? paragraph_delay : newline_delay;
		}
		else if (c == '.' || c == '!' || c == '?' || c == '。' || c == '！' || c == '？')
		{
			delay = punctuation_delay;
		}
		else if (c == ',' || c == ';' || c == ':' || c == '、' || c == '：' || c == '；')
		{
			delay = punctuation_delay * 0.6f;
		}

		if (jitter > 0f)
		{
			float j = 1f + UnityEngine.Random.Range(-jitter, jitter);
			delay *= Mathf.Max(0.05f, j);
		}

		return delay;
	}

	void BeginErase()
	{
		StopActiveCoroutine();
		SetTypingSound(false);
		current_state = State.Erasing;
		state_coroutine = StartCoroutine(EraseRoutine());
	}

	IEnumerator EraseRoutine()
	{
		int initial_count = typed_count;
		float erase_start_time = Time.time;

		while (typed_count > 0)
		{
			int chars_to_remove;

			if (EraseTotalDuration > 0f && initial_count > 0)
			{
				float elapsed = Time.time - erase_start_time;
				int target_typed = Mathf.RoundToInt(
					Mathf.Lerp(initial_count, 0f, Mathf.Clamp01(elapsed / EraseTotalDuration))
				);
				chars_to_remove = Mathf.Max(1, typed_count - target_typed);
			}
			else
			{
				chars_to_remove = Mathf.Max(1, EraseCharsPerStep);
			}

			typed_count = Mathf.Max(0, typed_count - chars_to_remove);
			TrimRedactionsToTyped();
			ApplyTextToTarget();

			float delay = EraseCharDelay;
			if (EraseCharJitter > 0f)
			{
				float j = 1f + UnityEngine.Random.Range(-EraseCharJitter, EraseCharJitter);
				delay *= Mathf.Max(0.05f, j);
			}

			if (delay > 0f)
				yield return new WaitForSeconds(delay);
			else
				yield return null;
		}

		ResetRedactions();
		ApplyTextToTarget();

		current_state = State.Restarting;

		yield return StartCoroutine(WaitForTriggeringEventToFinish());

		if (RestartDelay > 0f)
			yield return new WaitForSeconds(RestartDelay);

		is_japanese_cycle = !is_japanese_cycle;
		LoadLetter();
		BuildWordSpans();
		ApplyActiveTargetForCycle();

		BeginTyping();
	}

	IEnumerator WaitForTriggeringEventToFinish()
	{
		float elapsed = 0f;

		if (last_erase_source == EraseSource.Stomp && VideoManager != null)
		{
			if (DebugLog)
				Debug.Log("[daddy_letter] restart wait | source=stomp | video_playing=" + VideoManager.IsPlayingEvent);

			while (VideoManager.IsPlayingEvent && elapsed < RestartWaitTimeout)
			{
				elapsed += Time.unscaledDeltaTime;
				yield return null;
			}
		}
		else if (last_erase_source == EraseSource.WorldValidation && WorldValidation != null)
		{
			if (DebugLog)
				Debug.Log("[daddy_letter] restart wait | source=world_validation | active=" + WorldValidation.IsActive);

			while (WorldValidation.IsActive && elapsed < RestartWaitTimeout)
			{
				elapsed += Time.unscaledDeltaTime;
				yield return null;
			}
		}

		if (DebugLog && elapsed > 0f)
			Debug.Log("[daddy_letter] restart wait done | source=" + last_erase_source + " | elapsed=" + elapsed.ToString("F2"));
	}

	public void NotifyMirrorBroken()
	{
		if (current_state != State.Typing && current_state != State.Holding)
			return;

		if (source_text.Length == 0 || word_spans.Count == 0)
			return;

		int word_index = FindCurrentOrPreviousWordIndex();
		if (word_index < 0)
			return;

		WordSpan span = word_spans[word_index];
		if (span.redacted)
			return;

		span.redacted = true;
		word_spans[word_index] = span;
		current_word_index = word_index;

		ApplyTextToTarget();

		if (DebugLog)
		{
			string word = source_text.Substring(span.start, span.end - span.start);
			Debug.Log("[daddy_letter] redacted | word=" + word + " | index=" + word_index);
		}
	}

	public void NotifyStomp()
	{
		TriggerBackErase("stomp", EraseSource.Stomp);
	}

	public void NotifyWorldValidation()
	{
		TriggerBackErase("world_validation", EraseSource.WorldValidation);
	}

	void TriggerBackErase(string source, EraseSource erase_source)
	{
		if (current_state == State.Erasing || current_state == State.Restarting)
			return;

		if (DebugLog)
			Debug.Log("[daddy_letter] back-erase trigger | source=" + source + " | typed=" + typed_count);

		last_erase_source = erase_source;
		BeginErase();
	}

	int FindCurrentOrPreviousWordIndex()
	{
		int cursor = Mathf.Max(0, typed_count - 1);

		for (int i = word_spans.Count - 1; i >= 0; i--)
		{
			WordSpan span = word_spans[i];

			if (span.start > cursor)
				continue;

			if (cursor < span.end)
				return i;

			return i;
		}

		return -1;
	}

	void ResetRedactions()
	{
		for (int i = 0; i < word_spans.Count; i++)
		{
			WordSpan span = word_spans[i];
			span.redacted = false;
			word_spans[i] = span;
		}

		current_word_index = -1;
	}

	void TrimRedactionsToTyped()
	{
		for (int i = 0; i < word_spans.Count; i++)
		{
			WordSpan span = word_spans[i];
			if (!span.redacted)
				continue;

			if (span.start >= typed_count)
			{
				span.redacted = false;
				word_spans[i] = span;
			}
		}
	}

	void ApplyTextToTarget()
	{
		TextMeshPro target = ActiveText;
		if (target == null)
			return;

		target.text = BuildDisplayText();
	}

	void SetCursorArmed(bool armed)
	{
		if (cursor_armed == armed)
			return;

		cursor_armed = armed;
		if (armed)
			cursor_phase_start = Time.time;
	}

	bool IsCursorVisibleNow()
	{
		if (!ShowCursor || !cursor_armed)
			return false;

		if (CursorBlinkInterval <= 0f)
			return true;

		float elapsed = Time.time - cursor_phase_start;
		int phase = Mathf.FloorToInt(elapsed / CursorBlinkInterval);
		return (phase % 2) == 0;
	}

	string BuildDisplayText()
	{
		builder.Length = 0;

		int visible_length = Mathf.Clamp(typed_count, 0, source_text.Length);
		if (visible_length == 0)
		{
			if (IsCursorVisibleNow())
				builder.Append(CursorCharacter);
			return builder.ToString();
		}

		int word_cursor = 0;

		for (int i = 0; i < visible_length; i++)
		{
			while (word_cursor < word_spans.Count && word_spans[word_cursor].end <= i)
				word_cursor++;

			bool open_redaction =
				word_cursor < word_spans.Count &&
				word_spans[word_cursor].redacted &&
				word_spans[word_cursor].start == i;

			if (open_redaction)
				builder.Append(RedactionMarkOpen);

			builder.Append(source_text[i]);

			bool close_redaction =
				word_cursor < word_spans.Count &&
				word_spans[word_cursor].redacted &&
				(i + 1 == word_spans[word_cursor].end || i + 1 == visible_length);

			if (close_redaction)
				builder.Append(RedactionMarkClose);
		}

		if (IsCursorVisibleNow())
			builder.Append(CursorCharacter);

		return builder.ToString();
	}

	// --- Nephila / breakup-letter integration ---

	// Returns the words currently marked as redacted (mirror breaks), in reading
	// order. Used by BreakupLetterService to feed the Nephila move.
	public List<string> GetRedactedWords()
	{
		var words = new List<string>();
		for (int i = 0; i < word_spans.Count; i++)
		{
			WordSpan span = word_spans[i];
			if (!span.redacted)
				continue;
			if (span.start >= 0 && span.end <= source_text.Length && span.end > span.start)
				words.Add(source_text.Substring(span.start, span.end - span.start));
		}
		return words;
	}

	// True while on the English cycle (the one that gets redacted / replaced).
	public bool IsEnglishCycle
	{
		get { return !is_japanese_cycle; }
	}

	// Queues an AI-generated letter to fully replace the daddy letter on the next
	// EN write cycle. Ignored if null/empty.
	public void SetGeneratedLetterOverride(string letter)
	{
		if (string.IsNullOrEmpty(letter))
			return;

		generated_letter_override = letter;

		if (DebugLog)
			Debug.Log("[daddy_letter] breakup letter queued for next EN cycle | chars=" + letter.Length);
	}

	public bool HasGeneratedLetterPending
	{
		get { return !string.IsNullOrEmpty(generated_letter_override); }
	}
}
