using System.Collections;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
    [DisallowMultipleComponent]
    public sealed class HitscanWeapon : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform muzzle;
        [Tooltip("Вспышка выстрела (ParticleSystem у дула). Играет каждый выстрел. " +
              "Нет готовой — кнопка «Создать вспышку выстрела» ниже сделает её сама.")]
        [SerializeField] private ParticleSystem muzzleFlash;
        [Tooltip("Сколько частиц выпускать за выстрел (Emit — держит любой темп стрельбы).")]
        [SerializeField, Min(1)] private int muzzleFlashCount = 18;
        [Tooltip("Свет вспышки (Point Light у дула, intensity 0, выкл). Мигнёт каждый выстрел.")]
        [SerializeField] private Light muzzleLight;
        [Tooltip("Пиковая яркость света вспышки.")]
        [SerializeField, Min(0f)] private float muzzleFlashLightIntensity = 6f;
        [Tooltip("Сколько гаснет свет вспышки, сек.")]
        [SerializeField, Min(0.01f)] private float muzzleFlashDuration = 0.07f;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip shotSound;
        [SerializeField] private AudioClip reloadSound;
        [Tooltip("Предупредить в консоли, если нет звука выстрела или вспышки.")]
        [SerializeField] private bool warnAboutMissingAudio = true;

        [Header("Impact")]
        [Tooltip("Префаб эффекта попадания (пыль/искры). Необязателен.")]
        [SerializeField] private GameObject impactEffect;
        [SerializeField, Min(0f)] private float impactEffectLifetime = 3f;

        [Header("Ballistics")]
        [SerializeField, Min(1f)] private float damage = 25f;
        [SerializeField, Min(1f)] private float range = 150f;
        [SerializeField, Min(0f)] private float spreadAngle = 1.25f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Ignore;

        [Header("Дружественный огонь")]
        [Tooltip("Разрешить ли стрелять по союзникам. false (по умолчанию) — пуля проходит " +
                 "сквозь своих: урона союзникам нет, солдаты в шеренге не мешают друг другу.")]
        [SerializeField] private bool allowFriendlyFire = false;

        [Header("Magazine")]
        [SerializeField, Min(1)] private int magazineCapacity = 32;
        [SerializeField, Min(0)] private int startingReserve = 128;
        [SerializeField, Min(0.05f)] private float reloadDuration = 2.4f;

        [Header("Fire")]
        [Tooltip("Выстрелов в минуту.")]
        [SerializeField, Min(1f)] private float roundsPerMinute = 500f;

        [Header("Audible noise")]
        [SerializeField, Min(0f)] private float shotNoiseRadius = 35f;
        [Tooltip("Множитель радиуса слышимости выстрела.")]
        [SerializeField, Min(1f)] private float shotNoiseMultiplier = 4f;

        [Header("Tracer")]
        [SerializeField] private bool showTracer = true;
        [SerializeField] private Color tracerColor = new(1f, 0.8f, 0.25f);
        [SerializeField, Min(0.005f)] private float tracerWidth = 0.025f;
        [SerializeField, Min(0.01f)] private float tracerLifetime = 0.06f;
        [Tooltip("Размер пула трассеров. Трассеры берутся из пула — новые объекты " +
                 "на каждый выстрел больше не создаются.")]
        [SerializeField, Min(0)] private int tracerPoolSize = 8;

        public int AmmunitionInMagazine { get; private set; }
        public int ReserveAmmunition => _reserve;
        public bool IsReloading { get; private set; }
        public bool HasAmmunition => AmmunitionInMagazine > 0 || _reserve > 0;
        public bool NeedsReload => AmmunitionInMagazine <= 0 && _reserve > 0;

        /// <summary>Оружие готово к выстрелу прямо сейчас (темп, магазин, перезарядка).</summary>
        public bool CanFireNow =>
            !IsReloading && AmmunitionInMagazine > 0 && Time.time >= _nextShotTime;

        public bool HasMuzzle => muzzle != null;
        public bool HasAudioSource => audioSource != null;
        public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;
        public Vector3 MuzzleForward => muzzle != null ? muzzle.forward : transform.forward;
        public float Range => range;
        public float DamageScale { get; set; } = 1f;

        /// <summary>
        /// Множитель СОБСТВЕННОГО разброса оружия (как DamageScale, но для разброса).
        /// ИИ считает весь разброс сам и выставляет здесь множитель: 1 = базовый
        /// Spread Angle оружия, 2 = вдвое шире, 0.5 = вдвое точнее.
        /// </summary>
        public float SpreadScale { get; set; } = 1f;

        /// <summary>Базовый разброс оружия в градусах (без учёта SpreadScale).</summary>
        public float BaseSpreadAngle => spreadAngle;

        public bool AllowFriendlyFire => allowFriendlyFire;

        public event System.Action<Vector3> Fired;
        public event System.Action ReloadStarted;
        public event System.Action ReloadFinished;

        private int _reserve;
        private float _nextShotTime;
        private Coroutine _reloadRoutine;
        private Coroutine _flashRoutine;
        private GameObject _ownerRoot;
        private GameObject _ownerCache;
        private Team _ownerTeam = Team.Axis;
        private static Material _tracerMaterial;

        // Буферы рейкастов: без аллокаций в кадре и в стрельбе.
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
        private readonly RaycastHit[] _shotCheckBuffer = new RaycastHit[16];

        // Пул трассеров: объекты не создаются и не уничтожаются на каждый выстрел.
        private LineRenderer[] _tracerPool;
        private float[] _tracerExpireAt;
        private int _tracerCursor;

        private float ShotInterval => 60f / roundsPerMinute;

        private void Awake()
        {
            ResetAmmo();
            // Прайминг частиц: при rate 0 система тихо работает, Emit добавляет вспышку.
            if (muzzleFlash != null && !muzzleFlash.isPlaying) muzzleFlash.Play();
            if (muzzleLight != null)
            {
                muzzleLight.enabled = false;
                muzzleLight.intensity = 0f;
            }
            if (warnAboutMissingAudio)
            {
                if (shotSound == null)
                    Debug.LogWarning($"[HitscanWeapon] {name}: нет звука выстрела (Shot Sound).", this);
                if (muzzleFlash == null && muzzleLight == null)
                    Debug.Log($"[HitscanWeapon] {name}: нет вспышки выстрела. " +
                        "Кнопка «Создать вспышку выстрела» в инспекторе сделает её за секунду.", this);
            }
            EnsureTracerPool();
        }

        public void SetMuzzle(Transform muzzleTransform)
        {
            if (muzzleTransform != null) muzzle = muzzleTransform;
        }
        public void SetAudioSource(AudioSource source)
        {
            if (source != null) audioSource = source;
        }
        public void SetSounds(AudioClip shot, AudioClip reload)
        {
            if (shot != null) shotSound = shot;
            if (reload != null) reloadSound = reload;
        }

        public void ResetAmmo()
        {
            AmmunitionInMagazine = magazineCapacity;
            _reserve = startingReserve;
            IsReloading = false;
        }

        public bool TryFire(Vector3 targetPoint, GameObject owner)
        {
            if (IsReloading || Time.time < _nextShotTime)
                return false;

            if (AmmunitionInMagazine <= 0)
            {
                BeginReload();
                return false;
            }

            ResolveOwner(owner);

            AmmunitionInMagazine--;
            _nextShotTime = Time.time + ShotInterval;
            Vector3 origin = MuzzlePosition;
            Vector3 toTarget = targetPoint - origin;
            Vector3 direction = toTarget.sqrMagnitude > 0.0001f
                ? ApplySpread(toTarget.normalized)
                : ApplySpread(MuzzleForward);
            origin += direction * 0.35f;
            if (audioSource != null && shotSound != null)
                audioSource.PlayOneShot(shotSound);

            PlayMuzzleFx();

            NoiseSystem.Emit(MuzzlePosition, shotNoiseRadius * shotNoiseMultiplier, owner, 1f);

            Vector3 endPoint = origin + direction * range;
            bool hitSomething = false;

            // RaycastNonAlloc + ручная сортировка по дистанции: RaycastAll каждый раз
            // создавал новый массив (GC-аллокация на каждый выстрел).
            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, range,
                hitMask, triggerInteraction);
            SortHitsByDistance(_hitBuffer, count);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider == null) continue;

                if (_ownerRoot != null && hit.collider.transform.IsChildOf(_ownerRoot.transform))
                    continue;

                CharacterHealth hitHealth = hit.collider.GetComponentInParent<CharacterHealth>();
                if (hitHealth != null && !allowFriendlyFire && hitHealth.Team == _ownerTeam)
                {
                    // СВОЙ: пуля летит дальше, урона нет. Так солдаты в шеренге
                    // не убивают друг друга.
                    continue;
                }

                endPoint = hit.point;
                hitSomething = true;

                IDamageable damageable = hitHealth != null
                    ? hitHealth
                    : hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.IsAlive)
                    damageable.TakeDamage(new DamageInfo(
                        damage * Mathf.Max(0.01f, DamageScale), hit.point, direction, owner));
                else
                    SpawnImpact(hit.point, hit.normal);

                break;
            }
            if (showTracer) SpawnTracer(origin, endPoint);
            ProjectilePass.Emit(new ProjectilePass.Shot(
                origin, endPoint, owner, _ownerTeam, hitSomething));
            Fired?.Invoke(endPoint);
            if (AmmunitionInMagazine == 0) BeginReload();

            return true;
        }

        /// <summary>
        /// Чистая линия от дула до точки? Проверяет, не упирается ли пуля в геометрию
        /// (стену, укрытие) или в союзника раньше цели. ИИ зовёт это перед каждым
        /// TryFire: нет чистой линии — стрелок должен сменить позицию.
        /// </summary>
        public bool HasClearShot(Vector3 aimPoint, GameObject owner, out bool blockedByAlly)
        {
            blockedByAlly = false;

            Vector3 origin = MuzzlePosition;
            Vector3 dir = aimPoint - origin;
            float dist = dir.magnitude;
            if (dist <= 0.001f) return true;
            dir /= dist;

            ResolveOwner(owner);

            int count = Physics.RaycastNonAlloc(origin, dir, _shotCheckBuffer, dist,
                hitMask, triggerInteraction);
            SortHitsByDistance(_shotCheckBuffer, count);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _shotCheckBuffer[i];
                if (hit.collider == null) continue;
                if (_ownerRoot != null && hit.collider.transform.IsChildOf(_ownerRoot.transform))
                    continue;
                // Совсем вплотную (своё оружие/амуниция у дула) — не помеха.
                if (hit.distance < 0.35f) continue;

                CharacterHealth hitHealth = hit.collider.GetComponentInParent<CharacterHealth>();
                if (hitHealth != null)
                {
                    if (hitHealth.Team == _ownerTeam)
                    {
                        // Союзник прямо на линии: стрелять в упор нельзя.
                        blockedByAlly = true;
                        return false;
                    }
                    // Цель (или чужой персонаж) — линия чистая.
                    return true;
                }

                // Геометрия (стена, укрытие, ящик) — линия перекрыта.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Кто стреляет: команда владельца берётся из его CharacterHealth.
        /// Результат кешируется — GetComponentInParent на каждый выстрел не нужен.
        /// </summary>
        private void ResolveOwner(GameObject owner)
        {
            if (owner != null && owner == _ownerCache) return;

            if (owner == null)
            {
                _ownerRoot = null;
                _ownerCache = null;
                _ownerTeam = Team.Axis;
                return;
            }

            CharacterHealth ownerHealth = owner.GetComponentInParent<CharacterHealth>();
            _ownerRoot = ownerHealth != null ? ownerHealth.gameObject : owner;
            if (ownerHealth != null) _ownerTeam = ownerHealth.Team;
            _ownerCache = owner;
        }

        private static void SortHitsByDistance(RaycastHit[] hits, int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit key = hits[i];
                int j = i - 1;
                while (j >= 0 && hits[j].distance > key.distance)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = key;
            }
        }

        private void SpawnImpact(Vector3 point, Vector3 normal)
        {
            if (impactEffect == null) return;
            GameObject fx = Instantiate(impactEffect, point, Quaternion.LookRotation(normal));
            Destroy(fx, impactEffectLifetime);
        }

        /// <summary>Вспышка выстрела: частицы + мигающий свет. Пустых ссылок не боится.</summary>
        private void PlayMuzzleFx()
        {
            if (muzzleFlash != null)
                muzzleFlash.Emit(Mathf.Max(1, muzzleFlashCount));
            if (muzzleLight != null)
            {
                if (_flashRoutine != null) StopCoroutine(_flashRoutine);
                _flashRoutine = StartCoroutine(MuzzleLightRoutine());
            }
        }

        private IEnumerator MuzzleLightRoutine()
        {
            muzzleLight.enabled = true;
            float duration = Mathf.Max(0.01f, muzzleFlashDuration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                muzzleLight.intensity = muzzleFlashLightIntensity * Mathf.Clamp01(1f - t / duration);
                yield return null;
            }
            muzzleLight.intensity = 0f;
            muzzleLight.enabled = false;
            _flashRoutine = null;
        }

        /// <summary>
        /// Быстрая настройка: создаёт у дула вспышку (частицы) и свет без единого ассета.
        /// Выдели оружие → шестерёнка компонента → «Создать вспышку выстрела».
        /// </summary>
        [ContextMenu("Создать вспышку выстрела (частицы + свет)")]
        private void CreateDefaultMuzzleFlash()
        {
            Transform host = muzzle != null ? muzzle : transform;

            var fxObject = new GameObject("MuzzleFlashFX");
            fxObject.transform.SetParent(host, false);
            fxObject.transform.localPosition = Vector3.zero;
            fxObject.transform.localRotation = Quaternion.identity;

            ParticleSystem flash = fxObject.GetComponent<ParticleSystem>();
            if (flash == null) flash = fxObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = flash.main;
            main.duration = 0.15f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.09f;
            main.startSpeed = 7f;
            main.startSize = 0.32f;
            main.startColor = new Color(1f, 0.62f, 0.18f);
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = flash.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            ParticleSystem.ShapeModule shape = flash.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            var lightObject = new GameObject("MuzzleFlashLight");
            lightObject.transform.SetParent(host, false);
            lightObject.transform.localPosition = Vector3.zero;

            Light flashLight = lightObject.GetComponent<Light>();
            if (flashLight == null) flashLight = lightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.55f, 0.22f);
            flashLight.intensity = 0f;
            flashLight.range = 9f;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            muzzleFlash = flash;
            muzzleLight = flashLight;
            if (muzzle == null) muzzle = host;

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(fxObject, "Create muzzle flash");
            UnityEditor.Undo.RegisterCreatedObjectUndo(lightObject, "Create muzzle light");
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[HitscanWeapon] {name}: вспышка и свет созданы у «{host.name}». " +
                "Силу/длительность крути полями Muzzle Flash ниже.", this);
        }

        private void EnsureTracerPool()
        {
            int size = Mathf.Clamp(tracerPoolSize, 0, 32);
            if (_tracerPool != null && _tracerPool.Length == size) return;

            _tracerPool = new LineRenderer[size];
            _tracerExpireAt = new float[size];

            for (int i = 0; i < size; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = tracerWidth;
                line.endWidth = tracerWidth;
                line.material = TracerMaterial();
                line.enabled = false;
                _tracerPool[i] = line;
            }
            _tracerCursor = 0;
        }

        private static Material TracerMaterial()
        {
            if (_tracerMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader == null) return null;
                _tracerMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _tracerMaterial;
        }

        private void SpawnTracer(Vector3 from, Vector3 to)
        {
            if (_tracerPool == null || _tracerPool.Length == 0) return;

            int index = _tracerCursor;
            _tracerCursor = (_tracerCursor + 1) % _tracerPool.Length;

            LineRenderer line = _tracerPool[index];
            if (line == null) return;

            line.startWidth = tracerWidth;
            line.endWidth = tracerWidth;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.enabled = true;
            _tracerExpireAt[index] = Time.time + tracerLifetime;
        }

        private void Update()
        {
            if (_tracerPool == null) return;

            float now = Time.time;
            for (int i = 0; i < _tracerPool.Length; i++)
            {
                LineRenderer line = _tracerPool[i];
                if (line == null || !line.enabled) continue;
                if (now < _tracerExpireAt[i]) continue;
                line.enabled = false;
            }
        }

        public bool BeginReload()
        {
            if (IsReloading || AmmunitionInMagazine >= magazineCapacity || _reserve <= 0)
                return false;

            _reloadRoutine = StartCoroutine(ReloadRoutine());
            return true;
        }

        public void CancelReload()
        {
            if (_reloadRoutine != null) StopCoroutine(_reloadRoutine);
            _reloadRoutine = null;
            IsReloading = false;
        }

        private IEnumerator ReloadRoutine()
        {
            IsReloading = true;
            ReloadStarted?.Invoke();

            if (audioSource != null && reloadSound != null)
                audioSource.PlayOneShot(reloadSound);

            yield return new WaitForSeconds(reloadDuration);

            int required = magazineCapacity - AmmunitionInMagazine;
            int loaded = Mathf.Min(required, _reserve);

            AmmunitionInMagazine += loaded;
            _reserve -= loaded;

            IsReloading = false;
            _reloadRoutine = null;
            ReloadFinished?.Invoke();
        }

        private Vector3 ApplySpread(Vector3 direction)
        {
            float angle = spreadAngle * Mathf.Max(0f, SpreadScale);
            if (angle <= 0f)
                return direction;

            Quaternion lookRotation = Quaternion.LookRotation(direction);
            float yaw = Random.Range(-angle, angle);
            float pitch = Random.Range(-angle, angle);

            return lookRotation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        private void OnDisable() => CancelReload();

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(MuzzlePosition, MuzzleForward * range);
        }
#endif
    }
}
