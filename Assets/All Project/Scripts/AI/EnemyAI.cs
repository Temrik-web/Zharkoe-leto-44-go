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
        [Tooltip("Ближе этой дистанции (м) цель с чистой линией зрения обнаруживается " +
                 "почти мгновенно, без накопления awareness.")]
        [SerializeField, Min(0f)] private float instantDetectDistance = 15f;
        [SerializeField, Min(0.02f)] private float perceptionInterval = 0.15f;
        [Tooltip("Разброс тиков восприятия между солдатами, сек. Солдаты «смотрят в мир» " +
                 "вразнобой — нагрузка на физику распределена по кадрам, а не бьёт в один.")]
        [SerializeField, Min(0f)] private float perceptionTickJitter = 0.15f;
        [SerializeField, Min(0f)] private float targetMemoryDuration = 8f;

        [Header("Слух")]
        [Tooltip("Множитель радиуса слышимости для всех шумов: выстрелы, крики, шаги, удары.")]
        [SerializeField, Min(1f)] private float hearingRadiusMultiplier = 1.5f;
        [Tooltip("ВЕРХНЯЯ ГРАНИЦА слышимости, м. Даже очень громкий шум (выстрел) не " +
                 "поднимет весь уровень: радиус слышимости не превысит это значение.")]
        [SerializeField, Min(1f)] private float maxHearingDistance = 140f;
        [Tooltip("Ошибка позиции услышанного шума, доля от дистанции. Враг идёт в РАЙОН " +
                 "шума, а не точно в точку: 0.08 = на 100 м промах до 8 м.")]
        [SerializeField, Range(0f, 0.5f)] private float hearingPositionErrorRatio = 0.08f;
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
        [Tooltip("Скорость настороженного шага (jog), м/с. Отдельная от патруля и погони: " +
                 "раньше «смесь» walk+run в одной скорости (chaseSpeed×0.8) ломала якоря анимации. " +
                 "В Alert солдат переходит на бег (RunSpeed) — это поле база для переходов.")]
        [SerializeField, Min(0f)] private float alertSpeed = 2.2f;
        [Tooltip("Множитель скорости перебежки (спринта) от Chase Speed, м/с.")]
        [SerializeField, Min(1f)] private float sprintSpeedMultiplier = 1.25f;
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
        [Tooltip("Множитель скорости осторожного преследования. Не используется: скорости " +
                 "разделены явно (патруль / настороженный шаг / погоня / перебежка).")]
#pragma warning disable 0414 // поле оставлено ради сохранённых в сценах значений
        [SerializeField, Range(0.5f, 1f)] private float squadPursuitSpeedScale = 0.8f;
#pragma warning restore 0414

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
        [Tooltip("Радиус области, по которой солдаты расходятся, когда у них есть " +
                 "информация, ГДЕ игрок (шум, контакт, последняя известная позиция). " +
                 "Если места мало (стены, край NavMesh) — область сжимается сама.")]
        [SerializeField, Min(2f)] private float squadAreaSpreadRadius = 14f;
        [Tooltip("Насколько солдат может отойти от своей точки и считать, что он на месте, м.")]
        [SerializeField, Min(0.1f)] private float combatAnchorTolerance = 1.2f;
        [Tooltip("Допуск до боевой дистанции, м. Видимая цель ближе этого — солдат " +
                 "стоит и стреляет, а не идёт бесконечно вперёд.")]
        [SerializeField, Min(0f)] private float combatRangeSlack = 5f;
        [Tooltip("Скорость, с которой своя точка догоняет цель, м/с. Медленная — без рывков.")]
        [SerializeField, Min(0.1f)] private float combatAnchorTrackSpeed = 1.5f;

        [Header("Тактика отряда (огонь и манёвр)")]
        [Tooltip("Максимум солдат, стреляющих одновременно (жетоны атаки). " +
                 "Реальный лимит = min(это значение, ceil(отряд/2)).")]
        [SerializeField, Min(1)] private int maxShooters = 3;
        [Tooltip("Время фиксации решения отряда, сек. Решение не дрожит: смена ролей " +
                 "и жетонов — не чаще раза в это время.")]
        [SerializeField, Min(0.1f)] private float squadCommitTime = 1.5f;
        [Tooltip("Длительность перебежки для роли «Манёвр», м (8–12).")]
        [SerializeField, Min(2f)] private float maneuverDistance = 10f;
        [Tooltip("Множитель скорости перебежки на ВЫГОДНУЮ ПОЗИЦИЮ (боевой якорь, " +
                 "манёвр, фланг, укрытие). Применяется поверх Run Speed: солдат " +
                 "занимает место решительным рывком, а не шагом.")]
        [SerializeField, Min(1f)] private float repositionSpeedMultiplier = 1.5f;
        [Tooltip("Не подходить к игроку ближе этого расстояния, м.")]
        [SerializeField, Min(0f)] private float tacticMinTargetDistance = 6f;
        [Tooltip("Доля здоровья, ниже которой солдат считается раненым и стреляет реже.")]
        [SerializeField, Range(0f, 1f)] private float woundedHealthThreshold = 0.35f;
        [Tooltip("Множитель паузы между очередями для раненого (стреляет реже).")]
        [SerializeField, Min(1f)] private float woundedFireRateScale = 1.8f;
        [Tooltip("Сколько секунд солдат «выглядывает» из укрытия.")]
        [SerializeField, Min(0.1f)] private float coverPeekDuration = 1.6f;
        [Tooltip("Сколько секунд солдат прячется в укрытии.")]
        [SerializeField, Min(0.1f)] private float coverHideDuration = 1.8f;

        [Header("Очереди в тактическом режиме")]
        [Tooltip("Минимум патронов в очереди (используется при Use Squad Tactics = вкл).")]
        [SerializeField, Range(1, 16)] private int tacticalMinimumBurst = 3;
        [Tooltip("Максимум патронов в очереди (используется при Use Squad Tactics = вкл).")]
        [SerializeField, Range(1, 16)] private int tacticalMaximumBurst = 8;
        [Tooltip("Минимальная пауза между очередями в тактическом режиме, сек.")]
        [SerializeField, Min(0f)] private float tacticalMinimumPause = 0.4f;
        [Tooltip("Максимальная пауза между очередями в тактическом режиме, сек.")]
        [SerializeField, Min(0f)] private float tacticalMaximumPause = 1.2f;

        [Header("Точность (растёт при непрерывной видимости)")]
        [Tooltip("Множитель разброса сразу после обнаружения цели (2 = вдвое шире базы).")]
        [SerializeField, Min(0.1f)] private float spreadUnseenMultiplier = 2f;
        [Tooltip("Множитель разброса после нескольких секунд непрерывной видимости.")]
        [SerializeField, Min(0.1f)] private float spreadSeenMultiplier = 0.8f;
        [Tooltip("За сколько секунд непрерывной видимости точность доходит до нормы, сек.")]
        [SerializeField, Min(0.1f)] private float spreadSettleTime = 3f;

        [Header("Sprint")]
        [Tooltip("Задержка реакции на «внезапную» цель (появилась из-за угла), сек. " +
                 "Для цели, замеченной давно, реакция обычная (Reaction Time).")]
        [SerializeField, Min(0f)] private float suddenTargetReactionDelay = 0.3f;
        [Tooltip("Сколько секунд цель должна пропасть из виду, чтобы это считалось " +
                 "«внезапным» появлением и включило задержку реакции.")]
        [SerializeField, Min(0f)] private float suddenTargetMinHidden = 0.2f;
        [Tooltip("На сколько секунд вперёд просчитывается уход цели в погоне, сек.")]
        [SerializeField, Min(0f)] private float chaseLeadTime = 1.2f;
        [Tooltip("Максимальный упреждение по уходу цели, м.")]
        [SerializeField, Min(0f)] private float chaseLeadMaxDistance = 6f;
        [Tooltip("Задержка передачи контакта союзникам, сек. (мин/макс).")]
        [SerializeField] private Vector2 contactShareDelay = new(0.3f, 1f);
        [Tooltip("Множитель задержки передачи контакта: 1 — как настроено, " +
                 "0.3 — отряд подтягивается почти сразу.")]
        [SerializeField, Range(0.05f, 1f)] private float contactShareDelayScale = 0.25f;
        [Tooltip("Радиус тревоги вокруг заметившего, м. Слышат все, кто в этом " +
                 "радиусе, — они сразу бегут на игрока.")]
        [SerializeField, Min(5f)] private float contactAlertRadius = 80f;
        [Tooltip("Как часто стреляющий солдат сообщает отряду, где цель, сек. " +
                 "0 — только при входе в бой.")]
        [SerializeField, Min(0f)] private float radioInterval = 1.2f;
        [Tooltip("Радиус, в котором слышны выстрелы союзника, м. Услышали стрельбу " +
                 "своего — идём на неё, даже если бой не видно.")]
        [SerializeField, Min(5f)] private float allyGunshotRadius = 120f;
        [Tooltip("Минимальный радиум шума союзника, на который реагируем. " +
                 "Ниже — шаги и попадания (на них не бегут).")]
        [SerializeField, Min(1f)] private float allyNoiseMinRadius = 12f;
        [Tooltip("Сколько точек проверяет солдат при поиске (2–3).")]
        [SerializeField, Range(2, 3)] private int searchPointCount = 3;

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
        [Tooltip("Через сколько секунд вне боя темп схватки сбрасывается: новая схватка " +
                 "снова начинается с «царапины». Сек.")]
        [SerializeField, Min(0f)] private float engageResetDelay = 10f;

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
        [SerializeField, Range(0f, 45f)] private float maxFireAngle = 20f;

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
        [Tooltip("Экранная отладка: над солдатом показывать состояние, роль и жетон. " +
                 "Работает только в редакторе и dev-сборках.")]
        [SerializeField] private bool debugOverlay;

        private bool DiagEnabled => debugLogging || debugOverlay;

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

        /// <summary>
        /// Точка, где солдат СЧИТАЕТ, что находится игрок: подозрение (шум/контакт),
        /// центр поиска или последняя известная позиция. Нужна для распределения
        /// отряда по области, а не в одну воронку.
        /// </summary>
        internal Vector3 KnownArea => State switch
        {
            EnemyState.Alert => _suspicionPoint,
            EnemyState.Search => _searchCenter,
            _ => _lastKnownTargetPosition
        };

        // ── Данные для координатора отряда ──────────────────────────────────
        internal int SquadStamp { get; set; }
        internal SquadRole Role { get; private set; } = SquadRole.Fire;
        internal bool HasFireToken { get; set; }
        internal Vector3 SquadGoal { get; set; }
        internal bool WantsReposition { get; set; }
        internal bool IsBlockedByAlly => _blockedByAlly;
        internal float LastTokenTime { get; set; }
        internal CoverPoint AssignedCover { get; set; }
        internal float SquadPreferredCombatDistance => preferredCombatDistance;
        internal float TacticMinTargetDistance => tacticMinTargetDistance;
        internal int MaxShooters => maxShooters;
        internal float SquadCommitTime => squadCommitTime;
        internal float ManeuverDistance => maneuverDistance;

        /// <summary>
        /// Скорость БЕГА к цели/на позицию. Не ниже порога «run» у аниматора
        /// (chaseSpeed × sprintSpeedMultiplier), иначе солдат выглядит так, будто
        /// идёт шагом, и не выглядит «снаряжающимся на бой».
        /// </summary>
        private float RunSpeed => chaseSpeed * Mathf.Max(1f, sprintSpeedMultiplier);

        /// <summary>
        /// Скорость перебежки на ВЫГОДНУЮ ПОЗИЦИЮ: боевой якорь, манёвр, фланг,
        /// укрытие. Быстрее обычного бега — позицию занимают рывком.
        /// </summary>
        private float RepositionSpeed => RunSpeed * Mathf.Max(1f, repositionSpeedMultiplier);

        /// <summary>
        /// Может ли солдат держать жетон атаки: есть патроны. Раненые тоже
        /// стреляют — у них лишь длиннее паузы (woundedFireRateScale).
        /// Раньше раненый вообще не получал жетон и просто стоял, пока
        /// союзники стреляют.
        /// </summary>
        internal bool CanTakeFireToken => weapon != null && weapon.HasAmmunition;

        internal void ApplySquadRole(SquadRole role, bool token)
        {
            Role = role;
            HasFireToken = token;
        }

        /// <summary>
        /// СОЯЯ команда. Источник истины — CharacterHealth.Team. Поле enemyTeam
        /// используется только как запасной вариант (если CharacterHealth ещё нет).
        /// </summary>
        internal Team OwnTeam => _health != null ? _health.Team : enemyTeam;

        internal Team SquadTeam => OwnTeam;
        internal bool IsSquadAlive => _health != null && _health.IsAlive && State != EnemyState.Dead;

        /// <summary>Солдат в бою/погоне — только такие считаются союзниками по отряду.</summary>
        internal bool IsInEngagement => State == EnemyState.Chase || State == EnemyState.Combat;

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
        private float _nextRadioTime;

        private float _nextPerceptionTime;
        private float _lastPerceptionVisibleTime = float.NegativeInfinity;
        private float _lastPerceptionTime = float.NegativeInfinity;
        private bool _targetVisibleLastTick;
        private float _targetHiddenSince = float.NegativeInfinity;
        private bool _suddenDelayApplied;
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
        // Перекрытая линия огня: короткий боковой шаг, чтобы найти угол.
        private float _repositionUntil;
        private Vector3 _repositionGoal;
        private bool _repositionFlip;
        private float _squadPersonalSpace;
        private Vector3 _lastPathDestination;
        private bool _hasPathDestination;
        private bool _lastMoveSucceeded = true;
        private float _nextPatrolRetryTime;
        private const float patrolRetryDelay = 0.5f;

        // СВОЯ ТОЧКА В БОЮ: солдат держит своё место в строю, а не точку игрока.
        // Якорь двигается плавно (combatAnchorTrackSpeed) — никаких рывков.
        private Vector3 _combatAnchor;
        private bool _hasCombatAnchor;
        private Vector3 _formationForward = Vector3.forward;
        private bool _formationReady;

        // D4: тяжёлые расчёты (раскладка строя, мягкое разведение, проверка
        // якоря на NavMesh) — ~5 Гц со случайным сдвигом, а не каждый кадр.
        // Иначе 30 солдат в бою дают ~2×N² проверок дистанции в кадр.
        private const float FormationRefreshInterval = 0.2f;
        private const float FormationRefreshJitter = 0.1f;
        private const float AnchorCheckInterval = 0.2f;
        private const float AnchorCheckJitter = 0.1f;
        private const float AnchorSampleRadius = 2.5f;

        private float _nextFormationTime;
        private float _formationLateral;
        private float _formationDepth;
        private Vector3 _separationOffset;

        // Распределение по области вокруг «игрок там»: смещение от центра
        // кешируется ~5 Гц, чтобы не строить путь на каждого солдата каждый кадр.
        private float _nextAreaSpreadTime;
        private Vector3 _areaSpreadCenter;
        private Vector3 _areaSpreadOffset;
        private float _nextAnchorCheckTime;
        private Vector3 _anchorNavCorrection;
        private float _nextChaseGoalTime;
        private Vector3 _chaseGoalOffset;
        private float _nextDiagTime;
        private string _lastLosBlocker;
        private string _lastRejectNote;
        private string _lastNoiseInfo;
        private string _lastNotSeenReason;
        private string _lastTargetLossReason;
        private string _lastStateChangeReason;
        private string _lastExceptionText;
        private System.Type _lastExceptionType;
        // Троттлинг логов — ОБЩИЙ на всех врагов: 30 сломанных солдат не должны
        // давать 30 сообщений каждые 5 секунд (инвариант «один лог»).
        private static float s_lastExceptionLogTime = float.NegativeInfinity;
        private bool _diagLosOverflow;

        private Vector3 _searchCenter;
        private float _searchFaceUntil;
        private float _searchBaseYaw;
        private bool _searchBaseCaptured;

        // Поиск: 2–3 точки вокруг последней известной позиции.
        private readonly Vector3[] _searchPoints = new Vector3[3];
        private int _searchPointTotal;
        private int _searchPointIndex;
        private float _searchLookUntil;

        // Передача контакта союзникам.
        private float _pendingContactTime = float.NegativeInfinity;
        private Transform _pendingContactTarget;
        private Vector3 _pendingContactPoint;
        private float _targetLostTime = float.NegativeInfinity;

        private LookAroundPhase _lookAroundPhase;
        private float _lookAroundBaseYaw;
        private float _lookAroundTargetYaw;
        private float _lookAroundHoldUntil;
        private float _nextLookAroundTime;

        // Поворот корпуса на источник звука.
        private float _lookAtSoundUntil;
        private Vector3 _lookAtSoundPoint;

        private int _burstShotIndex;
        private float _wanderSpeedFactor = 1f;
        private float _wanderBaseAngle;
        private float _chaseStuckTime = -1f;
        private Transform _engageTarget;
        private float _engageStartTime;
        private float _engageEndTime = float.NegativeInfinity;
        private int _engageStage;
        private bool _engageOpen;
        private float _engageHpBaseline = 1f;
        private float _lastTargetHp;
        private bool _blockedByAlly;
        private bool _coverPeeking;
        private float _coverPeekUntil;
        private static bool s_fallbackTacticsWarned;
        private int _diagConsidered;
        private int _diagRejectedTeam;
        private int _diagRejectedDead;
        private int _diagRejectedLayer;
        private int _diagRejectedDist;
        private int _diagRejectedFov;
        private int _diagRejectedLos;

        private readonly Collider[] _eyeBuffer = new Collider[4];

        // Кеш точки прицеливания по персонажам: GetComponentInChildren<Collider>()
        // по ригу — дорогая операция, а восприятие дёргает её десятки раз в секунду.
        private struct AimCacheEntry
        {
            public CharacterHealth Health;
            public CharacterController Controller;
            public Collider Collider;
        }
        private readonly AimCacheEntry[] _aimCache = new AimCacheEntry[32];
        private int _aimCacheCursor;

        // Кеш видимости цели: CanSeeCurrentTarget() дёргается каждый кадр из Chase/Combat.
        private float _nextLosCheckTime;
        private float _losCheckInterval = 0.1f;
        private bool _cachedCanSee;
        private bool _losBlockedByAlly;

        private static readonly HashSet<CharacterHealth> s_characters = new();
        // Параллельный список: по HashSet нельзя итерировать, если в этот момент
        // регистрируется новый персонаж (InvalidOperationException). Список
        // обходим по индексу — это безопасно при изменении состава.
        private static readonly List<CharacterHealth> s_characterList = new();
        private static readonly List<EnemyAI> s_soldiers = new();

        // Игрок — приоритетная цель. Ссылка приходит из PlayerHealth и не зависит
        // от того, попал ли CharacterHealth в реестр или в TargetMask.
        private static CharacterHealth s_playerHealth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSoldierRegistry()
        {
            s_soldiers.Clear();
            s_characters.Clear();
            s_characterList.Clear();
            s_playerHealth = null;
            s_fallbackTacticsWarned = false;
            s_lastExceptionLogTime = float.NegativeInfinity;
        }

        public static void RegisterPlayer(CharacterHealth player)
        {
            if (player != null) s_playerHealth = player;
        }

        public static void UnregisterPlayer(CharacterHealth player)
        {
            if (s_playerHealth == player) s_playerHealth = null;
        }

        private void RegisterSoldier()
        {
            if (!s_soldiers.Contains(this)) s_soldiers.Add(this);
        }

        private void UnregisterSoldier() => s_soldiers.Remove(this);

        public static void RegisterCharacter(CharacterHealth character)
        {
            if (character == null) return;
            if (!s_characters.Add(character)) return;
            s_characterList.Add(character);
        }

        public static void UnregisterCharacter(CharacterHealth character)
        {
            if (character == null) return;
            if (!s_characters.Remove(character)) return;
            s_characterList.Remove(character);
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

            // CharacterHealth может лежать на самом объекте или выше по иерархии
            // (так же работают SetTarget / OnNoiseCreated / OnDamaged — через
            // GetComponentInParent). Ищем по родителям: иначе AddComponent создал
            // бы ВТОРОЕ здоровье с дефолтной командой Axis — враг стал бы
            // бессмертным (урон идёт в родительский CharacterHealth, а ИИ его
            // не видит) и команда врага разошлась бы с реальной (D1/D6).
            _health = GetComponentInParent<CharacterHealth>();
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

            // targetMask = 0 → «фильтр слоя цели не применён» (раньше молча ставился ~0).
            // Молчаливая подстановка прятала ошибку в сцене, поэтому теперь только внятное
            // предупреждение, а слой цели не фильтруется вовсе.
            if (targetMask == 0)
            {
                Debug.LogWarning($"[EnemyAI] {name}: targetMask пуст (0) — фильтр слоя цели " +
                    "отключён, целью может стать любой персонаж другой команды. Укажи слои " +
                    "целей в инспекторе.", this);
            }

            // МИГРАЦИЯ: «своя команда» = CharacterHealth.Team. Поле enemyTeam больше не
            // решает, кого считать целью, но держим его в согласии с реальной командой,
            // чтобы инспектор не врал. Прежний костыль «Allies → Axis» удалён.
            if (_health != null && enemyTeam != _health.Team)
            {
                Debug.LogWarning($"[EnemyAI] {name}: enemyTeam ({enemyTeam}) не совпадает с " +
                    $"командой персонажа ({_health.Team}) — взята команда CharacterHealth. " +
                    "Старое значение поля оставлено только для миграции.", this);
                enemyTeam = _health.Team;
            }

            if (weapon == null) weapon = GetComponentInChildren<HitscanWeapon>(true);
        }

        private void OnEnable()
        {
            if (_health == null) _health = GetComponentInParent<CharacterHealth>();
            if (_health == null) return;
            RegisterSoldier();
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            NoiseSystem.NoiseCreated += OnNoiseCreated;
            SubscribeWeapon(weapon);
            // ForceTarget мог быть вызван ДО активации объекта (сценарий «бросает
            // немцев на игрока» через FindObjectsOfType<EnemyAI>(true)). Раньше
            // OnEnable безусловно ставил Patrol — выбранная цель терялась, а
            // стейт оставался Patrol с непустой целью. Не затираем состояние,
            // если цель уже задана.
            if (State != EnemyState.Dead && _target == null)
                State = EnemyState.Patrol;
            if (_motor != null)
            {
                _motor.SetManualRotation(false);
                _motor.SetAutoRotation(true);
            }
            _fireAnchorUntil = 0f;
            _repositionUntil = 0f;
            _repositionGoal = Vector3.zero;
            _repositionFlip = false;
            _squadPersonalSpace = 0f;
            _hasCombatAnchor = false;
            _formationReady = false;
            _areaSpreadCenter = Vector3.zero;
            _areaSpreadOffset = Vector3.zero;
            _nextAreaSpreadTime = 0f;
            _lookAroundPhase = LookAroundPhase.None;
            _lookAtSoundUntil = 0f;
            _nextLookAroundTime = Time.time + Random.Range(lookAroundIntervalMin,
                lookAroundIntervalMax);
            _nextPerceptionTime = Time.time + Random.Range(0f,
                Mathf.Max(0.02f, perceptionInterval + perceptionTickJitter));
            _losCheckInterval = 0.1f + Random.Range(0f, 0.1f);
            _nextLosCheckTime = 0f;
            _cachedCanSee = false;
            _losBlockedByAlly = false;
            _targetVisibleLastTick = false;
            _targetHiddenSince = float.NegativeInfinity;
            _suddenDelayApplied = false;
            _hasPathDestination = false;
            _nextPathRefreshTime = 0f;
            _lastNotSeenReason = null;
            _lastTargetLossReason = null;
            _lastStateChangeReason = null;
            _lastExceptionText = null;
            _lastExceptionType = null;
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
            // Свой приоритет обхода у каждого солдата: иначе все с avoidancePriority = 50
            // толкались в проходе друг друга.
            _motor.SetAvoidancePriority(HashAvoidancePriority(GetInstanceID()));
            ApplySpeedAnchors();
        }

        /// <summary>
        /// ЕДИНЫЙ ИСТОЧНИК СКОРОСТЕЙ: якоря анимации = реальные скорости ИИ.
        /// Раньше клип бега крутился со скоростью патруля — ноги скользили.
        /// </summary>
        private void ApplySpeedAnchors()
        {
            if (enemyAnimator == null) return;
            enemyAnimator.ConfigureSpeedAnchors(
                patrolSpeed,
                chaseSpeed,
                Mathf.Max(RunSpeed, RepositionSpeed));
        }

        /// <summary>Приоритет обхода по хэшу id: у каждого солдата свой (20–99).</summary>
        private static int HashAvoidancePriority(int id)
        {
            unchecked
            {
                uint h = (uint)id * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return 20 + (int)(h % 80u);
            }
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

        /// <summary>
        /// Спавнер/сценарий: «держать точку» — выключить самостоятельное блуждание
        /// (солдат стоит и осматривается) или сменить его радиус. Патрульный
        /// маршрут не трогает: чтобы его снять, вызови SetPatrolRoute(null).
        /// </summary>
        public void SetWander(bool enabled, float radius = -1f)
        {
            wanderWhenNoRoute = enabled;
            if (radius > 0f) wanderRadius = Mathf.Max(1f, radius);
        }
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
            if (targetHealth == null)
            {
                // D1/D6: «своя команда» = ТОЛЬКО CharacterHealth.Team. Без
                // CharacterHealth команду определить нельзя — такую цель брать
                // нельзя (ИИ бросился бы на своего). Исключение — сам игрок:
                // у него CharacterHealth есть всегда (Playerhealth: RequireComponent).
                if (s_playerHealth != null &&
                    (target == s_playerHealth.transform || target.IsChildOf(s_playerHealth.transform)))
                {
                    targetHealth = s_playerHealth;
                }
                else
                {
                    if (debugLogging)
                        Debug.LogWarning($"[EnemyAI] {name}: цель «{target.name}» без CharacterHealth — " +
                            "команду определить нельзя, цель не беру.", this);
                    return;
                }
            }
            if (targetHealth.Team == OwnTeam) return;
            if (!targetHealth.IsAlive) return;

            SetTarget(target);
            if (_targetHealth == null) _targetHealth = targetHealth;
            // LOS-кэш сбрасываем ВСЕГДА: та же цель после смены обстановки могла
            // остаться «невидимой» по старому результату.
            _nextLosCheckTime = 0f;
            _cachedCanSee = false;
            _losBlockedByAlly = false;
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
            if (enemyAnimator != null)
            {
                // Анимация выстрела НЕ перезапускается на каждом выстреле очереди:
                // первый — полный клип, дальше — короткая отдача (если клип задан).
                if (_burstShotIndex <= 0) enemyAnimator.PlayShoot();
                else enemyAnimator.PlayShootRecoil();
            }
            // DЫРА: триггер на каждый выстрел вешал Animator. Legacy-путь троттлим.
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

            try
            {
                if (Time.time >= _nextPerceptionTime)
                {
                    _nextPerceptionTime = Time.time + perceptionInterval +
                        Random.Range(0f, Mathf.Max(0f, perceptionTickJitter));
                    UpdatePerception();
                }

                UpdatePendingContact();
            }
            catch (System.Exception e)
            {
                ReportSubsystemFailure("perception", e);
            }

            try
            {
                if (useSquadTactics) SquadCoordinator.Tick(Time.time);
            }
            catch (System.Exception e)
            {
                ReportSubsystemFailure("squad", e);
            }

            bool recentlySeen = Time.time - _lastPerceptionVisibleTime
                <= perceptionInterval * 2f + 0.05f;
            if (!recentlySeen && (_awareness < 1f || _target == null))
                _awareness = Mathf.Max(0f, _awareness - awarenessDecay * dt);

            try
            {
                switch (State)
                {
                    case EnemyState.Patrol: UpdatePatrol(); break;
                    case EnemyState.Alert: UpdateAlert(); break;
                    case EnemyState.Search: UpdateSearch(); break;
                    case EnemyState.Chase: UpdateChase(); break;
                    case EnemyState.Combat: UpdateCombat(); break;
                }
            }
            catch (System.Exception e)
            {
                ReportSubsystemFailure("state", e);
                ApplyStateFailureFallback();
            }

            try
            {
                // Поворот на звук — ПОСЛЕ стейт-подсистемы, чтобы её вызовы
                // мотора не затирали наш ручной поворот.
                UpdateLookAtSound();
            }
            catch (System.Exception e)
            {
                ReportSubsystemFailure("state", e);
            }

            try
            {
                UpdateAnimator();
            }
            catch (System.Exception e)
            {
                ReportSubsystemFailure("animator", e);
            }

            try
            {
                UpdateVoiceFootsteps();
            }
            catch (System.Exception e)
            {
                ReportSubsystemFailure("voice", e);
            }

            if (debugLogging && Time.time >= _nextDiagTime)
            {
                _nextDiagTime = Time.time + 1.5f;
                // Диагностика — тоже в try/catch: иначе исключение из неё
                // вылетело бы из Update() и Unity логировала бы его каждый кадр.
                try { LogDiag(); }
                catch (System.Exception e) { ReportSubsystemFailure("diag", e); }
            }
        }

        private void ReportSubsystemFailure(string subsystem, System.Exception exception)
        {
            if (exception == null) return;

            System.Type type = exception.GetType();
            bool newType = type != _lastExceptionType;
            bool throttled = Time.unscaledTime - s_lastExceptionLogTime >= 5f;
            if (!newType && !throttled) return;

            _lastExceptionType = type;
            s_lastExceptionLogTime = Time.unscaledTime;

            string stack = exception.StackTrace;
            int lineEnd = stack != null ? stack.IndexOf('\n') : -1;
            string firstLine = lineEnd > 0 ? stack.Substring(0, lineEnd) : stack;

            if (DiagEnabled)
                _lastExceptionText = $"{type.Name}: {exception.Message}";

            if (newType)
            {
                Debug.LogError($"[EnemyAI] {name}: сбой подсистемы «{subsystem}» ({State}), " +
                    $"тип {type.Name}: {exception.Message}. Враг продолжает работать. " +
                    $"Первая строка стека: {firstLine}", this);
            }
            else
            {
                Debug.LogWarning($"[EnemyAI] {name}: повтор сбоя «{subsystem}» ({State}), " +
                    $"тип {type.Name}.", this);
            }
        }

        private void ApplyStateFailureFallback()
        {
            if (_motor == null || _target == null) return;
            try
            {
                _motor.Stop();
                _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);
            }
            catch (System.Exception e)
            {
                // Fallback тоже может упасть (например, если упал сам мотор).
                // Без защиты исключение вылетело бы из Update() и Unity логировала
                // бы его КАЖДЫЙ кадр — троттлинг ReportSubsystemFailure при этом
                // уже израсходован. Здесь лог уходит в тот же троттлинг.
                ReportSubsystemFailure("state-fallback", e);
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
                // Повторное появление цели после паузы: короткая задержка
                // ставится ОДИН раз, а не на каждом тике восприятия.
                bool reacquired = _firstSightAcquired && !_targetVisibleLastTick;
                // Сколько цель пропадала из виду — для задержки на «внезапную» цель.
                float hiddenFor = _targetHiddenSince > 0f ? Time.time - _targetHiddenSince : 0f;

                _targetVisibleLastTick = true;
                _lastPerceptionVisibleTime = Time.time;
                _targetHiddenSince = float.NegativeInfinity;

                if (visDistance <= Mathf.Max(0f, instantDetectDistance))
                {
                    _awareness = 1f;
                }
                else
                {
                    float distanceFactor = Mathf.Clamp01(1f - visDistance / viewDistance);
                    float gain = (0.4f + distanceFactor) / Mathf.Max(0.05f, timeToDetect);
                    _awareness = Mathf.Min(1f, _awareness + gain * tickDt);
                }

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
                    else if (reacquired && !_suddenDelayApplied &&
                             hiddenFor >= suddenTargetMinHidden)
                    {
                        // Задержка на «внезапную» цель. Порог — СКОРОСТЬ
                        // исчезновения цели, а не число тиков восприятия: раньше
                        // мигание на один тик (≈0.15 с) задержки не давало вовсе.
                        // Флаг _suddenDelayApplied не даёт «мигающей» цели съесть
                        // стрельбу: задержка выдаётся один раз за серию исчезновений.
                        _suddenDelayApplied = true;
                        _canFireAfter = Mathf.Max(_canFireAfter,
                            Time.time + suddenTargetReactionDelay);
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

            _targetVisibleLastTick = false;

            // Цель пропала из виду — запоминаем КОГДА. Пока её не видно, идёт
            // отсчёт; если пропала надолго — серия задержек сбрасывается, чтобы
            // «мигающая» цель не съедала стрельбу.
            if (_target != null && _targetHiddenSince < 0f)
                _targetHiddenSince = Time.time;
            if (_targetHiddenSince > 0f && Time.time - _targetHiddenSince > 1f)
                _suddenDelayApplied = false;

            if (_target != null && Time.time - _lastTargetSeenTime > targetMemoryDuration)
            {
                if (debugLogging)
                    Debug.Log($"[EnemyAI] {name}: потерял цель «{_target.name}» — " +
                        $"не видел {targetMemoryDuration:F0} сек", this);
                ClearTarget($"MEMORY({targetMemoryDuration:F0}s)");
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
                $"роль: {Role}{(HasFireToken ? " +жетон" : "")} | " +
                $"схватка: стадия {_engageStage} | " +
                $"канд.: {_diagConsidered} " +
                $"(ком×{_diagRejectedTeam} мертв×{_diagRejectedDead} слой×{_diagRejectedLayer} " +
                $"дист×{_diagRejectedDist} fov×{_diagRejectedFov} los×{_diagRejectedLos})" +
                (_lastRejectNote != null ? $" | {_lastRejectNote}" : "") +
                (_lastNoiseInfo != null ? $" | последний {_lastNoiseInfo}" : "") +
                $" | не вижу: {(_lastNotSeenReason ?? "—")}" +
                $" | потеря: {(_lastTargetLossReason ?? "—")}" +
                $" | смена: {(_lastStateChangeReason ?? "—")}", this);
        }

        private bool IsTargetVisibleNow(out string blocker)
        {
            blocker = "";
            if (_target == null) return false;

            Vector3 chest = CurrentTargetAimPoint();
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

            // РЕЕСТР вместо OverlapSphere: обход всех коллайдеров в радиусе ломался на
            // 128 слотах (при targetMask = ~0 на открытой местности) и дёргал
            // GetComponentInParent на каждом. Персонажи и так знают друг о друге через
            // статический реестр s_characters — перебираем только их и отсекаем
            // по квадрату дистанции ещё до любых проверок.
            _lastRejectNote = null;
            _lastNotSeenReason = null;
            _diagLosOverflow = false;
            _diagConsidered = 0;
            _diagRejectedTeam = 0;
            _diagRejectedDead = 0;
            _diagRejectedLayer = 0;
            _diagRejectedDist = 0;
            _diagRejectedFov = 0;
            _diagRejectedLos = 0;
            Transform best = null;
            float bestScore = float.MaxValue;

            // Обход по индексу: список персонажей может измениться прямо сейчас
            // (кто-то зарегистрировался), итерация по HashSet бросила бы исключение.
            bool playerEvaluated = false;
            for (int i = 0; i < s_characterList.Count; i++)
            {
                CharacterHealth cand = s_characterList[i];
                if (cand == null) continue;
                if (cand == s_playerHealth ||
                    (DiagEnabled && !playerEvaluated && cand.CompareTag("Player")))
                    playerEvaluated = true;
                EvaluateCandidate(cand, ref best, ref bestScore, ref bestDistance);
            }

            // Игрок — всегда валидная цель, даже если его CharacterHealth не попал
            // в реестр или слой выпал из TargetMask. Это убирает «слепоту» на игрока
            // при рассинхроне настроек сцены/префаба.
            if (!playerEvaluated && s_playerHealth != null && s_playerHealth != _health)
            {
                playerEvaluated = true;
                EvaluateCandidate(s_playerHealth, ref best, ref bestScore, ref bestDistance, true);
            }

            if (DiagEnabled && !playerEvaluated)
                _lastNotSeenReason = "NOT_IN_REGISTRY";

            return best;
        }

        private void EvaluateCandidate(CharacterHealth cand, ref Transform best,
            ref float bestScore, ref float bestDistance, bool isPlayer = false)
        {
            _diagConsidered++;
            // Тег проверяем всегда: иначе при пустом s_playerHealth и выключенной
            // диагностике игрок мог быть отсечён фильтром слоя.
            bool playerTarget = isPlayer || cand == s_playerHealth || cand.CompareTag("Player");

            if (!cand.IsAlive)
            {
                _diagRejectedDead++;
                if (playerTarget && DiagEnabled) _lastNotSeenReason = "DEAD";
                return;
            }
            if (cand.Team == OwnTeam)
            {
                _diagRejectedTeam++;
                if (playerTarget && DiagEnabled) _lastNotSeenReason = "SAME_TEAM";
                return;
            }
            if (cand.gameObject == gameObject) return;

            // Слой цели: 0 = фильтр не применён (см. предупреждение в Awake).
            // Для игрока фильтр игнорируется: он должен быть виден всегда.
            if (targetMask.value != 0 &&
                (targetMask.value & (1 << cand.gameObject.layer)) == 0)
            {
                _diagRejectedLayer++;
                if (playerTarget)
                {
                    // LAYER фиксируем в отладке, но игрока всё равно проверяем:
                    // рассинхрон слоя не должен делать его невидимым.
                    if (DiagEnabled)
                        _lastRejectNote = $"LAYER: игрок вне TargetMask " +
                            $"(слой {LayerMask.LayerToName(cand.gameObject.layer)}) — обход";
                }
                else
                {
                    // D5: строку собираем ТОЛЬКО в диагностике — иначе на каждый
                    // отсеянный персонаж и каждый тик восприятия идёт аллокация.
                    if (DiagEnabled)
                        _lastRejectNote = $"слой {LayerMask.LayerToName(cand.gameObject.layer)} " +
                            "не в TargetMask";
                    return;
                }
            }

            // Ранний отсев по квадрату дистанции — до любых дорогих проверок.
            Vector3 eye = eyePoint.position;
            float rough = Vector3.Distance(eye, cand.transform.position);
            if (rough > viewDistance + 3f)
            {
                _diagRejectedDist++;
                if (playerTarget && DiagEnabled) _lastNotSeenReason = $"TOO_FAR({rough:F1})";
                return;
            }

            Vector3 aimPoint = GetTargetAimPoint(cand);
            Vector3 toTarget = aimPoint - eye;
            float distance = toTarget.magnitude;
            if (distance <= 0.01f || distance > viewDistance)
            {
                _diagRejectedDist++;
                if (playerTarget && DiagEnabled) _lastNotSeenReason = $"TOO_FAR({distance:F1})";
                return;
            }

            if (Mathf.Abs(aimPoint.y - eye.y) > verticalViewTolerance)
            {
                _diagRejectedFov++;
                if (playerTarget && DiagEnabled)
                    _lastNotSeenReason = $"OUT_OF_FOV(vert {Mathf.Abs(aimPoint.y - eye.y):F1})";
                return;
            }

            // БАГФИКС: в упор (closeDetectDistance) зрение круговое — раньше
            // игрок в 2 метрах сбоку/сзади был невидим, т.к. угол > FOV/2.
            // Для игрока радиус кругового зрения чуть больше: он не должен
            // проскакивать мимо солдата незамеченным.
            float closeRadius = playerTarget
                ? Mathf.Max(closeDetectDistance, 10f)
                : closeDetectDistance;
            bool isClose = distance <= closeRadius;
            float angle = 0f;
            if (!isClose)
            {
                // FOV считаем по телу, а не по EyePoint: у модели глаз может быть
                // повёрнут/смещён и «съедать» цель прямо по курсу.
                Vector3 flatFwd = transform.forward; flatFwd.y = 0f;
                Vector3 flatDir = toTarget; flatDir.y = 0f;
                if (flatDir.sqrMagnitude < 0.0001f) { _diagRejectedFov++; return; }
                if (flatFwd.sqrMagnitude < 0.0001f)
                {
                    flatFwd = eyePoint.forward; flatFwd.y = 0f;
                    if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
                }

                angle = Vector3.Angle(flatFwd, flatDir);
                if (angle > fieldOfView * 0.5f)
                {
                    _diagRejectedFov++;
                    if (playerTarget && DiagEnabled)
                        _lastNotSeenReason = $"OUT_OF_FOV({angle:F0})";
                    return;
                }
            }

            if (!HasLineOfSight(cand, aimPoint))
            {
                _diagRejectedLos++;
                if (playerTarget && DiagEnabled)
                {
                    _lastNotSeenReason = _diagLosOverflow
                        ? "BUFFER_OVERFLOW"
                        : (_lastLosBlocker != null
                            ? $"LOS_BLOCKED({_lastLosBlocker})"
                            : "LOS_BLOCKED");
                }
                return;
            }

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

        /// <summary>
        /// Луч обзора. СОЮЗНИКИ НЕ БЛОКИРУЮТ обзор: солдат в шеренге между стрелком и
        /// игроком больше не «съедает» цель. Помеха помехой считается только геометрия
        /// (стены, укрытия) и чужие.
        /// </summary>
        private bool HasLineOfSight(CharacterHealth targetHealth, Vector3 targetPoint,
            out bool blockedByAlly)
        {
            blockedByAlly = false;
            Vector3 origin = eyePoint.position;
            Vector3 dir = targetPoint - origin;
            float dist = dir.magnitude;
            if (dist <= 0.001f) return true;
            dir /= dist;

            // Слой цели добавляем к маске препятствий: иначе луч «проходит» сквозь
            // цель, если её слой не входит в visibilityMask, и видимость теряется.
            int losMask = visibilityMask;
            if (targetHealth != null) losMask |= 1 << targetHealth.gameObject.layer;

            int eyeCount = Physics.OverlapSphereNonAlloc(origin, 0.25f, _eyeBuffer,
                visibilityMask, QueryTriggerInteraction.Ignore);

            float travelled = 0f;
            int skipped = 0;
            while (travelled < dist)
            {
                if (++skipped > 128)
                {
                    _diagLosOverflow = true;
                    _lastLosBlocker = "BUFFER_OVERFLOW";
                    return false;
                }

                Vector3 rayOrigin = origin + dir * travelled;
                float remaining = dist + 0.2f - travelled;
                if (!Physics.Raycast(rayOrigin, dir, out RaycastHit hit, remaining,
                        losMask, QueryTriggerInteraction.Ignore))
                    break;

                float hitDistance = travelled + hit.distance;
                Transform hitTransform = hit.transform;
                if (hitTransform == null)
                {
                    travelled = hitDistance + 0.02f;
                    continue;
                }

                if (hitTransform.root == transform.root)
                {
                    travelled = hitDistance + 0.02f;
                    continue;
                }
                if (hitDistance < 0.5f && IsEnclosingEyeCollider(hit.collider, eyeCount))
                {
                    travelled = hitDistance + 0.02f;
                    continue;
                }

                CharacterHealth hitHealth = hit.collider.GetComponentInParent<CharacterHealth>();
                if (targetHealth != null && hitHealth == targetHealth)
                {
                    _lastLosBlocker = null;
                    return true;
                }

                if (hitDistance >= dist - 0.35f)
                {
                    _lastLosBlocker = null;
                    return true;
                }

                if (hitHealth != null && hitHealth.Team == OwnTeam)
                {
                    blockedByAlly = true;
                    _lastLosBlocker = null;
                    travelled = hitDistance + 0.02f;
                    continue;
                }

                _lastLosBlocker = hitTransform.name;
                return false;
            }

            _lastLosBlocker = null;
            return true;
        }

        private bool HasLineOfSight(Transform target, Vector3 targetPoint)
            => HasLineOfSight(target, targetPoint, out _);

        private bool HasLineOfSight(Transform target, Vector3 targetPoint,
            out bool blockedByAlly)
        {
            CharacterHealth health = null;
            if (target != null && target == _target && _targetHealth != null)
                health = _targetHealth;
            else if (target != null)
                health = target.GetComponentInParent<CharacterHealth>();

            return HasLineOfSight(health, targetPoint, out blockedByAlly);
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

            bool reached = flatDist <= pointReachRadius;
            if (!reached && _motor.HasArrived())
            {
                // HasArrived() == true и при hasPath == false — то есть «путь не
                // построен», а не «дошли». Если путь построить не удалось —
                // пробуем следующую точку, иначе солдат стоит навсегда.
                if (_lastMoveSucceeded)
                {
                    reached = true;
                }
                else
                {
                    // Путь не построен — пробуем следующую точку, иначе солдат
                    // стоит навсегда. Пауза между попытками, чтобы не дёргать
                    // SetDestination каждый кадр.
                    if (Time.time < _nextPatrolRetryTime) return;
                    _nextPatrolRetryTime = Time.time + patrolRetryDelay;

                    AdvancePatrolIndex();
                    Transform next = patrolRoute.GetPoint(_patrolIndex);
                    if (next != null) MoveTo(next.position);
                    return;
                }
            }

            if (reached)
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
            _motor.SetManualRotation(true);
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

        /// <summary>
        /// Поворот корпуса на источник звука. Делаем ТОЛЬКО когда солдат стоит:
        /// идёт — угол тела ведёт NavMeshAgent (updateRotation = true), и мотор
        /// с ним спорил бы (тело дёргается, звук игнорируется). После досмотра
        /// управление углом возвращаем агенту.
        /// </summary>
        private void UpdateLookAtSound()
        {
            if (_lookAtSoundUntil <= 0f) return;

            // В бою/погоне угол ведёт цель, в поиске — осматривание.
            if (State != EnemyState.Patrol && State != EnemyState.Alert)
            {
                _lookAtSoundUntil = 0f;
                return;
            }
            if (_lookAroundPhase != LookAroundPhase.None) return;

            if (Time.time < _lookAtSoundUntil)
            {
                if (_motor.CurrentSpeed > 0.3f)
                {
                    // Идёт — угол тела ведёт агент: сразу отдаём ему управление,
                    // иначе солдат дойдёт боком.
                    _motor.SetManualRotation(false);
                    _motor.SetAutoRotation(true);
                    return;
                }

                _motor.SetManualRotation(true);
                _motor.SetAutoRotation(false);
                _motor.FaceTowardsAtSpeed(_lookAtSoundPoint, 90f);
                return;
            }

            _lookAtSoundUntil = 0f;
            _motor.SetManualRotation(false);
            _motor.SetAutoRotation(true);
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

            // Настороженный — тоже бегом: подозрение горит, идти шагом нельзя.
            _motor.SetSpeed(RunSpeed);
            _motor.SetSprintMode(true);
            // Есть информация, где игрок — расходимся по области вокруг точки,
            // а не сбегаемся всей толпой в одну воронку.
            RefreshDestination(AreaSpreadGoal(_suspicionPoint));

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

            // Проверяем 2–3 точки вокруг последней известной позиции: у разных
            // солдат точки РАЗНЫЕ (свой сектор), иначе все идут в одно место.
            if (_searchPointIndex < _searchPointTotal)
            {
                Vector3 point = _searchPoints[_searchPointIndex];
                if (!_motor.HasDestination) MoveTo(point);

                if (!_motor.HasArrived())
                {
                    _motor.SetAutoRotation(true);
                    return;
                }

                // На месте: осматриваемся, потом идём к следующей точке.
                _motor.Stop();
                _motor.SetManualRotation(true);
                _motor.FaceTowardsAtSpeed(point + transform.forward * 6f, combatTurnSpeed * 1.5f);

                if (Time.time >= _searchLookUntil)
                {
                    _searchLookUntil = Time.time + 1.5f;
                    _searchPointIndex++;
                    if (_searchPointIndex < _searchPointTotal)
                        MoveTo(_searchPoints[_searchPointIndex]);
                }
                return;
            }

            // Все точки проверены — осматриваемся на месте.
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
                // Поворот — ТОЛЬКО через мотор: раньше ИИ писал transform.rotation сам и
                // спорил с мотором/агентом за угол тела.
                _motor.SetManualRotation(true);
                _motor.FaceYawAtSpeed(targetYaw, searchSweepSpeed);
            }

            // БАГФИКС: раньше поиск длился ~1 сек (0.4→0.02 при decay 0.35),
            // враг «забывал» игрока почти сразу и выглядел слепым.
            // Теперь держим поиск минимум до конца памяти о цели.
            if (_awareness <= 0.02f && Time.time >= _searchUntil &&
                Time.time - _lastTargetSeenTime > targetMemoryDuration)
                ChangeState(EnemyState.Patrol);
        }

        /// <summary>
        /// Точки для проверки: 2–3 точки вокруг последней известной позиции.
        /// Сектор у каждого солдата свой — отряд не топчется в одной точке.
        /// Точка берётся только если до неё ЕСТЬ путь (иначе солдат встанет).
        /// </summary>
        private void BuildSearchPoints()
        {
            _searchPointTotal = 0;
            _searchPointIndex = 0;
            _searchLookUntil = 0f;

            int count = Mathf.Clamp(searchPointCount, 2, _searchPoints.Length);
            float baseAngle = HashAngle(GetInstanceID()) + HashAngle(_patrolIndex * 7919 + 13);

            for (int i = 0; i < count && _searchPointTotal < _searchPoints.Length; i++)
            {
                float angle = (baseAngle + i * (360f / count) + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
                float radius = Random.Range(4f, 9f);
                Vector3 desired = _searchCenter +
                    new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);

                Vector3 point = desired;
                if (_motor != null)
                {
                    // Точка берётся только если до неё ЕСТЬ путь: иначе солдат
                    // встанет у стены и «проверит» точку, до которой не дойдёт.
                    // Если ни одна точка не годится — солдат просто осматривается
                    // на месте (UpdateSearch умеет и так).
                    if (!_motor.SampleReachablePoint(desired, 7f, out point, true))
                        continue;
                }

                // Точка не должна вставать в кучу с соседями: поиск идёт
                // по области, а не плечом к плечу в одной воронке.
                if (useSquadTactics && InfantrySquad.IsCrowded(this, point,
                        squadSeparationDistance * 0.75f))
                    continue;

                _searchPoints[_searchPointTotal++] = point;
            }
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

            // Погоня — бегом: если кто-то рядом уже стреляет, остальные должны
            // быстро подтянуться, а не топать шагом.
            _motor.SetSpeed(RunSpeed);
            _motor.SetSprintMode(true);
            bool recentlyVisible = Time.time - _lastTargetSeenTime <= perceptionInterval * 2f;

            Vector3 chaseGoal = _lastKnownTargetPosition;
            if (useSquadTactics && !recentlyVisible)
            {
                // Цель не видна, но информация «игрок там» есть (шум, контакт,
                // последняя известная точка) — расходимся по области вокруг неё,
                // а не сбегаемся всей толпой в одну точку.
                chaseGoal = AreaSpreadGoal(_lastKnownTargetPosition);
                _chaseGoalOffset = Vector3.zero;
                _nextChaseGoalTime = 0f;
            }
            else if (useSquadTactics)
            {
                // Раскладка строя в погоне — тоже тяжёлый O(N)-обход по отряду,
                // поэтому ~5 Гц со сдвигом (D4), а не каждый кадр.
                if (Time.time >= _nextChaseGoalTime)
                {
                    _nextChaseGoalTime = Time.time + FormationRefreshInterval +
                        Random.Range(0f, FormationRefreshJitter);
                    _chaseGoalOffset = InfantrySquad.AdjustChaseGoal(this, _lastKnownTargetPosition) -
                        _lastKnownTargetPosition;
                }
                chaseGoal += _chaseGoalOffset;
            }
            else
            {
                _chaseGoalOffset = Vector3.zero;
                _nextChaseGoalTime = 0f;
            }

            // Предсказание ухода цели: идём не в точку, а туда, куда она УХОДИТ.
            if (recentlyVisible && _targetVelocity.sqrMagnitude > 0.25f && chaseLeadTime > 0f)
            {
                Vector3 lead = Vector3.ClampMagnitude(_targetVelocity * chaseLeadTime,
                    chaseLeadMaxDistance);
                lead.y = 0f;
                chaseGoal += lead;
            }

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

            UpdateEngageStage();

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

            if (holdingThroughFlicker)
            {
                _motor.Stop();
                _motor.FaceTowardsAtSpeed(_lastKnownTargetPosition, combatTurnSpeed);
                return;
            }

            // ТАКТИКА ОТРЯДА: роли и жетоны решает координатор, солдат исполняет.
            if (useSquadTactics && TacticsAvailable)
            {
                ExecuteSquadRole(distance);
                return;
            }
            if (useSquadTactics) WarnFallbackTacticsOnce();

            _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);

            // СВОЯ ТОЧКА В БОЮ.
            // Раньше все шли на координаты игрока и вставали в одну точку.
            // Теперь у каждого своё место в строю (вбок + чуть по глубине),
            // якорь едет плавно — солдат перемещается без рывков:
            // либо чуть вперёд, либо чуть вперёд и вбок.
            Vector3 anchor = UpdateCombatAnchor(distance);

            bool holdingFire = Time.time < _fireAnchorUntil;
            bool repositioning = Time.time < _repositionUntil;
            if (repositioning &&
                FlatDistance(transform.position, _repositionGoal) <= 0.8f)
            {
                _repositionUntil = 0f;
                repositioning = false;
            }

            Vector3 moveGoal = repositioning ? _repositionGoal : anchor;
            float moveTolerance = repositioning ? 0.9f : combatAnchorTolerance;

            // Цель видна и мы в боевой дистанции — СТОИМ И СТРЕЛЯЕМ.
            // Якорь в этом случае заморожен (см. UpdateCombatAnchor), поэтому
            // солдат доходит до своей позиции и стоит, а не топает бесконечно
            // вперёд за уходящим якорем.
            // (visible уже посчитан выше — CanSeeCurrentTarget кеширует результат.)
            bool holdForShot = !repositioning && visible && !holdingFire &&
                WouldTurnAwayFromTarget(anchor);

            bool moving = false;
            bool shouldMove = repositioning
                ? FlatDistance(transform.position, moveGoal) > moveTolerance
                : !holdingFire && !holdForShot && FlatDistance(transform.position, moveGoal) > moveTolerance;

            if (shouldMove)
            {
                // Смена позиции — перебежкой: солдат не топает на месте, а быстро
                // занимает своё место в строю и оттуда стреляет.
                _motor.SetSpeed(RepositionSpeed);
                _motor.SetSprintMode(true);
                RefreshDestination(moveGoal, 1f);
                moving = true;
            }
            else
            {
                _motor.Stop();
                _motor.SetSprintMode(false);
            }

            // ПРАВИЛО ДВИЖЕНИЯ: «идёт — смотрит по ходу и не стреляет;
            // стреляет — стоит и довёрнут на цель».
            if (!holdForShot && (moving || _motor.CurrentSpeed > 0.3f))
            {
                _motor.SetAutoRotation(true);
            }
            else
            {
                _motor.SetAutoRotation(false);
                _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);
            }

            UpdateFiring(distance, _motor.CurrentSpeed);
        }

        /// <summary>
        /// Тактика доступна только на запечённом NavMesh: в режиме Fallback
        /// (навмеша нет) укрытия и перебежки отключаются.
        /// </summary>
        private bool TacticsAvailable =>
            _motor != null && _motor.Mode == EnemyMotor.MotorMode.NavMesh;

        private void WarnFallbackTacticsOnce()
        {
            if (s_fallbackTacticsWarned) return;
            s_fallbackTacticsWarned = true;
            Debug.LogWarning("[EnemyAI] NavMesh не запечён — укрытия и перебежки отключены, " +
                "солдаты ведут бой на месте (режим Fallback). Запеки NavMesh для полной тактики.", this);
        }

        /// <summary>Исполнение роли, выданной координатором отряда.</summary>
        private void ExecuteSquadRole(float distance)
        {
            switch (Role)
            {
                case SquadRole.Maneuver:
                    ExecuteManeuverRole(distance);
                    break;
                case SquadRole.Cover:
                    ExecuteCoverRole(distance);
                    break;
                case SquadRole.Flank:
                    ExecuteFlankRole(distance);
                    break;
                default:
                    ExecuteFireRole(distance);
                    break;
            }
        }

        /// <summary>
        /// Покажет, уходит ли солдат от цели спиной (назад), если пойдёт в точку.
        /// Боковой шаг в своё место строя разрешён: иначе отряд не расходится
        /// и снова сбивается в кучу перед игроком.
        /// </summary>
        private bool WouldTurnAwayFromTarget(Vector3 destination)
        {
            if (_target == null) return false;

            Vector3 toTarget = _target.position - transform.position;
            Vector3 toDestination = destination - transform.position;
            toTarget.y = 0f;
            toDestination.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f ||
                toDestination.sqrMagnitude < 0.01f) return false;

            return Vector3.Dot(toTarget.normalized, toDestination.normalized) < -0.15f;
        }

        /// <summary>Огонь: стоит на своей позиции, довёрнут на цель, стреляет.</summary>
        private void ExecuteFireRole(float distance)
        {
            Vector3 anchor = UpdateCombatAnchor(distance);

            bool holdingFire = Time.time < _fireAnchorUntil;
            bool repositioning = Time.time < _repositionUntil;
            if (repositioning &&
                FlatDistance(transform.position, _repositionGoal) <= 0.8f)
            {
                _repositionUntil = 0f;
                repositioning = false;
            }

            Vector3 moveGoal = repositioning ? _repositionGoal : anchor;
            float moveTolerance = repositioning ? 0.9f : combatAnchorTolerance;

            // Видимая цель в боевой дистанции — стоим и стреляем: якорь в этом
            // случае заморожен, солдат доходит до своей позиции и стоит.
            bool visible = CanSeeCurrentTarget();
            bool holdForShot = !repositioning && visible && !holdingFire &&
                WouldTurnAwayFromTarget(anchor);
            bool moving = false;

            bool shouldMove = repositioning
                ? FlatDistance(transform.position, moveGoal) > moveTolerance
                : !holdingFire && !holdForShot && FlatDistance(transform.position, moveGoal) > moveTolerance;

            if (shouldMove)
            {
                // Выход на выгодную позицию — решительной перебежкой.
                _motor.SetSpeed(RepositionSpeed);
                _motor.SetSprintMode(true);
                RefreshDestination(moveGoal, 1f);
                moving = true;
            }
            else
            {
                _motor.Stop();
                _motor.SetSprintMode(false);
            }

            if (!holdForShot && (moving || _motor.CurrentSpeed > 0.3f))
                _motor.SetAutoRotation(true);
            else
            {
                _motor.SetAutoRotation(false);
                _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);
            }

            // Стреляет только тот, кто держит жетон атаки (лимит на отряд).
            if (HasFireToken) UpdateFiring(distance, _motor.CurrentSpeed);
        }

        /// <summary>Манёвр: рывок на 8–12 м. Не стреляет, смотрит по ходу.</summary>
        private void ExecuteManeuverRole(float distance)
        {
            Vector3 goal = SquadGoal;
            if (FlatDistance(transform.position, goal) > 1.5f)
            {
                _motor.SetSpeed(RepositionSpeed);
                _motor.SetSprintMode(true);
                RefreshDestination(goal, 1f);
                _motor.SetAutoRotation(true);
                return;
            }

            _motor.Stop();
            _motor.SetSprintMode(false);
            _motor.SetAutoRotation(false);
            _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);
        }

        /// <summary>Фланг: обход сбоку. На месте — держит позицию.</summary>
        private void ExecuteFlankRole(float distance)
        {
            Vector3 goal = SquadGoal;
            if (FlatDistance(transform.position, goal) > 1.5f)
            {
                _motor.SetSpeed(RepositionSpeed);
                _motor.SetSprintMode(true);
                RefreshDestination(goal, 1f);
                _motor.SetAutoRotation(true);
                return;
            }

            _motor.Stop();
            _motor.SetSprintMode(false);
            _motor.SetAutoRotation(false);
            _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);
        }

        /// <summary>
        /// Укрытие: дошёл до точки — прячется, потом выглядывает на 1–2 короткие
        /// очереди (если держит жетон атаки) и снова прячется.
        /// </summary>
        private void ExecuteCoverRole(float distance)
        {
            Vector3 goal = SquadGoal;
            if (FlatDistance(transform.position, goal) > 1.2f)
            {
                _motor.SetSpeed(RepositionSpeed);
                _motor.SetSprintMode(true);
                RefreshDestination(goal, 1f);
                _motor.SetAutoRotation(true);
                return;
            }

            _motor.Stop();
            _motor.SetSprintMode(false);
            _motor.SetAutoRotation(false);

            Vector3 aimPoint = _target != null ? _target.position : transform.position;
            if (AssignedCover != null)
                aimPoint = AssignedCover.Position + (AssignedCover.GetFacingDirection(aimPoint)) * 2f;
            _motor.FaceTowardsAtSpeed(aimPoint, combatTurnSpeed);

            if (Time.time >= _coverPeekUntil)
            {
                _coverPeeking = !_coverPeeking;
                _coverPeekUntil = Time.time + (_coverPeeking
                    ? coverPeekDuration
                    : coverHideDuration);
                if (_coverPeeking) _shotsRemaining = 0;
            }

            if (_coverPeeking && HasFireToken)
                UpdateFiring(distance, 0f);
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
            //    Раскладка и разведение — тяжёлые O(N)-обходы по отряду, поэтому
            //    считаем их ~5 Гц со сдвигом (D4), а не каждый кадр.
            float lateral = 0f;
            float depth = 0f;
            Vector3 separation = Vector3.zero;
            if (useSquadTactics)
            {
                if (Time.time >= _nextFormationTime)
                {
                    _nextFormationTime = Time.time + FormationRefreshInterval +
                        Random.Range(0f, FormationRefreshJitter);
                    InfantrySquad.ComputeFormationSlot(this, targetPosition, right,
                        out _formationLateral, out _formationDepth);
                    // Смещение мягкого разведения не зависит от якоря — кешируем и его.
                    _separationOffset = InfantrySquad.ApplySeparation(this, Vector3.zero);
                }
                lateral = _formationLateral;
                depth = _formationDepth;
                separation = _separationOffset;
            }

            // Боковой сдвиг НЕ режем: это место солдата в строю. Раньше клип
            // 1.5 м стягивал весь отряд в узкую колонну — они бегали кучей.

            // 3. Дистанция до цели: держим боевую, но назад не отходим.
            float range = Mathf.Min(distance, preferredCombatDistance);
            range = Mathf.Clamp(range + depth, 1.5f, preferredCombatDistance + squadDepthSpread);

            // На близкой дистанции строй сужается — иначе солдаты разъезжаются
            // вокруг игрока шире, чем нужно.
            float lateralScale = Mathf.Clamp(range / Mathf.Max(1f, preferredCombatDistance), 0.4f, 1f);

            Vector3 anchor = targetPosition - forward * range + right * (lateral * lateralScale);

            // 4. Мягкое расхождение: если кто-то подошёл слишком близко —
            //    якорь сдвигается вбок (не рывок, а плавный уход).
            anchor += separation;

            // 4b. Якорь обязан лежать на NavMesh: иначе солдат бесконечно идёт
            //     в стену и не доходит до позиции. Поправка кешируется как
            //     смещение, поэтому якорь продолжает плавно идти за целью.
            anchor = ClampAnchorToNavMesh(anchor);

            // 5. Якорь догоняет цель с ограниченной скоростью.
            //    Но если цель видна и стрелять уже можно — якорь ЗАМОРАЖИВАЕТСЯ:
            //    солдат занимает своё место в строю и стоит, а не бесконечно
            //    топает за уходящим якорем (и не стреляет).
            bool holdInPlace = CanSeeCurrentTarget() &&
                distance <= preferredCombatDistance + combatRangeSlack;

            if (!_hasCombatAnchor)
            {
                _combatAnchor = anchor;
                _hasCombatAnchor = true;
            }
            else if (!holdInPlace)
            {
                Vector3 delta = anchor - _combatAnchor;
                delta.y = 0f;
                float maxStep = combatAnchorTrackSpeed * Time.deltaTime;
                if (delta.sqrMagnitude > maxStep * maxStep) delta = delta.normalized * maxStep;
                _combatAnchor += delta;
            }

            return _combatAnchor;
        }

        /// <summary>
        /// Поправка боевого якоря на NavMesh. SamplePosition (без проверки пути)
        /// дёшев, поэтому сэмплим ~5 Гц со сдвигом и храним ПОПРАВКУ как
        /// смещение: так якорь продолжает плавно идти за целью, а не «прыгает»
        /// на точку сэмпла. Без этого солдат может бесконечно идти в стену.
        /// </summary>
        private Vector3 ClampAnchorToNavMesh(Vector3 anchor)
        {
            if (_motor == null || _motor.Mode != EnemyMotor.MotorMode.NavMesh) return anchor;

            if (Time.time >= _nextAnchorCheckTime)
            {
                _nextAnchorCheckTime = Time.time + AnchorCheckInterval +
                    Random.Range(0f, AnchorCheckJitter);

                if (UnityEngine.AI.NavMesh.SamplePosition(anchor,
                        out UnityEngine.AI.NavMeshHit hit,
                        AnchorSampleRadius, UnityEngine.AI.NavMesh.AllAreas))
                {
                    Vector3 correction = hit.position - anchor;
                    correction.y = 0f;   // высота на NavMesh своя
                    _anchorNavCorrection = correction;
                }
                else
                {
                    _anchorNavCorrection = Vector3.zero;
                }
            }

            return anchor + _anchorNavCorrection;
        }

        /// <summary>
        /// Цель с распределением по области. Когда у солдат есть ИНФОРМАЦИЯ,
        /// где игрок (шум, контакт, последняя известная точка), они занимают
        /// места по кольцу вокруг неё, а не сбегаются в одну воронку.
        /// Смещение пересчитывается ~5 Гц; если места нет (стены, край NavMesh),
        /// InfantrySquad прижимает точку ближе к центру.
        /// </summary>
        private Vector3 AreaSpreadGoal(Vector3 center)
        {
            if (!useSquadTactics) return center;

            if (Time.time >= _nextAreaSpreadTime ||
                FlatDistance(center, _areaSpreadCenter) > 5f)
            {
                _nextAreaSpreadTime = Time.time + FormationRefreshInterval +
                    Random.Range(0f, FormationRefreshJitter);
                _areaSpreadCenter = center;
                _areaSpreadOffset = InfantrySquad.AdjustAreaGoal(this, center,
                    Mathf.Max(squadAreaSpreadRadius, squadSeparationDistance)) - center;
                _areaSpreadOffset.y = 0f;
            }

            return center + _areaSpreadOffset;
        }

        /// <summary>
        /// Стадия темпа схватки — ОБЩАЯ ДЛЯ ОТРЯДА и зависящая от потерянной доли
        /// здоровья цели (CharacterHealth.NormalizedHealth). Раньше стадия росла от
        /// числа попаданий, а HP у всех общий — «темп» нарастал у всего отряда
        /// одновременно и вовсе не отражал реальные потери.
        /// Сброс: 10 с вне боя (engageResetDelay) или восстановление здоровья цели.
        /// </summary>
        private void UpdateEngageStage()
        {
            if (_targetHealth == null) return;

            // Восстановление здоровья цели — схватка начинается заново.
            if (_lastTargetHp > 0f && _targetHealth.CurrentHealth > _lastTargetHp + 0.01f)
            {
                _engageStartTime = Time.time;
                _engageOpen = true;
                // Базовое здоровье = восстановленному: темп снова с нуля.
                _engageHpBaseline = Mathf.Clamp01(_targetHealth.NormalizedHealth);
            }
            _lastTargetHp = _targetHealth.CurrentHealth;

            int stages = pacingDamageStages != null ? pacingDamageStages.Length : 0;
            if (stages <= 0)
            {
                _engageStage = 0;
                return;
            }

            float normalized = Mathf.Clamp01(_targetHealth.NormalizedHealth);
            // D8: стадия = floor((1 - NormalizedHealth) * stages) — считаем от
            // ПОТЕРИ здоровья с начала схватки. Базовое здоровье = 1 → формула
            // ровно та же. Базовое < 1 (схватка началась после ран, либо прошло
            // engageResetDelay вне боя) → новая схватка стартует с «царапины»,
            // а стадия растёт по мере новых потерь. Раньше стадия считалась от
            // АБСОЛЮТНОГО HP каждый кадр, и сброс по engageResetDelay не давал
            // никакого эффекта.
            float baseline = Mathf.Clamp(_engageHpBaseline, 0.05f, 1f);
            float lost = Mathf.Clamp01((baseline - normalized) / baseline);
            _engageStage = Mathf.Clamp(Mathf.FloorToInt(lost * stages), 0, stages - 1);
            if (_engageStage > 0) _engageOpen = false;
        }

        private void UpdateFiring(float distance, float selfSpeed)
        {
            if (weapon == null || weapon.IsReloading || Time.time < _nextBurstTime)
                return;

            if (Time.time < _canFireAfter)
                return;

            if (distance > weapon.Range) return;

            // Идёт — не стреляет: корпус смотрит по ходу, очередь ушла бы мимо.
            // Проверяем ФАКТИЧЕСКОЕ движение и приказ двигаться (WantsToMove), а не
            // намерение «moving». Раньше солдат с недостижимым боевым якорем вечно
            // числился идущим (якорь недостижим → anchorDistance не сходится) и
            // НИКОГДА не стрелял.
            if (selfSpeed > 0.35f || _motor.WantsToMove) return;

            // Не стрелять в доводке: огонь только когда развернулся на игрока.
            // Серия не сгорает — при доводке очередь продолжится с того же места.
            if (_target != null && distance > 0.5f && !IsFacingTarget(maxFireAngle))
                return;

            // Патронов нет вообще — не стоим столбом: ждём роли/укрытия.
            if (!weapon.HasAmmunition)
            {
                WantsReposition = true;
                return;
            }

            if (weapon.AmmunitionInMagazine <= 0)
            {
                // Перезарядка: в укрытии или при смене роли безопаснее, но ждать нельзя.
                weapon.BeginReload();
                return;
            }

            // Проверка линии — только когда оружие реально готово стрелять,
            // иначе был бы рейкаст каждый кадр на каждого стрелка.
            if (!weapon.CanFireNow) return;

            if (_shotsRemaining <= 0)
            {
                if (useSquadTactics)
                    _shotsRemaining = Random.Range(tacticalMinimumBurst, tacticalMaximumBurst + 1);
                else
                    _shotsRemaining = Random.Range(minimumBurstSize, maximumBurstSize + 1);
                _burstShotIndex = 0;
            }

            Vector3 aimPoint = ComputeAimPoint(distance, selfSpeed);

            // Перед каждым выстрелом — проверка чистой линии. Союзник на линии
            // стрельбе не мешает: хитскан проходит сквозь своих. Блокирует только
            // геометрия (стена/укрытие).
            bool clearShot = weapon.HasClearShot(aimPoint, gameObject, out bool blockedByAlly);
            _blockedByAlly = blockedByAlly;
            if (!clearShot && !blockedByAlly)
            {
                // Линию перекрыла геометрия (стена/укрытие) — стрелять нельзя.
                // Не стоим столбом, пока союзники стреляют: смещаемся вбок,
                // ищем угол для выстрела.
                WantsReposition = true;
                RequestFireReposition();
                return;
            }

            UpdateSpreadModel(distance, selfSpeed);

            weapon.DamageScale = EngageDamageScale() *
                Random.Range(1f - pacingDamageJitter, 1f + pacingDamageJitter);

            if (weapon.TryFire(aimPoint, gameObject))
            {
                // «Радио»: стреляем — значит отряд должен знать, где бой.
                RadioContact();

                if (_fireAnchorUntil < Time.time)
                    _fireAnchorUntil = Time.time +
                        squadFireAnchorDuration * Random.Range(0.9f, 1.1f);

                _shotsRemaining--;
                _burstShotIndex++;
                if (_shotsRemaining <= 0)
                {
                    float pauseMin = useSquadTactics ? tacticalMinimumPause : minimumBurstPause;
                    float pauseMax = useSquadTactics ? tacticalMaximumPause : maximumBurstPause;
                    float pause = Random.Range(pauseMin, pauseMax);

                    // Середина схватки — пауза длиннее (игроку дают передышку).
                    if (_engageStage >= 1 && _engageStage <= 2) pause *= midFightPauseMult;
                    // Раненый стреляет реже.
                    if (_health != null && _health.NormalizedHealth <= woundedHealthThreshold)
                        pause *= woundedFireRateScale;

                    _nextBurstTime = Time.time + pause;
                }
            }
        }

        /// <summary>
        /// Короткий боковой шаг при перекрытой линии огня (стена, укрытие).
        /// Сторона чередуется: если справа тоже глухо — следующий шаг влево.
        /// Так солдат за укрытием не стоит и не смотрит вперёд, пока
        /// союзники стреляют, а ищет угол.
        /// </summary>
        private void RequestFireReposition()
        {
            if (Time.time < _repositionUntil) return;
            if (_target == null) return;

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return;

            Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
            _repositionFlip = !_repositionFlip;
            float side = _repositionFlip ? 1f : -1f;
            float step = Mathf.Clamp(squadSeparationDistance, 3f, 8f);
            Vector3 desired = transform.position + right * (side * step);

            if (!_motor.SampleReachablePoint(desired, step, out Vector3 point, true))
                return;

            _repositionGoal = point;
            _repositionUntil = Time.time + 2f;
        }

        /// <summary>
        /// ЕДИНАЯ МОДЕЛЬ РАЗБРОСА: ИИ считает весь разброс сам и отдаёт оружию
        /// множитель (weapon.SpreadScale). Точность растёт при непрерывной видимости
        /// цели (2.0 → 0.8 за ~3 с) и сбрасывается при потере.
        /// </summary>
        private void UpdateSpreadModel(float distance, float selfSpeed)
        {
            if (weapon == null) return;

            float meter = baseSpread + spreadPerTenMeters * (distance / 10f);
            meter *= Mathf.Lerp(1f, movingSpreadMultiplier,
                Mathf.Clamp01(selfSpeed / Mathf.Max(0.1f, chaseSpeed)));
            meter *= Mathf.Lerp(1f, suppressedSpreadMultiplier, _suppression);
            meter *= EngageSpreadScale();

            float seenTime = Mathf.Max(0.01f, Time.time - _lastTargetSeenTime);
            float settled = Mathf.Clamp01(seenTime / Mathf.Max(0.1f, spreadSettleTime));
            meter *= Mathf.Lerp(spreadUnseenMultiplier, spreadSeenMultiplier, settled);

            // Метры разброса → градусы → множитель базового разброса оружия.
            float angle = Mathf.Atan2(meter, Mathf.Max(1f, distance)) * Mathf.Rad2Deg;
            float baseAngle = weapon.BaseSpreadAngle;
            weapon.SpreadScale = baseAngle > 0.01f
                ? Mathf.Max(0.05f, angle / baseAngle)
                : 1f;
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
            if (pacingDamageStages == null || pacingDamageStages.Length == 0) return 1f;
            int index = Mathf.Clamp(_engageStage, 0, pacingDamageStages.Length - 1);
            return Mathf.Max(0.05f, pacingDamageStages[index]);
        }

        /// <summary>Разброс по стадии схватки: открытие → середина → концовка.</summary>
        private float EngageSpreadScale()
        {
            if (_engageOpen && _engageStage == 0 &&
                Time.time - _engageStartTime < openingDuration)
                return openingSpread;
            if (_engageStage <= 2) return midFightSpread;
            return closingSpread;
        }

        private Vector3 ComputeAimPoint(float distance, float selfSpeed)
        {
            Vector3 basePoint = CurrentTargetAimPoint();

            float lead = leadAccuracy * 0.15f;
            basePoint += _targetVelocity * lead;
            basePoint += Vector3.up * (burstClimb * Mathf.Min(1f, _burstShotIndex / 4f));

            // Случайный разброс НЕ добавляем здесь: весь разброс считает
            // UpdateSpreadModel и отдаёт оружию через weapon.SpreadScale —
            // иначе разброс был бы двойным (метры у ИИ + градусы у оружия).
            return basePoint;
        }

        private void GoSearchLastKnown()
        {
            _motor.SetSpeed(RunSpeed);
            _motor.SetSprintMode(true);
            // Идём не в одну точку, а в свою часть области вокруг неё.
            RefreshDestination(AreaSpreadGoal(_lastKnownTargetPosition));

            if (_motor.HasArrived())
            {
                _awareness = Mathf.Min(_awareness, 0.4f);
                _searchCenter = _lastKnownTargetPosition;
                ChangeState(EnemyState.Search);
            }
        }

        private void LoseTarget(string reason = "потеряна")
        {
            if (debugLogging && _target != null)
                Debug.Log($"[EnemyAI] {name}: цель «{_target.name}» {reason} — возврат в патруль.", this);
            ClearTarget(reason);
            _firstSightAcquired = false;
            ChangeState(EnemyState.Patrol);
        }

        private void SetTarget(Transform target)
        {
            if (target == null)
            {
                _target = null;
                _targetHealth = null;
                return;
            }

            if (_target != target)
            {
                _prevTargetPosition = target.position;
                _targetVelocity = Vector3.zero;
                // CharacterHealth лежит либо на самом объекте, либо выше по иерархии.
                _targetHealth = target.GetComponentInParent<CharacterHealth>();
                // Новая цель — новая схватка: темп считаем от её здоровья.
                _engageHpBaseline = _targetHealth != null
                    ? Mathf.Clamp01(_targetHealth.NormalizedHealth)
                    : 1f;
                // Новую цель нельзя оценивать по LOS-кешу старой.
                _nextLosCheckTime = 0f;
                _cachedCanSee = false;
                _losBlockedByAlly = false;
                _targetHiddenSince = float.NegativeInfinity;
                _suddenDelayApplied = false;
                if (debugLogging)
                    Debug.Log($"[EnemyAI] {name}: новая цель «{target.name}»", this);
            }
            _target = target;
        }

        /// <summary>
        /// «РАДИО» ОТРЯДА. Стреляющий солдат сообщает остальным, где цель.
        /// Один поднял тревогу — отряд подтягивается, даже если бой не видно:
        /// те, кто далеко, слышат стрельбу и бегут на неё.
        /// </summary>
        private void RadioContact()
        {
            if (_target == null) return;
            if (Time.time < _nextRadioTime) return;
            _nextRadioTime = Time.time + Mathf.Max(0.2f, radioInterval);
            NotifyAlliesOfContact(this, _target, _target.position);
        }

        /// <summary>
        /// Передача контакта союзникам: заметивший отдаёт точку и время, союзники
        /// в радиусе реагируют с короткой задержкой. Так один поднятый тревогу
        /// поднимает отряд, а не весь уровень.
        /// </summary>
        private static void NotifyAlliesOfContact(EnemyAI spotter, Transform target, Vector3 point)
        {
            if (spotter == null || target == null) return;

            List<EnemyAI> soldiers = s_soldiers;
            // Радиус тревоги шире, чем радиус отряда: если рядом кто-то начал
            // стрелять, подтягиваются ВСЕ, кто слышит — а не только соседи по строю.
            float radius = Mathf.Max(spotter.squadAwarenessRadius, spotter.contactAlertRadius);
            Vector3 origin = spotter.transform.position;

            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == spotter) continue;
                if (!other.IsSquadAlive || other.SquadTeam != spotter.SquadTeam) continue;
                if (other.State == EnemyState.Dead || other.State == EnemyState.Combat) continue;
                if (FlatDistance(origin, other.transform.position) > radius) continue;

                other.ReceiveContact(target, point);
            }
        }

        /// <summary>
        /// Услышали стрельбу СВОЕГО. Значит рядом бой — идём на звук, даже если
        /// ничего не видно. Так подтягиваются солдаты, стоящие далеко.
        /// </summary>
        private void OnAllyGunfire(CharacterHealth allyHealth, Vector3 noisePosition)
        {
            if (allyHealth == null || allyHealth == _health) return;

            float distance = Vector3.Distance(noisePosition, transform.position);
            if (distance > allyGunshotRadius) return;

            // У кого-то из своих есть цель — бежим к ней.
            EnemyAI ally = allyHealth.GetComponent<EnemyAI>() ??
                allyHealth.GetComponentInParent<EnemyAI>();
            if (ally != null && ally.State == EnemyState.Combat && ally.CurrentTarget != null)
            {
                ReceiveContact(ally.CurrentTarget, ally.CurrentTarget.position);
                return;
            }

            // Иначе просто идём посмотреть, откуда стрельба.
            if (_target != null) return;
            _awareness = Mathf.Max(_awareness, 0.6f);
            _suspicionPoint = noisePosition;
            _searchCenter = noisePosition;
            _hasSuspicion = true;
            if (State is EnemyState.Patrol or EnemyState.Search)
                ChangeState(EnemyState.Alert);
        }

        /// <summary>
        /// Приём контакта от союзника: цель и точка, с задержкой.
        /// Контакт принимают все, кроме тех, кто УЖЕ дерётся с видимой целью:
        /// раньше солдат с любой (даже потерянной) целью игнорировал радио
        /// и продолжал стоять/искать, пока союзники стреляли по игроку.
        /// </summary>
        private void ReceiveContact(Transform target, Vector3 point)
        {
            if (target == null) return;
            if (State == EnemyState.Combat && CanSeeCurrentTarget()) return;

            // Задержка короткая: товарищ уже стреляет — ждать нечего.
            float min = Mathf.Min(contactShareDelay.x, contactShareDelay.y) * contactShareDelayScale;
            float max = Mathf.Max(contactShareDelay.x, contactShareDelay.y) * contactShareDelayScale;
            _pendingContactTime = Time.time + Random.Range(min, max);
            _pendingContactTarget = target;
            _pendingContactPoint = point;
            // Куда идти, если цель пока не видна: точка контакта.
            _lastKnownTargetPosition = point;
        }

        private void UpdatePendingContact()
        {
            if (_pendingContactTime < 0f) return;
            if (Time.time < _pendingContactTime) return;

            _pendingContactTime = float.NegativeInfinity;
            // Пока ждали задержку, цель могла стать видимой — тогда не дёргаем
            // солдата из боя в погоню, он и так уже дерётся.
            if (State == EnemyState.Combat && CanSeeCurrentTarget())
            {
                _pendingContactTarget = null;
                return;
            }
            if (_target == null && _pendingContactTarget != null)
                SetTarget(_pendingContactTarget);

            // Контакт от союзника — почти полная осведомлённость: значит
            // цель уже видна кому-то, и мы идём на неё сразу.
            _awareness = Mathf.Max(_awareness, 0.9f);
            _suspicionPoint = _pendingContactPoint;
            _searchCenter = _pendingContactPoint;
            // Точка, откуда контакт — цель для погони. Без этого солдат бежал
            // в последнюю известную точку (а у нового солдата это вообще ноль).
            _lastKnownTargetPosition = _pendingContactPoint;
            _hasSuspicion = true;
            _pendingContactTarget = null;

            if (voice != null) voice.PlayAlert();

            if (_target == null) ChangeState(EnemyState.Alert);
            else ChangeState(EnemyState.Chase);
        }

        private void ClearTarget(string reason = null)
        {
            if (DiagEnabled && !string.IsNullOrEmpty(reason))
                _lastTargetLossReason = reason;
            _target = null;
            _targetHealth = null;
            _targetVelocity = Vector3.zero;
            _prevTargetPosition = Vector3.zero;
            _targetVisibleLastTick = false;
            _targetHiddenSince = float.NegativeInfinity;
            _suddenDelayApplied = false;
            // Сброс LOS-кеша: иначе после потери цели новый таргет
            // 0.1-0.2 сек считается невидимым по старому результату.
            _nextLosCheckTime = 0f;
            _cachedCanSee = false;
            _losBlockedByAlly = false;
            if (_health != null && _health.IsAlive) _targetLostTime = Time.time;
        }

        /// <summary>
        /// Видна ли цель СЕЙЧАС. Результат кешируется на ~0.1 с (со случайным сдвигом
        /// на солдата): раньше проверка шла каждый кадр из Chase/Combat — это 1–2 луча
        /// + OverlapSphere + поиск компонентов на КАЖДОГО солдата каждый кадр.
        /// </summary>
        private bool CanSeeCurrentTarget()
        {
            if (_target == null) return false;

            if (Time.time < _nextLosCheckTime) return _cachedCanSee;
            _nextLosCheckTime = Time.time + _losCheckInterval;

            Vector3 chest = CurrentTargetAimPoint();
            Vector3 toTarget = chest - eyePoint.position;
            if (toTarget.sqrMagnitude > viewDistance * viewDistance)
            {
                _cachedCanSee = false;
                _losBlockedByAlly = false;
                return false;
            }

            bool visible = HasLineOfSight(_target, chest, out _losBlockedByAlly);
            if (!visible)
            {
                bool ally;
                visible = HasLineOfSight(_target, chest + Vector3.up * 0.55f, out ally);
                if (ally) _losBlockedByAlly = true;
            }

            _cachedCanSee = visible;
            if (visible)
            {
                _lastKnownTargetPosition = _target.position;
                _lastTargetSeenTime = Time.time;
                TrackTargetKinematics(_target.position);
            }
            return visible;
        }

        /// <summary>
        /// Точка прицеливания по ТЕКУЩЕЙ цели. Берёт уже кешированный
        /// CharacterHealth, поэтому в горячем пути (LOS-тик 10 Гц + каждый
        /// выстрел) НЕ вызывается GetComponentInParent — он аллоцирует (D5).
        /// </summary>
        private Vector3 CurrentTargetAimPoint()
        {
            if (_target == null) return transform.position;
            if (_targetHealth != null) return GetTargetAimPoint(_targetHealth);
            return GetTargetAimPoint(_target);
        }

        private Vector3 GetTargetAimPoint(Transform target)
        {
            if (target == null) return transform.position;

            CharacterHealth health = target.GetComponentInParent<CharacterHealth>();
            if (health != null) return GetTargetAimPoint(health);

            var col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : target.position + Vector3.up * 1.4f;
        }

        /// <summary>
        /// Точка прицеливания по персонажу (центр груди). Кешируем ССЫЛКИ на
        /// CharacterController/Collider (они стабильны), но не саму точку — персонаж
        /// двигается, точка должна считаться каждый раз заново.
        /// </summary>
        private Vector3 GetTargetAimPoint(CharacterHealth health)
        {
            if (health == null) return transform.position;

            int index = FindAimCacheSlot(health);
            if (index < 0)
            {
                index = _aimCacheCursor;
                _aimCacheCursor = (_aimCacheCursor + 1) % _aimCache.Length;

                Transform root = health.transform;
                _aimCache[index].Health = health;
                _aimCache[index].Controller = root.GetComponentInParent<CharacterController>();
                _aimCache[index].Collider = root.GetComponentInChildren<Collider>();
            }

            ref AimCacheEntry entry = ref _aimCache[index];
            if (entry.Controller != null) return entry.Controller.bounds.center;
            if (entry.Collider != null) return entry.Collider.bounds.center;
            return health.transform.position + Vector3.up * 1.4f;
        }

        private int FindAimCacheSlot(CharacterHealth health)
        {
            for (int i = 0; i < _aimCache.Length; i++)
                if (_aimCache[i].Health == health) return i;
            return -1;
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
            // Motor.MoveTo возвращает false, если NavMesh не смог построить путь
            // (точка вне меша / за стеной). В Fallback-режиме мотор возвращает
            // allowFallbackMovement — там считаем приказ выполненным.
            bool moved = _motor.MoveTo(destination);
            _lastMoveSucceeded = moved || _motor.Mode != EnemyMotor.MotorMode.NavMesh;

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

        private void ChangeState(EnemyState newState,
            [System.Runtime.CompilerServices.CallerMemberName] string reason = null)
        {
            if (State == EnemyState.Dead || State == newState) return;
            EnemyState previous = State;
            State = newState;
            if (DiagEnabled) _lastStateChangeReason = reason;
            CancelLookAround();
            _areaSpreadCenter = Vector3.zero;
            _areaSpreadOffset = Vector3.zero;
            _nextAreaSpreadTime = 0f;
            if (newState != EnemyState.Combat)
            {
                _fireAnchorUntil = 0f;
                _repositionUntil = 0f;
                _hasCombatAnchor = false;
                _formationReady = false;
            }
            if (newState == EnemyState.Combat && previous != EnemyState.Combat)
            {
                // Новая схватка (другая цель или долго не дрались) — темп с нуля.
                if (_target == null || _target != _engageTarget ||
                    Time.time - _engageEndTime > engageResetDelay)
                {
                    _engageTarget = _target;
                    _engageStage = 0;
                    _engageOpen = true;
                    _engageStartTime = Time.time;
                    _lastTargetHp = _targetHealth != null ? _targetHealth.CurrentHealth : 0f;
                    // Новая схватка — темп с нуля: базовое здоровье цели = текущее.
                    _engageHpBaseline = _targetHealth != null
                        ? Mathf.Clamp01(_targetHealth.NormalizedHealth)
                        : 1f;
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
            _motor.SetManualRotation(false);
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
                    _motor.SetSpeed(alertSpeed);
                    _motor.SetSprintMode(false);
                    if (previous == EnemyState.Patrol && voice != null) voice.PlayAlert();
                    break;

                case EnemyState.Search:
                    _motor.SetSpeed(patrolSpeed);
                    _motor.Stop();
                    _searchFaceUntil = Time.time + 1.2f;
                    _searchBaseCaptured = false;
                    _searchUntil = Time.time + Mathf.Max(4f, targetMemoryDuration);
                    BuildSearchPoints();
                    break;

                case EnemyState.Chase:
                    _motor.SetSpeed(chaseSpeed);
                    _chaseStuckTime = -1f;
                    if (previous is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                    {
                        if (voice != null) voice.PlaySpotted();
                        // Передаём контакт союзникам: они подтянутся через 0.3–1 с.
                        if (_target != null)
                            NotifyAlliesOfContact(this, _target, _target.position);
                    }
                    break;

                case EnemyState.Combat:
                    _lastVisibleTime = Time.time;
                    if (previous is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                    {
                        if (voice != null) voice.PlaySpotted();
                        // Начали стрелять — поднимаем всех, кто рядом: отряд
                        // подтягивается на игрока, а не топчется по местам.
                        if (_target != null)
                            NotifyAlliesOfContact(this, _target, _target.position);
                    }
                    break;
            }
        }

        private void OnNoiseCreated(NoiseSystem.Noise noise)
        {
            if (_health == null || !_health.IsAlive) return;
            if (State == EnemyState.Dead) return;

            // ВЕРХНЯЯ ГРАНИЦА слышимости: выстрел ИИ = 35*4 = 140 м, *1.5 = 210 м —
            // такой радиус поднимал весь уровень от одного выстрела. Теперь потолок
            // maxHearingDistance (по умолчанию 140 м) решает, а не радиус шума.
            float hearingRadius = Mathf.Min(
                noise.Radius * Mathf.Max(1f, hearingRadiusMultiplier),
                Mathf.Max(1f, maxHearingDistance));
            float distance = Vector3.Distance(noise.Position, transform.position);
            if (distance > hearingRadius) return;

            if (noise.Source != null)
            {
                CharacterHealth srcHealth = noise.Source.GetComponentInParent<CharacterHealth>();
                if (srcHealth == _health) return;
                if (srcHealth != null && srcHealth.Team == _health.Team)
                {
                    // Свой стреляет — рядом бой. Идём на звук, даже если бой
                    // не видно: так далёкие солдаты подтягиваются к файту.
                    // Тихие звуки (шаги, попадания) игнорируем — иначе солдат
                    // бежал бы к ногам соседа.
                    if (noise.Radius >= allyNoiseMinRadius)
                        OnAllyGunfire(srcHealth, noise.Position);
                    return;
                }
            }

            float falloff = 1f - Mathf.Clamp01(distance / hearingRadius);
            float heard = Mathf.Clamp01(falloff * noise.Intensity *
                Mathf.Max(0.1f, hearingSensitivity));
            if (heard < hearingThreshold) return;

            // Ошибка позиции: враг идёт в РАЙОН шума, а не в точку. ~8 % от дистанции.
            Vector3 heardPosition = noise.Position;
            if (hearingPositionErrorRatio > 0f && distance > 1f)
            {
                float error = distance * Mathf.Clamp(hearingPositionErrorRatio, 0f, 0.5f);
                heardPosition += new Vector3(
                    Random.Range(-error, error), 0f, Random.Range(-error, error));
            }

            // Услышали — сразу реагируем: осведомлённость растёт быстро, солдат
            // бежит на звук (Alert) ищет источник.
            _awareness = Mathf.Min(0.95f, _awareness + heard * 0.95f);
            _suspicionPoint = heardPosition;
            _searchCenter = heardPosition;
            _hasSuspicion = true;
            // D5: строка — только для диагностики. Иначе на каждый услышанный
            // шум у каждого солдата идёт аллокация (выстрелы = 8/с на стрелка).
            if (DiagEnabled)
                _lastNoiseInfo = $"шум {distance:F0}м (громк. {heard:P0})";
            if (falloff > 0.5f)
            {
                // Корпус доворачиваем к звуку. Сам поворот ведёт UpdateLookAtSound:
                // если дёрнуть мотор прямо здесь, NavMeshAgent (updateRotation)
                // перезапишет угол после Update — тело дёрнется и не посмотрит
                // в сторону звука.
                _lookAtSoundPoint = heardPosition;
                _lookAtSoundUntil = Time.time + 1.2f;
            }

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
                if (attackerHealth != null && attackerHealth.IsAlive && attackerHealth.Team != OwnTeam)
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

            // Укрытие освобождаем — следующий солдат должен им воспользоваться.
            if (AssignedCover != null)
            {
                AssignedCover.Release(this);
                AssignedCover = null;
            }
            HasFireToken = false;

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
                // Скорость слалась в м/с напрямую — Blend Tree под 0..1 улетал в потолок.
                // EnemyAnimator сам раскладывает м/с по якорям; legacy-путь шлём как есть,
                // но только если параметры реально существуют (иначе был спам ошибок).
                enemyAnimator.SetLocomotion(_motor.Velocity, _motor.WantsToMove, speed);
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private string _overlayLabel;
        private float _nextOverlayBuildTime;

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

                DrawTacticsGizmos();
            }

            if (targetMask == 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            }
        }

        /// <summary>Гизмо тактики: роль, жетон, цель отряда, укрытие, точки поиска.</summary>
        private void DrawTacticsGizmos()
        {
            // Роль: цвет головы.
            Color roleColor = Role switch
            {
                SquadRole.Fire => new Color(1f, 0.25f, 0.2f),
                SquadRole.Maneuver => new Color(1f, 0.85f, 0.2f),
                SquadRole.Cover => new Color(0.3f, 0.8f, 1f),
                SquadRole.Flank => new Color(0.8f, 0.4f, 1f),
                _ => Color.gray
            };
            Gizmos.color = roleColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.1f, 0.12f);

            // Жетон атаки.
            if (HasFireToken)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.3f);
                Gizmos.DrawWireCube(transform.position + Vector3.up * 2.35f, Vector3.one * 0.16f);
            }

            // Цель отряда (куда идёт/куда стреляет).
            if (Role == SquadRole.Maneuver || Role == SquadRole.Flank || Role == SquadRole.Cover)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, SquadGoal);
                Gizmos.DrawWireSphere(SquadGoal, 0.35f);
            }

            // Укрытие.
            if (AssignedCover != null)
            {
                Gizmos.color = new Color(0.2f, 0.7f, 1f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, AssignedCover.Position);
                Gizmos.DrawWireSphere(AssignedCover.Position, 0.5f);
            }

            // Точки поиска.
            for (int i = 0; i < _searchPointTotal; i++)
            {
                Gizmos.color = new Color(0.9f, 0.9f, 0.2f, 0.7f);
                Gizmos.DrawWireSphere(_searchPoints[i], 0.4f);
            }
        }

        /// <summary>Экранная отладка: состояние, роль, жетон, цель.</summary>
        private void OnGUI()
        {
            if (!debugOverlay) return;

            if (Time.unscaledTime >= _nextOverlayBuildTime)
            {
                _nextOverlayBuildTime = Time.unscaledTime + 0.25f;
                _overlayLabel = BuildOverlayLabel();
            }
            if (string.IsNullOrEmpty(_overlayLabel)) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (screen.z <= 0f) return;

            GUI.Label(new Rect(screen.x - 280f, Screen.height - screen.y - 20f, 720f, 20f),
                _overlayLabel);
        }

        private string BuildOverlayLabel()
        {
            string role = Role.ToString();
            string token = HasFireToken ? "жетон" : "—";
            string target = _target != null ? _target.name : "—";
            string notSeen = _lastNotSeenReason ?? "—";
            string loss = _lastTargetLossReason ?? "—";
            string changed = _lastStateChangeReason ?? "—";
            string note = _lastRejectNote ?? "—";

            return $"{State} | aware {_awareness:F2}/1.00 | роль {role} | {token} | " +
                   $"цель {target} | не вижу: {notSeen} | потеря: {loss} | смена: {changed} | " +
                   $"фильтр: {note}";
        }
#endif
    }
}

