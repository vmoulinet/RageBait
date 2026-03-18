using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WordManager : MonoBehaviour
{
	[Header("References")]
	public MirrorManager MirrorManager;
	public TextAsset WordListAsset;

	[Header("Stage")]
	public int WordSlotCount = 7;

	[Header("Selection")]
	public int GlobalRecentHistorySize = 32;

	[Header("Debug")]
	public bool DebugWords = true;

	[Header("Broken Delay")]
	public bool UseBrokenDelay = true;
	public float BrokenDisplayDelay = 2f;

	readonly List<string> all_words = new List<string>();
	readonly List<MirrorActor> tracked_mirrors = new List<MirrorActor>();

	readonly Dictionary<MirrorActor, int> mirror_to_slot = new Dictionary<MirrorActor, int>();
	readonly Dictionary<MirrorActor, int> mirror_to_word_index = new Dictionary<MirrorActor, int>();

	readonly HashSet<MirrorActor> pending_mirrors = new HashSet<MirrorActor>();
	readonly Dictionary<MirrorActor, Coroutine> pending_reroll_coroutines = new Dictionary<MirrorActor, Coroutine>();

	readonly Queue<int> recent_word_indices = new Queue<int>();
	readonly HashSet<int> recent_word_lookup = new HashSet<int>();

	bool is_initialized = false;
	bool is_bootstrapped = false;

	public void Initialize(SimulationManager sim)
	{
		if (MirrorManager == null && sim != null)
			MirrorManager = sim.MirrorManager;

		LoadWordList();
		ValidateWordList();

		is_initialized = true;
	}

	public void BootstrapWords()
	{
		if (!is_initialized)
		{
			Debug.LogError("[word_manager] cannot bootstrap before initialize");
			return;
		}

		if (MirrorManager == null)
		{
			Debug.LogError("[word_manager] MirrorManager is missing");
			return;
		}

		List<MirrorActor> active_mirrors = GetEligibleMirrors();
		for (int i = 0; i < active_mirrors.Count; i++)
			RegisterMirror(active_mirrors[i]);
	}

	public void RegisterMirror(MirrorActor mirror)
	{
		if (!is_initialized || mirror == null)
			return;

		if (mirror_to_slot.ContainsKey(mirror))
		{
			is_bootstrapped = true;
			LogStageWordsLine();
			return;
		}

		int slot = GetNextFreeSlot();
		if (slot < 0)
		{
			if (DebugWords)
			{
				Debug.Log(
					"[word_manager] register skipped | mirror=" + mirror.name +
					" | no free slot available"
				);
			}
			return;
		}

		tracked_mirrors.Add(mirror);
		mirror_to_slot[mirror] = slot;

		int word_index = PickWordIndex(mirror, -1);
		if (word_index < 0)
		{
			Debug.LogError("[word_manager] failed to pick initial word for mirror " + mirror.name);
			return;
		}

		AssignWordToMirror(mirror, word_index, add_to_recent: true);
		is_bootstrapped = true;

		if (DebugWords)
		{
			Debug.Log(
				"[word_manager] bind | mirror=" + mirror.name +
				" | slot=" + slot +
				" | word=" + all_words[word_index]
			);
		}

		LogActiveWords();
		LogStageWordsLine();
	}

	public void OnMirrorBroken(MirrorActor mirror)
	{
		if (!is_bootstrapped || mirror == null)
			return;

		if (!mirror_to_slot.ContainsKey(mirror))
			return;

		if (pending_reroll_coroutines.TryGetValue(mirror, out Coroutine existing_coroutine))
		{
			if (existing_coroutine != null)
				StopCoroutine(existing_coroutine);

			pending_reroll_coroutines.Remove(mirror);
		}

		if (UseBrokenDelay)
		{
			pending_mirrors.Add(mirror);
			LogStageWordsLine();

			Coroutine reroll_coroutine = StartCoroutine(RerollMirrorWordDelayed(mirror));
			pending_reroll_coroutines[mirror] = reroll_coroutine;
			return;
		}

		RerollMirrorWordNow(mirror);
	}

	IEnumerator RerollMirrorWordDelayed(MirrorActor mirror)
	{
		yield return new WaitForSeconds(BrokenDisplayDelay);

		pending_reroll_coroutines.Remove(mirror);
		pending_mirrors.Remove(mirror);
		RerollMirrorWordNow(mirror);
	}

	void RerollMirrorWordNow(MirrorActor mirror)
	{
		if (!mirror_to_slot.ContainsKey(mirror))
			return;

		if (!mirror_to_word_index.TryGetValue(mirror, out int old_word_index))
			return;

		int new_word_index = PickWordIndex(mirror, old_word_index);
		if (new_word_index < 0)
		{
			Debug.LogWarning("[word_manager] no valid reroll found for mirror " + mirror.name);
			LogStageWordsLine();
			return;
		}

		AssignWordToMirror(mirror, new_word_index, add_to_recent: true);

		if (DebugWords)
		{
			Debug.Log(
				"[word_manager] reroll | mirror=" + mirror.name +
				" | slot=" + mirror_to_slot[mirror] +
				" | old=" + all_words[old_word_index] +
				" | new=" + all_words[new_word_index]
			);
		}

		LogStageWordsLine();
	}

	public string GetWordForMirror(MirrorActor mirror)
	{
		if (mirror == null)
			return string.Empty;

		if (!mirror_to_word_index.TryGetValue(mirror, out int word_index))
			return string.Empty;

		if (word_index < 0 || word_index >= all_words.Count)
			return string.Empty;

		return all_words[word_index];
	}

	public string GetWordForSlot(int slot)
	{
		MirrorActor mirror = GetMirrorForSlot(slot);
		if (mirror == null)
			return string.Empty;

		return GetWordForMirror(mirror);
	}

	public int GetSlotForMirror(MirrorActor mirror)
	{
		if (mirror == null)
			return -1;

		if (mirror_to_slot.TryGetValue(mirror, out int slot))
			return slot;

		return -1;
	}

	public bool IsSlotPending(int slot)
	{
		MirrorActor mirror = GetMirrorForSlot(slot);
		if (mirror == null)
			return false;

		return pending_mirrors.Contains(mirror);
	}

	public void LogActiveWords()
	{
		if (!DebugWords)
			return;

		for (int i = 0; i < tracked_mirrors.Count; i++)
		{
			MirrorActor mirror = tracked_mirrors[i];
			if (mirror == null)
				continue;

			string word = GetWordForMirror(mirror);
			int slot = GetSlotForMirror(mirror);

			Debug.Log(
				"[word_manager] active | slot=" + slot +
				" | mirror=" + mirror.name +
				" | word=" + word
			);
		}
	}

	public void LogStageWordsLine()
	{
		if (!DebugWords)
			return;

		List<string> parts = new List<string>();

		for (int slot = 0; slot < WordSlotCount; slot++)
		{
			MirrorActor mirror = GetMirrorForSlot(slot);
			if (mirror == null)
			{
				parts.Add("[ ]");
				continue;
			}

			if (pending_mirrors.Contains(mirror))
			{
				parts.Add("[X]");
				continue;
			}

			string word = GetWordForMirror(mirror);
			if (string.IsNullOrEmpty(word))
				parts.Add("[ ]");
			else
				parts.Add("[" + word.ToUpperInvariant() + "]");
		}

		Debug.Log("[word_manager] stage " + string.Join(" ", parts));
	}

	MirrorActor GetMirrorForSlot(int slot)
	{
		for (int i = 0; i < tracked_mirrors.Count; i++)
		{
			MirrorActor mirror = tracked_mirrors[i];
			if (mirror == null)
				continue;

			if (mirror_to_slot.TryGetValue(mirror, out int mirror_slot) && mirror_slot == slot)
				return mirror;
		}

		return null;
	}

	int GetNextFreeSlot()
	{
		for (int slot = 0; slot < WordSlotCount; slot++)
		{
			if (GetMirrorForSlot(slot) == null)
				return slot;
		}

		return -1;
	}

	void LoadWordList()
	{
		all_words.Clear();

		if (WordListAsset == null)
		{
			Debug.LogError("[word_manager] WordListAsset is missing");
			return;
		}

		string[] lines = WordListAsset.text.Split('\n');

		for (int i = 0; i < lines.Length; i++)
		{
			string word = lines[i].Trim().ToLowerInvariant();

			if (string.IsNullOrEmpty(word))
				continue;

			all_words.Add(word);
		}

		if (DebugWords)
			Debug.Log("[word_manager] loaded words=" + all_words.Count);
	}

	void ValidateWordList()
	{
		if (all_words.Count == 0)
		{
			Debug.LogError("[word_manager] word list is empty");
			return;
		}

		HashSet<string> unique_words = new HashSet<string>();
		HashSet<string> unique_prefixes = new HashSet<string>();

		for (int i = 0; i < all_words.Count; i++)
		{
			string word = all_words[i];

			if (!unique_words.Add(word))
				Debug.LogWarning("[word_manager] duplicate word found: " + word);

			if (i > 0 && string.CompareOrdinal(all_words[i - 1], word) > 0)
				Debug.LogWarning("[word_manager] word list is not sorted at index " + i + ": " + word);

			if (word.Length >= 4)
			{
				string prefix = word.Substring(0, 4);
				if (!unique_prefixes.Add(prefix))
					Debug.LogWarning("[word_manager] duplicate 4-letter prefix found: " + prefix);
			}
		}

		if (DebugWords)
		{
			Debug.Log(
				"[word_manager] validate | count=" + all_words.Count +
				" | unique=" + unique_words.Count
			);
		}
	}

	List<MirrorActor> GetEligibleMirrors()
	{
		List<MirrorActor> result = new List<MirrorActor>();

		if (MirrorManager == null || MirrorManager.ActiveMirrors == null)
			return result;

		for (int i = 0; i < MirrorManager.ActiveMirrors.Count; i++)
		{
			MirrorActor mirror = MirrorManager.ActiveMirrors[i];

			if (mirror == null || mirror.IsBroken || !mirror.gameObject.activeInHierarchy)
				continue;

			result.Add(mirror);
		}

		return result;
	}

	void AssignWordToMirror(MirrorActor mirror, int word_index, bool add_to_recent)
	{
		mirror_to_word_index[mirror] = word_index;

		if (add_to_recent)
			PushRecentWord(word_index);
	}

	void PushRecentWord(int word_index)
	{
		if (GlobalRecentHistorySize <= 0)
			return;

		recent_word_indices.Enqueue(word_index);
		recent_word_lookup.Add(word_index);

		while (recent_word_indices.Count > GlobalRecentHistorySize)
		{
			int removed = recent_word_indices.Dequeue();

			bool still_present = false;
			foreach (int existing in recent_word_indices)
			{
				if (existing == removed)
				{
					still_present = true;
					break;
				}
			}

			if (!still_present)
				recent_word_lookup.Remove(removed);
		}
	}

	int PickWordIndex(MirrorActor mirror, int current_word_index)
	{
		List<int> strict_candidates = new List<int>();
		List<int> fallback_candidates = new List<int>();

		HashSet<int> active_word_indices = GetActiveWordIndicesExcludingMirror(mirror);

		for (int i = 0; i < all_words.Count; i++)
		{
			if (i == current_word_index)
				continue;

			if (active_word_indices.Contains(i))
				continue;

			fallback_candidates.Add(i);

			if (!recent_word_lookup.Contains(i))
				strict_candidates.Add(i);
		}

		if (strict_candidates.Count > 0)
			return strict_candidates[Random.Range(0, strict_candidates.Count)];

		if (fallback_candidates.Count > 0)
			return fallback_candidates[Random.Range(0, fallback_candidates.Count)];

		return -1;
	}

	HashSet<int> GetActiveWordIndicesExcludingMirror(MirrorActor excluded_mirror)
	{
		HashSet<int> result = new HashSet<int>();

		foreach (KeyValuePair<MirrorActor, int> kvp in mirror_to_word_index)
		{
			if (kvp.Key == null || kvp.Key == excluded_mirror)
				continue;

			result.Add(kvp.Value);
		}

		return result;
	}
}