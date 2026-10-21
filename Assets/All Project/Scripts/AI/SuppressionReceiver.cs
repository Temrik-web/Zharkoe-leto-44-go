using UnityEngine;

namespace ZharkoeLeto44.AI
{
[DisallowMultipleComponent]
public sealed class SuppressionReceiver : MonoBehaviour
{
    [Header("Кого считать «рядом»")]
    [Tooltip("Радиус, в котором пролёт пули считается близким.")]
    [SerializeField, Min(0.1f)] private float nearMissRadius = 2.5f;

    [Header("Игрок: свист пули")]
    [Tooltip("Включи, если этот компонент висит на игроке или его камере. " +
             "Если галочка снята, но EnemyAI не найден — режим выбирается автоматически (игрок).")]
    [SerializeField] private bool isPlayer = false;
    [SerializeField] private AudioSource whizzSource;
    [SerializeField] private AudioClip[] whizzClips;
    [SerializeField, Min(0f)] private float whizzCooldown = 0.08f;

    [Header("Враг: подавление")]
    [Tooltip("Как часто пролёт пули может добавить подавление, сек. Ограничивает " +
             "накопление от очереди: без этого одна очередь «прижимает» врага к максуму.")]
    [SerializeField, Min(0f)] private float suppressionCooldown = 0.2f;
    [Tooltip("Сколько подавления даёт один близкий пролёт (0…1). На краю радиуса — ноль.")]
    [SerializeField, Range(0f, 1f)] private float maxSuppressionPerShot = 0.6f;
    [SerializeField] private EnemyAI enemyAI;

    private CharacterHealth _health;
    private float _nextWhizzTime;
    private float _nextSuppressionTime;

    /// <summary>
    /// Режим работы. Явно выбранный флаг — приоритет; если ИИ не найден,
    /// компонент работает как «игрок» (свист). Раньше флаг был единственным
    /// переключателем: без него и без EnemyAI компонент молча ничего не делал.
    /// </summary>
    private bool UsePlayerFeedback => isPlayer || enemyAI == null;

    private void Awake()
    {
        ResolveReferences();

        if (_health == null)
            Debug.LogWarning($"[SuppressionReceiver] {name}: CharacterHealth не найден в родителях — " +
                "ни свист, ни подавление работать не будут. Повесь компонент на игрока/врага или его ребёнка.", this);
        else if (UsePlayerFeedback && (whizzClips == null || whizzClips.Length == 0))
            Debug.LogWarning($"[SuppressionReceiver] {name}: не заполнены Whizz Clips — " +
                "свист пули не прозвучит.", this);
    }

    /// <summary>
    /// Ссылки могут появиться позже (например, CharacterHealth добавляется
    /// EnemyAI в Awake), поэтому ищем их не только один раз, но и при первом
    /// выстреле, если в Awake их ещё не было.
    /// </summary>
    private void ResolveReferences()
    {
        if (_health == null) _health = GetComponentInParent<CharacterHealth>();
        if (enemyAI == null) enemyAI = GetComponentInParent<EnemyAI>();
        if (UsePlayerFeedback && whizzSource == null) whizzSource = GetComponent<AudioSource>();
    }

    private void OnEnable()  => ProjectilePass.ShotFired += OnShot;
    private void OnDisable() => ProjectilePass.ShotFired -= OnShot;

    private void OnShot(ProjectilePass.Shot shot)
    {
        if (_health == null) ResolveReferences();
        if (_health == null || !_health.IsAlive) return;
        if (shot.ShooterTeam == _health.Team) return;
        if (shot.Shooter != null && IsOwnCharacter(shot.Shooter)) return;

        float radius = Mathf.Max(0.1f, nearMissRadius);
        float distance = shot.DistanceToPoint(transform.position);
        if (distance > radius) return;
        float closeness = 1f - Mathf.Clamp01(distance / radius);

        if (UsePlayerFeedback)
            PlayerFeedback(closeness);
        else
            EnemyFeedback(closeness);
    }

    /// <summary>Стрелок — часть этого же персонажа (свой выстрел не засчитываем).</summary>
    private bool IsOwnCharacter(GameObject shooter)
    {
        if (shooter == null) return false;
        Transform shooterTransform = shooter.transform;
        if (shooterTransform == null) return false;
        return shooterTransform.IsChildOf(_health.transform);
    }

    private void PlayerFeedback(float closeness)
    {
        if (whizzSource == null || whizzClips == null || whizzClips.Length == 0) return;
        if (Time.time < _nextWhizzTime) return;

        _nextWhizzTime = Time.time + whizzCooldown;

        AudioClip clip = whizzClips[Random.Range(0, whizzClips.Length)];
        if (clip == null) return;

        whizzSource.pitch = Random.Range(0.92f, 1.08f);
        whizzSource.PlayOneShot(clip, Mathf.Lerp(0.5f, 1f, closeness));
    }

    private void EnemyFeedback(float closeness)
    {
        if (enemyAI == null) return;
        if (Time.time < _nextSuppressionTime) return;

        _nextSuppressionTime = Time.time + suppressionCooldown;

        // На краю радиуса эффекта нет: раньше там был разрыв (0.2 на границе
        // и сразу 0 за ней) — теперь подавление плавно падает до нуля.
        enemyAI.ApplySuppression(Mathf.Lerp(0f, maxSuppressionPerShot, closeness));
    }
}
}
