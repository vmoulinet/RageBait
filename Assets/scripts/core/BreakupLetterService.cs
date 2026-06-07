using System.Collections.Generic;
using UnityEngine;

// Orchestrates the Nephila breakup-letter flow for Chase:
//   1. Mirrors break -> DaddyLetterProjector redacts words.
//   2. Letter finishes typing (EN cycle) -> we collect the redacted words.
//   3. Send them to the Nephila move (server-side LLM via OpenRouter).
//   4. When the generated letter comes back, queue it on the projector so it
//      replaces the whole letter on the NEXT EN write cycle.
//
// Generation is fired as soon as the letter finishes (pre-generation): the LLM
// call runs while the letter holds/erases, so the result is usually ready by the
// next rewrite. If it isn't, the projector simply shows the original letter and
// we try again next time.
[RequireComponent(typeof(NephilaClient))]
public class BreakupLetterService : MonoBehaviour
{
	[Header("References")]
	public DaddyLetterProjector DaddyLetterProjector;
	public NephilaClient NephilaClient;
	[Tooltip("Optional: also generate a letter when a WorldValidation event triggers.")]
	public WorldValidation WorldValidation;

	[Header("Behaviour")]
	[Tooltip("Minimum number of redacted words required before we bother calling Nephila.")]
	public int MinRedactedWords = 1;
	[Tooltip("If true, don't start a new generation while one is already in flight.")]
	public bool SkipIfRequestInFlight = true;
	[Tooltip("How many generated letters to keep ready ahead of time. A trigger only " +
		"generates when the projector's queue holds fewer than this. 1 = one letter in reserve.")]
	public int BufferSize = 1;

	[Header("Debug")]
	public bool DebugLog = false;
	[Tooltip("Press this key in Play mode to force a generation on demand (skips the " +
		"in-flight/pending guards). Uses the current redacted words, or TestWords if none.")]
	public KeyCode TestKey = KeyCode.U;
	[Tooltip("Fallback words used by the test key when no word is currently redacted.")]
	public string TestWords = "money, shame, leave, mine, quiet, enough, father, forgive";

	bool request_in_flight = false;

	void Reset()
	{
		NephilaClient = GetComponent<NephilaClient>();
	}

	void Awake()
	{
		if (NephilaClient == null)
			NephilaClient = GetComponent<NephilaClient>();
	}

	void OnEnable()
	{
		if (DaddyLetterProjector != null)
			DaddyLetterProjector.OnLetterFinishedTyping += HandleLetterFinished;

		if (WorldValidation != null)
			WorldValidation.OnTriggered += HandleWorldValidation;
	}

	void OnDisable()
	{
		if (DaddyLetterProjector != null)
			DaddyLetterProjector.OnLetterFinishedTyping -= HandleLetterFinished;

		if (WorldValidation != null)
			WorldValidation.OnTriggered -= HandleWorldValidation;
	}

	// WorldValidation fired: same generation logic as the end-of-typing trigger.
	void HandleWorldValidation()
	{
		if (DebugLog)
			Debug.Log("[breakup] WorldValidation triggered -> generate");
		HandleLetterFinished();
	}

	void Update()
	{
		if (TestKey != KeyCode.None && Input.GetKeyDown(TestKey))
			ForceGenerateNow();
	}

	// Manual trigger for debugging: generate a letter immediately without waiting
	// for the write cycle, and without the in-flight / pending guards. Uses the
	// words currently redacted, or TestWords as a fallback.
	[ContextMenu("Force Generate Now")]
	public void ForceGenerateNow()
	{
		if (NephilaClient == null)
		{
			Debug.LogWarning("[breakup] test ; no NephilaClient");
			return;
		}

		if (!NephilaClient.IsConfigured)
		{
			Debug.LogWarning("[breakup] test ; Nephila not configured (check StreamingAssets/nephila_config.json)");
			return;
		}

		List<string> words = DaddyLetterProjector != null ? DaddyLetterProjector.GetRedactedWords() : null;
		string csv = (words != null && words.Count > 0) ? string.Join(", ", words) : TestWords;

		Debug.Log("[breakup] TEST generate (key=" + TestKey + ") | words=" + csv);

		request_in_flight = true;
		NephilaClient.RunBreakupLetter(csv, OnTestLetterGenerated, OnGenerationFailed);
	}

	void OnTestLetterGenerated(string letter)
	{
		request_in_flight = false;

		// Log the whole letter so it's verifiable in the Console, and still queue
		// it on the projector so a later EN cycle shows it.
		Debug.Log("[breakup] TEST letter received (" + letter.Length + " chars):\n" + letter);

		if (DaddyLetterProjector != null)
			DaddyLetterProjector.EnqueueGeneratedLetter(letter);
	}

	void HandleLetterFinished()
	{
		if (DaddyLetterProjector == null || NephilaClient == null)
			return;

		if (!NephilaClient.IsConfigured)
		{
			if (DebugLog)
				Debug.Log("[breakup] skip ; Nephila not configured");
			return;
		}

		if (SkipIfRequestInFlight && request_in_flight)
		{
			if (DebugLog)
				Debug.Log("[breakup] skip ; request already in flight");
			return;
		}

		// Only generate if the reserve buffer isn't already full.
		if (DaddyLetterProjector.QueuedLetterCount >= Mathf.Max(1, BufferSize))
		{
			if (DebugLog)
				Debug.Log("[breakup] skip ; buffer full (" + DaddyLetterProjector.QueuedLetterCount + ")");
			return;
		}

		List<string> words = DaddyLetterProjector.GetRedactedWords();
		if (words == null || words.Count < Mathf.Max(1, MinRedactedWords))
		{
			if (DebugLog)
				Debug.Log("[breakup] skip ; not enough redacted words (" + (words == null ? 0 : words.Count) + ")");
			return;
		}

		string csv = string.Join(", ", words);

		if (DebugLog)
			Debug.Log("[breakup] requesting letter | words=" + csv);

		request_in_flight = true;
		NephilaClient.RunBreakupLetter(csv, OnLetterGenerated, OnGenerationFailed);
	}

	void OnLetterGenerated(string letter)
	{
		request_in_flight = false;

		if (DaddyLetterProjector == null)
			return;

		DaddyLetterProjector.EnqueueGeneratedLetter(letter);

		if (DebugLog)
			Debug.Log("[breakup] letter ready, enqueued | chars=" + letter.Length
				+ " | buffer=" + DaddyLetterProjector.QueuedLetterCount);
	}

	void OnGenerationFailed(string error)
	{
		request_in_flight = false;
		Debug.LogWarning("[breakup] generation failed: " + error + " ; keeping original letter");
	}
}
