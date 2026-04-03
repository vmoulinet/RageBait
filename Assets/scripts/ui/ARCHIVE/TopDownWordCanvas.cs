using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Vue de dessus : reproduit les positions XZ des miroirs sur un canvas UI.
// Les mots suivent les miroirs en temps réel.
// Pour revenir à l'ancien système (WordScreen), désactive simplement ce GameObject
// et réactive les objets WordScreen dans la scène.
public class TopDownWordCanvas : MonoBehaviour
{
	[Header("References")]
	public WordManager WordManager;
	public MirrorManager MirrorManager;
	public RectTransform CanvasRect;

	[Header("World Bounds (top-down XZ)")]
	public Vector2 WorldCenter = Vector2.zero;
	public Vector2 WorldSize = new Vector2(20f, 20f);

	[Header("Word Visuals")]
	public TMP_FontAsset Font;
	public float FontSize = 36f;
	public Color WordColor = Color.white;
	public Color BrokenColor = Color.red;

	[Header("Smoothing")]
	public float PositionSmoothSpeed = 8f;

	[Header("Overlap Prevention")]
	public int SeparationIterations = 5;
	public float SeparationMaxPushPerIter = 4f;

	readonly Dictionary<MirrorActor, RectTransform> mirror_to_label = new Dictionary<MirrorActor, RectTransform>();
	readonly Dictionary<MirrorActor, Vector2> mirror_smoothed_pos = new Dictionary<MirrorActor, Vector2>();
	readonly List<MirrorActor> mirror_list = new List<MirrorActor>();

	void Update()
	{
		if (WordManager == null || MirrorManager == null || CanvasRect == null)
		{
			Debug.LogWarning("[top_down_canvas] missing ref | WordManager=" + (WordManager != null) + " | MirrorManager=" + (MirrorManager != null) + " | CanvasRect=" + (CanvasRect != null));
			return;
		}

		SyncLabels();
		UpdateLabelPositions();
		ResolveSeparation();
	}

	void SyncLabels()
	{
		List<MirrorActor> active = MirrorManager.ActiveMirrors;

		// Supprime les labels des miroirs qui n'existent plus
		List<MirrorActor> to_remove = null;
		foreach (MirrorActor mirror in mirror_to_label.Keys)
		{
			if (mirror == null || !active.Contains(mirror))
			{
				if (to_remove == null) to_remove = new List<MirrorActor>();
				to_remove.Add(mirror);
			}
		}

		if (to_remove != null)
		{
			for (int i = 0; i < to_remove.Count; i++)
			{
				MirrorActor m = to_remove[i];
				if (mirror_to_label[m] != null)
					Destroy(mirror_to_label[m].gameObject);
				mirror_to_label.Remove(m);
				mirror_smoothed_pos.Remove(m);
			}
		}

		// Crée les labels pour les nouveaux miroirs
		for (int i = 0; i < active.Count; i++)
		{
			MirrorActor mirror = active[i];
			if (mirror == null)
				continue;

			if (!mirror_to_label.ContainsKey(mirror))
				CreateLabel(mirror);
		}
	}

	void CreateLabel(MirrorActor mirror)
	{
		Debug.Log("[top_down_canvas] create label | mirror=" + mirror.name + " | canvas_rect_size=" + CanvasRect.rect.size);

		GameObject go = new GameObject("WordLabel_" + mirror.name);
		go.transform.SetParent(CanvasRect, false);

		RectTransform rect = go.AddComponent<RectTransform>();
		rect.sizeDelta = new Vector2(300f, 80f);
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.zero;
		rect.pivot = new Vector2(0.5f, 0.5f);

		TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
		text.alignment = TextAlignmentOptions.Center;
		text.fontSize = FontSize;
		text.color = WordColor;

		if (Font != null)
			text.font = Font;

		mirror_to_label[mirror] = rect;

		// Position initiale immédiate (pas de lerp au premier frame)
		Vector2 canvas_pos = WorldToCanvas(mirror.WorldPosition);
		mirror_smoothed_pos[mirror] = canvas_pos;
		rect.anchoredPosition = canvas_pos;
	}

	void UpdateLabelPositions()
	{
		foreach (KeyValuePair<MirrorActor, RectTransform> kvp in mirror_to_label)
		{
			MirrorActor mirror = kvp.Key;
			RectTransform rect = kvp.Value;

			if (mirror == null || rect == null)
				continue;

			// Texte et couleur
			TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
			if (text != null)
			{
				int slot = WordManager.GetSlotForMirror(mirror);
				bool is_pending = slot >= 0 && WordManager.IsSlotPending(slot);
				string word = WordManager.GetWordForMirror(mirror);

				if (is_pending)
				{
					text.text = "X";
					text.color = BrokenColor;
				}
				else if (!string.IsNullOrEmpty(word))
				{
					text.text = word.ToUpperInvariant();
					text.color = WordColor;
				}
				else
				{
					text.text = "";
				}
			}

			// Position lissée
			Vector2 target = WorldToCanvas(mirror.WorldPosition);

			if (!mirror_smoothed_pos.TryGetValue(mirror, out Vector2 current))
				current = target;

			Vector2 smoothed = Vector2.Lerp(current, target, 1f - Mathf.Exp(-PositionSmoothSpeed * Time.deltaTime));
			mirror_smoothed_pos[mirror] = smoothed;
			rect.anchoredPosition = smoothed;
		}
	}

	void ResolveSeparation()
	{
		mirror_list.Clear();
		foreach (MirrorActor m in mirror_to_label.Keys)
		{
			if (m != null && mirror_to_label[m] != null)
				mirror_list.Add(m);
		}

		for (int iter = 0; iter < SeparationIterations; iter++)
		{
			for (int i = 0; i < mirror_list.Count; i++)
			{
				for (int j = i + 1; j < mirror_list.Count; j++)
				{
					MirrorActor ma = mirror_list[i];
					MirrorActor mb = mirror_list[j];

					RectTransform rect_a = mirror_to_label[ma];
					RectTransform rect_b = mirror_to_label[mb];

					Vector2 size_a = rect_a.sizeDelta;
					Vector2 size_b = rect_b.sizeDelta;

					float min_dist_x = (size_a.x + size_b.x) * 0.5f;
					float min_dist_y = (size_a.y + size_b.y) * 0.5f;

					// Travaille sur les positions lissées, pas sur anchoredPosition
					Vector2 pos_a = mirror_smoothed_pos[ma];
					Vector2 pos_b = mirror_smoothed_pos[mb];
					Vector2 delta = pos_a - pos_b;

					float overlap_x = min_dist_x - Mathf.Abs(delta.x);
					float overlap_y = min_dist_y - Mathf.Abs(delta.y);

					if (overlap_x <= 0f || overlap_y <= 0f)
						continue;

					Vector2 push;
					if (overlap_x < overlap_y)
						push = new Vector2(Mathf.Min(overlap_x * 0.5f, SeparationMaxPushPerIter) * Mathf.Sign(delta.x == 0f ? 1f : delta.x), 0f);
					else
						push = new Vector2(0f, Mathf.Min(overlap_y * 0.5f, SeparationMaxPushPerIter) * Mathf.Sign(delta.y == 0f ? 1f : delta.y));

					mirror_smoothed_pos[ma] = pos_a + push;
					mirror_smoothed_pos[mb] = pos_b - push;

					rect_a.anchoredPosition = mirror_smoothed_pos[ma];
					rect_b.anchoredPosition = mirror_smoothed_pos[mb];
				}
			}
		}
	}

	// Convertit une position monde (XZ) en position anchorée sur le canvas (pivot centre)
	Vector2 WorldToCanvas(Vector3 world_pos)
	{
		float half_w = WorldSize.x * 0.5f;
		float half_h = WorldSize.y * 0.5f;

		float t_x = Mathf.InverseLerp(WorldCenter.x - half_w, WorldCenter.x + half_w, world_pos.x);
		float t_z = Mathf.InverseLerp(WorldCenter.y - half_h, WorldCenter.y + half_h, world_pos.z);

		Vector2 canvas_size = CanvasRect.rect.size;
		return new Vector2(
			(t_x - 0.5f) * canvas_size.x,
			(t_z - 0.5f) * canvas_size.y
		);
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
		Vector3 center = new Vector3(WorldCenter.x, 0f, WorldCenter.y);
		Gizmos.DrawWireCube(center, new Vector3(WorldSize.x, 0.1f, WorldSize.y));
	}
}
