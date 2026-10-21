using System.Collections.Generic;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
    [RequireComponent(typeof(EnemyMotor))]
    [RequireComponent(typeof(CharacterHealth))]
    [DisallowMultipleComponent]
    public sealed class EnemyAI : MonoBehaviour
    {
        private enum LookAroundPhase
        {
            None,
            TurnOut,
            Hold,
            Return
        }

        [Header("References")]
        [SerializeField] private Transform eyePoint;
        [SerializeField] private HitscanWeapon weapon;
        [SerializeField] private PatrolRoute patrolRoute;
        [SerializeField] private Animator animator;
        [Tooltip("Голос врага. Пусто — будет найден на этом объекте автоматически.")]
        [SerializeField] private EnemyVoice voice;
        [Tooltip("Выдача оружия. Пусто — будет найдена на этом объекте автоматически.")]
        [SerializeField] private EnemyLoadout loadout;
        [Tooltip("Проигрыватель анимаций из клипов инспектора. Пусто — найдётся сам. " +
                 "Если его нет и Animator пуст — враг работает как капсула без анимаций.")]
        [SerializeField] private EnemyAnimator enemyAnimator;

        [Header("Target")]
        [SerializeField] private Team enemyTeam = Team.Axis;
        [SerializeField] private LayerMask targetMask;
        [SerializeField] private LayerMask visibilityMask = ~0;

        [Header("Perception")]
        [SerializeField, Min(1f)] private float viewDistance = 45f;
        [SerializeField, Range(1f, 180f)] private float fieldOfView = 110f;
        [SerializeField, Min(1f)] private float verticalViewTolerance = 6f;
        [Tooltip("В упор враг замечает движение/звук даже вне конуса зрения " +
                 "(периферия, шорох). Внутри этого радиуса FOV игнорируется.")]
        [SerializeField, Min(0f)] private float closeDetectDistance = 7f;
        [SerializeField, Min(0.02f)] private float perceptionInterval = 0.15f;
        [SerializeField, Min(0f)] private float targetMemoryDuration = 8f;

        [Header("Слух")]
        [Tooltip("Множитель радиуса слышимости для всех шумов: выстрелы, крики, шаги, удары.")]
        [SerializeField, Min(1f)] private float hearingRadiusMultiplier = 1.5f;
        [Tooltip("Множитель громкости услышанного шума.")]
        [SerializeField, Min(0.1f)] private float hearingSensitivity = 1.3f;
        [Tooltip("Минимальная слышимая громкость (0–1). Ниже этого шум игнорируется.")]
        [SerializeField, Range(0f, 1f)] private float hearingThreshold = 0.06f;

        [Header("Awareness")]
        [SerializeField, Min(0.05f)] private float timeToDetect = 0.9f;
        [SerializeField, Min(0.05f)] private float awarenessDecay = 0.35f;
        [SerializeField, Min(0f)] private float reactionTime = 0.55f;

        [Header("Patrol")]
        [SerializeField, Min(0f)] private float patrolSpeed = 1.6f;
        [SerializeField, Min(0f)] private float pointWaitDuration = 2f;
        [SerializeField, Min(0.1f)] private float pointReachRadius = 0.6f;
        [Tooltip("Если маршрут не задан — враг сам бродит по округе, а не стоит на месте.")]
        [SerializeField] private bool wanderWhenNoRoute = true;
        [SerializeField, Min(1f)] private float wanderRadius = 14f;
        [Tooltip("Сколько ждать на месте, если новую точку для прогулки найти не удалось.")]
        [SerializeField, Min(0.1f)] private float wanderRetryDelay = 1.5f;
        [Tooltip("Сектор, в котором бродит КАЖДЫЙ солдат (градусы). У каждого свой сектор — " +
                 "поэтому по местности они расходятся, а не сваливаются в одну точку.")]
        [SerializeField, Range(30f, 360f)] private float wanderSectorDegrees = 240f;

        [Header("Осматривание на месте")]
        [Tooltip("Иногда оглядываться по сторонам, стоя на точке патруля или прогулки.")]
        [SerializeField] private bool lookAroundWhenIdle = true;
        [SerializeField, Min(0f)] private float lookAroundIntervalMin = 2.5f;
        [SerializeField, Min(0f)] private float lookAroundIntervalMax = 6f;
        [Tooltip("Максимальный угол поворота в одну сторону, град.")]
        [SerializeField, Range(5f, 80f)] private float lookAroundAngle = 34f;
        [Tooltip("Скорость поворота при осматривании, град/сек.")]
        [SerializeField, Min(10f)] private float lookAroundTurnSpeed = 55f;
        [SerializeField, Min(0.2f)] private float lookAroundHoldMin = 0.6f;
        [SerializeField, Min(0.2f)] private float lookAroundHoldMax = 1.2f;

        [Header("Combat")]
        [Tooltip("Скорость погони, м/с. Держим низкой: солдат подходит к цели, а не " +
                 "мчится через карту (было 10 — выглядело как рывок).")]
        [SerializeField, Min(0f)] private float chaseSpeed = 2.8f;
        [Tooltip("Скорость сближения в бою, м/с. Медленно и плавно: подошёл на метр — " +
                 "остановился, без рывков туда-сюда.")]
        [SerializeField, Min(0f)] private float combatApproachSpeed = 1.6f;
        [Tooltip("Дистанция боя, м: дальше — подходит на скорости погони, " +
                 "на дистанции — стоит и стреляет. Назад/вбок в бою не уходит.")]
        [SerializeField, Min(1f)] private float preferredCombatDistance = 7f;
        [SerializeField, Min(1f)] private float maximumCombatDistance = 38f;

        [Header("Squad (открытое поле)")]
        [Tooltip("Отрядная логика: асимметричные личные дистанции и мягкие боковые шаги. " +
                 "Выключи для одиночного боя.")]
        [SerializeField] private bool useSquadTactics = true;
        [Tooltip("Радиус, в котором солдат знает о союзниках, м.")]
        [SerializeField, Min(1f)] private float squadAwarenessRadius = 25f;
        [Tooltip("Нижняя граница личной дистанции до союзника, м.")]
        [SerializeField, Min(0.5f)] private float squadMinimumSpacing = 5f;
        [Tooltip("Диапазон, из которого каждый солдат получает свою личную дистанцию, м.")]
        [SerializeField] private Vector2 squadComfortSpacing = new(6.5f, 8f);
        [Tooltip("Максимальный боковой шаг для мягкой правки дистанции, м.")]
        [SerializeField, Min(0.1f)] private float squadMicroAdjustDistance = 1.5f;
        [Tooltip("Дальше этой дистанции до игрока отряд переходит к преследованию, м.")]
        [SerializeField, Min(5f)] private float squadPursuitDistance = 33f;
        [Tooltip("Сколько стоять после начала стрельбы, прежде чем менять позицию. Сек.")]
        [SerializeField, Min(0f)] private float squadFireAnchorDuration = 1.2f;
        [Tooltip("Множитель скорости осторожного преследования.")]
        [SerializeField, Range(0.5f, 1f)] private float squadPursuitSpeedScale = 0.8f;

        [Header("Рассредоточение (строй и разброс по местности)")]
        [Tooltip("Ширина строя, м: насколько солдаты расходятся вбок друг от друга. " +
                 "Без этого все стоят в одной точке.")]
        [SerializeField, Min(0f)] private float squadLateralSpread = 9f;
        [Tooltip("Разброс по глубине, м: кто-то чуть ближе к цели, кто-то чуть дальше.")]
        [SerializeField, Min(0f)] private float squadDepthSpread = 3f;
        [Tooltip("Минимальная дистанция между солдатами, м. Ближе — мягко расходятся вбок.")]
        [SerializeField, Min(0.5f)] private float squadSeparationDistance = 4.5f;
        [Tooltip("Сила мягкого расхождения, 0–1.")]
        [SerializeField, Range(0f, 1f)] private float squadSeparationStrength = 0.8f;
        [Tooltip("Насколько солдат может отойти от своей точки и считать, что он на месте, м.")]
        [SerializeField, Min(0.1f)] private float combatAnchorTolerance = 1.2f;
        [Tooltip("Скорость, с которой своя точка догоняет цель, м/с. Медленная — без рывков.")]
        [SerializeField, Min(0.1f)] private float combatAnchorTrackSpeed = 1.5f;

        [Header("Fight pacing (темп схватки, как в топ-играх)")]
        [Tooltip("Первые секунды схватки враг бьёт кучнее (был наготове): " +
                 "первая кровь приходит быстро. Длительность, сек.")]
        [SerializeField, Min(0f)] private float openingDuration = 4f;
        [Tooltip("Множитель разброса в открытии схватки.")]
        [SerializeField, Min(0.1f)] private float openingSpread = 0.55f;
        [Tooltip("Множитель разброса в середине (1–2 попадания): свист мимо, передышка игроку.")]
        [SerializeField, Min(0.1f)] private float midFightSpread = 1.25f;
        [Tooltip("Множитель пауз между очередями в середине схватки.")]
        [SerializeField, Min(0.5f)] private float midFightPauseMult = 1.4f;
        [Tooltip("Множитель разброса в концовке (3+ попадания): пристрелялся, давит.")]
        [SerializeField, Min(0.1f)] private float closingSpread = 0.85f;
        [Tooltip("Множители урона по номеру попадания (0,1,2,3,4+). Сумма × базовый урон " +
                 "≈ 100: игрок умирает примерно за 5 попаданий, первое — царапина.")]
        [SerializeField] private float[] pacingDamageStages = { 0.45f, 0.65f, 0.8f, 1f, 1.2f };
        [Tooltip("Живой разброс урона стадии ±: не выглядит скриптом.")]
        [SerializeField, Range(0f, 0.5f)] private float pacingDamageJitter = 0.15f;
        [Tooltip("Сколько держать бой при кратковременной помехе (рывки луча " +
                 "из-за углов/косяков), прежде чем сорваться в погоню. Сек.")]
        [SerializeField, Min(0f)] private float combatLoseSightGrace = 0.4f;

        [Header("Humanity (человечность)")]
        [Tooltip("Скорость доводки корпуса в бою, град/сек. Человек доворачивается " +
                 "медленно — по стрейфящейся цели мажет. Было 540 (робот).")]
        [SerializeField, Min(20f)] private float combatTurnSpeed = 115f;
        [Tooltip("Разброс времени реакции: при каждом захвате цели реакция " +
                 "умножается на случайное 0.8–1.5. Кто-то шустрый, кто-то тормоз.")]
        [SerializeField] private bool jitterReaction = true;
        [Tooltip("Увод ствола вверх к концу очереди (отдача): первые пули точнее. Метры.")]
        [SerializeField, Min(0f)] private float burstClimb = 0.35f;
        [Tooltip("Пауза-осмотр на точках прогулки: стоит и смотрит, а не марширует. Мин. сек.")]
        [SerializeField, Min(0f)] private float wanderWaitMin = 0.5f;
        [Tooltip("Пауза-осмотр на точках прогулки. Макс. сек.")]
        [SerializeField, Min(0f)] private float wanderWaitMax = 2f;
        [Tooltip("Разброс темпа прогулки ±: каждый маршрут идёт чуть иначе.")]
        [SerializeField, Range(0f, 0.4f)] private float wanderSpeedVariation = 0.1f;
        [Tooltip("Амплитуда осматривающих качаний в поиске, град.")]
        [SerializeField, Min(10f)] private float searchSweepAmplitude = 70f;
        [Tooltip("Скорость этих качаний, град/сек.")]
        [SerializeField, Min(10f)] private float searchSweepSpeed = 95f;
        [Tooltip("Длина очереди: враг жмёт длинными очередями почти в весь магазин " +
                 "(32 патрона), а не по 2–3 пули.")]
        [SerializeField, Range(1, 64)] private int minimumBurstSize = 16;
        [SerializeField, Range(1, 64)] private int maximumBurstSize = 32;
        [SerializeField, Min(0f)] private float minimumBurstPause = 0.35f;
        [SerializeField, Min(0f)] private float maximumBurstPause = 0.9f;

        [Header("Accuracy (метры разброса по цели)")]
        [SerializeField, Min(0f)] private float baseSpread = 0.35f;
        [SerializeField, Min(0f)] private float spreadPerTenMeters = 0.45f;
        [SerializeField, Min(1f)] private float movingSpreadMultiplier = 2.2f;
        [SerializeField, Min(1f)] private float suppressedSpreadMultiplier = 3f;
        [SerializeField, Range(0f, 1f)] private float leadAccuracy = 0.6f;
        [Tooltip("Не стрелять, пока корпус не довернулся на цель (допуск, град, только " +
                  "рыскание). Иначе очередь уходит визуально вбок от неразвёрнутого ствола. " +
                  "Огонь только когда развернулся на игрока.")]
        [SerializeField, Range(0f, 45f)] private float maxFireAngle = 8f;

        [Header("Suppression")]
        [Tooltip("Подавление теперь только мажет (разброс), двигаться врага " +
                 "не заставляет: в бою он либо подходит, либо стоит.")]
        [SerializeField, Min(0.1f)] private float suppressionDecay = 0.6f;

        [Header("Navigation")]
        [SerializeField, Min(0.05f)] private float pathRefreshInterval = 0.25f;

        [Header("Death")]
        [Tooltip("Через сколько секунд убрать труп. 0 — оставить навсегда.")]
        [SerializeField, Min(0f)] private float corpseLifetime = 0f;

        [Header("Debug")]
        [Tooltip("Писать в консоль смену состояний, захват и потерю цели. " +
                 "Включай только у одного тестового врага — иначе будет спам.")]
        [SerializeField] private bool debugLogging;

        public EnemyState State { get; private set; }
        public Transform CurrentTarget => _target;
        public float Awareness => _awareness;
        public float Suppression => _suppression;

        public HitscanWeapon Weapon => weapon;
        public EnemyAnimator EnemyAnimator => enemyAnimator;

        internal float SquadAwarenessRadius => squadAwarenessRadius;
        internal float SquadMinimumSpacing => squadMinimumSpacing;
        internal float SquadMicroAdjustDistance => squadMicroAdjustDistance;
        internal float SquadLateralSpread => squadLateralSpread;
        internal float SquadDepthSpread => squadDepthSpread;
        internal float SquadSeparationDistance => squadSeparationDistance;
        internal float SquadSeparationStrength => squadSeparationStrength;
        internal float CombatAnchorTolerance => combatAnchorTolerance;
        internal float SquadPersonalSpace
        {
            get
            {
                if (_squadPersonalSpace <= 0.01f)
                    _squadPersonalSpace = Random.Range(squadMinimumSpacing + 0.75f,
                        Mathf.Max(squadMinimumSpacing + 1.5f, squadComfortSpacing.y + 3.5f));
                return _squadPersonalSpace;
            }
        }
        internal Transform SquadTarget => _target;
        internal EnemyMotor Motor => _motor;
        internal Team SquadTeam => _health != null ? _health.Team : enemyTeam;
        internal bool IsSquadAlive => _health != null && _health.IsAlive && State != EnemyState.Dead;

        private float SquadCombatEnterDistance => useSquadTactics
            ? Mathf.Min(maximumCombatDistance,
                Mathf.Max(preferredCombatDistance, squadPursuitDistance - 3f))
            : maximumCombatDistance;

        private float SquadCombatExitDistance => Mathf.Max(SquadCombatEnterDistance + 1f,
            Mathf.Min(squadPursuitDistance, maximumCombatDistance + 5f));

        internal static List<EnemyAI> RegisteredSoldiers => s_soldiers;

        // ДЫРА: Shoot-триггер дёргался на КАЖДЫЙ выстрел (~8/сек при 500 RPM) и забивал
        // Reload/Hit. Теперь legacy-триггеры тоже троттлятся, а основной путь — EnemyAnimator.
        private float _nextLegacyShootAnimTime;
        private float _nextLegacyHitAnimTime;
        private readonly System.Collections.Generic.HashSet<int> _animParams = new();

        private EnemyMotor _motor;
        private CharacterHealth _health;

        private Transform _target;
        private CharacterHealth _targetHealth;
        private Vector3 _lastKnownTargetPosition;
        private Vector3 _targetVelocity;
        private Vector3 _prevTargetPosition;
        private float _lastTargetSeenTime = float.NegativeInfinity;
        private bool _firstSightAcquired;
        private float _canFireAfter;

        private float _awareness;
        private float _suppression;
        private float _lastSuppressionShout;
        private Vector3 _suspicionPoint;
        private bool _hasSuspicion;

        private float _nextPerceptionTime;
        private float _lastPerceptionVisibleTime = float.NegativeInfinity;
        private float _lastPerceptionTime = float.NegativeInfinity;
        private float _searchUntil;
        private float _nextPathRefreshTime;
        private float _patrolWaitUntil;
        private bool _waitingAtPoint;
        private float _nextWanderAttemptTime;

        private int _patrolIndex;
        private Vector3 _homePosition;
        private int _shotsRemaining;
        private float _nextBurstTime;

        private float _lastVisibleTime;
        private float _fireAnchorUntil;
        private float _squadPersonalSpace;
        private Vector3 _lastPathDestination;
        private bool _hasPathDestination;

        // СВОЯ ТОЧКА В БОЮ: солдат держит своё место в строю, а не точку игрока.
        // Якорь двигается плавно (combatAnchorTrackSpeed) — никаких рывков.
        private Vector3 _combatAnchor;
        private bool _hasCombatAnchor;
        private Vector3 _formationForward = Vector3.forward;
        private bool _formationReady;
        private float _nextDiagTime;
        private string _lastLosBlocker;
        private string _lastRejectNote;
        private string _lastNoiseInfo;

        private Vector3 _searchCenter;
        private float _searchFaceUntil;
        private float _searchBaseYaw;
        private bool _searchBaseCaptured;

        private LookAroundPhase _lookAroundPhase;
        private float _lookAroundBaseYaw;
        private float _lookAroundTargetYaw;
        private float _lookAroundHoldUntil;
        private float _nextLookAroundTime;

        private int _burstShotIndex;
        private float _wanderSpeedFactor = 1f;
        private float _wanderBaseAngle;
        private float _chaseStuckTime = -1f;
        private Transform _engageTarget;
        private float _engageStartTime;
        private float _engageEndTime = float.NegativeInfinity;
        private int _engageHits;
        private float _lastTargetHp;
        private int _diagConsidered;
        private int _diagRejectedTeam;
        private int _diagRejectedDead;
        private int _diagRejectedLayer;
        private int _diagRejectedDist;
        private int _diagRejectedFov;
        private int _diagRejectedLos;

        private readonly Collider[] _targetBuffer = new Collider[128];
        private readonly HashSet<CharacterHealth> _candidateSet = new();
        private readonly RaycastHit[] _losBuffer = new RaycastHit[16];
        private readonly Collider[] _eyeBuffer = new Collider[4];

        private static readonly HashSet<CharacterHealth> s_characters = new();
        private static readonly List<EnemyAI> s_soldiers = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSoldierRegistry() => s_soldiers.Clear();

        private void RegisterSoldier()
        {
            if (!s_soldiers.Contains(this)) s_soldiers.Add(this);
        }

        private void UnregisterSoldier() => s_soldiers.Remove(this);

        public static void RegisterCharacter(CharacterHealth character)
        {
            if (character != null) s_characters.Add(character);
        }

        public static void UnregisterCharacter(CharacterHealth character)
        {
            if (character != null) s_characters.Remove(character);
        }

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int ShootHash = Animator.StringToHash("Shoot");
        private static readonly int ReloadHash = Animator.StringToHash("Reload");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");

        private void Awake()
        {
            _motor = GetComponent<EnemyMotor>();
            if (_motor == null)
            {
                if (GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                    gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();

                _motor = gameObject.AddComponent<EnemyMotor>();
            }

            _health = GetComponent<CharacterHealth>();
            if (_health == null)
            {
                _health = gameObject.AddComponent<CharacterHealth>();
                Debug.LogWarning($"[EnemyAI] {name}: CharacterHealth отсутствовал — добавлен автоматически.", this);
            }

            if (eyePoint == null) eyePoint = transform;
            if (voice == null) voice = GetComponent<EnemyVoice>();
            if (loadout == null) loadout = GetComponent<EnemyLoadout>();
            if (enemyAnimator == null) enemyAnimator = GetComponent<EnemyAnimator>();
            // Автопоиск Animator УДАЛЁН: только инспектор.
            // Пустое поле animator — значит legacy-анимаций нет, и это нормально,
            // если назначен EnemyAnimator с клипами.
            CacheAnimatorParams();

            // ДЫРА: включённый Root Motion у модели дрался бы с NavMeshAgent/EnemyMotor
            // (двойное движение). Движением владеет мотор — гасим принудительно.
            if (animator != null) animator.applyRootMotion = false;

            if (targetMask == 0)
            {
                targetMask = ~0;
                Debug.LogWarning($"[EnemyAI] {name}: targetMask был пуст (0) — " +
                    "автоматически включены все слои. Настрой targetMask в инспекторе " +
                    "для корректной работы.", this);
            }

            if (enemyTeam == Team.Allies)
            {
                enemyTeam = Team.Axis;
                Debug.LogWarning($"[EnemyAI] {name}: enemyTeam был Allies (старое значение " +
                    "из сцены) — автоматически исправлен на Axis.", this);
            }

            if (weapon == null) weapon = GetComponentInChildren<HitscanWeapon>(true);
        }

        private void OnEnable()
        {
            if (_health == null) _health = GetComponent<CharacterHealth>();
            if (_health == null) return;
            RegisterSoldier();
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            NoiseSystem.NoiseCreated += OnNoiseCreated;
            SubscribeWeapon(weapon);
            State = EnemyState.Patrol;
            _fireAnchorUntil = 0f;
            _squadPersonalSpace = 0f;
            _hasCombatAnchor = false;
            _formationReady = false;
            _lookAroundPhase = LookAroundPhase.None;
            _nextLookAroundTime = Time.time + Random.Range(lookAroundIntervalMin,
                lookAroundIntervalMax);
        }

        private void Start()
        {
            _homePosition = transform.position;
            _searchCenter = _homePosition;
            // Свой сектор блуждания: соседи по сцене не бродят в одну сторону
            // и не сваливаются в одну точку.
            _wanderBaseAngle = HashAngle(GetInstanceID());

            _motor.SetSpeed(patrolSpeed);
            _motor.SetAutoRotation(true);
        }

        /// <summary>Псевдослучайный угол 0–360° от id объекта: у соседей он разный.</summary>
        private static float HashAngle(int id)
        {
            unchecked
            {
                uint h = (uint)id * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return h % 360u;
            }
        }

        private void OnValidate()
        {
            if (maximumBurstSize < minimumBurstSize) maximumBurstSize = minimumBurstSize;
            if (maximumBurstPause < minimumBurstPause) maximumBurstPause = minimumBurstPause;
            if (wanderWaitMax < wanderWaitMin) wanderWaitMax = wanderWaitMin;
            if (lookAroundIntervalMax < lookAroundIntervalMin)
                lookAroundIntervalMax = lookAroundIntervalMin;
            if (lookAroundHoldMax < lookAroundHoldMin)
                lookAroundHoldMax = lookAroundHoldMin;
            if (squadComfortSpacing.y < squadComfortSpacing.x)
                squadComfortSpacing.y = squadComfortSpacing.x;
            if (squadComfortSpacing.x < squadMinimumSpacing)
                squadComfortSpacing.x = squadMinimumSpacing;
        }

        public void ConfigureForWizard(
            Transform eye,
            HitscanWeapon weaponRef,
            Team targetTeam,
            LayerMask targets,
            LayerMask visibility,
            Animator animatorRef = null,
            PatrolRoute route = null,
            EnemyAnimator enemyAnimatorRef = null)
        {
            eyePoint = eye;
            weapon = weaponRef;
            enemyTeam = targetTeam;
            targetMask = targets;
            visibilityMask = visibility;
            if (animatorRef != null) animator = animatorRef;
            if (route != null) patrolRoute = route;
            if (enemyAnimatorRef != null) enemyAnimator = enemyAnimatorRef;
            CacheAnimatorParams();
        }

        // ResolveAnimator УДАЛЁН по требованию «только через инспектор».
        // Раньше скрипт сам подбирал первый Animator в детях (иногда это был
        // Animator оружия или модели с чужим Controller) — отсюда эффект
        // «берёт сам какие-то анимации». Теперь: не перетащил в инспектор — не играет.

        private void CacheAnimatorParams()
        {
            _animParams.Clear();
            // Контроллера нет (клипами рулит EnemyAnimator через граф) —
            // parameters бросал бы "Animator is not playing an AnimatorController"
            // и ронял остаток Awake. Legacy-путь просто молча не работает.
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter p in animator.parameters)
                _animParams.Add(p.nameHash);
        }

        private bool HasAnimParam(int hash) => animator != null && _animParams.Contains(hash);

        private void SafeSetBool(int hash, bool value)
        {
            if (HasAnimParam(hash)) animator.SetBool(hash, value);
        }

        private void SafeSetFloat(int hash, float value, float damp, float dt)
        {
            if (HasAnimParam(hash)) animator.SetFloat(hash, value, damp, dt);
        }

        private void SafeSetTrigger(int hash)
        {
            if (HasAnimParam(hash)) animator.SetTrigger(hash);
        }

        public void SetPatrolRoute(PatrolRoute route) => patrolRoute = route;
        public void SetWeapon(HitscanWeapon newWeapon)
        {
            if (weapon == newWeapon) return;

            UnsubscribeWeapon(weapon);
            weapon = newWeapon;
            SubscribeWeapon(weapon);

            _shotsRemaining = 0;
            _nextBurstTime = 0f;
        }

        public void ApplySuppression(float amount)
        {
            if (_health == null || !_health.IsAlive) return;
            _suppression = Mathf.Clamp01(_suppression + amount);

            if (_suppression > 0.7f && Time.time - _lastSuppressionShout > 5f)
            {
                _lastSuppressionShout = Time.time;
                if (voice != null) voice.PlaySuppressed();
            }
        }

        /// <summary>
        /// Мгновенно взять цель: свист, окрик, выстрел из-за угла.
        /// Враг забывает патруль и бросается на игрока, даже если не видел его.
        /// </summary>
        public void ForceTarget(Transform target)
        {
            if (target == null) return;
            if (_health == null || !_health.IsAlive) return;
            if (State == EnemyState.Dead) return;

            CharacterHealth targetHealth = target.GetComponentInParent<CharacterHealth>();
            if (targetHealth != null && targetHealth.Team == enemyTeam) return;
            if (targetHealth != null && !targetHealth.IsAlive) return;

            SetTarget(target);
            _awareness = 1f;
            _hasSuspicion = false;
            _lastKnownTargetPosition = target.position;
            _lastTargetSeenTime = Time.time;

            if (!_firstSightAcquired)
            {
                _firstSightAcquired = true;
                _canFireAfter = Time.time + reactionTime * 0.5f *
                    (jitterReaction ? Random.Range(0.8f, 1.5f) : 1f);
            }

            ChangeState(EnemyState.Chase);
        }

        /// <summary>Мгновенно взять цель по объекту.</summary>
        public void ForceTarget(GameObject target) =>
            ForceTarget(target != null ? target.transform : null);

        private void SubscribeWeapon(HitscanWeapon target)
        {
            if (target == null) return;

            target.Fired -= OnWeaponFired;
            target.ReloadStarted -= OnWeaponReloadStarted;

            target.Fired += OnWeaponFired;
            target.ReloadStarted += OnWeaponReloadStarted;
        }

        private void UnsubscribeWeapon(HitscanWeapon target)
        {
            if (target == null) return;
            target.Fired -= OnWeaponFired;
            target.ReloadStarted -= OnWeaponReloadStarted;
        }

        private void OnWeaponFired(Vector3 impactPoint)
        {
            if (enemyAnimator != null) enemyAnimator.PlayShoot();
            // ДЫРА: триггер на каждый выстрел вешал Animator. Legacy-путь троттлим.
            else if (Time.time >= _nextLegacyShootAnimTime)
            {
                _nextLegacyShootAnimTime = Time.time + 0.3f;
                SafeSetTrigger(ShootHash);
            }
            if (voice != null) voice.PlayCombatChatter();
        }

        private void OnWeaponReloadStarted()
        {
            if (enemyAnimator != null) enemyAnimator.PlayReload();
            else SafeSetTrigger(ReloadHash);
            if (voice != null) voice.PlayReload();
        }

        private void Update()
        {
            if (_health == null || !_health.IsAlive) return;

            float dt = Time.deltaTime;
            _suppression = Mathf.Max(0f, _suppression - suppressionDecay * dt);

            if (Time.time >= _nextPerceptionTime)
            {
                _nextPerceptionTime = Time.time + perceptionInterval + Random.Range(0f, 0.04f);
                UpdatePerception();
            }

            // БАГФИКС: раньше awareness гас каждый кадр, даже пока цель была
            // прямо перед глазами. На дальней дистанции набор
            // (0.4..1.4)/timeToDetect в секунду почти полностью съедался
            // затуханием — враг мог смотреть на игрока 5+ секунд и не замечать.
            // Теперь гаснем только когда цель реально не видна.
            bool recentlySeen = Time.time - _lastPerceptionVisibleTime
                <= perceptionInterval * 2f + 0.05f;
            if (!recentlySeen && (_awareness < 1f || _target == null))
                _awareness = Mathf.Max(0f, _awareness - awarenessDecay * dt);

            switch (State)
            {
                case EnemyState.Patrol: UpdatePatrol(); break;
                case EnemyState.Alert: UpdateAlert(); break;
                case EnemyState.Search: UpdateSearch(); break;
                case EnemyState.Chase: UpdateChase(); break;
                case EnemyState.Combat: UpdateCombat(); break;
            }

            UpdateAnimator();
            UpdateVoiceFootsteps();

            if (debugLogging && Time.time >= _nextDiagTime)
            {
                _nextDiagTime = Time.time + 1.5f;
                LogDiag();
            }
        }

        private void UpdatePerception()
        {
            float tickDt = _lastPerceptionTime < 0f
                ? perceptionInterval
                : Mathf.Clamp(Time.time - _lastPerceptionTime, 0.02f, 1f);
            _lastPerceptionTime = Time.time;

            Transform visible = FindBestVisibleTarget(out float visDistance);

            if (visible != null)
            {
                _lastPerceptionVisibleTime = Time.time;

                float distanceFactor = Mathf.Clamp01(1f - visDistance / viewDistance);
                float gain = (0.4f + distanceFactor) / Mathf.Max(0.05f, timeToDetect);
                _awareness = Mathf.Min(1f, _awareness + gain * tickDt);

                SetTarget(visible);
                TrackTargetKinematics(visible.position, tickDt);
                _lastKnownTargetPosition = visible.position;
                _lastTargetSeenTime = Time.time;

                if (_awareness >= 1f)
                {
                    _hasSuspicion = false;

                    if (!_firstSightAcquired)
                    {
                        _firstSightAcquired = true;
                        _canFireAfter = Time.time + reactionTime *
                            (jitterReaction ? Random.Range(0.8f, 1.5f) : 1f);
                        if (debugLogging)
                            Debug.Log($"[EnemyAI] {name}: заметил цель «{visible.name}» " +
                                $"(дистанция {visDistance:F1} м)", this);
                    }

                    // Гистерезис границы боя: уже в бою — держим его до +5 м,
                    // иначе Chase↔Combat мигают каждый тик восприятия и дёргают анимацию.
                    float enterCombatAt = State == EnemyState.Combat
                        ? (useSquadTactics ? SquadCombatExitDistance
                            : maximumCombatDistance + 5f)
                        : SquadCombatEnterDistance;
                    ChangeState(visDistance <= enterCombatAt
                        ? EnemyState.Combat
                        : EnemyState.Chase);
                }
                else if (State is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                {
                    // БАГФИКС: раньше подозрительная точка обновлялась только
                    // из Patrol. Движущийся на краю зрения игрок терялся:
                    // Alert шёл в устаревшую точку. Теперь точка живёт,
                    // пока цель частично видна.
                    _suspicionPoint = visible.position;
                    _searchCenter = visible.position;
                    _hasSuspicion = true;
                    if (State == EnemyState.Patrol || State == EnemyState.Search)
                        ChangeState(EnemyState.Alert);
                }
                return;
            }

            if (_target != null && Time.time - _lastTargetSeenTime > targetMemoryDuration)
            {
                if (debugLogging)
                    Debug.Log($"[EnemyAI] {name}: потерял цель «{_target.name}» — " +
                        $"не видел {targetMemoryDuration:F0} сек", this);
                ClearTarget();
                _firstSightAcquired = false;
            }

            if (_target == null && _hasSuspicion && _awareness > 0.15f &&
                State != EnemyState.Search)
            {
                ChangeState(EnemyState.Alert);
            }
        }

        private void LogDiag()
        {
            string targetInfo = "—";
            float targetDist = 0f;
            if (_target != null)
            {
                targetDist = Vector3.Distance(transform.position, _target.position);
                bool vis = IsTargetVisibleNow(out string blocker);
                targetInfo = $"{_target.name} {targetDist:F1}м видно={(vis ? "да" : "нет")}" +
                    (vis ? "" : $" ({blocker})");
            }

            string weaponInfo;
            if (weapon == null)
            {
                weaponInfo = "НЕТ ОРУЖИЯ (не выдано — проверь EnemyLoadout/Weapon Prefab)";
            }
            else
            {
                string gate;
                if (weapon.IsReloading) gate = "перезарядка";
                else if (Time.time < _canFireAfter) gate = $"реакция ещё {(_canFireAfter - Time.time):F1}с";
                else if (Time.time < _nextBurstTime) gate = $"пауза между очередями ещё {(_nextBurstTime - Time.time):F1}с";
                else if (weapon.AmmunitionInMagazine <= 0)
                    gate = weapon.ReserveAmmunition > 0 ? "магазин пуст (перезаряжается)" : "НЕТ ПАТРОНОВ ВООБЩЕ";
                else if (_target != null && targetDist > weapon.Range)
                    gate = $"далеко: {targetDist:F0}м > дальности {weapon.Range:F0}м";
                else if (State != EnemyState.Combat)
                    gate = $"не в бою ({State})";
                else gate = "ДОЛЖЕН СТРЕЛЯТЬ";
                weaponInfo = $"{weapon.name} {weapon.AmmunitionInMagazine}/{weapon.ReserveAmmunition} → {gate}";
            }

            Debug.Log($"[EnemyAI] {name}: {State} aware={_awareness:F2} | цель: {targetInfo} | " +
                $"{weaponInfo} | мотор: {(_motor != null ? _motor.Mode.ToString() : "—")} | " +
                $"схватка: {_engageHits} попаданий | " +
                $"канд.: {_diagConsidered} " +
                $"(ком×{_diagRejectedTeam} мертв×{_diagRejectedDead} слой×{_diagRejectedLayer} " +
                $"дист×{_diagRejectedDist} fov×{_diagRejectedFov} los×{_diagRejectedLos})" +
                (_lastRejectNote != null ? $" | {_lastRejectNote}" : "") +
                (_lastNoiseInfo != null ? $" | последний {_lastNoiseInfo}" : ""), this);
        }

        private bool IsTargetVisibleNow(out string blocker)
        {
            blocker = "";
            if (_target == null) return false;

            Vector3 chest = GetTargetAimPoint(_target);
            if ((chest - eyePoint.position).sqrMagnitude > viewDistance * viewDistance)
            {
                blocker = "дальше viewDistance";
                return false;
            }

            if (HasLineOfSight(_target, chest)) return true;
            if (HasLineOfSight(_target, chest + Vector3.up * 0.55f)) return true;

            blocker = _lastLosBlocker != null ? $"мешает {_lastLosBlocker}" : "нет луча";
            return false;
        }

        private void TrackTargetKinematics(Vector3 pos, float dt = -1f)
        {
            if (dt <= 0f) dt = Mathf.Max(0.02f, perceptionInterval);
            if (_prevTargetPosition != Vector3.zero)
            {
                Vector3 delta = (pos - _prevTargetPosition) / Mathf.Max(0.0001f, dt);
                _targetVelocity = Vector3.Lerp(_targetVelocity, delta, 0.5f);
            }
            _prevTargetPosition = pos;
        }

        private Transform FindBestVisibleTarget(out float bestDistance)
        {
            bestDistance = float.MaxValue;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, viewDistance, _targetBuffer,
                targetMask, QueryTriggerInteraction.Ignore);

            _candidateSet.Clear();
            _lastRejectNote = null;
            _diagConsidered = 0;
            _diagRejectedTeam = 0;
            _diagRejectedDead = 0;
            _diagRejectedLayer = 0;
            _diagRejectedDist = 0;
            _diagRejectedFov = 0;
            _diagRejectedLos = 0;
            Transform best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider col = _targetBuffer[i];
                if (col == null) continue;

                CharacterHealth cand = col.GetComponentInParent<CharacterHealth>();
                if (cand == null) continue;
                EvaluateCandidate(cand, ref best, ref bestScore, ref bestDistance);
            }

            foreach (CharacterHealth cand in s_characters)
            {
                if (cand == null) continue;
                EvaluateCandidate(cand, ref best, ref bestScore, ref bestDistance);
            }

            return best;
        }

        private void EvaluateCandidate(CharacterHealth cand, ref Transform best,
            ref float bestScore, ref float bestDistance)
        {
            if (!_candidateSet.Add(cand)) return;
            _diagConsidered++;
            if (!cand.IsAlive) { _diagRejectedDead++; return; }
            if (cand.Team == enemyTeam) { _diagRejectedTeam++; return; }
            if (cand.gameObject == gameObject) return;

            if ((targetMask.value & (1 << cand.gameObject.layer)) == 0)
            {
                _diagRejectedLayer++;
                _lastRejectNote = $"слой {LayerMask.LayerToName(cand.gameObject.layer)} не в TargetMask";
                return;
            }

            Vector3 aimPoint = GetTargetAimPoint(cand.transform);
            Vector3 toTarget = aimPoint - eyePoint.position;
            float distance = toTarget.magnitude;
            if (distance <= 0.01f || distance > viewDistance) { _diagRejectedDist++; return; }

            if (Mathf.Abs(aimPoint.y - eyePoint.position.y) > verticalViewTolerance)
            {
                _diagRejectedDist++;
                return;
            }

            // БАГФИКС: в упор (closeDetectDistance) зрение круговое — раньше
            // игрок в 2 метрах сбоку/сзади был невидим, т.к. угол > FOV/2.
            bool isClose = distance <= closeDetectDistance;
            float angle = 0f;
            if (!isClose)
            {
                Vector3 flatFwd = eyePoint.forward; flatFwd.y = 0f;
                Vector3 flatDir = toTarget; flatDir.y = 0f;
                if (flatDir.sqrMagnitude < 0.0001f) { _diagRejectedFov++; return; }
                if (flatFwd.sqrMagnitude < 0.0001f)
                {
                    flatFwd = transform.forward; flatFwd.y = 0f;
                    if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
                }

                angle = Vector3.Angle(flatFwd, flatDir);
                if (angle > fieldOfView * 0.5f) { _diagRejectedFov++; return; }
            }

            if (!HasLineOfSight(cand, aimPoint)) { _diagRejectedLos++; return; }

            float score = distance + angle * 0.1f;
            if (score < bestScore)
            {
                bestScore = score;
                best = cand.transform;
                bestDistance = distance;
            }
        }

        private bool HasLineOfSight(CharacterHealth targetHealth, Vector3 targetPoint)
            => HasLineOfSight(targetHealth, targetPoint, out _);

        private bool HasLineOfSight(CharacterHealth targetHealth, Vector3 targetPoint,
            out bool blockedByAlly)
        {
            blockedByAlly = false;
            Vector3 origin = eyePoint.position;
            Vector3 dir = targetPoint - origin;
            float dist = dir.magnitude;
            if (dist <= 0.001f) return true;
            dir /= dist;

            int count = Physics.RaycastNonAlloc(origin, dir, _losBuffer,
                dist + 0.2f, visibilityMask, QueryTriggerInteraction.Ignore);

            int eyeCount = Physics.OverlapSphereNonAlloc(origin, 0.25f, _eyeBuffer,
                visibilityMask, QueryTriggerInteraction.Ignore);

            for (int i = 1; i < count; i++)
            {
                RaycastHit key = _losBuffer[i];
                int j = i - 1;
                while (j >= 0 && _losBuffer[j].distance > key.distance)
                {
                    _losBuffer[j + 1] = _losBuffer[j];
                    j--;
                }
                _losBuffer[j + 1] = key;
            }

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _losBuffer[i];
                Transform hitTransform = hit.transform;
                if (hitTransform == null) continue;

                if (hitTransform.root == transform.root) continue;
                if (hit.distance < 0.5f && IsEnclosingEyeCollider(hit.collider, eyeCount))
                    continue;

                // БАГФИКС 1: попадание в любое место тела цели (даже если
                // CharacterHealth висит не на том объекте, что коллайдер) —
                // раньше сравнивались Transform'ы и луч по своему же телу
                // считался стеной.
                if (targetHealth != null)
                {
                    CharacterHealth hitHealth =
                        hit.collider.GetComponentInParent<CharacterHealth>();
                    if (hitHealth == targetHealth)
                    {
                        _lastLosBlocker = null;
                        return true;
                    }
                }
                else if (hitTransform == targetHealth?.transform ||
                         (targetHealth != null && hitTransform.IsChildOf(targetHealth.transform)))
                {
                    _lastLosBlocker = null;
                    return true;
                }

                // БАГФИКС 2: стена ЗА спиной игрока (игрок без обычного
                // коллайдера — только CharacterController, который луч
                // игнорирует) давала ложное «не видно»: луч долетал до стены
                // в dist+0.1 и она считалась препятствием. Всё, что на уровне
                // цели или дальше неё — не препятствие.
                if (hit.distance >= dist - 0.35f)
                {
                    _lastLosBlocker = null;
                    return true;
                }

                CharacterHealth blockerHealth =
                    hit.collider.GetComponentInParent<CharacterHealth>();
                blockedByAlly = blockerHealth != null &&
                    blockerHealth != targetHealth &&
                    blockerHealth.Team == SquadTeam;

                _lastLosBlocker = hitTransform.name;
                return false;
            }

            _lastLosBlocker = null;
            return true;
        }

        private bool HasLineOfSight(Transform target, Vector3 targetPoint)
        {
            CharacterHealth health = null;
            if (target != null) health = target.GetComponentInParent<CharacterHealth>();
            if (health == null && _targetHealth != null && target == _target) health = _targetHealth;
            return HasLineOfSight(health, targetPoint);
        }

        private bool IsEnclosingEyeCollider(Collider collider, int eyeCount)
        {
            if (collider == null) return false;
            for (int i = 0; i < eyeCount; i++)
                if (_eyeBuffer[i] == collider) return true;
            return false;
        }

        private void UpdatePatrol()
        {
            if (patrolRoute == null || patrolRoute.Count == 0)
            {
                if (wanderWhenNoRoute) UpdateWander();
                else
                {
                    _motor.Stop();
                    UpdateIdleLookAround();
                }
                return;
            }

            Transform point = patrolRoute.GetPoint(_patrolIndex);
            if (point == null) return;

            _motor.SetSpeed(patrolSpeed);

            if (_waitingAtPoint)
            {
                if (Time.time >= _patrolWaitUntil)
                {
                    CancelLookAround();
                    _waitingAtPoint = false;
                    AdvancePatrolIndex();

                    Transform next = patrolRoute.GetPoint(_patrolIndex);
                    if (next != null) MoveTo(next.position);
                }
                else
                {
                    UpdateIdleLookAround();
                }
                return;
            }

            if (!_motor.HasDestination) MoveTo(point.position);

            float flatDist = FlatDistance(transform.position, point.position);

            if (flatDist <= pointReachRadius || _motor.HasArrived())
            {
                _waitingAtPoint = true;
                _patrolWaitUntil = Time.time + pointWaitDuration;
                _motor.Stop();
            }
        }

        private void AdvancePatrolIndex()
        {
            if (patrolRoute == null || patrolRoute.Count == 0) return;

            _patrolIndex = patrolRoute.NextIndex(_patrolIndex);
        }

        private void UpdateWander()
        {
            _motor.SetSpeed(patrolSpeed * _wanderSpeedFactor);
            if (_waitingAtPoint)
            {
                if (Time.time < _patrolWaitUntil)
                {
                    UpdateIdleLookAround();
                    return;
                }

                _waitingAtPoint = false;
                CancelLookAround();
            }

            if (_motor.HasDestination)
            {
                if (!_motor.HasArrived()) return;
                _motor.Stop();
                _waitingAtPoint = true;
                _patrolWaitUntil = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
                UpdateIdleLookAround();
                return;
            }

            if (Time.time < _nextWanderAttemptTime) return;

            _nextWanderAttemptTime = Time.time + wanderRetryDelay;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                // Свой сектор блуждания + равномерно по площади: точки не сыпятся
                // в одну сторону, солдаты расходятся по местности.
                float angle = _wanderBaseAngle +
                    Random.Range(-wanderSectorDegrees * 0.5f, wanderSectorDegrees * 0.5f);
                float radius = Mathf.Sqrt(Random.value) * wanderRadius;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 candidate = _homePosition +
                    new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius);

                if (!_motor.SampleReachablePoint(candidate, wanderRadius, out Vector3 point))
                    continue;

                if (FlatDistance(transform.position, point) <= pointReachRadius * 2f)
                    continue;

                // Не вставать в кучу с соседями по отряду.
                if (useSquadTactics &&
                    InfantrySquad.IsCrowded(this, point, squadSeparationDistance))
                    continue;

                _wanderSpeedFactor = 1f + Random.Range(-wanderSpeedVariation, wanderSpeedVariation);
                _motor.SetSpeed(patrolSpeed * _wanderSpeedFactor);
                MoveTo(point);
                return;
            }
            _waitingAtPoint = true;
            _patrolWaitUntil = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
            UpdateIdleLookAround();
        }

        private void UpdateIdleLookAround()
        {
            if (!lookAroundWhenIdle) return;
            if (_motor.CurrentSpeed > 0.2f) return;

            if (_lookAroundPhase == LookAroundPhase.None)
            {
                if (Time.time < _nextLookAroundTime) return;

                _lookAroundBaseYaw = transform.eulerAngles.y;
                float side = Random.value < 0.5f ? -1f : 1f;
                _lookAroundTargetYaw = _lookAroundBaseYaw +
                    side * Random.Range(lookAroundAngle * 0.55f, lookAroundAngle);
                _lookAroundPhase = LookAroundPhase.TurnOut;
                _lookAroundHoldUntil = 0f;
            }

            float targetYaw = _lookAroundPhase == LookAroundPhase.Return
                ? _lookAroundBaseYaw
                : _lookAroundTargetYaw;
            Vector3 lookPoint = transform.position +
                Quaternion.Euler(0f, targetYaw, 0f) * Vector3.forward * 4f;
            _motor.FaceTowardsAtSpeed(lookPoint, lookAroundTurnSpeed);

            float angle = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, targetYaw));
            switch (_lookAroundPhase)
            {
                case LookAroundPhase.TurnOut:
                    if (angle <= 3f)
                    {
                        _lookAroundPhase = LookAroundPhase.Hold;
                        _lookAroundHoldUntil = Time.time + Random.Range(lookAroundHoldMin,
                            lookAroundHoldMax);
                    }
                    break;

                case LookAroundPhase.Hold:
                    if (Time.time >= _lookAroundHoldUntil)
                        _lookAroundPhase = LookAroundPhase.Return;
                    break;

                case LookAroundPhase.Return:
                    if (angle <= 3f)
                    {
                        _lookAroundPhase = LookAroundPhase.None;
                        _nextLookAroundTime = Time.time + Random.Range(lookAroundIntervalMin,
                            lookAroundIntervalMax);
                    }
                    break;
            }
        }

        private void CancelLookAround()
        {
            if (_lookAroundPhase == LookAroundPhase.None) return;
            _lookAroundPhase = LookAroundPhase.None;
            _nextLookAroundTime = Time.time + Random.Range(lookAroundIntervalMin,
                lookAroundIntervalMax);
        }

        private void UpdateAlert()
        {
            if (!_hasSuspicion && _awareness <= 0.05f)
            {
                ChangeState(EnemyState.Patrol);
                return;
            }

            _motor.SetSpeed(chaseSpeed * 0.8f);
            RefreshDestination(_suspicionPoint);

            if (_motor.HasArrived())
            {
                _hasSuspicion = false;
                _awareness = Mathf.Min(_awareness, 0.4f);
                _motor.Stop();
                _searchCenter = _suspicionPoint;
                ChangeState(EnemyState.Search);
            }
        }

        private void UpdateSearch()
        {
            _motor.SetSpeed(patrolSpeed);
            _motor.Stop();

            if (Time.time < _searchFaceUntil)
            {
                _motor.FaceTowardsAtSpeed(_searchCenter, combatTurnSpeed * 1.5f);
            }
            else
            {
                if (!_searchBaseCaptured)
                {
                    _searchBaseCaptured = true;
                    _searchBaseYaw = transform.eulerAngles.y;
                }

                float sweepTime = Time.time - _searchFaceUntil;
                float targetYaw = _searchBaseYaw +
                    Mathf.Sin(sweepTime * 0.7f) * searchSweepAmplitude;
                Quaternion targetRot = Quaternion.Euler(0f, targetYaw, 0f);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, searchSweepSpeed * Time.deltaTime);
            }

            // БАГФИКС: раньше поиск длился ~1 сек (0.4→0.02 при decay 0.35),
            // враг «забывал» игрока почти сразу и выглядел слепым.
            // Теперь держим поиск минимум до конца памяти о цели.
            if (_awareness <= 0.02f && Time.time >= _searchUntil &&
                Time.time - _lastTargetSeenTime > targetMemoryDuration)
                ChangeState(EnemyState.Patrol);
        }

        private void UpdateChase()
        {
            if (_target == null) { GoSearchLastKnown(); return; }
            if (_targetHealth != null && !_targetHealth.IsAlive) { LoseTarget("мертва"); return; }

            float distance = Vector3.Distance(transform.position, _target.position);

            if (CanSeeCurrentTarget() && distance <= SquadCombatEnterDistance)
            {
                ChangeState(EnemyState.Combat);
                return;
            }

            _motor.SetSpeed(chaseSpeed * (useSquadTactics ? squadPursuitSpeedScale : 1f));
            bool recentlyVisible = Time.time - _lastTargetSeenTime <= perceptionInterval * 2f;
            Vector3 chaseGoal = useSquadTactics && recentlyVisible
                ? InfantrySquad.AdjustChaseGoal(this, _lastKnownTargetPosition)
                : _lastKnownTargetPosition;
            RefreshDestination(chaseGoal);

            if (_motor.HasArrived())
            {
                if (_chaseStuckTime < 0f) _chaseStuckTime = Time.time;
                if (Time.time - _chaseStuckTime > 2.5f)
                {
                    _chaseStuckTime = -1f;
                    if (debugLogging)
                        Debug.Log($"[EnemyAI] {name}: погоня упёрлась — перехожу к проверке.", this);
                    GoSearchLastKnown();
                }
            }
            else _chaseStuckTime = -1f;
        }

        private void UpdateCombat()
        {
            if (_target == null) { GoSearchLastKnown(); return; }
            if (_targetHealth != null && !_targetHealth.IsAlive) { LoseTarget("мертва"); return; }

            if (weapon == null)
            {
                weapon = GetComponentInChildren<HitscanWeapon>(true);
                if (weapon != null)
                {
                    SubscribeWeapon(weapon);
                    if (debugLogging)
                        Debug.Log($"[EnemyAI] {name}: подобрал оружие «{weapon.name}» уже в бою.", this);
                }
            }

            float distance = Vector3.Distance(transform.position, _target.position);
            bool visible = CanSeeCurrentTarget();
            if (visible) _lastVisibleTime = Time.time;

            UpdateEngageHitTracking();

            // Потеряли из виду — короткий «миг» (луч зацепил угол) терпим,
            // дальше идём по последней известной точке.
            bool holdingThroughFlicker = !visible &&
                Time.time - _lastVisibleTime <= combatLoseSightGrace;

            float exitDistance = useSquadTactics
                ? SquadCombatExitDistance
                : maximumCombatDistance + 5f;

            if ((!visible && !holdingThroughFlicker) || distance > exitDistance)
            {
                ChangeState(EnemyState.Chase);
                return;
            }

            _motor.FaceTowardsAtSpeed(holdingThroughFlicker ? _lastKnownTargetPosition : _target.position,
                combatTurnSpeed);

            if (holdingThroughFlicker)
            {
                _motor.Stop();
                return;
            }

            // СВОЯ ТОЧКА В БОЮ.
            // Раньше все шли на координаты игрока и вставали в одну точку.
            // Теперь у каждого своё место в строю (вбок + чуть по глубине),
            // якорь едет плавно — солдат перемещается без рывков:
            // либо чуть вперёд, либо чуть вперёд и вбок.
            Vector3 anchor = UpdateCombatAnchor(distance);
            float anchorDistance = FlatDistance(transform.position, anchor);

            bool holdingFire = Time.time < _fireAnchorUntil;

            if (!holdingFire && anchorDistance > combatAnchorTolerance)
            {
                // Далеко от цели — подходим быстрее (но всё равно не рывком),
                // на дистанции — переступаем не спеша.
                float speed = distance > preferredCombatDistance + 3f
                    ? chaseSpeed
                    : combatApproachSpeed;
                _motor.SetSpeed(speed);
                RefreshDestination(anchor, 1f);
            }
            else
            {
                _motor.Stop();
            }

            UpdateFiring(distance, _motor.CurrentSpeed);
        }

        /// <summary>
        /// Своя точка в бою. Считается от цели: дистанция — «не отступаем,
        /// но и не лезем в упор», бок — своё место в строю (солдаты расходятся
        /// вбок и не стоят в одной точке). Готовый якорь двигается к цели
        /// с ограниченной скоростью — перемещение плавное, без рывков.
        /// </summary>
        private Vector3 UpdateCombatAnchor(float distance)
        {
            Vector3 targetPosition = _target != null ? _target.position : transform.position;

            // 1. Стабильная система координат строя: медленно поворачивается
            //    вместе с целью, поэтому якорь не прыгает по кругу.
            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Vector3 desired = toTarget.normalized;
                if (!_formationReady)
                {
                    _formationForward = desired;
                    _formationReady = true;
                }
                else
                {
                    float k = 1f - Mathf.Exp(-2.5f * Time.deltaTime);
                    _formationForward = Vector3.Slerp(_formationForward, desired, k);
                    if (_formationForward.sqrMagnitude > 0.0001f) _formationForward.Normalize();
                }
            }

            Vector3 forward = _formationForward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // 2. Своё место в строю: боковой сдвиг + небольшой разброс по глубине.
            float lateral = 0f;
            float depth = 0f;
            if (useSquadTactics)
                InfantrySquad.ComputeFormationSlot(this, targetPosition, right, out lateral, out depth);

            // 3. Дистанция до цели: держим боевую, но назад не отходим.
            float range = Mathf.Min(distance, preferredCombatDistance);
            range = Mathf.Clamp(range + depth, 1.5f, preferredCombatDistance + squadDepthSpread);

            // На близкой дистанции строй сужается — иначе солдаты разъезжаются
            // вокруг игрока шире, чем нужно.
            float lateralScale = Mathf.Clamp(range / Mathf.Max(1f, preferredCombatDistance), 0.4f, 1f);

            Vector3 anchor = targetPosition - forward * range + right * (lateral * lateralScale);

            // 4. Мягкое расхождение: если кто-то подошёл слишком близко —
            //    якорь сдвигается вбок (не рывок, а плавный уход).
            if (useSquadTactics)
                anchor = InfantrySquad.ApplySeparation(this, anchor);

            // 5. Якорь догоняет цель с ограниченной скоростью.
            if (!_hasCombatAnchor)
            {
                _combatAnchor = anchor;
                _hasCombatAnchor = true;
            }
            else
            {
                Vector3 delta = anchor - _combatAnchor;
                delta.y = 0f;
                float maxStep = combatAnchorTrackSpeed * Time.deltaTime;
                if (delta.sqrMagnitude > maxStep * maxStep) delta = delta.normalized * maxStep;
                _combatAnchor += delta;
            }

            return _combatAnchor;
        }

        private void UpdateEngageHitTracking()
        {
            if (_targetHealth == null) return;

            if (_targetHealth.CurrentHealth < _lastTargetHp - 0.01f)
            {
                _engageHits++;
                if (debugLogging)
                    Debug.Log($"[EnemyAI] {name}: попадание {_engageHits} по «{_target.name}» " +
                        $"(осталось {_targetHealth.CurrentHealth:F0} HP)", this);
            }
            _lastTargetHp = _targetHealth.CurrentHealth;
        }

        private void UpdateFiring(float distance, float selfSpeed)
        {
            if (weapon == null || weapon.IsReloading || Time.time < _nextBurstTime)
                return;

            if (Time.time < _canFireAfter)
                return;

            if (distance > weapon.Range) return;

            // Не стрелять в доводке: огонь только когда развернулся на игрока.
            // Серия не сгорает — при доводке очередь продолжится с того же места.
            if (_target != null && distance > 0.5f && !IsFacingTarget(maxFireAngle))
                return;

            if (weapon.AmmunitionInMagazine <= 0) { weapon.BeginReload(); return; }

            if (_shotsRemaining <= 0)
            {
                _shotsRemaining = Random.Range(minimumBurstSize, maximumBurstSize + 1);
                _burstShotIndex = 0;
            }

            Vector3 aimPoint = ComputeAimPoint(distance, selfSpeed);
            weapon.DamageScale = EngageDamageScale() *
                Random.Range(1f - pacingDamageJitter, 1f + pacingDamageJitter);

            if (weapon.TryFire(aimPoint, gameObject))
            {
                if (_fireAnchorUntil < Time.time)
                    _fireAnchorUntil = Time.time +
                        squadFireAnchorDuration * Random.Range(0.9f, 1.1f);

                _shotsRemaining--;
                _burstShotIndex++;
                if (_shotsRemaining <= 0)
                    _nextBurstTime = Time.time + Random.Range(minimumBurstPause, maximumBurstPause) *
                        ((_engageHits >= 1 && _engageHits <= 2) ? midFightPauseMult : 1f);
            }
        }

        /// <summary>Корпус довернулся на цель в пределах допуска (только рыскание, без тангажа).</summary>
        private bool IsFacingTarget(float toleranceDeg)
        {
            if (_target == null) return false;
            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return true;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(fwd, toTarget) <= Mathf.Max(0f, toleranceDeg);
        }

        private float EngageDamageScale()
        {
            if (pacingDamageStages == null || pacingDamageStages.Length == 0) return 1f;            int index = Mathf.Min(_engageHits, pacingDamageStages.Length - 1);
            return Mathf.Max(0.05f, pacingDamageStages[index]);
        }

        private float EngageSpreadScale()
        {
            if (_engageHits <= 0 && Time.time - _engageStartTime < openingDuration)
                return openingSpread;
            if (_engageHits <= 2)
                return midFightSpread;
            return closingSpread;
        }

        private Vector3 ComputeAimPoint(float distance, float selfSpeed)
        {
            Vector3 basePoint = GetTargetAimPoint(_target);

            float lead = leadAccuracy * 0.15f;
            basePoint += _targetVelocity * lead;
            basePoint += Vector3.up * (burstClimb * Mathf.Min(1f, _burstShotIndex / 4f));
            float spread = baseSpread + spreadPerTenMeters * (distance / 10f);
            spread *= Mathf.Lerp(1f, movingSpreadMultiplier,
                Mathf.Clamp01(selfSpeed / Mathf.Max(0.1f, chaseSpeed)));
            spread *= Mathf.Lerp(1f, suppressedSpreadMultiplier, _suppression);
            spread *= EngageSpreadScale();

            Vector2 circle = Random.insideUnitCircle * spread;
            Vector3 right = transform.right;
            Vector3 up = Vector3.up;

            return basePoint + right * circle.x + up * circle.y;
        }

        private void GoSearchLastKnown()
        {
            _motor.SetSpeed(chaseSpeed);
            RefreshDestination(_lastKnownTargetPosition);

            if (_motor.HasArrived())
            {
                ClearTarget();
                _firstSightAcquired = false;
                _awareness = Mathf.Min(_awareness, 0.4f);
                _searchCenter = _lastKnownTargetPosition;
                ChangeState(EnemyState.Search);
            }
        }

        private void LoseTarget(string reason = "потеряна")
        {
            if (debugLogging && _target != null)
                Debug.Log($"[EnemyAI] {name}: цель «{_target.name}» {reason} — возврат в патруль.", this);
            ClearTarget();
            _firstSightAcquired = false;
            ChangeState(EnemyState.Patrol);
        }

        private void SetTarget(Transform target)
        {
            if (_target != target)
            {
                _prevTargetPosition = target.position;
                _targetVelocity = Vector3.zero;
            }
            _target = target;
            _targetHealth = target.GetComponentInParent<CharacterHealth>();
        }

        private void ClearTarget()
        {
            _target = null;
            _targetHealth = null;
            _targetVelocity = Vector3.zero;
            _prevTargetPosition = Vector3.zero;
        }

        private bool CanSeeCurrentTarget()
        {
            if (_target == null) return false;

            Vector3 chest = GetTargetAimPoint(_target);
            Vector3 toTarget = chest - eyePoint.position;
            if (toTarget.sqrMagnitude > viewDistance * viewDistance) return false;

            bool visible = HasLineOfSight(_target, chest);
            if (!visible)
                visible = HasLineOfSight(_target, chest + Vector3.up * 0.55f);

            if (visible)
            {
                _lastKnownTargetPosition = _target.position;
                _lastTargetSeenTime = Time.time;
                TrackTargetKinematics(_target.position);
            }
            return visible;
        }

        private Vector3 GetTargetAimPoint(Transform target)
        {
            if (target == null) return transform.position;

            var controller = target.GetComponentInParent<CharacterController>();
            if (controller != null) return controller.bounds.center;

            var col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : target.position + Vector3.up * 1.4f;
        }

        private void RefreshDestination(Vector3 destination, float minShift = 0.6f)
        {
            if (!_motor.HasDestination)
            {
                MoveTo(destination);
                return;
            }

            if (Time.time < _nextPathRefreshTime) return;
            _nextPathRefreshTime = Time.time + pathRefreshInterval;

            if (_hasPathDestination &&
                FlatDistance(destination, _lastPathDestination) < minShift)
                return;

            MoveTo(destination);
        }

        private void MoveTo(Vector3 destination)
        {
            _motor.MoveTo(destination);
            _lastPathDestination = destination;
            _hasPathDestination = true;
            _nextPathRefreshTime = Time.time + pathRefreshInterval;
        }
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void ChangeState(EnemyState newState)
        {
            if (State == EnemyState.Dead || State == newState) return;
            EnemyState previous = State;
            State = newState;
            CancelLookAround();
            if (newState != EnemyState.Combat)
            {
                _fireAnchorUntil = 0f;
                _hasCombatAnchor = false;
                _formationReady = false;
            }
            if (newState == EnemyState.Combat && previous != EnemyState.Combat)
            {
                if (_target == null || _target != _engageTarget ||
                    Time.time - _engageEndTime > 10f)
                {
                    _engageTarget = _target;
                    _engageHits = 0;
                    _engageStartTime = Time.time;
                    _lastTargetHp = _targetHealth != null ? _targetHealth.CurrentHealth : 0f;
                }
            }
            if (previous == EnemyState.Combat && newState != EnemyState.Combat)
                _engageEndTime = Time.time;

            if (debugLogging)
                Debug.Log($"[EnemyAI] {name}: {previous} → {newState} " +
                    $"(awareness={_awareness:F2}, target={(_target != null ? _target.name : "—")})", this);

            bool aiming = newState is EnemyState.Combat;
            if (enemyAnimator != null) enemyAnimator.SetAiming(aiming);
            SafeSetBool(IsAimingHash, aiming);
            _motor.SetAutoRotation(!aiming);

            switch (newState)
            {
                case EnemyState.Patrol:
                    _motor.SetSpeed(patrolSpeed);
                    _waitingAtPoint = false;
                    _nextWanderAttemptTime = 0f;
                    if (previous is EnemyState.Search or EnemyState.Chase or EnemyState.Combat)
                        if (voice != null) voice.PlayLostTarget();
                    break;

                case EnemyState.Alert:
                    _motor.SetSpeed(chaseSpeed * 0.8f);
                    if (previous == EnemyState.Patrol && voice != null) voice.PlayAlert();
                    break;

                case EnemyState.Search:
                    _motor.SetSpeed(patrolSpeed);
                    _searchFaceUntil = Time.time + 1.2f;
                    _searchBaseCaptured = false;
                    _searchUntil = Time.time + Mathf.Max(4f, targetMemoryDuration);
                    break;

                case EnemyState.Chase:
                    _motor.SetSpeed(chaseSpeed);
                    _chaseStuckTime = -1f;
                    if (previous is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                        if (voice != null) voice.PlaySpotted();
                    break;

                case EnemyState.Combat:
                    _lastVisibleTime = Time.time;
                    if (previous is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                        if (voice != null) voice.PlaySpotted();
                    break;
            }
        }

        private void OnNoiseCreated(NoiseSystem.Noise noise)
        {
            if (!_health.IsAlive || State == EnemyState.Dead) return;

            float hearingRadius = noise.Radius * Mathf.Max(1f, hearingRadiusMultiplier);
            float distance = Vector3.Distance(noise.Position, transform.position);
            if (distance > hearingRadius) return;

            if (noise.Source != null)
            {
                if (noise.Source.GetComponentInParent<CharacterHealth>() == _health) return;

                var srcHealth = noise.Source.GetComponentInParent<CharacterHealth>();
                if (srcHealth != null && srcHealth.Team == _health.Team) return;
            }

            float falloff = 1f - Mathf.Clamp01(distance / hearingRadius);
            float heard = Mathf.Clamp01(falloff * noise.Intensity *
                Mathf.Max(0.1f, hearingSensitivity));
            if (heard < hearingThreshold) return;

            _awareness = Mathf.Min(0.9f, _awareness + heard * 0.75f);
            _suspicionPoint = noise.Position;
            _searchCenter = noise.Position;
            _hasSuspicion = true;
            _lastNoiseInfo = $"шум {distance:F0}м (громк. {heard:P0})";
            if (falloff > 0.5f)
                _motor.FaceTowardsAtSpeed(noise.Position, 90f);

            if (debugLogging && _target == null)
                Debug.Log($"[EnemyAI] {name}: услышал {_lastNoiseInfo} (aware={_awareness:F2})", this);

            if (_target == null && _awareness > 0.1f &&
                State is EnemyState.Patrol or EnemyState.Search)
            {
                ChangeState(EnemyState.Alert);
            }
        }

        private void OnDamaged(DamageInfo damage)
        {
            _suppression = Mathf.Min(1f, _suppression + (damage.IsSuppression ? 0.5f : 0.35f));

            if (!damage.IsSuppression)
            {
                if (voice != null) voice.PlayPain();
                if (voice != null) voice.PlayImpact();
                // ДЫРА: Hit-триггер на КАЖДЫЙ урон спамил и рвал стрельбу.
                // Основной путь — EnemyAnimator со своим кулдауном, legacy троттлим тут.
                if (enemyAnimator != null) enemyAnimator.PlayHit();
                else if (Time.time >= _nextLegacyHitAnimTime)
                {
                    _nextLegacyHitAnimTime = Time.time + 0.45f;
                    SafeSetTrigger(HitHash);
                }
            }

            if (damage.Attacker != null)
            {
                var attackerHealth = damage.Attacker.GetComponentInParent<CharacterHealth>();
                if (attackerHealth != null && attackerHealth.IsAlive && attackerHealth.Team != enemyTeam)
                {
                    SetTarget(attackerHealth.transform);
                    _lastKnownTargetPosition = attackerHealth.transform.position;
                    _lastTargetSeenTime = Time.time;
                    _awareness = 1f;
                    if (!_firstSightAcquired)
                    {
                        _firstSightAcquired = true;
                        _canFireAfter = Time.time + reactionTime * 0.5f *
                            (jitterReaction ? Random.Range(0.8f, 1.5f) : 1f);
                    }
                }
            }

            if (_target != null &&
                State is not EnemyState.Combat and not EnemyState.Chase)
                ChangeState(EnemyState.Chase);
        }

        private void OnDied(DamageInfo damage)
        {
            State = EnemyState.Dead;

            if (voice != null) voice.PlayDeath();
            if (loadout != null) loadout.HandleOwnerDeath();
            else if (weapon != null)
            {
                weapon.CancelReload();
                weapon.enabled = false;
            }

            UnsubscribeWeapon(weapon);

            if (enemyAnimator != null) enemyAnimator.PlayDeath();
            else if (animator != null)
            {
                SafeSetBool(IsAimingHash, false);
                SafeSetBool(IsMovingHash, false);
                SafeSetFloat(SpeedHash, 0f, 0.15f, Time.deltaTime);
                SafeSetTrigger(DieHash);
            }

            _motor.Disable();

            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = false;

            if (corpseLifetime > 0f) Destroy(gameObject, corpseLifetime);

            enabled = false;
        }

        private void UpdateAnimator()
        {
            float speed = _motor.CurrentSpeed;
            // ДЫРА: скорость слалась в м/с напрямую — Blend Tree под 0..1 улетал в потолок.
            // EnemyAnimator сам раскладывает м/с по якорям; legacy-путь шлём как есть,
            // но только если параметры реально существуют (иначе был спам ошибок).
            if (enemyAnimator != null)
            {
                enemyAnimator.SetLocomotion(speed, _motor.WantsToMove);
                return;
            }
            if (animator == null) return;

            SafeSetFloat(SpeedHash, speed, 0.15f, Time.deltaTime);
            if (HasAnimParam(IsMovingHash)) animator.SetBool(IsMovingHash, speed > 0.2f || _motor.WantsToMove);
        }

        private void UpdateVoiceFootsteps()
        {
            if (voice == null) return;

            Vector3 velocity = _motor.Velocity;
            velocity.y = 0f;
            voice.UpdateFootsteps(velocity.magnitude, _motor.IsGrounded);
        }

        private void OnDisable()
        {
            UnregisterSoldier();
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }
            NoiseSystem.NoiseCreated -= OnNoiseCreated;
            UnsubscribeWeapon(weapon);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform origin = eyePoint != null ? eyePoint : transform;

            Gizmos.color = new Color(1f, 0.85f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, viewDistance);

            Vector3 fwd = origin.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * fwd;
            Vector3 right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * fwd;

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(origin.position, left * viewDistance);
            Gizmos.DrawRay(origin.position, right * viewDistance);

            if (wanderWhenNoRoute && (patrolRoute == null || patrolRoute.Count == 0))
            {
                Vector3 center = Application.isPlaying ? _homePosition : transform.position;
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
                Gizmos.DrawWireSphere(center, wanderRadius);
            }

            if (Application.isPlaying)
            {
                if (_target != null)
                {
                    Gizmos.color = Color.Lerp(Color.green, Color.red, _awareness);
                    Gizmos.DrawLine(origin.position, _target.position);
                    Gizmos.DrawWireSphere(_target.position, 0.3f);
                }
                else
                {
                    Gizmos.color = _awareness > 0.15f ? new Color(1f, 0.5f, 0f) : Color.gray;
                    Gizmos.DrawRay(origin.position, origin.forward * 3f);
                }

                if (useSquadTactics)
                {
                    Gizmos.color = new Color(0.35f, 0.8f, 1f, 0.2f);
                    Gizmos.DrawWireSphere(transform.position, squadAwarenessRadius);

                    Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.35f);
                    Gizmos.DrawWireSphere(transform.position, squadMinimumSpacing);

                    Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.25f);
                    if (Application.isPlaying)
                    {
                        Gizmos.DrawWireSphere(transform.position, SquadPersonalSpace);
                    }
                    else
                    {
                        Gizmos.DrawWireSphere(transform.position, squadComfortSpacing.x);
                        Gizmos.DrawWireSphere(transform.position, squadComfortSpacing.y);
                    }

                    EnemyAI nearest = InfantrySquad.FindNearestAlly(this, out float nearestDistance);
                    if (nearest != null)
                    {
                        float pairSpacing = Application.isPlaying
                            ? (SquadPersonalSpace + nearest.SquadPersonalSpace) * 0.5f
                            : squadComfortSpacing.x;
                        Gizmos.color = nearestDistance < pairSpacing - 0.8f
                            ? new Color(1f, 0.25f, 0.2f, 0.8f)
                            : new Color(0.35f, 0.9f, 1f, 0.5f);
                        Gizmos.DrawLine(transform.position, nearest.transform.position);
                    }

                    if (_hasCombatAnchor)
                    {
                        Gizmos.color = new Color(0.2f, 1f, 1f, 0.8f);
                        Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, _combatAnchor);
                        Gizmos.DrawWireSphere(_combatAnchor, 0.3f);
                    }
                }
            }

            if (targetMask == 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            }
        }
#endif
    }
}

