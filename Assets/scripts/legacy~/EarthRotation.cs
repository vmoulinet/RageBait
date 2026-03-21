using UnityEngine;

public class FoucaultEarthRotation : MonoBehaviour
{
    [Range(-90f, 90f)]
    public float latitude = 48.8f;

    [Header("Time")]
    public float earthTimeScale = 30000f; // accélération visible

    float accumulatedTime; // en secondes

    void Update()
    {
        accumulatedTime += Time.deltaTime * earthTimeScale;

        float dayRatio = accumulatedTime / 86400f;

        float angle =
            360f * Mathf.Sin(latitude * Mathf.Deg2Rad) * dayRatio;

        transform.localRotation = Quaternion.Euler(0f, angle, 0f);
    }
}
