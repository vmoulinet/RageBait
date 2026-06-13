using UnityEngine;
using UnityEngine.UI;

// Debug-menu toggle for the "break-up letter" feature. When ON:
//   - the DaddyLetterProjector GameObject is active
//   - the typing loop is suspended during WorldValidation
// When OFF, both are disabled.
//
// The choice is persisted in PlayerPrefs and re-applied on Awake, so it
// survives across launches and takes effect from boot (default: OFF).
public class BreakupLetterToggle : MonoBehaviour
{
	const string PrefKey = "breakup_letter_enabled";
	const bool DefaultEnabled = false;

	[Header("References")]
	[Tooltip("GameObject of the DaddyLetterProjector, enabled/disabled with this toggle.")]
	public GameObject DaddyLetterProjectorObject;
	[Tooltip("WorldValidation whose SuspendTypingDuringEvent follows this toggle.")]
	public WorldValidation WorldValidation;

	[Header("UI")]
	[Tooltip("Debug-menu toggle reflecting and driving the feature.")]
	public Toggle Toggle;

	[Header("Debug")]
	public bool DebugLog = false;

	bool enabled_state;

	void Awake()
	{
		enabled_state = PlayerPrefs.GetInt(PrefKey, DefaultEnabled ? 1 : 0) != 0;
		Apply(enabled_state);
	}

	void OnEnable()
	{
		if (Toggle != null)
		{
			Toggle.SetIsOnWithoutNotify(enabled_state);
			Toggle.onValueChanged.AddListener(OnToggleChanged);
		}
	}

	void OnDisable()
	{
		if (Toggle != null)
			Toggle.onValueChanged.RemoveListener(OnToggleChanged);
	}

	void OnToggleChanged(bool value)
	{
		enabled_state = value;
		Apply(value);

		PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
		PlayerPrefs.Save();

		if (DebugLog)
			Debug.Log("[breakup_letter] toggle | enabled=" + value);
	}

	void Apply(bool value)
	{
		if (DaddyLetterProjectorObject != null)
			DaddyLetterProjectorObject.SetActive(value);

		if (WorldValidation != null)
			WorldValidation.SuspendTypingDuringEvent = value;
	}
}
