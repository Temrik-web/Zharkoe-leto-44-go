using System.Collections.Generic;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>Выпущенная граната: летит, тикает, взрывается. Урон падает к краю радиуса, стена укрывает.</summary>
[DisallowMultipleComponent]
public class ThrownGrenade : MonoBehaviour, IDamageable
{
    [Header("Запал")]
    [Tooltip("Через сколько секунд после броска рванёт.")]
    [Min(0.1f)] public float fuseTime = 3.8f;

    [Tooltip("Взрываться при первом же ударе о поверхность (ударный взрыватель).")]
    public bool explodeOnImpact = false;

    [Tooltip("Минимальная скорость удара для срабатывания ударного взрывателя.")]
    public float impactSpeedThreshold = 4f;

    [Header("Физика полёта")]
    [Tooltip("Максимальная угловая скорость в полёте, рад/с. " +
             "Ограничение убирает «бешеное» кручение после удара о землю.")]
    [Min(0f)] public float maxAngularVelocity = 12f;

    [Tooltip("Гасить вращение и скорость после первого удара: " +
             "граната отскакивает, а не улетает волчком.")]
    public bool dampenOnImpact = true;

    [Tooltip("Сколько скорости остаётся после удара. 0.35 — заметно тормозит.")]
    [Range(0f, 1f)] public float impactVelocityDamping = 0.35f;

    [Tooltip("Через сколько секунд после удара граната замирает на месте " +
             "(isKinematic). 0 — не замирать, катиться до взрыва.")]
    [Min(0f)] public float settleDelay = 0.3f;

    [Tooltip("Замирать только если граната уже почти не двигается. " +
             "Снимай галочку, если хочешь жёсткую остановку строго по таймеру.")]
    public bool settleOnlyWhenSlow = true;

    [Tooltip("Скорость, ниже которой граната считается остановившейся, м/с.")]
    [Min(0f)] public float settleSpeedThreshold = 0.55f;

    [Header("Защита от провала под землю")]
    [Tooltip("Проверять пол под гранатой при старте и поднимать её на поверхность.")]
    public bool snapToGroundOnStart = true;

    [Tooltip("Слои пола для проверки.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Как глубоко вниз искать пол от точки появления, метры.")]
    [Min(0.1f)] public float groundSearchDistance = 3f;

    [Tooltip("Зазор над полом, чтобы модель не касалась текстуры, метры.")]
    [Min(0f)] public float groundClearance = 0.02f;

    [Tooltip("Следить за гранатой в полёте и выталкивать её наверх, если утонула. " +
             "Лечит случай, когда граната легла в пол уже после отскока.")]
    public bool keepAboveGroundAlways = true;

    [Header("Урон")]
    public float damage = 110f;
    public float damageRadius = 6f;

    [Tooltip("Доля урона на самой границе радиуса. 0 — на краю урона нет вовсе.")]
    [Range(0f, 1f)] public float edgeDamageFactor = 0.15f;

    [Tooltip("Слои, по которым проверяется, не закрыта ли цель стеной.")]
    public LayerMask coverMask = ~0;

    [Tooltip("Слои, среди которых ищутся цели.")]
    public LayerMask targetMask = ~0;

    [Header("Физика взрыва")]
    public float explosionForce = 520f;
    public float explosionRadius = 6f;
    public float explosionUpwardModifier = 0.4f;

    [Header("Эффекты")]
    public GameObject explosionEffectPrefab;
    public float effectLifetime = 4f;
    public AudioClip explosionSound;
    [Range(0f, 1f)] public float explosionVolume = 1f;
    public AudioClip bounceSound;
    [Range(0f, 1f)] public float bounceVolume = 0.5f;

    [Header("Тряска камеры")]
    public float shakeIntensity = 0.55f;
    public float shakeDuration = 0.45f;
    [Tooltip("Дальше этого расстояния взрыв камеру уже не трясёт.")]
    public float shakeRange = 18f;

    [Header("Шум для ИИ")]
    [Tooltip("Радиус, в котором враги услышат взрыв.")]
    public float noiseRadius = 45f;

    [Header("Детонация от внешнего воздействия")]
    [Tooltip("Через сколько секунд после пули/удара/близкого взрыва сдетонировать, " +
             "если свой запал дольше. Реалистичная пауза срабатывания.")]
    [Min(0f)] public float impactDetonationDelay = 0.12f;

    [Header("Отладка")]
    public bool logDamage = false;

    /// <summary>Кто бросил. Нужно, чтобы граната не «попадала» в саму себя и в руку.</summary>
    public GameObject Thrower { get; private set; }

    private float fuseTimer;
    private bool hasExploded;
    private bool bounceSoundPlayed;

    private Rigidbody body;
    private bool hasLanded;
    private float settleTimer;
    private bool isSettled;
    public void Launch(Vector3 velocity, Vector3 angularVelocity, GameObject thrower, float fuse = -1f)
    {
        Thrower = thrower;
        if (fuse > 0f) fuseTime = fuse;
        fuseTimer = fuseTime;
        Rigidbody rb = EnsureBody();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Без лимита мелкий коллайдер после удара получает бешеное вращение
        if (maxAngularVelocity > 0f) rb.maxAngularVelocity = maxAngularVelocity;
        rb.velocity = velocity;
        rb.angularVelocity = ClampAngular(angularVelocity);
        hasLanded = false;
        isSettled = false;
        settleTimer = 0f;
        IgnoreThrowerCollisions();
    }

    void Awake()
    {
        // Копия из рук наследует слой HeldObjects, который исключён из HitMask оружия, —
        // пули проходили бы сквозь гранату и выстрелом её было бы не взорвать.
        int heldLayer = LayerMask.NameToLayer("HeldObjects");
        if (heldLayer >= 0 && gameObject.layer == heldLayer)
            SetLayerRecursively(gameObject, 0);
        if (fuseTimer <= 0f) fuseTimer = fuseTime;
        EnsureBody();
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // Пуля (своя, вражеская) или чужой взрыв — мгновенная детонация.
    // Цепные взрывы лежащих рядом гранат получаются сами собой.
    bool IDamageable.IsAlive => !hasExploded;

    Team IDamageable.Team
    {
        get
        {
            if (Thrower != null)
            {
                CharacterHealth health = Thrower.GetComponentInParent<CharacterHealth>();
                if (health != null) return health.Team;
            }
            return Team.Axis;
        }
    }

    void IDamageable.TakeDamage(DamageInfo damage)
    {
        if (hasExploded) return;
        // Внешнее воздействие не глушит свой запал, а ускоряет его до паузы срабатывания.
        if (impactDetonationDelay <= 0f) Explode();
        else fuseTimer = Mathf.Min(fuseTimer, impactDetonationDelay);
    }

    void Start()
    {
        if (snapToGroundOnStart) SnapAboveGround();
    }
    // Берём самую высокую поверхность не выше низа гранаты — иначе закатившуюся под стол телепортирует на столешницу
    void SnapAboveGround()
    {
        Collider col = FindOwnCollider();
        float bottomY = col != null ? col.bounds.min.y : transform.position.y;
        float lift = Mathf.Max(0.5f, GetColliderHeight(col));
        Vector3 origin = transform.position + Vector3.up * lift;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, lift + groundSearchDistance,
                                               groundMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        float bestY = 0f;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (Thrower != null && hit.collider.transform.IsChildOf(Thrower.transform.root)) continue;
            if (hit.point.y > bottomY + groundClearance + 0.02f) continue;
            if (!found || hit.point.y > bestY)
            {
                bestY = hit.point.y;
                found = true;
            }
        }
        if (found) PlaceAboveSurface(bestY, col);
    }
    // Ставим по bounds.min: фактический низ с учётом поворота и масштаба
    void PlaceAboveSurface(float surfaceY, Collider col)
    {
        float bottomOffset = col != null
            ? transform.position.y - col.bounds.min.y
            : 0.08f;
        float targetY = surfaceY + bottomOffset + groundClearance;
        if (transform.position.y >= targetY) return;
        transform.position = new Vector3(transform.position.x, targetY, transform.position.z);
        if (body != null && !body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
    Collider FindOwnCollider()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (c != null && c.enabled && !c.isTrigger) return c;
        return GetComponentInChildren<Collider>();
    }
    float GetColliderHeight(Collider col) =>
        col != null ? col.bounds.size.y : 0.16f;
    void Update()
    {
        if (hasExploded) return;
        UpdateSettling();
        fuseTimer -= Time.deltaTime;
        if (fuseTimer <= 0f) Explode();
    }
    // Kinematic вместо Sleep: спящий Rigidbody просыпается от любого касания
    void UpdateSettling()
    {
        if (!hasLanded || isSettled || settleDelay <= 0f || body == null) return;
        settleTimer += Time.deltaTime;
        if (settleTimer < settleDelay) return;
        if (settleOnlyWhenSlow && body.velocity.magnitude > settleSpeedThreshold)
            return;
        if (keepAboveGroundAlways) SnapAboveGround();

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        isSettled = true;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (!bounceSoundPlayed && bounceSound != null && impactSpeed > 1.5f)
        {
            bounceSoundPlayed = true;
            AudioSource.PlayClipAtPoint(bounceSound, transform.position, bounceVolume);
        }
        if (explodeOnImpact && impactSpeed >= impactSpeedThreshold)
        {
            Explode();
            return;
        }
        if (!hasLanded)
        {
            hasLanded = true;
            settleTimer = 0f;
        }
        if (keepAboveGroundAlways) SnapAboveGround();
        if (dampenOnImpact && body != null && !body.isKinematic)
        {
            body.velocity *= impactVelocityDamping;
            body.angularVelocity = ClampAngular(body.angularVelocity * impactVelocityDamping);
        }
    }

    Rigidbody EnsureBody()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();

        if (maxAngularVelocity > 0f) body.maxAngularVelocity = maxAngularVelocity;
        return body;
    }

    Vector3 ClampAngular(Vector3 angular)
    {
        if (maxAngularVelocity <= 0f) return angular;
        return Vector3.ClampMagnitude(angular, maxAngularVelocity);
    }

    public void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;
        Vector3 center = transform.position;
        // Коллайдеры гасим до урона: OverlapSphere иначе найдёт саму гранату, а рейкаст примет корпус за стену
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (c != null) c.enabled = false;
        // Модель прячем сразу: Destroy в конце кадра, иначе граната видна внутри вспышки
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (r != null) r.enabled = false;
        SpawnEffects(center);
        ApplyDamage(center);
        ApplyPhysics(center);
        ShakeCamera(center);
        if (noiseRadius > 0f)
            NoiseSystem.Emit(center, noiseRadius, Thrower, 1f);
        Destroy(gameObject);
    }
    void SpawnEffects(Vector3 center)
    {
        if (explosionEffectPrefab != null)
        {
            GameObject fx = Instantiate(explosionEffectPrefab, center, Quaternion.identity);
            Destroy(fx, effectLifetime);
        }
        if (explosionSound != null)
            AudioSource.PlayClipAtPoint(explosionSound, center, explosionVolume);
    }
    // Один объект бьёт один раз: без словаря враг с коллайдерами на конечностях получал бы урон кратно их числу
    void ApplyDamage(Vector3 center)
    {
        Collider[] overlapped = Physics.OverlapSphere(center, damageRadius, targetMask,
                                                     QueryTriggerInteraction.Collide);
        var best = new Dictionary<ZharkoeLeto44.AI.IDamageable, KeyValuePair<Collider, float>>();
        foreach (Collider col in overlapped)
        {
            if (col == null) continue;
            var target = col.GetComponentInParent<ZharkoeLeto44.AI.IDamageable>();
            if (target == null || !target.IsAlive) continue;
            Vector3 targetPoint = col.ClosestPoint(center);
            if (IsBehindCover(center, targetPoint, col)) continue;
            float dealt = DamageAtDistance(Vector3.Distance(center, targetPoint));
            if (dealt <= 0.01f) continue;
            if (best.TryGetValue(target, out var current) && current.Value >= dealt) continue;
            best[target] = new KeyValuePair<Collider, float>(col, dealt);
        }
        foreach (var pair in best)
        {
            Collider col = pair.Value.Key;
            float dealt = pair.Value.Value;
            Vector3 targetPoint = col.ClosestPoint(center);
            if (DealDamage(col, dealt, center, targetPoint) && logDamage)
                Debug.Log($"[Grenade] {col.name} получил {dealt:0.#} урона.");
        }
    }

    bool DealDamage(Collider col, float amount, Vector3 center, Vector3 targetPoint)
    {
        Vector3 direction = (targetPoint - center).normalized;
        var aiTarget = col.GetComponentInParent<ZharkoeLeto44.AI.IDamageable>();
        if (aiTarget != null)
        {
            if (!aiTarget.IsAlive) return false;
            aiTarget.TakeDamage(new DamageInfo(amount, targetPoint, direction, Thrower));
            return true;
        }
        return false;
    }
    float DamageAtDistance(float distance)
    {
        if (damageRadius <= 0.01f) return damage;
        float t = Mathf.Clamp01(distance / damageRadius);
        return damage * Mathf.Lerp(1f, edgeDamageFactor, t);
    }
    bool IsBehindCover(Vector3 center, Vector3 targetPoint, Collider target)
    {
        Vector3 dir = targetPoint - center;
        float distance = dir.magnitude;
        if (distance < 0.05f) return false;
        RaycastHit[] hits = Physics.RaycastAll(center, dir.normalized, distance, coverMask,
                                               QueryTriggerInteraction.Ignore);
        // RaycastAll вразброс — сортируем, иначе дальняя стена закроет ближнюю цель
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        var targetHealth = target.GetComponentInParent<ZharkoeLeto44.AI.IDamageable>();

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider == target) return false;
            if (targetHealth != null &&
                hit.collider.GetComponentInParent<ZharkoeLeto44.AI.IDamageable>() == targetHealth) return false;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (Thrower != null && hit.collider.transform.IsChildOf(Thrower.transform)) continue;
            return true;
        }
        return false;
    }

    void ApplyPhysics(Vector3 center)
    {
        if (explosionForce <= 0f) return;

        foreach (Collider col in Physics.OverlapSphere(center, explosionRadius))
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            rb.AddExplosionForce(explosionForce, center, explosionRadius,
                                 explosionUpwardModifier, ForceMode.Impulse);
        }
    }

    void ShakeCamera(Vector3 center)
    {
        if (shakeIntensity <= 0f) return;

        var fps = FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
        if (fps == null) return;

        float distance = Vector3.Distance(center, fps.transform.position);
        if (distance > shakeRange) return;

        float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, shakeRange));
        Vector3 direction = (fps.transform.position - center).normalized;

        fps.TriggerCameraShake(shakeIntensity * falloff, shakeDuration, direction);
    }

    // Без этого граната застревает в капсуле игрока и падает под ноги
    void IgnoreThrowerCollisions()
    {
        if (Thrower == null) return;

        Collider[] own = GetComponentsInChildren<Collider>();
        Collider[] throwerColliders = Thrower.GetComponentsInChildren<Collider>();

        foreach (Collider a in own)
        {
            if (a == null) continue;
            foreach (Collider b in throwerColliders)
            {
                if (b == null || b == a) continue;
                Physics.IgnoreCollision(a, b, true);
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, damageRadius);
    }
#endif
}
