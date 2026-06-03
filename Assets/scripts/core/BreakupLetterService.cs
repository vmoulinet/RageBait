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

	[Header("Behaviour")]
	[Tooltip("Minimum number of redacted words required before we bother calling Nephila.")]
	public int MinRedactedWords = 1;
	[Tooltip("If true, don't start a new generation while one is already in flight.")]
	public bool SkipIfRequestInFlight = true;
	[Tooltip("If true, don't regenerate while a letter is already queued for the next cycle.")]
	public bool SkipIfLetterPending = true;

	[Header("Debug")]
	public bool DebugLog = false;

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
	}

	void OnDisable()
	{
		if (DaddyLetterProjector != null)
			DaddyLetterProjector.OnLetterFinishedTyping -= HandleLetterFinished;
	}

	void HandleLetterFinished()
	{
		if (DaddyLetterProjector == null || NephilaClient == null)
			return;

		if (!NephilaClient.IsConfigured)
		{
			if (DebugLog)
				Debug.Log("[breakup] skip — Nephila not configured");
			return;
		}

		if (SkipIfRequestInFlight && request_in_flight)
		{
			if (DebugLog)
				Debug.Log("[breakup] skip — request already in flight");
			return;
		}

		if (SkipIfLetterPending && DaddyLetterProjector.HasGeneratedLetterPending)
		{
			if (DebugLog)
				Debug.Log("[breakup] skip — a generated letter is already queued");
			return;
		}

		List<string> words = DaddyLetterProjector.GetRedactedWords();
		if (words == null || words.Count < Mathf.Max(1, MinRedactedWords))
		{
			if (DebugLog)
				Debug.Log("[breakup] skip — not enough redacted words (" + (words == null ? 0 : words.Count) + ")");
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

		DaddyLetterProjector.SetGeneratedLetterOverride(letter);

		if (DebugLog)
			Debug.Log("[breakup] letter ready, queued for next EN cycle | chars=" + letter.Length);
	}

	void OnGenerationFailed(string error)
	{
		request_in_flight = false;
		Debug.LogWarning("[breakup] generation failed: " + error + " — keeping original letter");
	}
}
