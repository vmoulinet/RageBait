using UnityEngine;

public class ConstantRotation : MonoBehaviour
{
    [Tooltip("Vitesse de rotation en degrés par seconde.")]
    public float speed = 10f;

    [Tooltip("Axe autour duquel tourner (par défaut Y, vertical).")]
    public Vector3 axis = Vector3.up;

    [Tooltip("Tourner dans le repère local plutôt que monde.")]
    public Space space = Space.World;

    void Update()
    {
        // -speed = sens horaire vu de dessus (autour de Y)
        transform.Rotate(axis, -speed * Time.deltaTime, space);
    }
}
