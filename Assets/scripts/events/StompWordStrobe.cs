using System.Collections;
using TMPro;
using UnityEngine;

// Affiche les mots DADDY / LOVES / YOU / TOO un par un (une seule passe), en
// noir, plein ecran par-dessus la video a chaque stomp. Chaque mot apparait une
// fois, separe par un blank. L'overlay (Canvas + TextMeshProUGUI) est cree au
// runtime pour rester au-dessus de tout et ne dependre d'aucun objet de scene.
public class StompWordStrobe : MonoBehaviour
{
	[Header("Words")]
	public string[] Words = new string[] { "DADDY", "LOVES", "YOU", "TOO" };

	[Header("Timing")]
	[Tooltip("Duree d'affichage de chaque mot, en secondes (temps reel).")]
	public float WordDuration = 0.06f;
	[Tooltip("Duree du blank entre deux mots, en secondes (temps reel).")]
	public float BlankDuration = 0.04f;

	[Header("Look")]
	public Color TextColor = Color.black;
	[Tooltip("Hauteur de la police en proportion de la hauteur d'ecran (1 = plein ecran).")]
	[Range(0.1f, 1f)]
	public float FontHeightFraction = 0.55f;
	public TMP_FontAsset Font;

	[Tooltip("Ordre de tri du Canvas overlay. Doit etre superieur aux autres canvas pour passer devant la video.")]
	public int SortingOrder = 32000;

	Canvas canvas;
	CanvasGroup canvas_group;
	TextMeshProUGUI label;
	Coroutine strobe_routine;

	void Awake()
	{
		Build_overlay();
		Set_visible(false);
	}

	void Build_overlay()
	{
		GameObject canvas_go = new GameObject("StompWordStrobe_Canvas");
		canvas_go.transform.SetParent(transform, false);

		canvas = canvas_go.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = SortingOrder;

		canvas_group = canvas_go.AddComponent<CanvasGroup>();
		canvas_group.interactable = false;
		canvas_group.blocksRaycasts = false;

		GameObject label_go = new GameObject("Label");
		label_go.transform.SetParent(canvas_go.transform, false);

		label = label_go.AddComponent<TextMeshProUGUI>();
		label.alignment = TextAlignmentOptions.Center;
		label.textWrappingMode = TextWrappingModes.NoWrap;
		label.color = TextColor;
		label.raycastTarget = false;
		if (Font != null)
			label.font = Font;

		RectTransform rect = label.rectTransform;
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}

	void Set_visible(bool visible)
	{
		if (canvas_group != null)
			canvas_group.alpha = visible ? 1f : 0f;

		if (canvas != null && canvas.enabled != visible)
			canvas.enabled = visible;
	}

	// Appele a chaque stomp : (re)lance le burst depuis le premier mot.
	public void Trigger()
	{
		if (!isActiveAndEnabled)
			return;

		if (Words == null || Words.Length == 0)
			return;

		if (strobe_routine != null)
			StopCoroutine(strobe_routine);

		strobe_routine = StartCoroutine(Strobe_routine());
	}

	IEnumerator Strobe_routine()
	{
		// On adapte la taille de police a la resolution courante.
		float font_size = Mathf.Max(8f, Screen.height * FontHeightFraction);
		label.fontSize = font_size;
		label.color = TextColor;

		for (int i = 0; i < Words.Length; i++)
		{
			label.text = Words[i];

			Set_visible(true);
			yield return Wait_unscaled(WordDuration);

			Set_visible(false);

			// Pas de blank apres le dernier mot.
			if (i < Words.Length - 1)
				yield return Wait_unscaled(BlankDuration);
		}

		Set_visible(false);
		strobe_routine = null;
	}

	IEnumerator Wait_unscaled(float duration)
	{
		float t = 0f;
		while (t < duration)
		{
			t += Time.unscaledDeltaTime;
			yield return null;
		}
	}
}
