using UnityEngine;
using UnityEngine.EventSystems;

public class DebugMenuManager : MonoBehaviour
{
	public GameObject VisualRoot;
	public GameObject FirstSelected;

	bool is_open = false;

	void Start()
	{
		SetOpen(false);
	}

	void Update()
	{
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Debug.Log("[debug_menu] esc pressed | current_open=" + is_open);
			Toggle();
		}
	}

	public void Toggle()
	{
		SetOpen(!is_open);
	}

	public void SetOpen(bool open)
	{
		is_open = open;

		Debug.Log(
			"[debug_menu] set_open | open=" + open +
			" | visual_root=" + (VisualRoot != null ? VisualRoot.name : "null")
		);

		if (VisualRoot != null)
			VisualRoot.SetActive(open);

		Time.timeScale = open ? 0f : 1f;
		Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
		Cursor.visible = open;

		if (open)
			Invoke(nameof(SelectFirst), 0.01f);
	}

	void SelectFirst()
	{
		if (FirstSelected == null)
		{
			Debug.Log("[debug_menu] no first selected");
			return;
		}

		if (EventSystem.current == null)
		{
			Debug.Log("[debug_menu] no event system");
			return;
		}

		EventSystem.current.SetSelectedGameObject(null);
		EventSystem.current.SetSelectedGameObject(FirstSelected);

		Debug.Log("[debug_menu] selected first=" + FirstSelected.name);
	}
}