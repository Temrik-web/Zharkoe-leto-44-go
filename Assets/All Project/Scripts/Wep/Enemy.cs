using UnityEngine;
using System;
using ZharkoeLeto44.AI;

/// <summary>Эффекты смерти. HP хранит только CharacterHealth.</summary>
[RequireComponent(typeof(CharacterHealth))]
[DisallowMultipleComponent]
public class Enemy : MonoBehaviour
{
    private CharacterHealth combatHealth;
    private CharacterHealth Health => combatHealth != null
        ? combatHealth : combatHealth = GetComponent<CharacterHealth>();
    public float maxHealth => Health.MaximumHealth;
    [Header("Смерть")]
    public GameObject deathEffectPrefab;
    public AudioClip deathSound;
    public bool destroyOnDeath = false;
    [Tooltip("Задержка перед уничтожением трупа. Должна быть >= длины анимации смерти.")]
    public float destroyDelay = 3f;
    private AudioSource audioSource;
    public event Action OnDeath;
    public event Action<float, Vector3> OnDamaged;
    public bool IsDead => !Health.IsAlive;
    public float CurrentHealth => Health.CurrentHealth;
    public float HealthPercent => Health.NormalizedHealth;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && deathSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
        }
    }

    void OnEnable()
    {
        Health.Damaged += HandleDamage;
        Health.Died += HandleDeath;
    }

    void OnDisable()
    {
        if (combatHealth == null) return;
        combatHealth.Damaged -= HandleDamage;
        combatHealth.Died -= HandleDeath;
    }

    void HandleDamage(DamageInfo damage) => OnDamaged?.Invoke(damage.Amount,
        damage.Attacker != null ? damage.Attacker.transform.position : damage.Point - damage.Direction);
    void HandleDeath(DamageInfo damage) => Die();

    // Оставлен для совместимости с оружием игрока
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position);
    }
    // С позицией атакующего — нужно ИИ, чтобы понимать откуда стреляют
    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        Health.TakeDamage(new DamageInfo(damage, transform.position,
            (transform.position - attackerPosition).normalized, null));
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} погиб.");
        OnDeath?.Invoke();
        if (deathEffectPrefab != null)
        {
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
        }
        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }
        if (destroyOnDeath)
        {
            float delay = Mathf.Max(destroyDelay,
                deathSound != null ? deathSound.length : 0f);
            Destroy(gameObject, delay);
        }
        else
        {
            // Труп остаётся: гасим коллайдер, падение отыграет EnemyAI через Death trigger
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
    }
}
