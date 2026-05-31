using UnityEngine;
using UnityEngine.Rendering;

// Animates the RippleDistortion custom post process when triggered.
// Drop this on the same Volume object that holds a RippleDistortion override,
// or assign a Volume manually. Call Play() to fire one ripple.
[RequireComponent(typeof(Volume))]
public class RippleEffectController : MonoBehaviour
{
	[Header("Ripple shape")]
	[Tooltip("Where on screen the ripple starts (0-1 UV). 0.5,0.5 = center.")]
	public Vector2 Center = new Vector2(0.5f, 0.5f);

	[Tooltip("How far the wavefront travels (UV units). ~1.4 covers the whole screen.")]
	public float MaxRadius = 1.4f;

	[Tooltip("Seconds for the wavefront to reach MaxRadius.")]
	public float Duration = 1.2f;

	[Tooltip("Peak pixel displacement at the start of the ripple.")]
	public float Amplitude = 0.04f;

	[Tooltip("Width of the distorted band.")]
	public float Width = 0.12f;

	[Tooltip("Number of waves inside the band.")]
	public float Frequency = 30f;

	[Header("Curves")]
	[Tooltip("Wavefront radius over normalized time (0-1).")]
	public AnimationCurve RadiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

	[Tooltip("Amplitude multiplier over normalized time (0-1). Fade out at the end.")]
	public AnimationCurve AmplitudeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

	[Header("Debug")]
	[Tooltip("Press the debug key to fire a ripple manually.")]
	public bool EnableKeyboardTrigger = true;
	public KeyCode TriggerKey = KeyCode.Y;

	Volume volume;
	RippleDistortion ripple;
	float timer;
	bool playing;

	void Awake()
	{
		volume = GetComponent<Volume>();
		if (volume != null && volume.profile != null)
			volume.profile.TryGet(out ripple);

		if (ripple == null)
			Debug.LogWarning("[ripple] no RippleDistortion override found on the Volume profile.");

		SetIdle();
	}

	public void Play()
	{
		if (ripple == null)
			return;

		timer = 0f;
		playing = true;

		ripple.Center.Override(Center);
		ripple.Width.Override(Width);
		ripple.Frequency.Override(Frequency);
	}

	void Update()
	{
		if (EnableKeyboardTrigger && Input.GetKeyDown(TriggerKey))
			Play();

		if (!playing || ripple == null)
			return;

		timer += Time.deltaTime;
		float t = Mathf.Clamp01(timer / Duration);

		ripple.Radius.Override(RadiusCurve.Evaluate(t) * MaxRadius);
		ripple.Amplitude.Override(AmplitudeCurve.Evaluate(t) * Amplitude);

		if (t >= 1f)
		{
			playing = false;
			SetIdle();
		}
	}

	void SetIdle()
	{
		if (ripple == null)
			return;

		// Amplitude 0 makes IsActive() return false, so the pass is skipped entirely.
		ripple.Amplitude.Override(0f);
		ripple.Radius.Override(0f);
	}
}
