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
    private static void ResetStatics() => NoiseCreated = null;

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
