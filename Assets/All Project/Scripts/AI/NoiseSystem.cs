using System;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
public static class NoiseSystem
{
    public readonly struct Noise
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly float Intensity;
        public readonly GameObject Source;

        public Noise(Vector3 position, float radius, float intensity, GameObject source)
        {
            Position = position;
            Radius = radius;
            Intensity = intensity;
            Source = source;
        }
    }

    public static event Action<Noise> NoiseCreated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Подписки живых объектов обнуляются здесь. Если сцену не перезагружают
        // (Editor → Enter Play Mode Options → снять Reload Scene/Reload Domain),
        // OnEnable повторно не вызовется — и слушатели отвалятся молча.
        if (NoiseCreated != null)
            Debug.LogWarning("[NoiseSystem] При входе в Play Mode сброшены живые подписки на NoiseCreated. " +
                "Если сцена не перезагружается (Edit → Project Settings → Editor → Enter Play Mode Options), " +
                "слушатели должны переподписаться в OnEnable.");
        NoiseCreated = null;
    }

    public static void Emit(
        Vector3 position,
        float radius,
        GameObject source = null,
        float intensity = 1f)
    {
        if (radius <= 0f)
            return;
        NoiseCreated?.Invoke(new Noise(position, radius, Mathf.Clamp01(intensity), source));
    }
}
}
