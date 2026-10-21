using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Немец, который стучит в дверь и требует еду. До сигнала (свиста) — мирный:
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

    [Header("Поза у двери")]
    [Tooltip("Анимация, когда немец стоит у двери и ждёт (клип выбирается в инспекторе). " +
             "Пусто — обычный idle из EnemyAnimator.")]
    public AnimationClip standAnimation;
    [Tooltip("Играть позу ожидания, пока немец стоит у двери.")]
    public bool playStandAnimation = true;

    [Header("Мирный режим")]
    [Tooltip("Немец никогда не вступает в бой: даже если сценарий попросит. " +
             "Он стоит у двери, стучит и говорит.")]
    public bool stayPeaceful = true;

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
    private EnemyAnimator animatorDriver;
    private Animator plainAnimator;
    private bool standPoseActive;
    private bool peacefulRefusalLogged;
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
        if (animatorDriver == null) animatorDriver = GetComponentInChildren<EnemyAnimator>(true);
        if (plainAnimator == null) plainAnimator = GetComponentInChildren<Animator>(true);
    }

    void Update()
    {
        if (IsHostile) return;
        if (IsLeaving)
        {
            DriveLocomotion(true);
            return;
        }
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
            {
                GiveRotationToAgent();
                Motor.MoveTo(walkTarget);
            }

            DriveLocomotion(true);

            if (ReachedWalkTarget())
            {
                IsWalking = false;
                StopMotor();
            }
            return;
        }

        FacePlayer();
        KeepPeace();
        DriveLocomotion(false);
        PlayStandPose();
    }

    /// <summary>Мирный режим держим, даже если кто-то включит ИИ снаружи.</summary>
    void KeepPeace()
    {
        if (!stayPeaceful) return;
        if (Ai != null && Ai.enabled) Ai.enabled = false;
        if (weapon != null && weapon.enabled) weapon.enabled = false;
        if (hideWeapon && weaponObject != null && weaponObject != gameObject && weaponObject.activeSelf)
            weaponObject.SetActive(false);
    }

    /// <summary>
    /// Локомоция вручную: ИИ у немца выключен, поэтому анимацию двигает сам
    /// (иначе он скользит статуей). Есть EnemyAnimator — играем через него.
    /// </summary>
    void DriveLocomotion(bool moving)
    {
        if (animatorDriver == null) return;
        float speed = moving && Motor != null ? Motor.Velocity.magnitude : 0f;
        animatorDriver.SetLocomotion(speed, moving);
    }

    /// <summary>Поза ожидания у двери (клип из инспектора).</summary>
    void PlayStandPose()
    {
        if (!playStandAnimation || standAnimation == null) return;
        if (standPoseActive) return;
        standPoseActive = true;

        if (animatorDriver != null)
        {
            animatorDriver.PlayStandPose(standAnimation);
            return;
        }

        // Без EnemyAnimator — обычный Animator с контроллером.
        if (plainAnimator != null && plainAnimator.runtimeAnimatorController != null)
            plainAnimator.CrossFade(standAnimation.name, 0.2f);
    }

    /// <summary>Снять позу ожидания (пошёл — снова локомоция).</summary>
    void ClearStandPose()
    {
        if (!standPoseActive) return;
        standPoseActive = false;
        if (animatorDriver != null) animatorDriver.ClearStandPose();
    }

    void StopMotor()
    {
        if (Motor != null) Motor.Stop();
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
        ClearStandPose();
        walkArriveDistance = arriveDistance;
        walkTarget = destination;
        lastWalkCheckTime = Time.time;
        lastWalkPosition = transform.position;
        stuckSeconds = 0f;

        if (Motor == null) return;
        if (Motor.SampleReachablePoint(destination, 6f, out Vector3 reachable))
            walkTarget = reachable;
        Motor.SetSpeed(walkSpeed);
        GiveRotationToAgent();
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

    /// <summary>
    /// Углом тела управляет агент: он ведёт немца по маршруту.
    /// Нужно перед КАЖДЫМ приказом движения — пока агент не рулит углом,
    /// солдат едет боком.
    /// </summary>
    void GiveRotationToAgent()
    {
        if (Motor == null) return;
        Motor.SetManualRotation(false);
        Motor.SetAutoRotation(true);
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
        // Корпус ведём сами: NavMeshAgent (updateRotation) перезаписал бы наш
        // поворот после Update — тело дёргалось бы между «к игроку» и «по пути».
        Motor.SetManualRotation(true);
        Motor.SetAutoRotation(false);
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

        if (stayPeaceful)
        {
            // Режим «просто поговорить»: немец стоит у двери и в бой не идёт.
            // Лог — один раз: сценарий может звать GoHostile каждый кадр.
            if (!peacefulRefusalLogged)
            {
                peacefulRefusalLogged = true;
                Debug.Log($"[GermanBeggar] {name}: stayPeaceful включён — в бой не вступаю, стою у двери.", this);
            }
            return;
        }

        IsHostile = true;
        IsLeaving = false;
        ClearStandPose();

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
        ClearStandPose();

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
        GiveRotationToAgent();
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
