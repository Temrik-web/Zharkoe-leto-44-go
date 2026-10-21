using System;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
[DisallowMultipleComponent]
public sealed class CharacterHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private Team team = Team.Axis;
    [SerializeField, Min(1f)] private float maximumHealth = 100f;

    public event Action<DamageInfo> Damaged;
    public event Action<DamageInfo> Died;

    public Team Team => team;
    public float MaximumHealth => maximumHealth;
    public float CurrentHealth { get; private set; }
    public float NormalizedHealth => maximumHealth > 0f ? CurrentHealth / maximumHealth : 0f;
    public bool IsAlive { get; private set; }
    public float LastDamageTime { get; private set; } = float.NegativeInfinity;

    private void Awake()
    {
        // Старые префабы могли остаться с maximumHealth = 0 — делить на ноль нельзя.
        if (maximumHealth <= 0f) maximumHealth = 1f;
        ResetHealth();
    }

    private void OnEnable() => EnemyAI.RegisterCharacter(this);
    private void OnDisable() => EnemyAI.UnregisterCharacter(this);

    public void ResetHealth()
    {
        CurrentHealth = maximumHealth;
        IsAlive = true;
        LastDamageTime = float.NegativeInfinity;
    }

    public void TakeDamage(DamageInfo damage)
    {
        if (!IsAlive) return;

        // Попадание-супрессия может быть без урона по HP — но подписчики
        // (подавление, анимация) всё равно должны о нём узнать.
        if (damage.Amount <= 0f && !damage.IsSuppression) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage.Amount);
        LastDamageTime = Time.time;
        bool died = CurrentHealth <= 0f;
        IsAlive = !died;

        Damaged?.Invoke(damage);

        // Обработчик Damaged мог воскресить персонажа (ResetHealth) — тогда Died
        // поднимать нельзя, иначе «живой» персонаж получит смерть.
        if (died && !IsAlive) Died?.Invoke(damage);
    }

    public void RestoreHealth(float amount)
    {
        if (!IsAlive || amount <= 0f) return;

        // Восстановление поднимает CurrentHealth — по нему ИИ отслеживает
        // сброс темпа схватки (D8): базовое здоровье цели обновляется.
        CurrentHealth = Mathf.Min(maximumHealth, CurrentHealth + amount);
    }
}
}
