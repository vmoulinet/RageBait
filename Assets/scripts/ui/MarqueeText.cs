using System.Text;
using TMPro;
using UnityEngine;

public class MarqueeText : MonoBehaviour
{
	public RectTransform Viewport;
	public RectTransform Content;
	public float Speed = 100f;
	public float Gap = 60f;
	[Min(2)] public int MinimumRepeats = 3;

	TMP_Text text_component;
	string source_text = "";
	float viewport_width = 0f;
	float cycle_width = 0f;
	bool layout_dirty = true;

	void Start()
	{
		RebuildIfNeeded(true);
	}

	void OnEnable()
	{
		layout_dirty = true;
	}

	void Update()
	{
		RebuildIfNeeded(false);

		if (Content == null || cycle_width <= 0f)
			return;

		Vector2 pos = Content.anchoredPosition;
		pos.x -= Speed * Time.unscaledDeltaTime;

		while (pos.x <= -cycle_width)
			pos.x += cycle_width;

		Content.anchoredPosition = pos;
	}

	public void SetText(string new_text)
	{
		CacheReferences();

		if (text_component == null)
			return;

		source_text = string.IsNullOrWhiteSpace(new_text) ? " " : new_text.Trim();
		layout_dirty = true;
	}

	void RebuildIfNeeded(bool force_reset_position)
	{
		if (!layout_dirty && !force_reset_position)
			return;

		CacheReferences();

		if (Viewport == null || Content == null || text_component == null)
			return;

		viewport_width = Viewport.rect.width;
		string base_text = string.IsNullOrWhiteSpace(source_text) ? text_component.text : source_text;
		base_text = string.IsNullOrWhiteSpace(base_text) ? " " : base_text.Trim();
		source_text = base_text;

		string separator = BuildSeparator();
		string cycle_text = source_text + separator;
		cycle_width = Mathf.Max(1f, text_component.GetPreferredValues(cycle_text).x);

		int repeat_count = Mathf.Max(
			MinimumRepeats,
			Mathf.CeilToInt((viewport_width * 2f) / cycle_width) + 2
		);

		StringBuilder builder = new StringBuilder(cycle_text.Length * repeat_count);
		for (int i = 0; i < repeat_count; i++)
			builder.Append(cycle_text);

		text_component.textWrappingMode = TextWrappingModes.NoWrap;
		text_component.overflowMode = TextOverflowModes.Overflow;
		text_component.text = builder.ToString();
		text_component.ForceMeshUpdate();

		if (force_reset_position || layout_dirty)
			Content.anchoredPosition = new Vector2(0f, Content.anchoredPosition.y);

		layout_dirty = false;
	}

	void CacheReferences()
	{
		if (Content == null)
			return;

		if (text_component == null)
			text_component = Content.GetComponent<TMP_Text>();
	}

	string BuildSeparator()
	{
		float space_width = Mathf.Max(1f, GetSpaceWidth());
		int space_count = Mathf.Max(1, Mathf.CeilToInt(Gap / space_width));
		return new string(' ', space_count);
	}

	float GetSpaceWidth()
	{
		if (text_component == null)
			return 16f;

		return Mathf.Max(1f, text_component.GetPreferredValues(" ").x);
	}

	void OnRectTransformDimensionsChange()
	{
		layout_dirty = true;
	}

	#if UNITY_EDITOR
	void OnValidate()
	{
		layout_dirty = true;
	}
	#endif
}