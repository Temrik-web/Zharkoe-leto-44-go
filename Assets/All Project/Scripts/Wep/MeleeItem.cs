using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZharkoeLeto44.AI;

[DisallowMultipleComponent]
public class MeleeItem : HeldItem
{
    [System.Serializable]
    public class AttackPhase
    {
        [Tooltip("Название фазы")]
        public string phaseName = "Фаза";

        [Header("Поза")]
        public Vector3 position;
        [Tooltip("Углы Эйлера (градусы)")]
        public Vector3 rotation;

        [Header("Время выполнения")]
        [Tooltip("Время перехода к этой фазе от предыдущей (в секундах)")]
        public float duration = 0.2f;
        [Tooltip("Кривая интерполяции на этом сегменте")]
        public AnimationCurve interpolationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Урон")]
        public bool dealDamage = false;
    }

    [Header("Урон")]
    public float lightDamage = 40f;
    public float heavyDamage = 85f;
    public float backstabMultiplier = 4f;
    [Range(0f, 180f)] public float backstabAngle = 100f;

    [Header("Досягаемость")]
    public float range = 2.1f;
    public float hitRadius = 0.28f;
    public LayerMask hitMask = ~0;
    public float swingRange = 1.9f;
    [Range(10f, 180f)] public float swingArcAngle = 100f;
    [Range(3, 15)] public int swingRayCount = 7;
    public float swingRayRadius = 0.22f;
    [Min(1)] public int maxTargetsPerLight = 1;
    [Min(1)] public int maxTargetsPerHeavy = 3;

    [Header("Фазы тычка (ЛКМ)")]
    public List<AttackPhase> lightAttackPhases = new List<AttackPhase>
    {
        new AttackPhase { phaseName = "Замах", position = new Vector3(0.34f, -0.3f, 0.3f), rotation = new Vector3(-10f, 26f, 10f), duration = 0.2f },
        new AttackPhase { phaseName = "Контакт", position = new Vector3(0.06f, -0.2f, 0.9f), rotation = new Vector3(4f, -8f, -4f), duration = 0.2f, dealDamage = true }
    };

    [Header("Фазы размаха (ПКМ)")]
    public List<AttackPhase> heavyAttackPhases = new List<AttackPhase>
    {
        new AttackPhase { phaseName = "Замах", position = new Vector3(0.62f, 0.02f, 0.1f), rotation = new Vector3(-40f, 62f, 34f), duration = 0.35f },
        new AttackPhase { phaseName = "Контакт", position = new Vector3(-0.3f, -0.26f, 0.66f), rotation = new Vector3(24f, -58f, -34f), duration = 0.3f, dealDamage = true }
    };

    [Header("Возврат")]
    [Tooltip("Время возврата к базовой позе после завершения всех фаз")]
    public float returnDuration = 0.25f;

    [Header("Клавиши")]
    public KeyCode backstabKey = KeyCode.F;

    [Header("Отдача руки")]
    public Vector3 hitKickPosition = new Vector3(-0.04f, 0.02f, -0.09f);
    public Vector3 hitKickRotation = new Vector3(-7f, 3f, 0f);
    public Vector3 missKickPosition = new Vector3(-0.015f, 0.01f, -0.03f);
    public Vector3 missKickRotation = new Vector3(-3f, 1.5f, 0f);

    [Header("Эффекты")]
    public GameObject fleshImpactPrefab;
    public GameObject surfaceImpactPrefab;
    public float impactLifetime = 3f;

    [Header("Звук и шум")]
    public AudioClip swingSound;
    public AudioClip hitFleshSound;
    public AudioClip hitSurfaceSound;
    [Range(0f, 1f)] public float soundVolume = 0.9f;
    public float swingNoiseRadius = 3f;
    public float hitNoiseRadius = 6f;

    [Header("Отладка")]
    public bool logActions = true;
    public bool drawHitGizmo = false;

    [Header("Кулдауны")]
    public float lightCooldown = 0.12f;
    public float heavyCooldown = 0.35f;

    private Coroutine strikeRoutine;
    private bool isStriking;
    private float nextStrikeTime;

    private Vector3 lastHitOrigin;
    private Vector3 lastHitDirection;

    public bool IsStriking => isStriking;
    public override bool HidesCrosshair => true;

    protected override void Awake()
    {
        base.Awake();
        useRightMouseAsAim = false;
        manageCrosshair = false;
        HideCrosshair();
        DisableHeldColliders();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        isStriking = false;
        nextStrikeTime = 0f;
        HideCrosshair();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (strikeRoutine != null)
        {
            StopCoroutine(strikeRoutine);
            strikeRoutine = null;
        }
        isStriking = false;
        ClearPoseOverride();
        RestoreCrosshair();
    }

    void HideCrosshair()
    {
        GameObject cross = ResolveCrosshair();
        if (cross != null && cross.activeSelf) cross.SetActive(false);
    }

    void RestoreCrosshair()
    {
        if (!Application.isPlaying) return;
        GameObject cross = ResolveCrosshair();
        if (cross != null && !cross.activeSelf) cross.SetActive(true);
    }

    GameObject ResolveCrosshair()
    {
        if (crosshairObject != null) return crosshairObject;
        foreach (Wep w in FindObjectsOfType<Wep>(true))
        {
            if (w != null && w.crosshairObject != null)
            {
                crosshairObject = w.crosshairObject;
                break;
            }
        }
        return crosshairObject;
    }

    protected override void HandleInput()
    {
        if (isStriking || Time.time < nextStrikeTime) return;

        if (backstabKey != KeyCode.None && Input.GetKeyDown(backstabKey))
        {
            Strike(heavy: true, forceBackstabAttempt: true);
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Strike(heavy: false);
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            Strike(heavy: true);
        }
    }

    public void Strike(bool heavy, bool forceBackstabAttempt = false)
    {
        if (isStriking || Time.time < nextStrikeTime) return;
        strikeRoutine = StartCoroutine(StrikeSequence(heavy, forceBackstabAttempt));
    }

    void SnapToHipPose()
    {
        Transform model = itemModel != null ? itemModel : transform;
        model.localPosition = hipPosition;
        model.localRotation = Quaternion.Euler(hipRotation);
        SetPoseOverride(hipPosition, hipRotation, 1f);
    }

    IEnumerator StrikeSequence(bool heavy, bool forceBackstabAttempt)
    {
        isStriking = true;

        List<AttackPhase> phases = heavy ? heavyAttackPhases : lightAttackPhases;
        if (phases == null || phases.Count == 0)
        {
            Debug.LogWarning($"[Melee] Нет фаз для удара {(heavy ? "размах" : "тычок")}");
            isStriking = false;
            yield break;
        }

        SnapToHipPose();

        PlaySound(swingSound, soundVolume);
        if (swingNoiseRadius > 0f)
            NoiseSystem.Emit(transform.position, swingNoiseRadius, gameObject, 0.3f);

        Vector3 currentPos = hipPosition;
        Vector3 currentRot = hipRotation;
        foreach (AttackPhase phase in phases)
        {
            float effectiveDuration = Mathf.Max(0.001f, phase.duration);

            float t = 0f;
            while (t < effectiveDuration)
            {
                t += Time.deltaTime;
                float normalized = Mathf.Clamp01(t / effectiveDuration);
                float curveValue = phase.interpolationCurve != null ? phase.interpolationCurve.Evaluate(normalized) : normalized;

                Vector3 newPos = Vector3.Lerp(currentPos, phase.position, curveValue);
                Vector3 newRot = new Vector3(
                    Mathf.LerpAngle(currentRot.x, phase.rotation.x, curveValue),
                    Mathf.LerpAngle(currentRot.y, phase.rotation.y, curveValue),
                    Mathf.LerpAngle(currentRot.z, phase.rotation.z, curveValue)
                );

                SetPoseOverride(newPos, newRot, curveValue);
                yield return null;
            }
            SetPoseOverride(phase.position, phase.rotation, 1f);
            currentPos = phase.position;
            currentRot = phase.rotation;

            if (phase.dealDamage)
            {
                int hits = ResolveHits(heavy, forceBackstabAttempt);
                if (hits > 0) AddKick(hitKickPosition, hitKickRotation);
                else AddKick(missKickPosition, missKickRotation);
            }
        }
        float returnT = 0f;
        Vector3 startPos = currentPos;
        Vector3 startRot = currentRot;

        while (returnT < returnDuration)
        {
            returnT += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, returnT / Mathf.Max(0.001f, returnDuration));
            Vector3 pos = Vector3.Lerp(startPos, hipPosition, k);
            Vector3 rot = new Vector3(
                Mathf.LerpAngle(startRot.x, hipRotation.x, k),
                Mathf.LerpAngle(startRot.y, hipRotation.y, k),
                Mathf.LerpAngle(startRot.z, hipRotation.z, k)
            );
            SetPoseOverride(pos, rot, 1f - k);
            yield return null;
        }

        ClearPoseOverride();
        nextStrikeTime = Time.time + (heavy ? heavyCooldown : lightCooldown);
        isStriking = false;
        strikeRoutine = null;
    }

    int ResolveHits(bool heavy, bool forceBackstabAttempt)
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 origin = cam.position;
        Vector3 direction = cam.forward;

        lastHitOrigin = origin;
        lastHitDirection = direction;

        List<RaycastHit> hits = heavy
            ? GatherArcHits(cam, origin)
            : GatherThrustHits(origin, direction);

        var touchedRoots = new HashSet<GameObject>();
        GameObject self = Controller != null ? Controller.gameObject : gameObject;
        int limit = heavy ? maxTargetsPerHeavy : maxTargetsPerLight;
        int applied = 0;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;

            var target = hit.collider.GetComponentInParent<CharacterHealth>();
            GameObject root = target != null ? target.gameObject : hit.collider.gameObject;
            if (hit.collider.transform.IsChildOf(self.transform)) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (touchedRoots.Contains(root)) continue;

            touchedRoots.Add(root);

            float damage = heavy ? heavyDamage : lightDamage;
            bool backstab = IsBackstab(hit.collider);
            if (backstab) damage = heavyDamage * backstabMultiplier;

            bool damagedLiving = DealDamage(hit.collider, damage, hit.point, direction);

            SpawnImpact(damagedLiving, hit.point, hit.normal);
            PlaySound(damagedLiving ? hitFleshSound : hitSurfaceSound, soundVolume);

            if (hitNoiseRadius > 0f)
                NoiseSystem.Emit(hit.point, hitNoiseRadius, self, damagedLiving ? 0.7f : 0.4f);

            applied++;

            if (!damagedLiving)
            {
                if (!heavy) break;
                continue;
            }

            if (applied >= limit) break;
        }

        return applied;
    }

    List<RaycastHit> GatherThrustHits(Vector3 origin, Vector3 direction)
    {
        RaycastHit[] hits = Physics.SphereCastAll(origin, hitRadius, direction, range, hitMask,
                                                 QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return new List<RaycastHit>(hits);
    }

    List<RaycastHit> GatherArcHits(Transform cam, Vector3 origin)
    {
        var result = new List<RaycastHit>();
        var seenColliders = new HashSet<Collider>();

        int rays = Mathf.Max(3, swingRayCount);
        float half = swingArcAngle * 0.5f;

        for (int i = 0; i < rays; i++)
        {
            float t = rays == 1 ? 0.5f : i / (float)(rays - 1);
            float angle = Mathf.Lerp(half, -half, t);
            Vector3 dir = Quaternion.AngleAxis(angle, cam.up) * cam.forward;

            RaycastHit[] hits = Physics.SphereCastAll(origin, swingRayRadius, dir, swingRange,
                                                     hitMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (!seenColliders.Add(hit.collider)) continue;
                result.Add(hit);
            }
        }

        return result;
    }

    bool DealDamage(Collider col, float amount, Vector3 point, Vector3 direction)
    {
        GameObject attacker = Controller != null ? Controller.gameObject : gameObject;

        var aiTarget = col.GetComponentInParent<IDamageable>();
        if (aiTarget != null)
        {
            if (!aiTarget.IsAlive) return false;
            aiTarget.TakeDamage(new DamageInfo(amount, point, direction, attacker));
            return true;
        }

        return false;
    }

    bool IsBackstab(Collider col)
    {
        if (backstabMultiplier <= 1f) return false;

        var health = col.GetComponentInParent<CharacterHealth>();
        Transform target = health != null ? health.transform : col.transform;
        Vector3 toPlayer = transform.position - target.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f) return false;

        Vector3 targetForward = target.forward;
        targetForward.y = 0f;

        return Vector3.Angle(targetForward, toPlayer.normalized) > backstabAngle;
    }

    void SpawnImpact(bool living, Vector3 point, Vector3 normal)
    {
        GameObject prefab = living ? fleshImpactPrefab : surfaceImpactPrefab;
        if (prefab == null) return;

        GameObject fx = Instantiate(prefab, point, Quaternion.LookRotation(normal));
        Destroy(fx, impactLifetime);
    }

    void DisableHeldColliders()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c != null) c.enabled = false;

        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawHitGizmo) return;

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 origin = Application.isPlaying ? lastHitOrigin : cam.position;
        Vector3 dir = Application.isPlaying ? lastHitDirection : cam.forward;

        Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(origin + dir * range, hitRadius);
        Gizmos.DrawLine(origin, origin + dir * range);

        Gizmos.color = new Color(0.95f, 0.75f, 0.25f, 0.5f);
        int rays = Mathf.Max(3, swingRayCount);
        float half = swingArcAngle * 0.5f;

        for (int i = 0; i < rays; i++)
        {
            float t = rays == 1 ? 0.5f : i / (float)(rays - 1);
            float angle = Mathf.Lerp(half, -half, t);
            Vector3 arcDir = Quaternion.AngleAxis(angle, cam.up) * dir;

            Gizmos.DrawLine(origin, origin + arcDir * swingRange);
            Gizmos.DrawWireSphere(origin + arcDir * swingRange, swingRayRadius);
        }
    }

    [ContextMenu("Захватить текущую позу как фазу тычка")]
    void CaptureCurrentPoseAsLightPhase()
    {
        if (lightAttackPhases == null) lightAttackPhases = new List<AttackPhase>();
        lightAttackPhases.Add(new AttackPhase
        {
            phaseName = "Захваченная фаза",
            position = transform.localPosition,
            rotation = transform.localEulerAngles,
            duration = 0.2f,
            dealDamage = false
        });
        Debug.Log("Добавлена фаза тычка из текущей позы");
    }

    [ContextMenu("Захватить текущую позу как фазу размаха")]
    void CaptureCurrentPoseAsHeavyPhase()
    {
        if (heavyAttackPhases == null) heavyAttackPhases = new List<AttackPhase>();
        heavyAttackPhases.Add(new AttackPhase
        {
            phaseName = "Захваченная фаза",
            position = transform.localPosition,
            rotation = transform.localEulerAngles,
            duration = 0.2f,
            dealDamage = false
        });
        Debug.Log("Добавлена фаза размаха из текущей позы");
    }
#endif
}