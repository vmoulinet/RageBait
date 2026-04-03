using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Vue de dessus sur un canvas World Space.
// Point blanc = actif, rouge = brisé, bleu = à sa position triangle.
// Lignes entre partenaires triangle, avec demi-gradient depuis chaque extrémité.
public class TopDownMirrorCanvas : MonoBehaviour
{
	[Header("References")]
	public MirrorManager MirrorManager;
	public ChoreographyManager ChoreographyManager;
	public RectTransform CanvasRect;

	[Header("World Bounds (XZ)")]
	public Vector2 WorldCenter = Vector2.zero;
	public Vector2 WorldSize = new Vector2(20f, 20f);

	[Header("Dots")]
	public Sprite DotSprite;
	[Tooltip("Fraction de la largeur du canvas (ex: 0.02 = 2%)")]
	public float DotSize = 0.02f;
	public Color DotColorActive = Color.white;
	public Color DotColorBroken = Color.red;
	public Color DotColorStable = Color.blue;

	[Header("Lines")]
	public Color LineColor = new Color(1f, 1f, 1f, 0.4f);
	public Color LineColorStable = new Color(0f, 0.4f, 1f, 0.8f);
	[Tooltip("Fraction de la largeur du canvas (ex: 0.003)")]
	public float LineWidth = 0.003f;

	readonly Dictionary<MirrorActor, RectTransform> mirror_dots = new Dictionary<MirrorActor, RectTransform>();

	// Pool de demi-lignes (chaque connexion = 2 demi-lignes)
	readonly List<RectTransform> half_line_pool = new List<RectTransform>();
	int half_line_pool_used = 0;

	GameObject dots_root;
	GameObject lines_root;

	void Awake()
	{
		dots_root = new GameObject("Dots");
		dots_root.transform.SetParent(CanvasRect, false);

		lines_root = new GameObject("Lines");
		lines_root.transform.SetParent(CanvasRect, false);
		lines_root.transform.SetAsFirstSibling();
	}

	void Update()
	{
		if (MirrorManager == null || CanvasRect == null)
			return;

		SyncDots();
		UpdateDots();
		UpdateLines();
	}

	void SyncDots()
	{
		List<MirrorActor> active = MirrorManager.ActiveMirrors;

		List<MirrorActor> to_remove = null;
		foreach (MirrorActor m in mirror_dots.Keys)
		{
			if (m == null || !active.Contains(m))
			{
				if (to_remove == null) to_remove = new List<MirrorActor>();
				to_remove.Add(m);
			}
		}
		if (to_remove != null)
		{
			for (int i = 0; i < to_remove.Count; i++)
			{
				if (mirror_dots[to_remove[i]] != null)
					Destroy(mirror_dots[to_remove[i]].gameObject);
				mirror_dots.Remove(to_remove[i]);
			}
		}

		for (int i = 0; i < active.Count; i++)
		{
			MirrorActor m = active[i];
			if (m == null || mirror_dots.ContainsKey(m))
				continue;
			CreateDot(m);
		}
	}

	void CreateDot(MirrorActor mirror)
	{
		GameObject go = new GameObject("Dot_" + mirror.name);
		go.transform.SetParent(dots_root.transform, false);

		RectTransform rect = go.AddComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.zero;
		rect.pivot = new Vector2(0.5f, 0.5f);

		Image img = go.AddComponent<Image>();
		img.sprite = DotSprite;
		img.type = Image.Type.Simple;
		img.color = DotColorActive;

		mirror_dots[mirror] = rect;
	}

	void UpdateDots()
	{
		float dot_px = CanvasRect.rect.width * DotSize;

		foreach (KeyValuePair<MirrorActor, RectTransform> kvp in mirror_dots)
		{
			MirrorActor mirror = kvp.Key;
			RectTransform rect = kvp.Value;
			if (mirror == null || rect == null) continue;

			rect.sizeDelta = new Vector2(dot_px, dot_px);
			rect.anchoredPosition = WorldToCanvas(mirror.WorldPosition);

			Image img = rect.GetComponent<Image>();
			if (img == null) continue;

			if (mirror.IsBroken)
				img.color = DotColorBroken;
			else if (ChoreographyManager != null && ChoreographyManager.IsMirrorAtTrianglePosition(mirror))
				img.color = DotColorStable;
			else
				img.color = DotColorActive;
		}
	}

	void UpdateLines()
	{
		half_line_pool_used = 0;

		if (ChoreographyManager == null)
			return;

		HashSet<long> drawn_pairs = new HashSet<long>();

		foreach (KeyValuePair<MirrorActor, RectTransform> kvp in mirror_dots)
		{
			MirrorActor mirror = kvp.Key;
			if (mirror == null || mirror.IsBroken) continue;

			MirrorActor[] partners = ChoreographyManager.GetTrianglePartnersFor(mirror);
			if (partners == null) continue;

			bool mirror_stable = ChoreographyManager.IsMirrorAtTrianglePosition(mirror);

			for (int i = 0; i < partners.Length; i++)
			{
				MirrorActor partner = partners[i];
				if (partner == null || partner.IsBroken) continue;
				if (!mirror_dots.ContainsKey(partner)) continue;

				int id_a = mirror.GetInstanceID();
				int id_b = partner.GetInstanceID();
				long pair_key = id_a < id_b
					? ((long)id_a << 32) | (uint)id_b
					: ((long)id_b << 32) | (uint)id_a;

				if (!drawn_pairs.Add(pair_key)) continue;

				bool partner_stable = ChoreographyManager.IsMirrorAtTrianglePosition(partner);

				Vector2 pos_a = WorldToCanvas(mirror.WorldPosition);
				Vector2 pos_b = WorldToCanvas(partner.WorldPosition);
				Vector2 mid = (pos_a + pos_b) * 0.5f;

				Color color_a = mirror_stable ? LineColorStable : LineColor;
				Color color_b = partner_stable ? LineColorStable : LineColor;

				// Demi-ligne côté mirror (pos_a → mid)
				DrawHalfLine(pos_a, mid, color_a);
				// Demi-ligne côté partner (pos_b → mid)
				DrawHalfLine(pos_b, mid, color_b);
			}
		}

		for (int i = half_line_pool_used; i < half_line_pool.Count; i++)
		{
			if (half_line_pool[i] != null)
				half_line_pool[i].gameObject.SetActive(false);
		}
	}

	void DrawHalfLine(Vector2 from, Vector2 to, Color color)
	{
		RectTransform rect = GetOrCreateHalfLine();

		Vector2 delta = to - from;
		float length = delta.magnitude;
		float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
		float line_px = CanvasRect.rect.width * LineWidth;

		rect.anchoredPosition = (from + to) * 0.5f;
		rect.sizeDelta = new Vector2(length, line_px);
		rect.localRotation = Quaternion.Euler(0f, 0f, angle);

		Image img = rect.GetComponent<Image>();
		if (img != null)
			img.color = color;

		rect.gameObject.SetActive(true);
	}

	RectTransform GetOrCreateHalfLine()
	{
		if (half_line_pool_used < half_line_pool.Count)
			return half_line_pool[half_line_pool_used++];

		GameObject go = new GameObject("HalfLine");
		go.transform.SetParent(lines_root.transform, false);

		RectTransform rect = go.AddComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.zero;
		rect.pivot = new Vector2(0.5f, 0.5f);

		Image img = go.AddComponent<Image>();
		img.color = LineColor;

		half_line_pool.Add(rect);
		half_line_pool_used++;
		return rect;
	}

	Vector2 WorldToCanvas(Vector3 world_pos)
	{
		float t_x = Mathf.InverseLerp(WorldCenter.x - WorldSize.x * 0.5f, WorldCenter.x + WorldSize.x * 0.5f, world_pos.x);
		float t_z = Mathf.InverseLerp(WorldCenter.y - WorldSize.y * 0.5f, WorldCenter.y + WorldSize.y * 0.5f, world_pos.z);

		Vector2 canvas_size = CanvasRect.rect.size;
		return new Vector2((t_x - 0.5f) * canvas_size.x, (t_z - 0.5f) * canvas_size.y);
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
		Vector3 center = new Vector3(WorldCenter.x, 0f, WorldCenter.y);
		Gizmos.DrawWireCube(center, new Vector3(WorldSize.x, 0.1f, WorldSize.y));
	}
}
