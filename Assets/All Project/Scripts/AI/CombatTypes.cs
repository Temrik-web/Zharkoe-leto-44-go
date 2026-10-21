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
        // Направление всегда единичное: по нему считают «откуда прилетел урон».
        // Раньше каждый вызывающий был обязан нормализовать сам — иначе «позиция
        // стрелка» (Point - Direction) считалась неверно.
        Direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.zero;
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
