using System;
using System.Collections;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Совместимый API для инвентаря и UI. Всё здоровье хранит CharacterHealth.
/// Этот компонент не является второй целью для оружия и не хранит отдельные HP.
/// </summary>
[RequireComponent(typeof(CharacterHealth))]
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [Header("Возрождение")]
    [Tooltip("Через сколько секунд возродить игрока после смерти. 0 — не возрождать " +
             "(игра зависнет на трупе: враги трупы игнорируют).")]
    [SerializeField] private float autoRespawnDelay = 3f;

    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private Coroutine _respawnRoutine;
    private CharacterHealth combatHealth;
    private CharacterHealth Health => combatHealth != null
        ? combatHealth : combatHealth = GetComponent<CharacterHealth>();

    public float maxHealth => Health.MaximumHealth;
    public bool IsDead => !Health.IsAlive;
    public float HealthPercent => Health.NormalizedHealth;
    public event Action OnDeath;
    public event Action<float> OnDamaged;

    private void Awake()
    {
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
    }

    private void OnEnable()
    {
        EnemyAI.RegisterPlayer(Health);
        Health.Damaged += HandleDamage;
        Health.Died += HandleDeath;
    }

    private void OnDisable()
    {
        EnemyAI.UnregisterPlayer(Health);
        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }
        if (combatHealth == null) return;
        combatHealth.Damaged -= HandleDamage;
        combatHealth.Died -= HandleDeath;
    }

    private void HandleDamage(DamageInfo damage) => OnDamaged?.Invoke(damage.Amount);
    private void HandleDeath(DamageInfo damage)
    {
        OnDeath?.Invoke();
        Debug.LogWarning($"[PlayerHealth] {name} погиб. Враги труп игнорируют. " +
            (autoRespawnDelay > 0f
                ? $"Возрождение через {autoRespawnDelay:F0} сек на точке спавна."
                : "Автовозрождение выключено (autoRespawnDelay = 0)."), this);
        if (autoRespawnDelay > 0f && _respawnRoutine == null && Application.isPlaying)
            _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(autoRespawnDelay);
        _respawnRoutine = null;

        Health.ResetHealth();
        transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);

        Debug.Log($"[PlayerHealth] {name} возрождён на точке спавна.", this);
    }

    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        Health.TakeDamage(new DamageInfo(damage, transform.position,
            (transform.position - attackerPosition).normalized, null));
    }

    public void Heal(float amount) => Health.RestoreHealth(amount);
}
