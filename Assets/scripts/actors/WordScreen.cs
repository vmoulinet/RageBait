using TMPro;
using UnityEngine;

public class WordScreen : MonoBehaviour
{
	[Header("References")]
	public WordManager WordManager;
	public Renderer DisplayRenderer;
	public TextMeshPro DisplayText;
	public TextMeshPro DisplayCross;

	[Header("Slot")]
	public int SlotIndex = 0;

	[Header("Colors")]
	public Color WordSurfaceColor = Color.black;
	public Color WordEmissionColor = Color.black;
	public Color BrokenSurfaceColor = new Color(0.15f, 0f, 0f);
	public Color BrokenEmissionColor = Color.red;
	public Color WordTextColor = Color.white;
	public Color BrokenTextColor = Color.red;

	[Header("Refresh")]
	public bool RefreshEveryFrame = true;
	public float RefreshInterval = 0.1f;

	MaterialPropertyBlock property_block;
	float refresh_timer = 0f;

	static readonly int base_color_id = Shader.PropertyToID("_BaseColor");
	static readonly int emissive_color_id = Shader.PropertyToID("_EmissiveColor");

	void Awake()
	{
		property_block = new MaterialPropertyBlock();
		RefreshVisual();
	}

	void OnEnable()
	{
		if (property_block == null)
			property_block = new MaterialPropertyBlock();
	}

	void Update()
	{
		if (RefreshEveryFrame)
		{
			RefreshVisual();
			return;
		}

		refresh_timer += Time.deltaTime;
		if (refresh_timer >= RefreshInterval)
		{
			refresh_timer = 0f;
			RefreshVisual();
		}
	}

	public void RefreshVisual()
	{
		if (WordManager == null)
			return;

		bool is_pending = WordManager.IsSlotPending(SlotIndex);
		string word = WordManager.GetWordForSlot(SlotIndex);

		if (is_pending)
		{
			ShowBrokenState();
			return;
		}

		if (!string.IsNullOrEmpty(word))
		{
			ShowWordState(word);
			return;
		}

		ShowEmptyState();
	}

	void ShowWordState(string word)
	{
		if (DisplayText != null)
		{
			DisplayText.gameObject.SetActive(true);
			DisplayText.text = word.ToUpperInvariant();
			DisplayText.color = WordTextColor;
		}

		if (DisplayCross != null)
			DisplayCross.gameObject.SetActive(false);

		ApplyDisplayColors(WordSurfaceColor, WordEmissionColor);
	}

	void ShowBrokenState()
	{
		if (DisplayText != null)
			DisplayText.gameObject.SetActive(false);

		if (DisplayCross != null)
		{
			DisplayCross.gameObject.SetActive(true);
			DisplayCross.text = "";
			DisplayCross.color = BrokenTextColor;
		}

		ApplyDisplayColors(BrokenSurfaceColor, BrokenEmissionColor);
	}

	void ShowEmptyState()
	{
		if (DisplayText != null)
		{
			DisplayText.gameObject.SetActive(true);
			DisplayText.text = "";
		}

		if (DisplayCross != null)
			DisplayCross.gameObject.SetActive(false);

		ApplyDisplayColors(WordSurfaceColor, WordEmissionColor);
	}

	void ApplyDisplayColors(Color base_color, Color emission_color)
	{
		if (DisplayRenderer == null)
			return;

		DisplayRenderer.GetPropertyBlock(property_block);
		property_block.SetColor(base_color_id, base_color);
		property_block.SetColor(emissive_color_id, emission_color);
		DisplayRenderer.SetPropertyBlock(property_block);
	}
}