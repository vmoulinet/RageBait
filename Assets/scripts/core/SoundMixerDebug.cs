using UnityEngine;
using UnityEngine.UI;

// Binds five debug-menu sliders to the live SoundManager mix multipliers.
//
// Each slider is a MULTIPLIER on the preset volume set in the inspector:
//   1 = preset as-is, 2 = twice as loud, 0 = silent.
// The preset volumes on SoundManager are never overwritten, so your mix stays
// intact and the sliders just scale it. Set each slider to Min 0 / Max 2 /
// Value 1 in the inspector.
//
// Persistence is owned by SoundManager (PlayerPrefs): it loads the saved
// multipliers in Awake so the loops start at the right mix even while this menu
// is inactive. Here we just reflect those values on the sliders, write changes
// back, and flush to disk when the menu closes (Escape disables this object) or
// on quit/pause.
public class SoundMixerDebug : MonoBehaviour
{
	public SoundManager SoundManager;

	[Header("Sliders (multipliers, 0..2, default 1)")]
	public Slider MirrorShatterSlider;
	public Slider StompSlider;
	public Slider PendulumSlider;
	public Slider TypingSlider;
	public Slider SoundtrackSlider;

	void Reset()
	{
		if (SoundManager == null)
			SoundManager = FindObjectOfType<SoundManager>();
	}

	void Awake()
	{
		if (SoundManager == null)
			SoundManager = FindObjectOfType<SoundManager>();
	}

	void OnEnable()
	{
		if (SoundManager == null)
		{
			Debug.LogWarning("[sound_mixer_debug] no SoundManager reference");
			return;
		}

		Bind(MirrorShatterSlider, SoundManager.MirrorBreakVolumeMult, OnMirrorShatterChanged);
		Bind(StompSlider, SoundManager.StompVolumeMult, OnStompChanged);
		Bind(PendulumSlider, SoundManager.PendulumLoopVolumeMult, OnPendulumChanged);
		Bind(TypingSlider, SoundManager.TypingLoopVolumeMult, OnTypingChanged);
		Bind(SoundtrackSlider, SoundManager.DaddyLoopVolumeMult, OnSoundtrackChanged);
	}

	void OnDisable()
	{
		Unbind(MirrorShatterSlider, OnMirrorShatterChanged);
		Unbind(StompSlider, OnStompChanged);
		Unbind(PendulumSlider, OnPendulumChanged);
		Unbind(TypingSlider, OnTypingChanged);
		Unbind(SoundtrackSlider, OnSoundtrackChanged);

		// Menu closed (Escape / back to game): persist the current mix.
		// Quit/pause are handled by SoundManager itself as a safety net.
		if (SoundManager != null)
			SoundManager.SaveVolumeMultipliers();
	}

	void Bind(Slider slider, float current_mult, UnityEngine.Events.UnityAction<float> handler)
	{
		if (slider == null)
			return;

		// Reflect the live multiplier without firing the callback.
		slider.SetValueWithoutNotify(Mathf.Clamp(current_mult, slider.minValue, slider.maxValue));
		slider.onValueChanged.AddListener(handler);
	}

	void Unbind(Slider slider, UnityEngine.Events.UnityAction<float> handler)
	{
		if (slider == null)
			return;

		slider.onValueChanged.RemoveListener(handler);
	}

	void OnMirrorShatterChanged(float value)
	{
		if (SoundManager != null)
			SoundManager.MirrorBreakVolumeMult = value;
	}

	void OnStompChanged(float value)
	{
		if (SoundManager != null)
			SoundManager.StompVolumeMult = value;
	}

	void OnPendulumChanged(float value)
	{
		if (SoundManager != null)
			SoundManager.PendulumLoopVolumeMult = value;
	}

	void OnTypingChanged(float value)
	{
		if (SoundManager != null)
			SoundManager.TypingLoopVolumeMult = value;
	}

	void OnSoundtrackChanged(float value)
	{
		if (SoundManager != null)
			SoundManager.DaddyLoopVolumeMult = value;
	}
}
