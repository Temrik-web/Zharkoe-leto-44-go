using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Немец, который стучит в дверь и просит еду. До сигнала (свиста) — мирный:
/// ИИ выключен, оружие спрятано, стоит лицом к игроку.
/// После свиста может броситься в бой (GoHostile) или уйти (Leave).
/// </summary>
[DisallowMultipleComponent]
public class GermanBeggar : MonoBehaviour
{
    [Header("Внешность")]
    [Tooltip("Спрятать оружие на время разговора: он же беженец, а не боец.")]
    public bool hideWeapon = true;

    [Header("Движение")]
    [Tooltip("Скорость, когда уходит прочь, м/с.")]
    public float walkSpeed = 2.4f;
    [Tooltip("Скорость поворота к игроку, град/с.")]
    public float faceTurnSpeed = 90f;

    [Header("Звук")]
    [Tooltip("Свист: сигнал для остальных немцев.")]
    public AudioClip whistleClip;
    [Tooltip("Источник для свиста. Пусто — возьмём с объекта.")]
    public AudioSource audioSource;

    public EnemyAI Ai { get; private set; }
    public EnemyMotor Motor { get; private set; }
    /// <summary>Стал враждебным (свистнул).</summary>
    public bool IsHostile { get; private set; }
    /// <summary>Уходит (игрок дал еду).</summary>
    public bool IsLeaving { get; private set; }
    /// <summary>Идёт к двери.</summary>
    public bool IsWalking { get; private set; }

    private GameObject cachedPlayer;
    private HitscanWeapon weapon;
    private GameObject weaponObject;
    private Vector3 leaveTarget;
    private bool hasLeaveTarget;
    private Vector3 walkTarget;
    private float walkArriveDistance = 1.3f;
    private float lastWalkCheckTime;
    private Vector3 lastWalkPosition;
    private float stuckSeconds;

    void Awake()
    {
        EnsureRefs();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        weapon = GetComponentInChildren<HitscanWeapon>(true);
        if (weapon != null) weaponObject = weapon.gameObject;

        SetPeaceful();
    }

    /// <summary>Ссылки могли не найтись в Awake (порядок компонентов) — ищем при случае.</summary>
    void EnsureRefs()
    {
        if (Ai == null) Ai = GetComponent<EnemyAI>();
        if (Motor == null) Motor = GetComponent<EnemyMotor>();
        if (weapon == null) weapon = GetComponentInChildren<HitscanWeapon>(true);
        if (weapon != null && weaponObject == null) weaponObject = weapon.gameObject;
    }

    void Update()
    {
        if (IsHostile || IsLeaving) return;
        if (Ai == null || Motor == null) EnsureRefs();

        if (IsWalking)
        {
            // Сторож: если не идёт (нет NavMesh, нет земли, упёрся) — ставим сразу.
            if (Time.time - lastWalkCheckTime >= 1f)
            {
                lastWalkCheckTime = Time.time;
                float moved = Vector3.Distance(transform.position, lastWalkPosition);
                lastWalkPosition = transform.position;
                stuckSeconds = moved < 0.15f ? stuckSeconds + 1f : 0f;
                if (stuckSeconds >= 2f)
                {
                    stuckSeconds = 0f;
                    TeleportTo(walkTarget);
                    return;
                }
            }

            // Путь потерялся — идём заново. Проверку «дошёл» ведём по расстоянию:
            // Motor.HasArrived() врёт, когда у агента нет пути (hasPath == false).
            if (Motor != null && !Motor.HasDestination)
                Motor.MoveTo(walkTarget);

            if (ReachedWalkTarget()) IsWalking = false;
            return;
        }

        FacePlayer();
    }

    /// <summary>Дошёл ли до точки (по расстоянию, а не по флажку агента).</summary>
    bool ReachedWalkTarget()
    {
        Vector3 a = transform.position;
        Vector3 b = walkTarget;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b) <= walkArriveDistance;
    }

    /// <summary>Есть ли под немцем NavMesh (если нет — он не сможет идти).</summary>
    public bool CanNavigate
    {
        get
        {
            UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            return agent != null && agent.enabled && agent.isOnNavMesh;
        }
    }

    /// <summary>Мгновенно поставить в точку (страховка, если не идёт).</summary>
    public void TeleportTo(Vector3 point)
    {
        EnsureRefs();
        IsWalking = false;
        stuckSeconds = 0f;
        lastWalkPosition = point;

        if (Motor != null)
        {
            Motor.Stop();
            if (Motor.SampleReachablePoint(point, 8f, out Vector3 reachable)) point = reachable;
        }

        // Через агента, иначе он вернёт объект на прежнее место.
        UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Warp(point);
        }
        else
        {
            transform.position = point;
        }
        FacePlayer();
    }

    /// <summary>Идти к точке (к двери). Придёт — встанет и будет ждать.</summary>
    public void WalkTo(Vector3 destination, float arriveDistance = 1.3f)
    {
        EnsureRefs();
        IsWalking = true;
        IsLeaving = false;
        walkArriveDistance = arriveDistance;
        walkTarget = destination;
        lastWalkCheckTime = Time.time;
        lastWalkPosition = transform.position;
        stuckSeconds = 0f;

        if (Motor == null) return;
        if (Motor.SampleReachablePoint(destination, 6f, out Vector3 reachable))
            walkTarget = reachable;
        Motor.SetSpeed(walkSpeed);
        Motor.MoveTo(walkTarget);
    }

    /// <summary>Дошёл ли до цели (по расстоянию — флажок агента тут врёт).</summary>
    public bool ArrivedAtTarget
    {
        get
        {
            if (!IsWalking) return true;
            if (ReachedWalkTarget())
            {
                IsWalking = false;
                if (Motor != null) Motor.Stop();
                return true;
            }
            return false;
        }
    }

    /// <summary>Мирный режим: ИИ выключен, оружие спрятано.</summary>
    public void SetPeaceful()
    {
        EnsureRefs();
        IsHostile = false;
        if (Ai != null) Ai.enabled = false;
        if (weapon != null) weapon.enabled = false;
        if (hideWeapon && weaponObject != null && weaponObject != gameObject)
            weaponObject.SetActive(false);
        if (Motor != null) Motor.Stop();
    }

    /// <summary>Смотреть на игрока.</summary>
    public void FacePlayer()
    {
        if (cachedPlayer == null)
        {
            try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
            catch { cachedPlayer = null; }
        }
        if (cachedPlayer == null || Motor == null) return;
        Motor.FaceTowardsAtSpeed(cachedPlayer.transform.position, faceTurnSpeed);
    }

    /// <summary>Свистнуть: звук + шум, на который сбегутся немцы.</summary>
    public void Whistle(float noiseRadius = 60f)
    {
        if (whistleClip != null)
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
                audioSource.minDistance = 3f;
                audioSource.maxDistance = 60f;
            }
            audioSource.pitch = Random.Range(0.94f, 1.08f);
            audioSource.PlayOneShot(whistleClip);
        }

        NoiseSystem.Emit(transform.position, Mathf.Max(1f, noiseRadius), gameObject, 1f);
    }

    /// <summary>Броситься на игрока (после свиста).</summary>
    public void GoHostile(Transform player)
    {
        EnsureRefs();
        if (IsHostile) return;
        IsHostile = true;
        IsLeaving = false;

        if (weaponObject != null && hideWeapon) weaponObject.SetActive(true);
        if (weapon != null) weapon.enabled = true;
        if (Ai != null)
        {
            Ai.enabled = true;
            if (player != null) Ai.ForceTarget(player);
        }
    }

    /// <summary>Уйти прочь: дойти до точки и исчезнуть.</summary>
    public void Leave(Vector3 destination, float arriveDistance = 1.5f)
    {
        EnsureRefs();
        IsLeaving = true;
        IsHostile = false;

        if (Motor == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 target = destination;
        if (Motor.SampleReachablePoint(destination, 6f, out Vector3 reachable))
            target = reachable;

        leaveTarget = target;
        hasLeaveTarget = true;
        Motor.SetSpeed(walkSpeed);
        Motor.MoveTo(target);
        leaveArriveDistance = arriveDistance;
    }

    private float leaveArriveDistance = 1.5f;

    /// <summary>Дошёл ли до точки ухода.</summary>
    public bool ReachedExit
    {
        get
        {
            if (!IsLeaving || !hasLeaveTarget) return false;
            Vector3 a = transform.position;
            Vector3 b = leaveTarget;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) <= leaveArriveDistance;
        }
    }

    void LateUpdate()
    {
        if (!IsLeaving || !hasLeaveTarget) return;
        if (Motor == null) return;

        if (ReachedExit)
        {
            Motor.Stop();
            return;
        }

        if (!Motor.HasDestination || Motor.HasArrived())
            Motor.MoveTo(leaveTarget);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.6f);
    }
}
