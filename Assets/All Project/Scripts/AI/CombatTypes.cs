using UnityEngine;

namespace ZharkoeLeto44.AI
{
public enum EnemyState
{
    Patrol,
    Alert,
    Search,
    Chase,
    Combat,
    Dead
}

public enum Team
{
    Allies,
    Axis
}

public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly Vector3 Point;
    public readonly Vector3 Direction;
    public readonly GameObject Attacker;
    public readonly bool IsSuppression;

    public DamageInfo(
        float amount,
        Vector3 point,
        Vector3 direction,
        GameObject attacker,
        bool isSuppression = false)
    {
        Amount = amount;
        Point = point;
        Direction = direction;
        Attacker = attacker;
        IsSuppression = isSuppression;
    }
}

public interface IDamageable
{
    bool IsAlive { get; }
    Team Team { get; }
    void TakeDamage(DamageInfo damage);
}
}
