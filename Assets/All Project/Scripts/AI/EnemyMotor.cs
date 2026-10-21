using UnityEngine;
using UnityEngine.AI;

namespace ZharkoeLeto44.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class EnemyMotor : MonoBehaviour
    {
        public enum MotorMode { NavMesh, Fallback }

        [Header("Общее")]
        [Tooltip("Радиус, в котором цель считается достигнутой.")]
        [SerializeField, Min(0.05f)] private float arriveRadius = 1.2f;
        [Tooltip("Скорость разворота корпуса, град/сек. Это максимум: доводка " +
                 "замедляется к концу поворота, как у человека.")]
        [SerializeField, Min(1f)] private float turnSpeed = 180f;

        [Header("Плавность хода")]
        [Tooltip("Разгон текущей скорости, м/с². Убирает рывок в начале движения.")]
        [SerializeField, Min(0.5f)] private float speedAcceleration = 2.5f;
        [Tooltip("Торможение текущей скорости, м/с². Убирает резкую остановку.")]
        [SerializeField, Min(0.5f)] private float speedDeceleration = 5f;
        [Tooltip("Разгон на ПЕРЕБЕЖКЕ, м/с². Отдельный от обычного: с 2.5 м/с² перебежка " +
                 "выглядела «вязкой» — солдат долго раскачивался на месте, прежде чем " +
                 "разогнаться. Солдат должен срываться в перебежку решительно.")]
        [SerializeField, Min(1f)] private float sprintAcceleration = 12f;
        [Tooltip("Торможение на перебежке, м/с².")]
        [SerializeField, Min(1f)] private float sprintDeceleration = 16f;
        [Tooltip("Насколько резко скорость режется у самой точки: 0 — полный разгон " +
                 "до упора, 1 — плавное подтормаживание. По умолчанию солдат мягко " +
                 "подходит к точке, а не тормозит на месте.")]
        [SerializeField, Range(0f, 1f)] private float arriveDamping = 0.75f;

        [Header("Проверка пути")]
        [Tooltip("Проверять ли, что до точки действительно есть путь (а не только точка " +
                 "на NavMesh). Точка «через стену» на навмеше есть, но пути до неё нет.")]
        [SerializeField] private bool validatePath = true;
        [Tooltip("Во сколько раз длина пути может превышать прямую дистанцию. " +
                 "Больше — солдат идёт в обход, а не сквозь стену.")]
        [SerializeField, Min(1f)] private float maxPathLengthRatio = 2.5f;

        [Header("NavMeshAgent: согласование с мотором")]
        [Tooltip("Разгон агента, м/с². Ставим высокий: иначе у агента СВОЙ разгон " +
                 "(в префабе 12) и он спорит с разгоном мотора — солдат стартует " +
                 "рывком независимо от настроек ИИ.")]
        [SerializeField, Min(0f)] private float agentAccelerationOverride = 60f;
        [Tooltip("Отключить автоторможение агента (autoBraking): тормозит мотор, " +
                 "иначе двойное торможение (агент + мотор) и солдат «проседает» на месте.")]
        [SerializeField] private bool disableAgentAutoBraking = true;
        [Tooltip("Радиус агента, м. Должен совпадать с радиусом тела, иначе солдаты " +
                 "протискиваются друг сквозь друга.")]
        [SerializeField, Min(0.05f)] private float agentRadius = 0.4f;

        [Header("Режим без NavMesh")]
        [Tooltip("Разрешить ходьбу без запечённого NavMesh. Выключи, если ходьба " +
                 "должна быть строго по навмешу.")]
        [SerializeField] private bool allowFallbackMovement = true;
        [Tooltip("Как часто проверять, не появился ли NavMesh под ногами, сек.")]
        [SerializeField, Min(0.1f)] private float navMeshRecheckInterval = 1f;
        [Tooltip("Радиус поиска NavMesh вокруг врага при попытке вернуться на него.")]
        [SerializeField, Min(0.5f)] private float navMeshSnapRadius = 3f;

        [Header("Земля и гравитация (режим Fallback)")]
        [Tooltip("Слои, которые считаются землёй.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Максимальная высота ступеньки, на которую враг может забраться.")]
        [SerializeField, Min(0f)] private float stepHeight = 0.45f;
        [Tooltip("Насколько далеко вниз искать землю, прежде чем считать, что враг падает.")]
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 2.5f;
        [Tooltip("Максимальный уклон поверхности, по которой можно идти, град.")]
        [SerializeField, Range(1f, 89f)] private float maximumSlope = 50f;
        [Tooltip("Максимальный перепад высоты на шаг вперёд (1.4 м), м. Неровный " +
                 "рельеф: холмы и овраги требуют 1.5–2.5, иначе враг отказывается " +
                 "идти по склону и топчется.")]
        [SerializeField, Min(0.2f)] private float maximumStepDown = 1.5f;
        [SerializeField, Min(0f)] private float gravity = 22f;
        [Tooltip("Отступ центра капсулы от земли. Обычно половина высоты врага.")]
        [SerializeField, Min(0f)] private float groundOffset = 0f;

        [Header("Притяжение к земле (чтобы не летал)")]
        [Tooltip("Притягивать к земле и в режиме NavMesh. Если точка маршрута " +
                 "висит чуть выше территории — враг будет бежать по земле, а не лететь. " +
                 "Выключи только если враг должен летать.")]
        [SerializeField] private bool stickToGround = true;
        [Tooltip("Насколько далеко вниз искать землю в режиме NavMesh.")]
        [SerializeField, Min(0.5f)] private float navMeshGroundProbe = 4f;
        [Tooltip("Мгновенно ставить на землю при старте, если она рядом.")]
        [SerializeField] private bool snapToGroundOnStart = true;
        [Tooltip("Притягивать точку назначения к земле/NavMesh по Y, чтобы точки " +
                 "маршрута в воздухе не поднимали врага.")]
        [SerializeField] private bool snapDestinationToGround = true;

        [Header("Humanity (плавность без NavMesh)")]
        [Tooltip("Разгон в режиме Fallback, м/с². Без него враг стартует и " +
                 "меняет направление мгновенно, как робот.")]
        [SerializeField, Min(0.5f)] private float fallbackAcceleration = 6f;
        [Tooltip("Торможение в режиме Fallback, м/с².")]
        [SerializeField, Min(0.5f)] private float fallbackDeceleration = 12f;
        [Tooltip("Скорость смены направления в режиме Fallback, град/сек. " +
                 "На резких поворотах враг ещё и притормаживает.")]
        [SerializeField, Min(30f)] private float fallbackTurnRate = 270f;

        [Header("Объезд препятствий (режим Fallback)")]
        [Tooltip("Слои, которые считаются препятствиями на пути.")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [Tooltip("Радиус тела для проверки препятствий.")]
        [SerializeField, Min(0.05f)] private float bodyRadius = 0.4f;
        [Tooltip("На какое расстояние вперёд смотреть в поисках препятствия.")]
        [SerializeField, Min(0.2f)] private float lookAheadDistance = 1.4f;
        [Tooltip("Углы объезда, которые перебираются по очереди (град).")]
        [SerializeField] private float[] avoidanceAngles = { 30f, -30f, 60f, -60f, 90f, -90f };
        [Tooltip("Не шагать туда, где под ногами нет земли (обрывы, ямы).")]
        [SerializeField] private bool avoidLedges = true;

        public MotorMode Mode { get; private set; } = MotorMode.NavMesh;

        public bool HasDestination { get; private set; }

        public Vector3 Destination => _destination;

        public float CurrentSpeed
        {
            get
            {
                Vector3 flat = _measuredVelocity;
                flat.y = 0f;
                return flat.magnitude;
            }
        }

        public Vector3 Velocity => _measuredVelocity;

        public bool IsGrounded => _isGrounded;

        public bool CanMove =>
            (Mode == MotorMode.NavMesh && AgentUsable) ||
            (Mode == MotorMode.Fallback && allowFallbackMovement);

        public float DesiredSpeed => _desiredSpeed;

        /// <summary>
        /// Есть ПРИКАЗ двигаться (точка задана, не дошли, мотор способен).
        /// Используется аниматором как «движение = анимация»: даже если замеренная
        /// скорость на этом кадре 0 (разгон, доворот, первый кадр приказа) —
        /// играть locomotion, а не idle.
        /// </summary>
        public bool WantsToMove =>
            CanMove && !_blockedCompletely &&
            ((HasDestination && _desiredSpeed > 0.01f && !HasArrived()) ||
             (_stopping && CurrentSpeed > 0.1f));

        private NavMeshAgent _agent;
        private CharacterController _controller;

        private Vector3 _destination;
        private float _destinationSetTime = float.NegativeInfinity;
        private float _desiredSpeed = 2f;
        private float _verticalVelocity;
        private bool _isGrounded = true;
        private bool _blockedCompletely;
        private bool _autoRotation = true;
        private bool _manualRotation;

        private Vector3 _previousPosition;
        private Vector3 _measuredVelocity;
        private float _fallbackSpeed;
        private bool _stopping;
        private float _stopUntil;
        private Vector3 _fallbackMoveDir;
        private Vector3 _lastProgressPos;
        private float _lastProgressTime;
        private float _nextNavMeshCheckTime;
        private bool _sprintMode;

        // NavMeshPath НЕЛЬЗЯ создавать в инициализаторе поля: Unity выдаёт
        // "InitializeNavMeshPath is not allowed to be called from a MonoBehaviour
        // constructor", из-за чего падал конструктор и ПОСЛЕ него не создавались
        // буферы рейкастов (были null → NRE в TryFindGround). Создаём в Awake.
        private NavMeshPath _path;
        // Буфер углов пути: NavMeshPath.corners каждый раз создаёт массив,
        // а путь считается несколько раз в секунду на каждого солдата.
        private readonly Vector3[] _pathCorners = new Vector3[32];

        private static bool s_fallbackNoticeShown;
        private static bool s_fallbackDisabledNoticeShown;
        private readonly Collider[] _obstacleCheckBuffer = new Collider[8];
        private readonly RaycastHit[] _walkabilityHits = new RaycastHit[32];
        private readonly RaycastHit[] _groundHits = new RaycastHit[32];
        private static readonly float[] RearAvoidanceAngles = { 135f, -135f, 180f };

        private bool AgentUsable => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _controller = GetComponent<CharacterController>();
            _previousPosition = transform.position;
            if (_path == null) _path = new NavMeshPath();

            if (groundOffset <= 0f) groundOffset = MeasureGroundOffset();

            _agent.stoppingDistance = Mathf.Min(_agent.stoppingDistance, arriveRadius);
            _autoRotation = _agent.updateRotation;

            // Согласование агента с мотором. Мотор сам управляет разгоном и
            // торможением (_agent.speed), поэтому:
            //  • acceleration агента поднимаем — иначе агент разгоняется по-своему
            //    (в префабе 12 м/с²) и моторный разгон 2.5 не имеет значения;
            //  • autoBraking выключаем — иначе агент тормозит сам, дублируя
            //    торможение мотора (солдат «проседает» у точки);
            //  • радиус выравниваем с телом, иначе солдаты проходят друг сквозь друга.
            if (agentAccelerationOverride > 0f) _agent.acceleration = agentAccelerationOverride;
            if (disableAgentAutoBraking) _agent.autoBraking = false;
            if (agentRadius > 0.05f) _agent.radius = agentRadius;
        }

        private float MeasureGroundOffset()
        {
            if (_controller != null)
                return _controller.height * 0.5f - _controller.center.y + _controller.skinWidth;
            float best = 0f;

            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger) continue;

                float offset = transform.position.y - collider.bounds.min.y;
                if (offset > best) best = offset;
            }

            return best > 0.01f ? best : 1f;
        }

        private void Start()
        {
            EvaluateMode(true);
            if (snapToGroundOnStart) SnapToGroundImmediate();
        }

        private void OnEnable()
        {
            _previousPosition = transform.position;
            _measuredVelocity = Vector3.zero;
            _stopping = false;
            _lastProgressPos = transform.position;
            _lastProgressTime = Time.time;
        }

        public void SetSpeed(float speed)
        {
            _desiredSpeed = Mathf.Max(0f, speed);
        }

        /// <summary>
        /// Режим перебежки: разгон/торможение заметно резче обычных. ИИ включает его
        /// на время рывка (роль «Манёвр»).
        /// </summary>
        public void SetSprintMode(bool sprint)
        {
            _sprintMode = sprint;
        }

        /// <summary>
        /// Свой приоритет обхода у каждого солдата: у всех в префабе был
        /// avoidancePriority = 50, и в проёмах солдаты толкались друг в друга.
        /// </summary>
        public void SetAvoidancePriority(int priority)
        {
            if (_agent == null) return;
            _agent.avoidancePriority = Mathf.Clamp(priority, 0, 99);
        }

        /// <summary>
        /// Длина пути до точки по NavMesh. false — пути нет (точка недостижима).
        /// </summary>
        public bool TryGetPathLength(Vector3 destination, out float length)
        {
            length = 0f;
            if (Mode != MotorMode.NavMesh || !AgentUsable) return false;
            if (_path == null) return false;

            if (!NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, _path))
                return false;
            if (_path.status != NavMeshPathStatus.PathComplete) return false;

            int corners = _path.GetCornersNonAlloc(_pathCorners);
            length = 0f;
            for (int i = 1; i < corners; i++)
                length += Vector3.Distance(_pathCorners[i - 1], _pathCorners[i]);
            return length > 0f;
        }

        public bool MoveTo(Vector3 target)
        {
            if (snapDestinationToGround) target = SnapDestinationY(target);
            _destination = target;
            HasDestination = true;
            _stopping = false;
            _blockedCompletely = false;
            _destinationSetTime = Time.time;
            // Новый приказ на движение — тело снова ведёт агент (по маршруту).
            // Ручной поворот (SetManualRotation) снимаем: приказ на движение
            // означает, что корпус ведёт агент, а не ИИ.
            SetManualRotation(false);

            if (Mode == MotorMode.NavMesh)
            {
                if (!AgentUsable && !TryReturnToNavMesh())
                {
                    EvaluateMode(false);
                    return Mode == MotorMode.Fallback && allowFallbackMovement;
                }

                _agent.isStopped = false;
                return _agent.SetDestination(target);
            }

            return allowFallbackMovement;
        }

        public void Stop()
        {
            _blockedCompletely = false;
            if (_stopping) return;

            bool agentBusy = AgentUsable && (_agent.hasPath || _agent.pathPending || !_agent.isStopped);
            if (!HasDestination && !agentBusy) return;

            HasDestination = false;

            if (AgentUsable)
            {
                if (_agent.speed <= 0.05f)
                {
                    _agent.isStopped = true;
                    _agent.ResetPath();
                    _agent.speed = 0f;
                    return;
                }

                _stopping = true;
                float brake = _sprintMode ? sprintDeceleration : speedDeceleration;
                _stopUntil = Time.time + _agent.speed / brake + 0.15f;
                _agent.isStopped = false;
            }
        }

        public bool HasArrived()
        {
            if (!HasDestination) return true;
            if (Time.time - _destinationSetTime < 0.15f) return false;
            if (Mode == MotorMode.NavMesh && AgentUsable)
            {
                if (_agent.pathPending) return false;
                if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;
                if (!_agent.hasPath) return true;

                if (_agent.remainingDistance > Mathf.Max(_agent.stoppingDistance, arriveRadius) + 0.15f)
                    return false;

                return _agent.velocity.sqrMagnitude < 0.05f;
            }

            if (_blockedCompletely) return true;
            return FlatDistance(transform.position, _destination) <= arriveRadius;
        }
        public bool SampleReachablePoint(Vector3 desired, float searchRadius, out Vector3 result)
            => SampleReachablePoint(desired, searchRadius, out result, false);

        /// <summary>
        /// Точка, до которой реально можно дойти. При requirePath=true проверяется
        /// ПОЛНЫЙ путь: точка может лежать на NavMesh, но быть за стеной (пути нет).
        /// </summary>
        public bool SampleReachablePoint(Vector3 desired, float searchRadius, out Vector3 result,
            bool requirePath)
        {
            if (Mode == MotorMode.NavMesh && AgentUsable)
            {
                if (NavMesh.SamplePosition(desired, out NavMeshHit navHit, searchRadius, NavMesh.AllAreas))
                {
                    result = navHit.position;
                    if (!requirePath || !validatePath) return true;

                    if (!TryGetPathLength(result, out float pathLength)) return false;
                    float straight = FlatDistance(transform.position, result);
                    return pathLength <= straight * maxPathLengthRatio + 1f;
                }

                result = desired;
                return false;
            }

            Vector3 probe = new(desired.x, transform.position.y - groundOffset + stepHeight + 1f, desired.z);
            if (TryFindGround(probe, out Vector3 grounded))
            {
                result = grounded + Vector3.up * groundOffset;
                if (IsPointInsideObstacle(result)) { result = desired; return false; }

                return true;
            }

            result = desired;
            return false;
        }

        private bool IsPointInsideObstacle(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, bodyRadius * 0.9f,
                _obstacleCheckBuffer, obstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _obstacleCheckBuffer[i];
                if (collider == null || collider.isTrigger) continue;
                if (collider.transform.root == transform.root) continue;
                float footY = point.y - groundOffset;
                if (collider.bounds.max.y - footY <= stepHeight) continue;

                return true;
            }

            return false;
        }

        public void FaceTowards(Vector3 worldPoint, float turnSpeedMultiplier = 1f)
        {
            FaceTowardsAtSpeed(worldPoint, turnSpeed * Mathf.Max(0.05f, turnSpeedMultiplier));
        }
        public void FaceTowardsAtSpeed(Vector3 worldPoint, float degreesPerSecond)
        {
            Vector3 direction = worldPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target,
                EaseTurnSpeed(transform.rotation, target, degreesPerSecond) * Time.deltaTime);
        }

        /// <summary>
        /// Доворот корпуса на заданный УГОЛ РЫСКАНИЯ (мировые градусы) с ограничением
        /// скорости. Нужен для «осматривания» и поиска: поворот идёт ТОЛЬКО через мотор,
        /// чтобы NavMeshAgent не спорил с ИИ за угол тела.
        /// </summary>
        public void FaceYawAtSpeed(float targetYaw, float degreesPerSecond)
        {
            Quaternion target = Quaternion.Euler(0f, targetYaw, 0f);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target,
                EaseTurnSpeed(transform.rotation, target, degreesPerSecond) * Time.deltaTime);
        }

        /// <summary>
        /// Ручное управление поворотом корпуса. true — агент не крутит тело сам
        /// (ИИ ведёт взгляд/прицел), false — агент снова ведёт тело по маршруту.
        /// </summary>
        public void SetManualRotation(bool manual)
        {
            _manualRotation = manual;
            // ПОСЛЕДНИЙ ВЫЗОВ ВЫИГРЫВАЕТ: SetManualRotation(true) — тело ведёт ИИ,
            // SetAutoRotation(true) — тело ведёт агент. Раньше тут было
            // «auto && !manual», и SetAutoRotation(true) МОЛЧА НИЧЕГО НЕ ДЕЛАЛ,
            // если manual уже стоял (например, ИИ держал ручной поворот в Search).
            if (_agent != null) _agent.updateRotation = !manual && _autoRotation;
        }
        public void SetAutoRotation(bool enabled)
        {
            _autoRotation = enabled;
            // ВАЖНО: enabled=true ВСЕГДА включает доворот агента, даже если раньше
            // стоял ручной поворот. Иначе правило «идёт — SetAutoRotation(true)»
            // не работало: солдат шёл боком/задом по маршруту.
            if (_agent != null) _agent.updateRotation = enabled;
        }

        public static float EaseTurnSpeed(Quaternion current, Quaternion target, float maxSpeed)
        {
            float angle = Quaternion.Angle(current, target);
            return Mathf.Max(1f, maxSpeed) * Mathf.Clamp01(angle / 40f + 0.12f);
        }

        public void Disable()
        {
            Stop();
            HasDestination = false;
            _measuredVelocity = Vector3.zero;
            _stopping = false;

            if (_agent != null) _agent.enabled = false;
            enabled = false;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (Time.time >= _nextNavMeshCheckTime)
            {
                _nextNavMeshCheckTime = Time.time + navMeshRecheckInterval;
                EvaluateMode(false);
            }

            if (Mode == MotorMode.NavMesh && AgentUsable)
            {
                if (_stopping)
                {
                    _agent.speed = Mathf.MoveTowards(_agent.speed, 0f,
                        speedDeceleration * dt);
                    if (_agent.speed <= 0.05f || Time.time >= _stopUntil)
                    {
                        _agent.isStopped = true;
                        _agent.ResetPath();
                        _agent.speed = 0f;
                        _stopping = false;
                    }
                }
                else if (_agent.isStopped || !HasDestination)
                {
                    _agent.speed = 0f;
                }
                else
                {
                    // Разгон/торможение: обычный или резкий (перебежка).
                    bool accelerating = _desiredSpeed > _agent.speed;
                    float rate;
                    if (_sprintMode)
                        rate = accelerating ? sprintAcceleration : sprintDeceleration;
                    else
                        rate = accelerating ? speedAcceleration : speedDeceleration;

                    _agent.speed = Mathf.MoveTowards(_agent.speed, DampedSpeed(), rate * dt);
                }
            }

            if (Mode == MotorMode.Fallback && allowFallbackMovement)
                UpdateFallbackMovement(dt);

            Vector3 position = transform.position;
            _measuredVelocity = (position - _previousPosition) / dt;
            _previousPosition = position;
        }

        /// <summary>
        /// Скорость с учётом подтормаживания у точки: чем ближе к цели,
        /// тем тише идём. Без этого враг разгоняется, подлетает к точке и
        /// тормозит на месте — выглядит рывком.
        /// </summary>
        private float DampedSpeed()
        {
            if (arriveDamping <= 0f || !HasDestination) return _desiredSpeed;

            float distance = FlatDistance(transform.position, _destination);
            float slowZone = Mathf.Max(0.5f, arriveRadius * 2.5f);
            float t = Mathf.Clamp01(distance / slowZone);
            return _desiredSpeed * Mathf.Lerp(1f - arriveDamping, 1f, t);
        }

        // NavMesh-агент сам двигает тело в своём внутреннем обновлении и гравитации
        // у него нет: если заспавнить чуть выше меша — будет лететь вечно.
        // Поэтому прилипание делаем в LateUpdate — после того, как агент уже
        // подвинул тело в этом кадре. Иначе агент перезапишет нашу коррекцию.
        private void LateUpdate()
        {
            if (!stickToGround || Mode != MotorMode.NavMesh) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            UpdateNavMeshGroundStick(dt);
        }

        /// <summary>
        /// Небольшая физика для режима NavMesh: если под ногами земля ниже —
        /// тянем врага вниз с гравитацией, если рядом — прилипаем.
        /// Без земли в зонде (напр. неверный groundMask) — доверяемся NavMesh,
        /// чтобы враг не провалился в бесконечность.
        /// </summary>
        private void UpdateNavMeshGroundStick(float dt)
        {
            if (!AgentUsable) return;

            Vector3 pos = transform.position;
            Vector3 probeStart = new(pos.x, pos.y + 1f, pos.z);
            float probeDist = 1f + navMeshGroundProbe + groundOffset;

            Vector3 from = probeStart;
            bool found = false;
            Vector3 ground = pos;
            float rayDist = probeDist;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, rayDist,
                    groundMask, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.root != transform.root &&
                Vector3.Angle(hit.normal, Vector3.up) <= maximumSlope)
            {
                ground = hit.point;
                found = true;
            }
            else if (TryFindGround(from, out Vector3 altGround))
            {
                ground = altGround;
                found = true;
            }

            if (!found)
            {
                _isGrounded = true;
                _verticalVelocity = 0f;
                return;
            }

            float targetY = ground.y + groundOffset;
            float diff = pos.y - targetY;

            if (diff <= stepHeight + 0.05f && diff >= -stepHeight)
            {
                // Земля рядом — прилипаем (ступеньки, мелкие неровности).
                _isGrounded = true;
                _verticalVelocity = 0f;
                if (Mathf.Abs(diff) > 0.005f) ShiftVertically(targetY - pos.y);
                return;
            }

            if (diff > 0f)
            {
                // Висим в воздухе — падаем с гравитацией.
                _isGrounded = false;
                _verticalVelocity -= gravity * dt;
                float fall = Mathf.Max(_verticalVelocity * dt, -diff);
                ShiftVertically(fall);
                if (Mathf.Abs(pos.y + fall - targetY) < 0.01f)
                {
                    _isGrounded = true;
                    _verticalVelocity = 0f;
                }
                return;
            }

            // Оказались под землёй (точка в склоне) — выталкиваем наверх.
            _isGrounded = true;
            _verticalVelocity = 0f;
            ShiftVertically(targetY - pos.y);
        }

        /// <summary>Сдвинуть по Y и трансформ, и внутреннюю позицию агента,
        /// чтобы NavMesh не возвращал тело обратно вверх.</summary>
        private void ShiftVertically(float deltaY)
        {
            if (Mathf.Abs(deltaY) < 0.0001f) return;
            Vector3 pos = transform.position;
            pos.y += deltaY;
            transform.position = pos;
            if (AgentUsable)
            {
                Vector3 np = _agent.nextPosition;
                np.y += deltaY;
                _agent.nextPosition = np;
            }
        }

        private void SnapToGroundImmediate()
        {
            Vector3 pos = transform.position;
            Vector3 probeStart = new(pos.x, pos.y + 1f, pos.z);
            if (!TryFindGround(probeStart, out Vector3 ground)) return;
            float targetY = ground.y + groundOffset;
            if (Mathf.Abs(pos.y - targetY) > navMeshGroundProbe + 1f) return;
            float delta = targetY - pos.y;
            pos.y = targetY;
            transform.position = pos;
            if (AgentUsable)
            {
                Vector3 np = _agent.nextPosition;
                np.y += delta;
                _agent.nextPosition = np;
            }
            _verticalVelocity = 0f;
            _isGrounded = true;
        }

        /// <summary>Притянуть Y точки назначения к NavMesh/земле,
        /// чтобы точки маршрута в воздухе не поднимали врага.</summary>
        private Vector3 SnapDestinationY(Vector3 target)
        {
            if (_agent != null && Mode == MotorMode.NavMesh)
            {
                if (NavMesh.SamplePosition(target, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
                {
                    target.y = navHit.position.y + groundOffset - _agent.baseOffset;
                    return target;
                }
            }
            Vector3 probe = new(target.x, target.y + 1.5f, target.z);
            if (TryFindGround(probe, out Vector3 grounded))
                target.y = grounded.y + groundOffset;
            return target;
        }

        private void EvaluateMode(bool initial)
        {
            if (_agent == null) return;

            if (_agent.enabled && _agent.isOnNavMesh)
            {
                if (Mode != MotorMode.NavMesh)
                {
                    Mode = MotorMode.NavMesh;
                    _stopping = false;
                    _verticalVelocity = 0f;
                    _isGrounded = true;

                    // CharacterController в режиме NavMesh ОСТАЁТСЯ включённым:
                    // на префабе врага (Enemy.prefab / GermanSoldier.prefab) это
                    // ЕДИНСТВЕННЫЙ коллайдер тела — хитбокс и физическое тело.
                    // Раньше здесь он выключался, и враг, ушедший в Fallback и
                    // вернувшийся на навмеш, ТЕРЯЛ хитбокс (становился неуязвимым
                    // и пропускал игрока сквозь себя). Двигает тело агент, а
                    // контроллер в этом режиме только стоит как коллайдер.
                    if (_controller != null && !_controller.enabled)
                        _controller.enabled = true;

                    if (HasDestination) MoveTo(_destination);
                }
                return;
            }

            if (TryReturnToNavMesh())
            {
                Mode = MotorMode.NavMesh;
                _stopping = false;
                _verticalVelocity = 0f;
                _isGrounded = true;
                // Контроллер НЕ выключаем: это единственный коллайдер тела врага
                // (хитбокс). См. комментарий в ветке выше.
                if (_controller != null && !_controller.enabled)
                    _controller.enabled = true;
                if (HasDestination) MoveTo(_destination);
                return;
            }

            if (Mode != MotorMode.Fallback)
            {
                Mode = MotorMode.Fallback;
                _stopping = false;
                if (_agent.enabled) _agent.enabled = false;

                if (_controller != null && !_controller.enabled)
                    _controller.enabled = true;

                if (allowFallbackMovement && !s_fallbackNoticeShown)
                {
                    s_fallbackNoticeShown = true;
                    Debug.Log("[EnemyMotor]: NavMesh недоступен — враги ходят в режиме " +
                              "Fallback (по коллайдерам земли). Запеки NavMesh, чтобы " +
                              "включилась полноценная навигация.");
                }
                else if (!allowFallbackMovement && !s_fallbackDisabledNoticeShown)
                {
                    s_fallbackDisabledNoticeShown = true;
                    Debug.LogWarning("[EnemyMotor]: NavMesh не найден, а Fallback выключен — " +
                                     "враги не будут двигаться.");
                }
            }

            if (initial) _previousPosition = transform.position;
        }

        private bool TryReturnToNavMesh()
        {
            if (_agent == null) return false;

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
                    navMeshSnapRadius, NavMesh.AllAreas))
                return false;

            // Не варпать сквозь стены: проверяем прямую на высоте груди.
            // Но если меш почти строго под ногами (заспавнили чуть выше склона) —
            // варпаем в любом случае, иначе луч чиркнет по склону, агент останется
            // выключенным навсегда, а враг будет ходить в тупом Fallback.
            float flatDist = FlatDistance(transform.position, hit.position);
            Vector3 from = transform.position + Vector3.up * 1.5f;
            Vector3 to = hit.position + Vector3.up * 1.5f;
            if (flatDist > 1.5f && Physics.Linecast(from, to, obstacleMask,
                    QueryTriggerInteraction.Ignore))
                return false;

            bool wasEnabled = _agent.enabled;
            if (!wasEnabled) _agent.enabled = true;
            if (!_agent.Warp(hit.position))
            {
                if (!wasEnabled) _agent.enabled = false;
                return false;
            }

            _agent.speed = 0f;
            _stopping = false;
            _verticalVelocity = 0f;
            return _agent.isOnNavMesh;
        }

        private void UpdateFallbackMovement(float dt)
        {
            Vector3 position = transform.position;
            float footY = position.y - groundOffset;
            Vector3 horizontal = Vector3.zero;
            float desiredSpeed = 0f;
            Vector3 desiredDirection = Vector3.zero;
            bool hasDirection = false;

            if (HasDestination && _desiredSpeed > 0.01f)
            {
                Vector3 toTarget = _destination - position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;

                if (distance <= arriveRadius)
                {
                    HasDestination = false;
                }
                else
                {
                    Vector3 straight = toTarget / distance;

                    if (TryResolveDirection(position, footY, straight, out Vector3 clearDirection))
                    {
                        _blockedCompletely = false;
                        desiredSpeed = Mathf.Min(DampedSpeed(), distance / Mathf.Max(dt, 0.0001f));
                        desiredDirection = clearDirection;
                        hasDirection = true;

                        if (_autoRotation && !_manualRotation)
                        {
                            // Тело ведёт агент. Если ИИ сейчас рулит корпусом сам
                            // (SetManualRotation) — не доворачиваем: иначе два
                            // писателя transform.rotation в одном кадре.
                            Quaternion target = Quaternion.LookRotation(clearDirection, Vector3.up);
                            transform.rotation = Quaternion.RotateTowards(
                                transform.rotation, target,
                                EaseTurnSpeed(transform.rotation, target, turnSpeed) * dt);
                        }
                    }
                    else
                    {
                        _blockedCompletely = true;
                    }
                }
            }

            if (hasDirection)
            {
                if (_fallbackMoveDir.sqrMagnitude < 0.001f)
                    _fallbackMoveDir = desiredDirection;
                else
                    _fallbackMoveDir = Vector3.RotateTowards(_fallbackMoveDir, desiredDirection,
                        fallbackTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;

                float alignment = Vector3.Dot(_fallbackMoveDir, desiredDirection);
                float cappedSpeed = alignment < 0.5f ? desiredSpeed * 0.55f : desiredSpeed;
                _fallbackSpeed = Mathf.MoveTowards(_fallbackSpeed, cappedSpeed,
                    fallbackAcceleration * dt);

                horizontal = _fallbackMoveDir * _fallbackSpeed;
            }
            else
            {
                _fallbackSpeed = Mathf.MoveTowards(_fallbackSpeed, 0f,
                    fallbackDeceleration * dt);
                horizontal = _fallbackMoveDir * _fallbackSpeed;
            }

            if (HasDestination && _desiredSpeed > 0.01f && !_blockedCompletely)
            {
                if (Time.time - _lastProgressTime >= 1f)
                {
                    if (FlatDistance(position, _lastProgressPos) < 0.25f)
                        _blockedCompletely = true;
                    _lastProgressPos = position;
                    _lastProgressTime = Time.time;
                }
            }
            else
            {
                _lastProgressPos = position;
                _lastProgressTime = Time.time;
            }

            Vector3 probeStart = new(position.x, footY + stepHeight + 0.1f, position.z);
            bool groundFound = TryFindGround(probeStart, out Vector3 groundPoint);

            _isGrounded = groundFound && footY - groundPoint.y <= stepHeight + 0.05f;

            Vector3 motion = horizontal * dt;

            if (_isGrounded)
            {
                _verticalVelocity = 0f;
                float targetY = groundPoint.y + groundOffset;
                motion.y = Mathf.Lerp(position.y, targetY, 1f - Mathf.Exp(-12f * dt)) - position.y;
            }
            else
            {
                _verticalVelocity -= gravity * dt;
                motion.y = _verticalVelocity * dt;
                if (groundFound)
                {
                    float floor = groundPoint.y + groundOffset;
                    if (position.y + motion.y < floor)
                    {
                        motion.y = floor - position.y;
                        _verticalVelocity = 0f;
                    }
                }
            }

            if (motion.sqrMagnitude < 1e-10f) return;

            if (_controller != null && _controller.enabled)
            {
                _controller.Move(motion);
            }
            else
            {
                Vector3 desiredPos = position + motion;
                float radius = bodyRadius;
                Vector3 direction = motion;
                float distance = direction.magnitude;

                if (distance > 0.001f &&
                    Physics.SphereCast(position + Vector3.up * groundOffset,
                        radius, direction.normalized, out RaycastHit hit,
                        distance + radius, obstacleMask,
                        QueryTriggerInteraction.Ignore))
                {
                    float safeDistance = Mathf.Max(0f, hit.distance - radius);
                    desiredPos = position + direction.normalized * safeDistance;
                    _blockedCompletely = true;
                }

                desiredPos.y = position.y + motion.y;
                transform.position = desiredPos;
            }
        }

        private bool TryResolveDirection(Vector3 position, float footY, Vector3 desired, out Vector3 result)
        {
            if (IsDirectionWalkable(position, footY, desired))
            {
                result = desired;
                return true;
            }

            if (avoidanceAngles != null)
            {
                foreach (float angle in avoidanceAngles)
                {
                    Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * desired;
                    if (!IsDirectionWalkable(position, footY, candidate)) continue;

                    result = candidate;
                    return true;
                }
            }

            foreach (float angle in RearAvoidanceAngles)
            {
                Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * desired;
                if (!IsDirectionWalkable(position, footY, candidate)) continue;

                result = candidate;
                return true;
            }

            result = desired;
            return false;
        }

        private bool IsDirectionWalkable(Vector3 position, float footY, Vector3 direction)
        {
            Vector3 origin = new(position.x, footY + stepHeight + bodyRadius, position.z);

            // D5: без аллокаций. Раньше при переполнении буфера вызывался
            // Physics.SphereCastAll → новый массив КАЖДЫЙ кадр (GC).
            // Буфера в 32 хитта хватает: для ответа «есть ли препятствие» этого
            // достаточно, а лишние хитты могли бы только добавить препятствия.
            int hitCount = Physics.SphereCastNonAlloc(origin, bodyRadius, direction,
                _walkabilityHits, lookAheadDistance, obstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _walkabilityHits[i];
                if (hit.collider.transform.root == transform.root) continue;
                if (hit.collider.bounds.max.y - footY <= stepHeight) continue;
                return false;
            }
            if (!avoidLedges) return true;
            Vector3 ahead = position + direction * lookAheadDistance;
            Vector3 aheadProbe = new(ahead.x, footY + stepHeight + 0.5f, ahead.z);

            if (!TryFindGround(aheadProbe, out Vector3 aheadGround)) return false;
            return Mathf.Abs(aheadGround.y - footY) <= Mathf.Max(stepHeight, maximumStepDown);
        }
        private bool TryFindGround(Vector3 from, out Vector3 point)
        {
            float distance = groundProbeDistance + stepHeight + 1f;

            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, distance,
                    groundMask, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.root != transform.root &&
                Vector3.Angle(hit.normal, Vector3.up) <= maximumSlope)
            {
                point = hit.point;
                return true;
            }

            // D5: без аллокаций и без сортировки. Раньше здесь был
            // Physics.RaycastAll (новый массив каждый кадр) + Array.Sort.
            // Теперь ближайший ВАЛИДНЫЙ хит ищем сами по буферу.
            int hitCount = Physics.RaycastNonAlloc(from, Vector3.down, _groundHits,
                distance, groundMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            bool found = false;
            Vector3 bestPoint = from;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = _groundHits[i];
                if (candidate.collider.transform.root == transform.root) continue;
                if (Vector3.Angle(candidate.normal, Vector3.up) > maximumSlope) continue;
                if (candidate.distance >= bestDistance) continue;

                bestDistance = candidate.distance;
                bestPoint = candidate.point;
                found = true;
            }

            if (found)
            {
                point = bestPoint;
                return true;
            }

            point = from;
            return false;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!HasDestination) return;

            Gizmos.color = Mode == MotorMode.NavMesh ? Color.green : new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, _destination);
            Gizmos.DrawWireSphere(_destination, arriveRadius);
        }
#endif
    }
}
