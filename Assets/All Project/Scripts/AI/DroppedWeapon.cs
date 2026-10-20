using UnityEngine;

namespace ZharkoeLeto44.AI
{
    [DisallowMultipleComponent]
    public sealed class DroppedWeapon : MonoBehaviour
    {
        [Header("Падение")]
        [SerializeField, Min(0f)] private float gravityDelay = 0.5f;
        [SerializeField, Min(0f)] private float gravityDelayRandom = 0.2f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0f)] private float watchDuration = 6f;
        [SerializeField, Min(0.05f)] private float fallThroughTolerance = 0.3f;
        [SerializeField, Min(1f)] private float groundSearchHeight = 30f;

        [Header("Физика")]
        [Tooltip("Материал трения. Пусто — используется материал по умолчанию из " +
                 "Project Settings -> Physics. Задай скользкий/шершавый, если оружие " +
                 "слишком долго скользит по полу.")]
        [SerializeField] private PhysicMaterial physicMaterial;
        [SerializeField] private bool autoCenterOfMass = true;
        [SerializeField] private Vector3 centerOfMassOffset = Vector3.zero;
        [SerializeField, Range(0f, 1f)] private float velocityRandomness = 0.3f;
        [SerializeField, Range(0f, 1f)] private float spinRandomness = 0.3f;

        [Header("Заморозка")]
        [SerializeField] private bool freezeWhenSettled = true;
        [SerializeField, Min(0f)] private float freezeAfterCalm = 0.5f;
        [SerializeField, Min(0f)] private float calmSpeedThreshold = 0.2f;
        [SerializeField, Min(0f)] private float calmAngularThreshold = 0.2f;
        [SerializeField, Min(1f)] private float forceFreezeAfter = 10f;
        [Tooltip("Скорость опрокидывания воткнутого ствола (рад/с): успокоился стоя " +
                 "на дуле — получил толчок вбок, а не заморозку вертикально.")]
        [SerializeField, Min(0f)] private float uprightTipRate = 1.5f;

        [Header("Страховка от провала")]
        [Tooltip("Возвращать оружие наверх, если оно ушло под пол. " +
                 "Выключи, если карта многоуровневая и оружие должно падать в проёмы.")]
        [SerializeField] private bool guardAgainstFallThrough = true;

        [Header("Жизнь")]
        [SerializeField, Min(0f)] private float lifetime = 30f;

        private Rigidbody body;
        private Vector3 initialVelocity;
        private Vector3 initialSpin;
        private bool applySpin = true;
        private float actualDelay;
        private float delayTimer;
        private float watchTimer;
        private bool gravityEnabled;
        private bool hasTouchedGround;
        private float calmTimer;
        private float uprightTimer;
        private float aliveSinceGravity;
        private bool isFrozen;
        private Collider[] ownColliders;
        private Vector3[] groundCheckPoints;
        private int rescueAttempts;
        private bool initialized;

        public void Initialize(float gravityDelay, Vector3 launchVelocity, Vector3 spin,
                               LayerMask groundMask, float lifetime, bool applySpin = true)
        {
            this.gravityDelay = Mathf.Max(0f, gravityDelay);
            this.groundMask = groundMask;
            this.lifetime = Mathf.Max(0f, lifetime);
            this.applySpin = applySpin;

            initialVelocity = launchVelocity;
            initialSpin = spin;
            actualDelay = this.gravityDelay + Random.Range(0f, gravityDelayRandom);
            initialized = true;

            PrepareBody();
            CacheGroundPoints();

            if (this.lifetime > 0f) Destroy(gameObject, this.lifetime);
        }

        private void Awake() => PrepareBody();

        private void Start()
        {
            if (initialized) return;

            actualDelay = gravityDelay + Random.Range(0f, gravityDelayRandom);
            initialized = true;

            CacheGroundPoints();
            if (lifetime > 0f) Destroy(gameObject, lifetime);
        }

        private void PrepareBody()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();

            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            RefreshColliders();
            if (ownColliders.Length == 0)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                Renderer rend = GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    box.size = rend.bounds.size;
                    box.center = rend.bounds.center - transform.position;
                }
                else
                {
                    box.size = new Vector3(1.0f, 0.2f, 0.3f);
                }
                ownColliders = new Collider[] { box };
            }

            if (physicMaterial != null)
            {
                foreach (var col in ownColliders) col.material = physicMaterial;
            }

            if (!autoCenterOfMass)
            {
                body.centerOfMass += centerOfMassOffset;
            }

            SetCollidersEnabled(false);
        }

        private void SetCollidersEnabled(bool enabled)
        {
            RefreshColliders();
            foreach (var col in ownColliders)
            {
                if (col == null) continue;
                col.enabled = enabled;
            }
        }

        /// <summary>
        /// БАГФИКС: ownColliders кешируется в PrepareBody, а между кешированием
        /// и использованием (пауза перед гравитацией ~0.5 c) любой коллайдер
        /// могут уничтожить (префаб оружия, подгонка коллайдера, подбор).
        /// Доступ к col.bounds у уничтоженного коллайдера бросает
        /// MissingReferenceException при убийстве врага. Поэтому перед каждым
        /// использованием выкидываем мёртвые ссылки и добираем новые.
        /// GetComponentsInChildren вызываем с includeInactive=true: иначе
        /// выключенные нами же коллайдеры терялись из кэша и PrepareBody
        /// плодил лишний BoxCollider при повторном вызове из Initialize.
        /// </summary>
        private void RefreshColliders()
        {
            if (ownColliders != null && ownColliders.Length > 0)
            {
                bool hasDead = false;
                foreach (var col in ownColliders)
                {
                    if (col == null) { hasDead = true; break; }
                }

                if (hasDead)
                {
                    var alive = new System.Collections.Generic.List<Collider>(ownColliders.Length);
                    foreach (var col in ownColliders)
                    {
                        if (col != null) alive.Add(col);
                    }
                    ownColliders = alive.ToArray();
                }
            }

            if (ownColliders == null || ownColliders.Length == 0)
                ownColliders = GetComponentsInChildren<Collider>(true);
        }

        private void CacheGroundPoints()
        {
            RefreshColliders();
            if (ownColliders == null || ownColliders.Length == 0) return;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool hasAny = false;
            foreach (var col in ownColliders)
            {
                if (col == null) continue;
                bounds.Encapsulate(col.bounds);
                hasAny = true;
            }
            if (!hasAny) return;

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            var points = new System.Collections.Generic.List<Vector3>
            {
                transform.InverseTransformPoint(center),
                transform.InverseTransformPoint(center + Vector3.up * extents.y * 0.5f),
                transform.InverseTransformPoint(center - Vector3.up * extents.y * 0.5f),
                transform.InverseTransformPoint(center + Vector3.forward * extents.z * 0.5f),
                transform.InverseTransformPoint(center - Vector3.forward * extents.z * 0.5f),
                transform.InverseTransformPoint(center + Vector3.right * extents.x * 0.5f),
                transform.InverseTransformPoint(center - Vector3.right * extents.x * 0.5f)
            };
            groundCheckPoints = points.ToArray();
        }

        private void Update()
        {
            if (isFrozen) return;

            if (!gravityEnabled)
            {
                delayTimer += Time.deltaTime;
                if (delayTimer >= actualDelay)
                {
                    EnableGravity();
                }
                return;
            }

            aliveSinceGravity += Time.deltaTime;
            if (guardAgainstFallThrough && watchTimer < watchDuration)
            {
                watchTimer += Time.deltaTime;
                GuardAgainstFallThrough();
            }

            UpdateFreeze();
        }

        private void EnableGravity()
        {
            gravityEnabled = true;
            ResolveInitialOverlap();
            SetCollidersEnabled(true);

            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null) return;
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Vector3 vel = initialVelocity + Random.insideUnitSphere * velocityRandomness;
            body.AddForce(vel * body.mass, ForceMode.Impulse);
            if (applySpin)
                body.angularVelocity = initialSpin + Random.insideUnitSphere * spinRandomness;
        }

        private void ResolveInitialOverlap()
        {
            RefreshColliders();
            if (ownColliders == null || ownColliders.Length == 0) return;

            Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);
            bool hasAny = false;
            foreach (var col in ownColliders)
            {
                if (col == null) continue;
                combinedBounds.Encapsulate(col.bounds);
                hasAny = true;
            }
            if (!hasAny) return;

            int maxIterations = 20;
            float stepUp = 0.1f;

            for (int i = 0; i < maxIterations; i++)
            {
                if (!HasForeignOverlap(combinedBounds.center, combinedBounds.extents)) break;

                transform.position += Vector3.up * stepUp;
                foreach (var col in ownColliders)
                {
                    if (col == null) continue;
                    combinedBounds.Encapsulate(col.bounds);
                }
            }

            if (HasForeignOverlap(combinedBounds.center, combinedBounds.extents))
            {
                Vector3 randomDir = Random.onUnitSphere;
                randomDir.y = Mathf.Abs(randomDir.y);
                transform.position += randomDir * 0.3f;
            }
        }

        private bool HasForeignOverlap(Vector3 center, Vector3 extents)
        {
            if (extents.sqrMagnitude <= 0f) return false;
            Collider[] overlaps = Physics.OverlapBox(center, extents,
                transform.rotation, groundMask, QueryTriggerInteraction.Ignore);
            foreach (var other in overlaps)
            {
                if (other == null) continue;
                if (other.transform != transform && !other.transform.IsChildOf(transform))
                    return true;
            }
            return false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!gravityEnabled) return;
            hasTouchedGround = true;
        }

        private bool IsGrounded()
        {
            if (groundCheckPoints == null) CacheGroundPoints();
            foreach (var localPoint in groundCheckPoints)
            {
                Vector3 worldPoint = transform.TransformPoint(localPoint);
                if (Physics.Raycast(worldPoint + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit,
                                    0.25f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform)) continue;
                    return true;
                }
            }
            return false;
        }

        private bool IsNearGround(out Vector3 groundPoint, out Vector3 normal)
        {
            groundPoint = transform.position;
            normal = Vector3.up;
            float minDist = float.MaxValue;
            bool found = false;

            if (groundCheckPoints == null) CacheGroundPoints();

            foreach (var localPoint in groundCheckPoints)
            {
                Vector3 worldPoint = transform.TransformPoint(localPoint);
                if (Physics.Raycast(worldPoint + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit,
                                    0.5f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform)) continue;

                    if (hit.distance < minDist)
                    {
                        minDist = hit.distance;
                        groundPoint = hit.point;
                        normal = hit.normal;
                        found = true;
                    }
                }
            }
            return found;
        }

        private void UpdateFreeze()
        {
            if (!freezeWhenSettled || body == null || body.isKinematic) return;

            bool calmLinear = body.velocity.magnitude <= calmSpeedThreshold;
            bool calmAngular = body.angularVelocity.magnitude <= calmAngularThreshold;
            bool grounded = IsGrounded();

            if (!grounded && calmLinear && calmAngular)
            {
                Vector3 groundPoint;
                Vector3 normal;
                if (IsNearGround(out groundPoint, out normal))
                {
                    float bottomOffset = transform.position.y - GetBottomY();
                    Vector3 targetPos = new Vector3(transform.position.x,
                                                    groundPoint.y + bottomOffset + 0.02f,
                                                    transform.position.z);
                    if (targetPos.y < transform.position.y)
                        transform.position = Vector3.MoveTowards(transform.position, targetPos, 0.05f);
                    grounded = IsGrounded();
                }
            }

            Vector3 barrel = LongAxisWorldFull();
            bool isLaying = Mathf.Abs(barrel.y) < 0.7f;
            bool calmLongEnough;
            if (grounded && calmLinear && calmAngular && isLaying)
            {
                calmTimer += Time.deltaTime;
                calmLongEnough = calmTimer >= freezeAfterCalm;
            }
            else
            {
                calmTimer = 0f;
                calmLongEnough = false;
            }

            if (grounded && calmLinear && calmAngular && !isLaying)
            {
                uprightTimer += Time.deltaTime;
                if (uprightTimer >= 1f)
                {
                    uprightTimer = 0f;
                    TipUpright(barrel);
                }
            }
            else uprightTimer = 0f;

            bool timedOut = aliveSinceGravity >= forceFreezeAfter && grounded && isLaying;
            bool expired = aliveSinceGravity >= forceFreezeAfter + 10f;

            if (!calmLongEnough && !timedOut && !expired) return;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
            isFrozen = true;
        }

        private Vector3 LongAxisWorldFull()
        {
            Bounds bounds = default;
            bool hasBounds = false;
            if (ownColliders != null)
                foreach (Collider col in ownColliders)
                {
                    if (col == null || !col.enabled) continue;
                    if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
                    else bounds.Encapsulate(col.bounds);
                }
            if (!hasBounds) return transform.forward;
            Vector3 size = bounds.size;
            if (size.magnitude < 0.05f) return transform.forward;
            return (size.x >= size.y && size.x >= size.z) ? Vector3.right
                : (size.y >= size.z ? Vector3.up : Vector3.forward);
        }

        private void TipUpright(Vector3 barrel)
        {
            if (body == null || body.isKinematic || uprightTipRate <= 0f) return;
            Vector3 axis = Vector3.Cross(barrel, Vector3.up);
            if (axis.sqrMagnitude < 0.01f) axis = transform.right;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.right;
            body.WakeUp();
            body.angularVelocity = axis.normalized * uprightTipRate *
                (Random.value < 0.5f ? 1f : -1f);
        }

        private void GuardAgainstFallThrough()
        {
            if (body == null || body.isKinematic) return;
            if (rescueAttempts >= 3) return;

            if (!TryGetGroundY(out float groundY)) return;
            float bottomY = GetBottomY();
            if (bottomY >= groundY - fallThroughTolerance)
            {
                CheckStuckInAir(groundY);
                return;
            }

            rescueAttempts++;

            float lift = groundY + (transform.position.y - bottomY) + 0.05f;
            transform.position = new Vector3(transform.position.x, lift, transform.position.z);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (rescueAttempts >= 3)
            {
                hasTouchedGround = true;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
                isFrozen = true;

                Debug.LogWarning($"[DroppedWeapon] {name} трижды провалилось под пол — " +
                                 "закреплено на месте. Проверь коллайдер и Ground Mask.", this);
            }
        }

        private void CheckStuckInAir(float groundY)
        {
            if (hasTouchedGround) return;
            if (aliveSinceGravity < 0.4f) return;

            bool stuck = body.velocity.magnitude < 0.05f && body.angularVelocity.magnitude < 0.05f;
            bool highAbove = GetBottomY() > groundY + 0.4f;

            if (!stuck || !highAbove) return;

            body.WakeUp();
            body.AddForce((Vector3.down * 3f + Random.insideUnitSphere * 0.8f) * body.mass,
                          ForceMode.Impulse);
        }

        private float GetBottomY()
        {
            if (ownColliders == null || ownColliders.Length == 0) return transform.position.y;

            float lowest = float.MaxValue;
            foreach (Collider c in ownColliders)
            {
                if (c == null || !c.enabled) continue;
                lowest = Mathf.Min(lowest, c.bounds.min.y);
            }

            return lowest < float.MaxValue ? lowest : transform.position.y;
        }

        private bool TryGetGroundY(out float groundY)
        {
            groundY = 0f;

            if (groundCheckPoints == null) CacheGroundPoints();

            float sum = 0f;
            int count = 0;

            foreach (var localPoint in groundCheckPoints)
            {
                Vector3 worldPoint = transform.TransformPoint(localPoint);
                if (Physics.Raycast(worldPoint + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit,
                                    groundSearchHeight, groundMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(transform)) continue;
                    sum += hit.point.y;
                    count++;
                }
            }

            if (count == 0) return false;

            groundY = sum / count;
            return true;
        }
    }
}
