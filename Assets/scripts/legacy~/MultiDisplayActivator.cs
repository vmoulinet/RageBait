using UnityEngine;

public class MultiDisplayActivator : MonoBehaviour
{
    void Start()
    {
        for (int i = 1; i < Display.displays.Length; i++)
        {
            Display.displays[i].Activate();
        }
    }
}
