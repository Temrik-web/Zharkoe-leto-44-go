using System;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
public static class ProjectilePass
{
    public readonly struct Shot
    {
        public readonly Vector3 Origin;
        public readonly Vector3 End;
        public readonly Vector3 Direction;
        public readonly GameObject Shooter;
        public readonly Team ShooterTeam;
        public readonly bool DidHitSomething;

        public Shot(Vector3 origin, Vector3 end, GameObject shooter,
                    Team shooterTeam, bool didHitSomething)
        {
            Origin = origin;
            End = end;
            Direction = (end - origin).normalized;
            Shooter = shooter;
            ShooterTeam = shooterTeam;
            DidHitSomething = didHitSomething;
        }

        public float DistanceToPoint(Vector3 point)
        {
            Vector3 ab = End - Origin;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f)
                return Vector3.Distance(point, Origin);

            float t = Mathf.Clamp01(Vector3.Dot(point - Origin, ab) / lenSq);
            Vector3 closest = Origin + ab * t;
            return Vector3.Distance(point, closest);
        }
    }
    public static event Action<Shot> ShotFired;

    public static void Emit(Shot shot) => ShotFired?.Invoke(shot);
}
}
