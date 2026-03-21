using UnityEngine;

public class DebugBreakerSphere_XZ : MonoBehaviour
{
    public float moveSpeed = 15f;
    public float planeY = 0f;

    Rigidbody rb;
    Camera cam;
    Plane plane;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cam = Camera.main;

        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));
    }

    void FixedUpdate()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (plane.Raycast(ray, out float enter))
        {
            Vector3 targetPos = ray.GetPoint(enter);
            Vector3 velocity = (targetPos - rb.position) * moveSpeed;
            rb.linearVelocity = velocity;
        }
    }
}
