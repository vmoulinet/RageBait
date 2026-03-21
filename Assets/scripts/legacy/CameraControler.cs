using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleCameraController : MonoBehaviour
{
    public float lookSpeed = 2f;
    public float moveSpeed = 10f;
    public float zoomSpeed = 10f;
    public float minZoom = 5f;
    public float maxZoom = 100f;

    Vector3 dragOrigin;

    void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        if (mouse == null || keyboard == null)
            return;

        // ---------------- ROTATION (clic droit)
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();

            float mouseX = delta.x * lookSpeed * Time.deltaTime;
            float mouseY = delta.y * lookSpeed * Time.deltaTime;

            transform.eulerAngles += new Vector3(-mouseY, mouseX, 0f);
        }

        // ---------------- ZOOM (molette)
        float scroll = mouse.scroll.ReadValue().y * 0.01f;
        Vector3 direction = transform.forward * scroll * zoomSpeed;
        Vector3 newPos = transform.position + direction;

        float distance = Vector3.Distance(Vector3.zero, newPos);
        if (distance >= minZoom && distance <= maxZoom)
            transform.position = newPos;

        // ---------------- PAN (clic molette)
        if (mouse.middleButton.wasPressedThisFrame)
            dragOrigin = mouse.position.ReadValue();

        if (mouse.middleButton.isPressed)
        {
            Vector2 mousePos = mouse.position.ReadValue();
            Vector3 diff = Camera.main.ScreenToViewportPoint(
            new Vector3(mousePos.x - dragOrigin.x, mousePos.y - dragOrigin.y, 0f)
        );


            Vector3 move = new Vector3(-diff.x, -diff.y, 0f) * moveSpeed;
            transform.Translate(move, Space.Self);

            dragOrigin = mouse.position.ReadValue();
        }

        // ---------------- DÉPLACEMENT CLAVIER (WASD)
        Vector3 keyboardMove = Vector3.zero;

        if (keyboard.aKey.isPressed) keyboardMove -= transform.right;
        if (keyboard.dKey.isPressed) keyboardMove += transform.right;
        if (keyboard.wKey.isPressed) keyboardMove += transform.forward;
        if (keyboard.sKey.isPressed) keyboardMove -= transform.forward;

        transform.position += keyboardMove * moveSpeed * Time.deltaTime;
    }
}
