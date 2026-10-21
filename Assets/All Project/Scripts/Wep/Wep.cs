using UnityEngine;
using EasyPeasyFirstPersonController;
using System.Collections;

public class Wep : MonoBehaviour
{
    [Header("Характеристики оружия")]
    public int maxAmmo = 71;
    public int currentAmmo;
    public int spareMagazines = 2;
    public float fireRate = 0.066f;
    public float reloadTime = 4.5f;
    public float damage = 25f;

    [Header("Баллистика")]
    [Tooltip("Дальность выстрела в метрах.")]
    public float range = 500f;

    [Tooltip("По каким слоям пуля вообще может попасть. " +
             "Убери отсюда слой BulletHole, чтобы пули не застревали в дырках от других пуль.")]
    public LayerMask hitMask = ~0;

    [Tooltip("Урон при попадании в голову. В остальное тело — обычный damage.")]
    [Min(1f)] public float headshotDamage = 100f;

    [Tooltip("Имена коллайдеров, считающихся головой (регистр не важен).")]
    public string[] headColliderNames = { "head", "golova", "skull" };

    [Tooltip("Высота макушки, считающаяся головой, м. Запасной вариант, когда у цели " +
             "нет отдельного коллайдера головы (капсула CharacterController) — " +
             "головой считается верхняя часть роста.")]
    [Min(0.05f)] public float headZoneHeight = 0.3f;

    [Tooltip("Команда стрелка. Нужна, чтобы враги правильно реагировали на пролёт пули.")]
    public ZharkoeLeto44.AI.Team shooterTeam = ZharkoeLeto44.AI.Team.Allies;

    [Tooltip("Радиус, в котором враги слышат выстрел. 0 — выстрел бесшумный для ИИ.")]
    [Min(0f)] public float shotNoiseRadius = 40f;
    [Tooltip("Множитель радиуса слышимости выстрела.")]
    [Min(1f)] public float shotNoiseMultiplier = 4f;

    [Tooltip("Писать в консоль, во что попала пуля и сколько урона нанесла.")]
    public bool logHits = false;

    [Header("Разброс (динамический)")]
    [Tooltip("Базовый конус разброса в градусах (пуля гуляет ±половина). " +
             "4° — это ±1.75 м на 50 м, поэтому вдаль не летело.")]
    public float baseSpread = 1.5f;
    [Tooltip("Насколько растёт разброс с каждым выстрелом очередью.")]
    public float autoSpreadPerShot = 0.7f;
    [Tooltip("Потолок разброса: длинная очередь всё равно расползается.")]
    public float maxSpread = 8f;
    [Tooltip("Как быстро разброс возвращается к базовому после очереди.")]
    public float spreadRecoverySpeed = 18f;
    private float currentSpread;

    [Header("Прицеливание")]
    public float normalFOV = 60f;
    public float aimFOV = 40f;
    [Tooltip("Во сколько раз прицел режет разброс. 0.25: в прицеле одиночные бьют точно и вдаль.")]
    public float aimSpreadMultiplier = 0.25f;
    public float aimRecoilMultiplier = 0.6f;
    public Vector3 hipPosition = new Vector3(0.458f, -1.01f, 0.73f);
    public Vector3 aimPosition = new Vector3(0.004f, -0.778f, 0.429f);
    public Vector3 hipRotation = new Vector3(0.4f, -90f, -0.771f);
    public Vector3 aimRotation = new Vector3(0.4f, -90.254f, -0.417f);

    [Header("Настройка перехода в прицел")]
    public float aimTransitionSpeed = 6.5f;
    public float aimSmoothing = 0.5f;
    public float aimSpeedWalkMultiplier = 0.6f;
    public float aimSpeedRunMultiplier = 0.3f;
    private bool isAiming = false;
    private float aimTransitionProgress = 0f;
    private float aimBlend = 0f;
    private float fovVelocity = 0f;

    [Header("Отдача (передаётся в контроллер)")]
    public AnimationCurve verticalRecoilCurve = new AnimationCurve(
        new Keyframe(0, 1.0f), new Keyframe(2, 1.4f), new Keyframe(4, 2.0f), new Keyframe(5, 2.8f));
    public AnimationCurve horizontalRecoilCurve = new AnimationCurve(
        new Keyframe(0, 0.25f), new Keyframe(2, 0.35f), new Keyframe(4, 0.5f), new Keyframe(5, 0.7f));
    public float recoilStrengthMultiplier = 0.9f;
    private int consecutiveShots = 0;
    private float lastShotTime;
    private float timeOfLastShot = -10f;

    [Header("Ссылки")]
    public Camera playerCamera;
    public Transform weaponModel;
    public Transform muzzlePoint;
    public ParticleSystem muzzleFlash;
    public GameObject[] bulletHolePrefabs;
    public LineRenderer bulletTrailPrefab;
    public GameObject crosshairObject;
    public bool preventOverlappingHoles = false;
    public float holeMinDistance = 0.05f;
    public LayerMask holeCheckMask = ~0;

    [Header("Состояния движения (авто из контроллера)")]
    public bool isCrouching = false;
    public bool isRunning = false;
    public bool isGrounded = true;

    [Header("Анимации оружия (реалистичные)")]
    public float swayAmount = 0.04f;
    public float swaySmoothness = 8f;
    public float moveSwayMultiplier = 1.0f;
    public float idleSwaySpeed = 1.2f;
    public float idleSwayAmount = 0.015f;

    public float bobAmplitudeWalk = 0.04f;
    public float bobAmplitudeRun = 0.07f;
    public float bobFrequencyWalk = 1.8f;
    public float bobFrequencyRun = 2.2f;
    public float bobSmoothTime = 0.12f;
    public float bobDistanceSmoothTime = 0.05f;

    public float bobVerticalMultiplier = 0.8f;
    public float bobHorizontalMultiplier = 0.6f;
    public float bobForwardMultiplier = 0.7f;
    public float bobRotationMultiplier = 0.5f;

    public float jumpBobOffsetY = -0.08f;
    public float jumpBobOffsetZ = 0.05f;
    public float crouchPositionOffsetY = -0.06f;
    public float crouchPositionOffsetZ = 0.03f;
    public float crouchRotationOffsetX = 5f;

    public float shootShakeAmount = 0.002f;
    public float shootShakeSpeed = 30f;
    private float shootShakeTimer = 0f;

    public float kickbackPositionY = 0.03f;
    public float kickbackPositionZ = -0.09f;
    public float kickbackRotationX = -6f;
    public float kickbackRotationY = 1.0f;

    public float breathAmplitude = 0.004f;
    public float breathSpeed = 1.0f;
    private float breathTimer = 0f;

    [Header("Появление в руках (equip)")]
    [Tooltip("Плавное появление оружия при взятии в руки / переключении. " +
             "Выключено — оружие появляется мгновенно, как раньше.")]
    public bool enableEquipAnimation = true;

    [Tooltip("Сколько секунд оружие поднимается в руки (с пояса, из-за нижнего края экрана). В это время нельзя стрелять — как draw time в шутерах.")]
    [Min(0.05f)] public float equipDuration = 0.65f;

    [Tooltip("Откуда поднимается: смещение от позы в руках. Глубоко снизу из-за кадра — как с пояса.")]
    public Vector3 equipFromOffset = new Vector3(0.04f, -0.58f, -0.16f);

    [Tooltip("Стартовый наклон при появлении, градусы. Добавляется к позе в руках.")]
    public Vector3 equipFromRotation = new Vector3(-35f, 12f, 8f);

    [Tooltip("Сочность: перелёт в конце подъёма. 0 — максимально плавно, больше — резче.")]
    [Range(0f, 3f)] public float equipOvershoot = 0.8f;

    [Tooltip("Дуга подъёма: выгиб траектории в середине (вперёд и чуть вбок), " +
             "чтобы оружие выходило с пояса по дуге, а не ехало по прямой как лифт.")]
    public Vector3 equipArc = new Vector3(0.02f, 0f, 0.08f);

    [Tooltip("Какая доля подъёма уходит на выпрямление наклона. " +
             "Поворот заканчивается раньше позиции — как живая кисть: сначала доворот, потом доводка.")]
    [Range(0.3f, 1f)] public float rotationLead = 0.55f;

    [Header("Убирание из рук (holster)")]
    [Tooltip("Сколько секунд оружие опускается перед выключением. " +
             "Управляется WeaponSlotManager: он ждёт это время, потом прячет объект.")]
    [Min(0.05f)] public float holsterDuration = 0.32f;

    [Tooltip("Куда опускается: смещение от позы в руках. Глубоко вниз за край экрана — как на пояс.")]
    public Vector3 holsterToOffset = new Vector3(0f, -0.52f, -0.12f);

    [Tooltip("Наклон при убирании, градусы.")]
    public Vector3 holsterToRotation = new Vector3(-25f, 8f, 6f);

    private Vector2 mouseDelta;
    private Vector3 inertiaPosition = Vector3.zero;
    private Vector3 inertiaRotation = Vector3.zero;
    public float inertiaSmoothness = 0.08f;
    public float inertiaMultiplier = 0.02f;

    private float landingSpring = 0f;
    private float landingSpringVelocity = 0f;
    public float landingSpringStiffness = 80f;
    public float landingSpringDamping = 10f;

    public AnimationCurve reloadCurvePositionY = new AnimationCurve(
        new Keyframe(0, 0), new Keyframe(0.2f, -0.08f), new Keyframe(0.8f, -0.1f), new Keyframe(1, 0));
    public AnimationCurve reloadCurveRotationX = new AnimationCurve(
        new Keyframe(0, 0), new Keyframe(0.3f, -10f), new Keyframe(0.7f, -15f), new Keyframe(1, 0));
    private float reloadProgress = 0f;
    private bool isReloading = false;
    public bool IsReloading => isReloading;

    [Header("Отладочный HUD")]
    [Tooltip("Старый вывод патронов через OnGUI. IMGUI рисуется поверх любого Canvas, " +
             "поэтому при включённом WeaponHudUI его надо выключить — иначе тексты наложатся.")]
    public bool drawDebugGUI = true;

    // Запас магазинов зеркалит инвентарь
    /// <summary>Запас магазинов изменился.</summary>
    public event System.Action<int> OnMagazinesChanged;
    /// <summary>Патроны изменились: (в магазине, всего).</summary>
    public event System.Action<int, int> OnAmmoChanged;
    /// <summary>Потратить один магазин при перезарядке — инвентарь уберёт предмет.</summary>
    public void ConsumeMagazine()
    {
        spareMagazines = Mathf.Max(0, spareMagazines - 1);
        OnMagazinesChanged?.Invoke(spareMagazines);
    }
    /// <summary>Выставить запас напрямую (синхронизация с инвентарём).</summary>
    public void SetSpareMagazines(int count)
    {
        int clamped = Mathf.Max(0, count);
        if (clamped == spareMagazines) return;

        spareMagazines = clamped;
        OnMagazinesChanged?.Invoke(spareMagazines);
    }

    /// <summary>Добавить магазины (подбор).</summary>
    public void AddMagazines(int count)
    {
        if (count <= 0) return;
        spareMagazines += count;
        OnMagazinesChanged?.Invoke(spareMagazines);
    }

    [Header("Звуки")]
    public AudioSource audioSource;
    public AudioClip shootSound;
    public AudioClip reloadSound;
    public AudioClip boltSound;
    public AudioClip magazineReleaseSound;
    public AudioClip magazineDropSound;
    public AudioClip magazineSlideSound;
    public AudioClip magazineSnapSound;
    public GameObject shellPrefab;
    public Transform shellEjectPoint;
    public float shellEjectForce = 3f;
    public float shellLifetime = 3f;

    private Vector3 swayPositionOffset = Vector3.zero;
    private Vector3 swayRotationOffset = Vector3.zero;
    private Vector3 bobPosition = Vector3.zero;
    private Vector3 bobRotation = Vector3.zero;
    private Vector3 bobVelocity = Vector3.zero;
    private Vector3 bobRotVelocity = Vector3.zero;
    private float idleSwayTimer = 0f;
    private float bobTime = 0f;
    private Vector3 lastPosition;

    private Vector3 recoilKickPosition = Vector3.zero;
    private Vector3 recoilKickRotation = Vector3.zero;

    private Vector3 smoothMoveOffset;
    private Vector3 smoothMoveRotation;
    private Vector3 moveOffsetVelocity;
    private Vector3 moveRotVelocity;

    [Header("Режимы огня")]
    public FireMode currentFireMode = FireMode.Auto;
    private FirstPersonController fpsController;
    private Coroutine reloadRoutine;
    private bool initFailed = false;
    [Header("Кинематографичная перезарядка")]
    public bool useCinematicReload = true;
    public Vector3 reloadStartPosition = new Vector3(0.562f, -0.826f, 1.049f);
    public Vector3 reloadStartRotation = new Vector3(-9.881f, -131.71f, 8.691f);
    public Vector3 reloadBoltPullPosition = new Vector3(1.03f, -0.178f, 1.142f);
    public Vector3 reloadBoltPullRotation = new Vector3(64.73f, 31.196f, 135.383f);
    public float closeUpTransitionTime = 0.4f;
    public float boltPullTransitionTime = 0.5f;

    [Tooltip("Старый магазин (Circle.004)")]
    public Transform oldMagazine;
    [Tooltip("Новый магазин (Circle.005)")]
    public Transform newMagazine;

    public Vector3 newMagazineInsertLocalPos = new Vector3(0.9392396f, 1.30955f, -2.842171e-16f);
    public Vector3 newMagazineInsertLocalRot = new Vector3(0f, 0f, 5.2f);

    [Header("Вставка магазина")]
    public Vector3 newMagazineStartOffset = new Vector3(0f, -1.2f, 0.5f);
    public Vector3 newMagazineStartRotationOffset = new Vector3(35f, 0f, 15f);
    public float newMagazineAppearDelay = 0.5f;
    public float newMagazineMoveTime = 0.9f;
    public AnimationCurve newMagazineInsertCurve = new AnimationCurve(
        new Keyframe(0, 0, 0, 0.5f),
        new Keyframe(0.6f, 0.7f, 1.5f, 0),
        new Keyframe(1, 1, -0.3f, 0)
    );
    public float newMagazinePreInsertAmount = 0.97f;
    public float newMagazineSnapPause = 0.12f;
    public float newMagazineSnapSpeed = 2.5f;

    [Header("Инерция оружия при вставке")]
    public Vector3 newMagInertiaOffsetPos = new Vector3(-0.12f, 0.07f, -0.18f);
    public Vector3 newMagInertiaOffsetRot = new Vector3(-12f, 5f, 0f);
    public float inertiaDuration = 0.35f;

    [Header("Рычажок (затворная задержка)")]
    public Transform lever;
    public float leverMoveOffsetX = 0.1f;
    public float leverMoveTime = 0.15f;
    private Vector3 leverOriginalLocalPos;

    [Header("Затвор")]
    public Transform bolt;
    public Vector3 boltReloadPullPosition = new Vector3(0.081f, 1.907f, 0f);
    public Vector3 boltFireOffset = new Vector3(0f, 0f, -0.03f);
    public float boltFireReturnTime = 0.1f;
    private Vector3 boltOriginalLocalPos;
    private Quaternion boltOriginalLocalRot;

    [Header("Выпадение магазина")]
    public float magazineDropHeight = 2.2f;
    public float magazineDropTime = 0.8f;
    public float magazineDropRotationZ = 40f;

    [Header("Инерция оружия (выпадение)")]
    public Vector3 oldMagInertiaOffsetPos = new Vector3(0.04f, 0.02f, 0.06f);
    public Vector3 oldMagInertiaOffsetRot = new Vector3(4f, -2f, 0f);

    [Header("Инерция затвора")]
    public Vector3 boltPullInertiaOffsetPos = new Vector3(0.05f, 0.02f, 0.06f);
    public Vector3 boltPullInertiaOffsetRot = new Vector3(3f, 0f, 0f);
    public float boltPullInertiaDuration = 0.2f;

    [Header("Эффекты камеры")]
    public float reloadFOVReduction = 4f;
    public float fovTransitionTime = 0.25f;
    public float cameraShakeIntensityInsert = 0.02f;
    public float cameraShakeIntensityBolt = 0.03f;
    public float cameraShakeDuration = 0.15f;

    [Header("Наклон оружия при вставке")]
    public Vector3 insertTiltRotation = new Vector3(0f, 0f, 7f);
    public float insertTiltTime = 0.2f;

    [Header("Микро-паузы")]
    public float pauseBeforeMagazineDrop = 0.15f;
    public float pauseBeforeMagazineInsert = 0.25f;
    public float pauseBeforeBoltPull = 0.15f;

    [Header("Визуальные эффекты затвора")]
    public ParticleSystem boltParticles;
    public Light boltLight;
    public bool useBoltFOVKick = true;
    public float boltFOVKickAmount = 6f;
    public float boltFOVKickDuration = 0.08f;

    [Header("Задержки звуков")]
    public float magazineReleaseSoundDelay = 0f;
    public float magazineDropSoundDelay = 0.6f;
    public float magazineSlideSoundDelay = 0f;
    public float magazineSnapSoundDelay = 0f;
    public float boltSoundDelay = 0f;

    // === СИСТЕМА ОСМОТРА ОРУЖИЯ ===
    [Header("Осмотр оружия (Weapon Inspect)")]
    public KeyCode inspectKey = KeyCode.B;
    public bool enableInspect = true;
    public Vector3 inspectStage1Position = new Vector3(0.562f, -0.826f, 1.049f);
    public Vector3 inspectStage1Rotation = new Vector3(-9.881f, -131.71f, 8.691f);
    public float stage1TransitionTime = 0.6f;
    public float stage1HoldTime = 1.5f;
    public Vector3 inspectStage2Position = new Vector3(0.5f, -0.8f, 1.0f);
    public Vector3 inspectStage2Rotation = new Vector3(-5f, -110f, 10f);
    public float stage2TransitionTime = 0.8f;
    public float stage2HoldTime = 1.2f;
    public Vector3 inspectStage3Position = new Vector3(0.55f, -0.78f, 0.95f);
    public Vector3 inspectStage3Rotation = new Vector3(-10f, -150f, -5f);
    public float stage3TransitionTime = 0.8f;
    public float stage3HoldTime = 1.2f;
    public Vector3 inspectStage4Position = new Vector3(0.53f, -0.85f, 1.02f);
    public Vector3 inspectStage4Rotation = new Vector3(-20f, -130f, 0f);
    public float stage4TransitionTime = 0.7f;
    public float stage4HoldTime = 1.5f;
    public float returnTransitionTime = 0.9f;
    public float inspectFOVChange = -5f;
    public float inspectFOVSpeed = 3f;
    public AudioClip inspectSound1;
    public AudioClip inspectSound2;
    public AudioClip inspectSound3;
    [Range(0f, 1f)]
    public float inspectSoundVolume = 0.7f;
    public bool interruptOnShoot = true;
    public bool interruptOnReload = true;
    public bool interruptOnRun = true;

    [Header("Эффекты попадания")]
    [Tooltip("Правила спавна эффектов в точке попадания пули. " +
             "Проверяются по очереди: сначала тегом поверхности, затем физическим материалом. " +
             "Первое совпадение используется. Если ничего не совпало — берётся Default Impact Prefab.")]
    public ImpactEffectEntry[] impactEffects;
    [Tooltip("Префаб по умолчанию, если поверхность не совпала ни с одним правилом. " +
             "Пусто — эффект не спавнится.")]
    public GameObject defaultImpactPrefab;
    [Tooltip("Смещение эффекта от поверхности, чтобы он не утонул в геометрии.")]
    public float impactOffset = 0.03f;
    [Tooltip("Время жизни эффекта попадания (сек).")]
    public float impactLifetime = 3f;

    [Header("Попадание по взрывчатке")]
    [Tooltip("Радиус помощи попадания по гранате, метры. Луч считается попавшим, " +
             "даже если прошёл рядом с мелкой целью — прощает промахи на дистанции. " +
             "На стены, врагов и дырки не влияет.")]
    [Range(0f, 1f)] public float explosiveHitAssist = 0.35f;

    private bool isInspecting = false;
    private Coroutine inspectRoutine;
    private bool isCinematicReload = false;

    // Плавное появление/убирание: считается по времени в Update,
    // поэтому не ломается при выключении объекта и не спорит с отдачей/покачиванием.
    private float equipProgress = 1f;
    private bool isHolstering;
    private float holsterProgress;
    private bool started;

    /// <summary>Оружие сейчас поднимается в руки (первые доли секунды после экипировки).</summary>
    public bool IsEquipping => enableEquipAnimation && equipProgress < 1f;

    /// <summary>Оружие сейчас опускается (WeaponSlotManager вот-вот его спрячет).</summary>
    public bool IsHolstering => isHolstering;

    /// <summary>Начать опускание перед убиранием. Менеджер ждёт holsterDuration и прячет объект.</summary>
    public void BeginHolster()
    {
        if (!enableEquipAnimation) return;
        isHolstering = true;
        holsterProgress = 0f;
        equipProgress = 1f;
    }

    /// <summary>Отменить опускание (быстро переключились обратно) и проиграть подъём заново.</summary>
    public void ReplayEquipAnimation()
    {
        isHolstering = false;
        holsterProgress = 0f;
        equipProgress = enableEquipAnimation ? 0f : 1f;
        if (enableEquipAnimation) SnapToEquipStart();
    }

    /// <summary>Мгновенно поставить модель в стартовую позу подъёма (пояс).</summary>
    void SnapToEquipStart()
    {
        if (weaponModel == null) weaponModel = transform;
        if (weaponModel == null) return;
        weaponModel.localPosition = hipPosition + equipFromOffset;
        weaponModel.localRotation = Quaternion.Euler(hipRotation + equipFromRotation);
    }

    void UpdateEquipTimers()
    {
        if (enableEquipAnimation && equipProgress < 1f)
            equipProgress = Mathf.Min(1f, equipProgress + Time.deltaTime / Mathf.Max(0.05f, equipDuration));
        if (isHolstering)
            holsterProgress = Mathf.Min(1f, holsterProgress + Time.deltaTime / Mathf.Max(0.05f, holsterDuration));
    }
    private Vector3 oldMagOriginalLocalPos;
    private Quaternion oldMagOriginalLocalRot;
    private Vector3 newMagOriginalLocalPos;
    private Quaternion newMagOriginalLocalRot;

    public enum FireMode { Semi, Auto }

    [System.Serializable]
    public class ImpactEffectEntry
    {
        [Tooltip("Тег поверхности (например Metal, Wood, Concrete). " +
                 "Если заполнен — проверяется только тег, материал игнорируется.")]
        public string surfaceTag;

        [Tooltip("Физический материал поверхности. Проверяется, если surfaceTag пуст.")]
        public PhysicMaterial surfaceMaterial;

        [Tooltip("Префаб эффекта (Particle System, Decal и т.д.).")]
        public GameObject effectPrefab;

        [Tooltip("Опциональный звук попадания в эту поверхность.")]
        public AudioClip impactSound;
    }

    void Start()
    {
        currentFireMode = FireMode.Auto;
        currentAmmo = maxAmmo;
        currentSpread = baseSpread;
        lastShotTime = -fireRate;

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null) playerCamera = GetComponentInParent<Camera>();
            if (playerCamera == null) playerCamera = FindObjectOfType<Camera>();
        }
        if (playerCamera == null)
        {
            Debug.LogError("[Gun] Камера не найдена!");
            initFailed = true;
            enabled = false;
            return;
        }

        if (weaponModel == null) weaponModel = transform;

        fpsController = FindObjectOfType<FirstPersonController>();
        if (fpsController == null)
            Debug.LogWarning("[Gun] FirstPersonController не найден. Отдача не будет работать.");

        ExcludeSelfFromHitMask();

        if (bolt != null && bolt.parent != weaponModel) bolt.SetParent(weaponModel, true);
        if (lever != null && lever.parent != weaponModel) lever.SetParent(weaponModel, true);
        if (oldMagazine != null && oldMagazine.parent != weaponModel) oldMagazine.SetParent(weaponModel, true);
        if (newMagazine != null && newMagazine.parent != weaponModel) newMagazine.SetParent(weaponModel, true);

        if (muzzlePoint == null)
        {
            GameObject mp = new GameObject("MuzzlePoint");
            mp.transform.SetParent(weaponModel, false);
            mp.transform.localPosition = new Vector3(0, 0, 0.5f);
            muzzlePoint = mp.transform;
        }

        if (shellEjectPoint == null)
        {
            GameObject sp = new GameObject("ShellEjectPoint");
            sp.transform.SetParent(weaponModel, false);
            sp.transform.localPosition = new Vector3(0.02f, -0.02f, 0.2f);
            shellEjectPoint = sp.transform;
        }

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        weaponModel.localPosition = hipPosition;
        weaponModel.localRotation = Quaternion.Euler(hipRotation);

        // Start идёт после OnEnable и только что вернул модель в хип.
        // Если подъём уже запрошен — сразу ставим её обратно на пояс,
        // иначе в первые кадры будет видно нырок вниз перед подъёмом.
        if (enableEquipAnimation && equipProgress < 1f) SnapToEquipStart();

        lastPosition = transform.position;
        bobTime = 0f;

        if (oldMagazine != null)
        {
            oldMagOriginalLocalPos = oldMagazine.localPosition;
            oldMagOriginalLocalRot = oldMagazine.localRotation;
        }
        if (newMagazine != null)
        {
            newMagOriginalLocalPos = newMagazine.localPosition;
            newMagOriginalLocalRot = newMagazine.localRotation;
            if (newMagazineInsertLocalPos == Vector3.zero)
                newMagazineInsertLocalPos = newMagOriginalLocalPos;
            if (newMagazineInsertLocalRot == Vector3.zero)
                newMagazineInsertLocalRot = newMagOriginalLocalRot.eulerAngles;
            newMagazine.gameObject.SetActive(false);
        }
        if (lever != null)
            leverOriginalLocalPos = lever.localPosition;
        if (bolt != null)
        {
            boltOriginalLocalPos = bolt.localPosition;
            boltOriginalLocalRot = bolt.localRotation;
        }

        if (oldMagazine != null) oldMagazine.gameObject.SetActive(true);
        if (boltLight != null) boltLight.enabled = false;
        started = true;
    }

    void OnEnable()
    {
        // Нож/граната гасят прицел — при возврате ППШ включаем обратно
        if (crosshairObject != null) crosshairObject.SetActive(true);
        // Каждое включение (подбор, переключение, старт сцены) — красивый подъём в руки.
        isHolstering = false;
        holsterProgress = 0f;
        equipProgress = enableEquipAnimation ? 0f : 1f;
        // Хвост отдачи с прошлого раза ни к чему — иначе первый кадр дёрнется.
        recoilKickPosition = Vector3.zero;
        recoilKickRotation = Vector3.zero;
        // Стартуем кадр сразу с пояса: иначе модель долетит туда из старой позы
        // и будет видно, как оружие сначала дёргается назад, а потом идёт вверх.
        if (enableEquipAnimation) SnapToEquipStart();
    }
    void Update()
    {
        if (initFailed || playerCamera == null || weaponModel == null) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;
        // Замок вместо enabled=false, чтобы не конфликтовать со сменой оружия
        if (PlayerInputLock.WeaponsLocked)
        {
            if (crosshairObject != null) crosshairObject.SetActive(false);
            return;
        }

        if (fpsController != null)
            SyncFromController();

        UpdateEquipTimers();

        if (isInspecting)
        {
            if (interruptOnShoot && Input.GetMouseButtonDown(0)) StopInspect();
            else if (interruptOnReload && Input.GetKeyDown(KeyCode.R)) StopInspect();
            else if (interruptOnRun && isRunning) StopInspect();
        }

        if (enableInspect && Input.GetKeyDown(inspectKey) && !isReloading && !isCinematicReload
            && !IsEquipping && !isHolstering)
        {
            if (!isInspecting) StartInspect();
            else StopInspect();
        }

        isAiming = Input.GetMouseButton(1);
        if (crosshairObject != null)
            crosshairObject.SetActive(!isAiming && !isInspecting);

        if (!isInspecting && !isCinematicReload)
        {
            float currentAimSpeed = aimTransitionSpeed;
            if (isRunning) currentAimSpeed *= aimSpeedRunMultiplier;
            else if (IsMoving()) currentAimSpeed *= aimSpeedWalkMultiplier;

            if (isAiming) aimTransitionProgress = Mathf.MoveTowards(aimTransitionProgress, 1f, Time.deltaTime * currentAimSpeed);
            else aimTransitionProgress = Mathf.MoveTowards(aimTransitionProgress, 0f, Time.deltaTime * currentAimSpeed);

            aimBlend = Mathf.SmoothStep(0f, 1f, aimTransitionProgress);
        }

        float targetFOV = isAiming ? aimFOV : normalFOV;
        if (isInspecting) targetFOV += inspectFOVChange;
        playerCamera.fieldOfView = Mathf.SmoothDamp(playerCamera.fieldOfView, targetFOV, ref fovVelocity, 0.12f);

        if (Time.time - timeOfLastShot > 0.3f)
            consecutiveShots = 0;

        if (!isCinematicReload && !isInspecting)
        {
            UpdateIdleSway();
            UpdateBob();
            UpdateSway();
            UpdateBreathing();
            UpdateLandingSpring();
            UpdateShootShake();
        }
        else if (isInspecting)
        {
            swayPositionOffset = Vector3.zero;
            swayRotationOffset = Vector3.zero;
            bobPosition = Vector3.zero;
            bobRotation = Vector3.zero;
            recoilKickPosition = Vector3.Lerp(recoilKickPosition, Vector3.zero, 1 - Mathf.Exp(-25f * Time.deltaTime));
            recoilKickRotation = Vector3.Lerp(recoilKickRotation, Vector3.zero, 1 - Mathf.Exp(-25f * Time.deltaTime));
        }
        else
        {
            swayPositionOffset = Vector3.zero;
            swayRotationOffset = Vector3.zero;
            bobPosition = Vector3.zero;
            bobRotation = Vector3.zero;
            recoilKickPosition = Vector3.Lerp(recoilKickPosition, Vector3.zero, 1 - Mathf.Exp(-25f * Time.deltaTime));
            recoilKickRotation = Vector3.Lerp(recoilKickRotation, Vector3.zero, 1 - Mathf.Exp(-25f * Time.deltaTime));
        }

        if (!isInspecting)
        {
            Vector3 basePos;
            Vector3 baseRot;
            if (!isCinematicReload)
            {
                basePos = Vector3.Lerp(hipPosition, aimPosition, aimBlend);
                baseRot = Vector3.Lerp(hipRotation, aimRotation, aimBlend);

                // Двухфазный подъём: кисть выпрямляется первой (поворот — быстро и без перелёта),
                // затем рука доводит оружие по дуге с мягким дотягом в конце. Складывается с позой,
                // поэтому не спорит с отдачей, дыханием и покачиванием.
                if (enableEquipAnimation)
                {
                    if (equipProgress < 1f)
                    {
                        float rotK = WeaponEquipEases.EaseOutCubic(Mathf.Clamp01(equipProgress / Mathf.Max(0.3f, rotationLead)));
                        float posK = WeaponEquipEases.EaseOutBack(equipProgress, equipOvershoot);
                        float arcK = Mathf.Sin(Mathf.Clamp01(equipProgress) * Mathf.PI);
                        basePos += Vector3.Lerp(equipFromOffset, Vector3.zero, posK) + equipArc * arcK;
                        baseRot += Vector3.Lerp(equipFromRotation, Vector3.zero, rotK);
                    }
                    else if (isHolstering)
                    {
                        float h = WeaponEquipEases.EaseInQuad(holsterProgress);
                        basePos += Vector3.Lerp(Vector3.zero, holsterToOffset, h);
                        baseRot += Vector3.Lerp(Vector3.zero, holsterToRotation, h);
                    }
                }
            }
            else
            {
                basePos = reloadStartPosition;
                baseRot = reloadStartRotation;
            }

            bool isMovingNow = IsMoving();
            float stabilityFactor = 1f - aimBlend * (isMovingNow ? 0.75f : 0.85f);
            // Покачивание гаснет плавно вместе с анимацией — иначе в конце подъёма виден щелчок.
            if (enableEquipAnimation)
            {
                if (equipProgress < 1f)
                    stabilityFactor *= Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, equipProgress));
                else if (isHolstering)
                    stabilityFactor *= Mathf.Lerp(1f, 0.25f, Mathf.SmoothStep(0f, 1f, holsterProgress));
            }

            Vector3 offsetPos = (swayPositionOffset + bobPosition + recoilKickPosition) * stabilityFactor;
            Vector3 offsetRot = (swayRotationOffset + bobRotation + recoilKickRotation) * stabilityFactor;

            offsetPos += new Vector3(0, reloadCurvePositionY.Evaluate(reloadProgress), 0);
            offsetRot += new Vector3(reloadCurveRotationX.Evaluate(reloadProgress), 0, 0);

            if (!isCinematicReload)
            {
                offsetPos += new Vector3(0, breathAmplitude * Mathf.Sin(breathTimer), 0);
                offsetPos += new Vector3(0, -landingSpring * 0.08f, landingSpring * 0.04f);
                offsetRot += new Vector3(-landingSpring * 6f, 0, 0);

                if (shootShakeTimer > 0)
                {
                    float shakeAmount = shootShakeAmount * (shootShakeTimer / 0.08f);
                    offsetPos += new Vector3(
                        Mathf.Sin(Time.time * shootShakeSpeed) * shakeAmount,
                        Mathf.Cos(Time.time * shootShakeSpeed * 1.3f) * shakeAmount,
                        0);
                    offsetRot += new Vector3(
                        Mathf.Cos(Time.time * shootShakeSpeed * 0.9f) * shakeAmount * 10f,
                        Mathf.Sin(Time.time * shootShakeSpeed * 1.1f) * shakeAmount * 10f,
                        0);
                }
            }

            Vector3 targetPos = basePos + offsetPos;
            Quaternion targetRot = Quaternion.Euler(baseRot) * Quaternion.Euler(offsetRot);

            if (!isCinematicReload)
            {
                float lerpFactor = 1f - Mathf.Exp(-12f * Time.deltaTime);
                weaponModel.localPosition = Vector3.Lerp(weaponModel.localPosition, targetPos, lerpFactor);
                weaponModel.localRotation = Quaternion.Slerp(weaponModel.localRotation, targetRot, lerpFactor);
            }

            recoilKickPosition = Vector3.Lerp(recoilKickPosition, Vector3.zero, 1 - Mathf.Exp(-25f * Time.deltaTime));
            recoilKickRotation = Vector3.Lerp(recoilKickRotation, Vector3.zero, 1 - Mathf.Exp(-25f * Time.deltaTime));

            bool fireHeld = Input.GetMouseButton(0);
            bool fireDown = Input.GetMouseButtonDown(0);

            if (!isReloading && !IsEquipping && !isHolstering && Time.time - lastShotTime >= fireRate)
            {
                if (currentFireMode == FireMode.Auto && fireHeld) Shoot();
                else if (currentFireMode == FireMode.Semi && fireDown) Shoot();
            }

            if (Input.GetKeyDown(KeyCode.R) && !isReloading && !IsEquipping && !isHolstering
                && currentAmmo < maxAmmo && spareMagazines > 0)
            {
                StartCoroutine(ReloadSequence());
            }

            if (Input.GetKeyDown(KeyCode.V))
                currentFireMode = (currentFireMode == FireMode.Auto) ? FireMode.Semi : FireMode.Auto;

            if (Time.time - lastShotTime > fireRate * 2f)
                currentSpread = Mathf.Lerp(currentSpread, baseSpread, 1 - Mathf.Exp(-spreadRecoverySpeed * Time.deltaTime));
        }
    }

    void Shoot()
    {
        // Дополнительная защита от выстрела во время диалога
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;

        // И от выстрела при открытом инвентаре: Shoot могли вызвать из корутины
        if (PlayerInputLock.WeaponsLocked) return;

        if (currentAmmo <= 0 || muzzlePoint == null || playerCamera == null || isInspecting) return;

        currentAmmo--;
        OnAmmoChanged?.Invoke(currentAmmo, maxAmmo);
        timeOfLastShot = Time.time;
        lastShotTime = Time.time;
        consecutiveShots = Mathf.Clamp(consecutiveShots + 1, 0, 5);

        if (muzzleFlash != null) muzzleFlash.Play();
        if (audioSource != null && shootSound != null) audioSource.PlayOneShot(shootSound);

        if (shellPrefab != null && shellEjectPoint != null)
        {
            GameObject shell = Instantiate(shellPrefab, shellEjectPoint.position, shellEjectPoint.rotation);
            Rigidbody rb = shell.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(shellEjectPoint.right * Random.Range(1f, 2f) * shellEjectForce + shellEjectPoint.up * Random.Range(2f, 4f), ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 5f, ForceMode.Impulse);
            }
            Destroy(shell, shellLifetime);
        }

        shootShakeTimer = 0.08f;

        float recoilMultiplier = 1f;
        float spreadMultiplier = 1f;

        if (isCrouching) { recoilMultiplier *= 0.7f; spreadMultiplier *= 0.7f; }
        if (isRunning) { recoilMultiplier *= 1.6f; spreadMultiplier *= 1.8f; }
        if (!isGrounded) { recoilMultiplier *= 2.0f; spreadMultiplier *= 2.5f; }
        if (isAiming) { recoilMultiplier *= aimRecoilMultiplier; spreadMultiplier *= aimSpreadMultiplier; }

        float vertStrength = verticalRecoilCurve.Evaluate(consecutiveShots) * recoilMultiplier * recoilStrengthMultiplier;
        float horizStrength = horizontalRecoilCurve.Evaluate(consecutiveShots) * recoilMultiplier * recoilStrengthMultiplier;

        float vert = Random.Range(vertStrength * 0.9f, vertStrength * 1.1f);
        float horiz = Random.Range(-horizStrength, horizStrength);

        if (fpsController != null)
            fpsController.AddRecoil(vert, horiz);

        float kickY = kickbackPositionY * recoilMultiplier;
        float kickZ = kickbackPositionZ * recoilMultiplier;
        float kickRotX = kickbackRotationX * recoilMultiplier;
        float kickRotY = Random.Range(-kickbackRotationY, kickbackRotationY) * recoilMultiplier;

        recoilKickPosition += new Vector3(0, Random.Range(kickY * 0.7f, kickY), Random.Range(kickZ * 0.7f, kickZ));
        recoilKickRotation += new Vector3(Random.Range(kickRotX * 0.8f, kickRotX), kickRotY, 0);

        float effectiveSpread = currentSpread * spreadMultiplier;
        Vector3 direction = GetSpreadDirection(effectiveSpread);
        Vector3 rayOrigin = playerCamera.transform.position;
        Vector3 endPoint = rayOrigin + direction * range;
        bool hitSomething = false;
        GameObject shooter = fpsController != null ? fpsController.gameObject : gameObject;

        if (Physics.Raycast(rayOrigin, direction, out RaycastHit hit, range, hitMask,
                            QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            hitSomething = true;

            bool hitLiving = ApplyBulletDamage(hit, direction);

            // Дырка только по неживому: на враге она висела бы поверх модели
            if (!hitLiving && bulletHolePrefabs != null && bulletHolePrefabs.Length > 0 &&
                (!preventOverlappingHoles || CanPlaceHole(hit.point)))
            {
                Quaternion holeRot = Quaternion.FromToRotation(Vector3.forward, hit.normal) * Quaternion.Euler(0, 180, 0);
                GameObject hole = Instantiate(bulletHolePrefabs[Random.Range(0, bulletHolePrefabs.Length)],
                                              hit.point + hit.normal * 0.02f, holeRot);
                hole.tag = "BulletHole";
                hole.transform.SetParent(hit.collider.transform);
            }

            SpawnImpactEffect(hit);
        }

        // Пикапы в мире обычно висят на триггерах — основной луч их не видит.
        // Отдельный проход: граната перехватывает пулю, если она ближе твёрдого попадания.
        CheckTriggerExplosives(rayOrigin, direction, hitSomething ? hit.distance : range,
                               shooter, ref endPoint, ref hitSomething);

        if (bulletTrailPrefab != null)
        {
            LineRenderer trail = Instantiate(bulletTrailPrefab, muzzlePoint.position, Quaternion.identity);
            trail.SetPosition(0, muzzlePoint.position);
            trail.SetPosition(1, endPoint);
            Destroy(trail.gameObject, 0.05f);
        }

        // Враги должны реагировать на пролетевшую пулю (подавление, поиск стрелка)
        // и на звук выстрела, иначе игрок стреляет в полной «тишине» для ИИ.
        var shooterHealth = shooter.GetComponentInParent<ZharkoeLeto44.AI.CharacterHealth>();
        var team = shooterHealth != null ? shooterHealth.Team : shooterTeam;
        ZharkoeLeto44.AI.ProjectilePass.Emit(new ZharkoeLeto44.AI.ProjectilePass.Shot(
            rayOrigin, endPoint, shooter, team, hitSomething));

        if (shotNoiseRadius > 0f)
            ZharkoeLeto44.AI.NoiseSystem.Emit(muzzlePoint.position,
                shotNoiseRadius * shotNoiseMultiplier, shooter, 1f);

        if (currentFireMode == FireMode.Auto)
            currentSpread = Mathf.Min(currentSpread + autoSpreadPerShot * (1 - currentSpread / maxSpread), maxSpread);
        else
            currentSpread = baseSpread;
    }

    // === МЕТОДЫ ОСМОТРА ===
    public void StartInspect()
    {
        if (isInspecting || isReloading || isCinematicReload) return;
        if (inspectRoutine != null) StopCoroutine(inspectRoutine);
        inspectRoutine = StartCoroutine(InspectWeapon());
    }

    public void StopInspect()
    {
        if (inspectRoutine != null)
        {
            StopCoroutine(inspectRoutine);
            inspectRoutine = null;
        }
        StartCoroutine(ReturnToOriginalPosition());
        float targetFOV = isAiming ? aimFOV : normalFOV;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * inspectFOVSpeed);
    }

    private IEnumerator InspectWeapon()
    {
        isInspecting = true;
        Vector3 originalPos = weaponModel.localPosition;
        Quaternion originalRot = weaponModel.localRotation;

        if (inspectSound1 != null && audioSource != null)
            audioSource.PlayOneShot(inspectSound1, inspectSoundVolume);

        yield return StartCoroutine(SmoothTransition(inspectStage1Position, Quaternion.Euler(inspectStage1Rotation), stage1TransitionTime));
        yield return new WaitForSeconds(stage1HoldTime);

        if (inspectSound2 != null && audioSource != null)
            audioSource.PlayOneShot(inspectSound2, inspectSoundVolume);

        yield return StartCoroutine(SmoothTransition(inspectStage2Position, Quaternion.Euler(inspectStage2Rotation), stage2TransitionTime));
        yield return new WaitForSeconds(stage2HoldTime);

        yield return StartCoroutine(SmoothTransition(inspectStage3Position, Quaternion.Euler(inspectStage3Rotation), stage3TransitionTime));
        yield return new WaitForSeconds(stage3HoldTime);

        yield return StartCoroutine(SmoothTransition(inspectStage4Position, Quaternion.Euler(inspectStage4Rotation), stage4TransitionTime));
        yield return new WaitForSeconds(stage4HoldTime);

        if (inspectSound3 != null && audioSource != null)
            audioSource.PlayOneShot(inspectSound3, inspectSoundVolume);

        yield return StartCoroutine(SmoothTransition(originalPos, originalRot, returnTransitionTime));

        weaponModel.localPosition = originalPos;
        weaponModel.localRotation = originalRot;
        isInspecting = false;
        inspectRoutine = null;
    }

    private IEnumerator SmoothTransition(Vector3 targetPos, Quaternion targetRot, float duration)
    {
        float elapsed = 0f;
        Vector3 startPos = weaponModel.localPosition;
        Quaternion startRot = weaponModel.localRotation;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = t * t * (3f - 2f * t);
            weaponModel.localPosition = Vector3.Lerp(startPos, targetPos, smoothT);
            weaponModel.localRotation = Quaternion.Slerp(startRot, targetRot, smoothT);
            yield return null;
        }
        weaponModel.localPosition = targetPos;
        weaponModel.localRotation = targetRot;
    }

    private IEnumerator ReturnToOriginalPosition()
    {
        Vector3 targetPos;
        Quaternion targetRot;
        if (isCinematicReload)
        {
            targetPos = reloadStartPosition;
            targetRot = Quaternion.Euler(reloadStartRotation);
        }
        else
        {
            targetPos = Vector3.Lerp(hipPosition, aimPosition, aimBlend);
            targetRot = Quaternion.Euler(Vector3.Lerp(hipRotation, aimRotation, aimBlend));
        }
        yield return StartCoroutine(SmoothTransition(targetPos, targetRot, 0.3f));
        isInspecting = false;
    }

    // === ОСТАЛЬНЫЕ МЕТОДЫ ===
    bool IsMoving()
    {
        if (fpsController != null && fpsController.characterController != null)
            return fpsController.characterController.velocity.magnitude > 0.5f;
        else
            return Mathf.Abs(Input.GetAxis("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxis("Vertical")) > 0.1f;
    }

    void SyncFromController()
    {
        isGrounded = fpsController.isGrounded;
        isCrouching = fpsController.targetCameraY < fpsController.standingCameraHeight - 0.05f;
        isRunning = isGrounded && fpsController.characterController.velocity.magnitude > fpsController.walkSpeed + 0.5f;
    }

    IEnumerator CameraShake(float duration, float intensity)
    {
        Vector3 originalPos = playerCamera.transform.localPosition;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float x = Random.Range(-1f, 1f) * intensity;
            float y = Random.Range(-1f, 1f) * intensity;
            playerCamera.transform.localPosition = originalPos + new Vector3(x, y, 0);
            yield return null;
        }
        playerCamera.transform.localPosition = originalPos;
    }

    IEnumerator FOVChange(float targetFOV, float duration)
    {
        float startFOV = playerCamera.fieldOfView;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            playerCamera.fieldOfView = Mathf.Lerp(startFOV, targetFOV, t / duration);
            yield return null;
        }
    }

    IEnumerator FOVKick(float amount, float duration)
    {
        float startFOV = playerCamera.fieldOfView;
        float targetFOV = startFOV + amount;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            playerCamera.fieldOfView = Mathf.Lerp(startFOV, targetFOV, t / duration);
            yield return null;
        }
        t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            playerCamera.fieldOfView = Mathf.Lerp(targetFOV, startFOV, t / duration);
            yield return null;
        }
    }

    IEnumerator DisableLightAfterDelay(Light light, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (light != null) light.enabled = false;
    }

    IEnumerator PlayDelayedSound(AudioClip clip, float delay)
    {
        if (delay > 0) yield return new WaitForSeconds(delay);
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }

    void UpdateIdleSway()
    {
        idleSwayTimer += Time.deltaTime * idleSwaySpeed;
        Vector3 idlePos = new Vector3(
            Mathf.Sin(idleSwayTimer * 1.3f) * idleSwayAmount,
            Mathf.Cos(idleSwayTimer * 1.7f) * idleSwayAmount,
            Mathf.Sin(idleSwayTimer * 0.9f) * idleSwayAmount * 0.3f
        );
        Vector3 idleRot = new Vector3(
            Mathf.Cos(idleSwayTimer * 1.5f) * idleSwayAmount * 4f,
            Mathf.Sin(idleSwayTimer * 1.1f) * idleSwayAmount * 3f,
            0
        );
        swayPositionOffset = idlePos;
        swayRotationOffset = idleRot;
    }

    void UpdateBob()
    {
        float speed = 0f;
        if (fpsController != null && fpsController.characterController != null)
        {
            Vector3 horizontalVelocity = fpsController.characterController.velocity;
            horizontalVelocity.y = 0f;
            speed = horizontalVelocity.magnitude;
        }
        else
        {
            float inputX = Input.GetAxis("Horizontal");
            float inputY = Input.GetAxis("Vertical");
            speed = new Vector2(inputX, inputY).magnitude * (isRunning ? 6f : 3f);
        }

        bool isMoving = speed > 0.5f;

        if (isMoving && isGrounded)
        {
            float freq = isRunning ? bobFrequencyRun : bobFrequencyWalk;
            bobTime += Time.deltaTime * freq;
        }

        Vector3 targetBobPos = Vector3.zero;
        Vector3 targetBobRot = Vector3.zero;

        if (isMoving && isGrounded)
        {
            float amp = isRunning ? bobAmplitudeRun : bobAmplitudeWalk;
            float t = bobTime * Mathf.PI * 2f;
            float vertical = Mathf.Sin(t) * amp * bobVerticalMultiplier;
            float horizontal = Mathf.Sin(t * 2f) * amp * bobHorizontalMultiplier;
            float forward = Mathf.Cos(t) * amp * bobForwardMultiplier;
            targetBobPos = new Vector3(horizontal, vertical, forward);
            float rotX = Mathf.Sin(t) * amp * 5f * bobRotationMultiplier;
            float rotZ = Mathf.Sin(t * 2f) * amp * 3f * bobRotationMultiplier;
            float rotY = Mathf.Sin(t) * amp * 2f * bobRotationMultiplier;
            targetBobRot = new Vector3(rotX, rotY, rotZ);
        }
        else if (!isGrounded)
        {
            targetBobPos = new Vector3(0f, jumpBobOffsetY, jumpBobOffsetZ);
            targetBobRot = new Vector3(-5f, 0f, 0f);
        }

        if (isCrouching)
        {
            targetBobPos += new Vector3(0f, crouchPositionOffsetY, crouchPositionOffsetZ);
            targetBobRot += new Vector3(crouchRotationOffsetX, 0f, 0f);
        }

        bobPosition = Vector3.SmoothDamp(bobPosition, targetBobPos, ref bobVelocity, bobSmoothTime);
        bobRotation = Vector3.SmoothDamp(bobRotation, targetBobRot, ref bobRotVelocity, bobSmoothTime);
    }

    void UpdateSway()
    {
        float mouseX = Input.GetAxis("Mouse X") * swayAmount;
        float mouseY = Input.GetAxis("Mouse Y") * swayAmount;
        mouseDelta = Vector2.Lerp(mouseDelta, new Vector2(mouseX, mouseY), inertiaSmoothness);
        inertiaPosition = Vector3.Lerp(inertiaPosition, new Vector3(-mouseDelta.x, -mouseDelta.y, 0), Time.deltaTime * swaySmoothness) * inertiaMultiplier;
        inertiaRotation = Vector3.Lerp(inertiaRotation, new Vector3(mouseDelta.y, mouseDelta.x, 0), Time.deltaTime * swaySmoothness) * inertiaMultiplier * 15f;

        Vector3 targetMoveOffsetPos = Vector3.zero;
        Vector3 targetMoveOffsetRot = Vector3.zero;

        if (fpsController != null && fpsController.characterController != null)
        {
            Vector3 vel = fpsController.characterController.velocity;
            Vector3 localVel = fpsController.transform.InverseTransformDirection(vel);

            float moveX = Mathf.Clamp(localVel.x / fpsController.walkSpeed, -1f, 1f);
            float moveZ = Mathf.Clamp(localVel.z / fpsController.walkSpeed, -1f, 1f);

            targetMoveOffsetPos = new Vector3(
                -moveX * swayAmount * moveSwayMultiplier,
                -moveZ * swayAmount * moveSwayMultiplier * 0.5f,
                moveZ * swayAmount * moveSwayMultiplier * 0.5f
            );
            targetMoveOffsetRot = new Vector3(
                moveZ * swayAmount * moveSwayMultiplier * 5f,
                moveX * swayAmount * moveSwayMultiplier * 5f,
                moveX * swayAmount * moveSwayMultiplier * 3f
            );
        }
        else
        {
            float moveX = Input.GetAxis("Horizontal");
            float moveY = Input.GetAxis("Vertical");
            targetMoveOffsetPos = new Vector3(
                -moveX * swayAmount * moveSwayMultiplier * 0.2f,
                -moveY * swayAmount * moveSwayMultiplier * 0.15f,
                moveY * swayAmount * moveSwayMultiplier * 0.2f
            );
            targetMoveOffsetRot = new Vector3(
                moveY * swayAmount * moveSwayMultiplier * 0.4f,
                moveX * swayAmount * moveSwayMultiplier * 0.4f,
                moveX * swayAmount * moveSwayMultiplier * 0.4f
            ) * 3f;
        }

        smoothMoveOffset = Vector3.SmoothDamp(smoothMoveOffset, targetMoveOffsetPos, ref moveOffsetVelocity, 0.1f);
        smoothMoveRotation = Vector3.SmoothDamp(smoothMoveRotation, targetMoveOffsetRot, ref moveRotVelocity, 0.1f);

        swayPositionOffset += inertiaPosition + smoothMoveOffset;
        swayRotationOffset += inertiaRotation + smoothMoveRotation;
    }

    void UpdateBreathing()
    {
        breathTimer += Time.deltaTime * breathSpeed;
    }

    void UpdateLandingSpring()
    {
        if (!isGrounded)
        {
            landingSpring = 1f;
        }
        else if (landingSpring > 0.01f)
        {
            landingSpringVelocity += (-landingSpringStiffness * landingSpring - landingSpringDamping * landingSpringVelocity) * Time.deltaTime;
            landingSpring += landingSpringVelocity * Time.deltaTime;
            if (landingSpring < 0.01f) landingSpring = 0f;
        }
        else
        {
            landingSpringVelocity = 0f;
            landingSpring = 0f;
        }
    }

    void UpdateShootShake()
    {
        if (shootShakeTimer > 0)
            shootShakeTimer -= Time.deltaTime;
    }

    /// <summary>
    /// Нанести урон тому, во что попала пуля. Возвращает true, если цель живая.
    ///
    /// Все персонажи получают урон через ZharkoeLeto44.AI.IDamageable.
    /// Поиск в родителях поддерживает коллайдеры на дочерних костях.
    /// </summary>
    bool ApplyBulletDamage(RaycastHit hit, Vector3 direction)
    {
        Collider col = hit.collider;
        if (col == null) return false;

        GameObject shooter = fpsController != null ? fpsController.gameObject : gameObject;
        float finalDamage = IsHeadCollider(col, hit.point) ? headshotDamage : damage;

        var aiTarget = col.GetComponentInParent<ZharkoeLeto44.AI.IDamageable>();
        if (aiTarget != null)
        {
            if (!aiTarget.IsAlive) return true;   // труп: дырку на нём всё равно не рисуем

            aiTarget.TakeDamage(new ZharkoeLeto44.AI.DamageInfo(
                finalDamage, hit.point, direction, shooter));

            if (logHits) Debug.Log($"[Gun] Попадание в {col.name}: {finalDamage:0.#} урона.");
            return true;
        }

        // Выстрел по лежащей гранате-пикапу — детонация вместо дырки.
        // Живая брошенная граната обрабатывается выше через IDamageable.
        if (TryDetonateExplosivePickup(col, shooter)) return true;

        // Попали в физический объект — толкаем его, чтобы выстрел ощущался
        Rigidbody body = col.attachedRigidbody;
        if (body != null && !body.isKinematic)
            body.AddForce(direction * finalDamage * 0.35f, ForceMode.Impulse);

        if (logHits) Debug.Log($"[Gun] Попадание в {col.name} (не живое).");
        return false;
    }

    /// <summary>
    /// Лежащая граната-пикап взрывается от пули. Настройки взрыва берутся
    /// с GrenadeItem в руках (там уже настроены урон, радиус, эффекты).
    /// True если это была граната и она сдетонировала.
    /// </summary>
    bool TryDetonateExplosivePickup(Collider col, GameObject shooter)
    {
        Pickup pickup = col.GetComponentInParent<Pickup>();
        if (pickup == null || pickup.item == null) return false;
        if (string.IsNullOrEmpty(pickup.item.equipWeaponId)) return false;
        if (WeaponSlotManager.Instance == null) return false;

        EquippableWeapon eq = WeaponSlotManager.Instance.Find(pickup.item.equipWeaponId);
        GrenadeItem grenade = eq != null ? eq.GetComponentInChildren<GrenadeItem>(true) : null;
        if (grenade == null) return false;

        grenade.DetonatePickup(pickup.gameObject, shooter);
        return true;
    }

    /// <summary>
    /// Триггер-коллайдеры основной луч игнорирует (иначе пули вязли бы в невидимых
    /// зонах), а пикапы в мире часто именно триггеры. Отдельный проход по триггерам
    /// вдоль полёта пули: граната перехватывает её, если она ближе твёрдого попадания.
    /// Пуля при этом гаснет на гранате — трассер и шум ИИ идут от точки взрыва.
    /// </summary>
    void CheckTriggerExplosives(Vector3 origin, Vector3 direction, float maxDistance, GameObject shooter,
                                ref Vector3 endPoint, ref bool hitSomething)
    {
        // Толстый луч: мелкая граната детонирует и при прохождении пули рядом, а не только при прямом попадании.
        float assist = Mathf.Max(0.01f, explosiveHitAssist);
        RaycastHit[] hits = Physics.SphereCastAll(origin, assist, direction, maxDistance, hitMask,
                                                  QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit th in hits)
        {
            if (th.collider == null || !th.collider.isTrigger) continue;
            // Граната ближе дула (под ногами, за спиной) — пуля стартует мимо неё, не взрываемся сами.
            if (th.distance < 0.4f) continue;

            // Живая брошенная граната на триггере — детонация от пули.
            ThrownGrenade live = th.collider.GetComponentInParent<ThrownGrenade>();
            if (live != null)
            {
                ((ZharkoeLeto44.AI.IDamageable)live).TakeDamage(
                    new ZharkoeLeto44.AI.DamageInfo(damage, th.point, direction, shooter));
                endPoint = th.point;
                hitSomething = true;
                return;
            }

            // Лежащий пикап-граната — детонация вместо пролёта сквозь.
            if (TryDetonateExplosivePickup(th.collider, shooter))
            {
                endPoint = th.point;
                hitSomething = true;
                return;
            }
        }
    }

    /// <summary>
    /// Считается ли попадание головным. Сначала по имени коллайдера
    /// (headColliderNames), затем — по высоте: у целей без отдельного коллайдера
    /// головы (капсула CharacterController) головой считается верхняя часть роста.
    /// </summary>
    bool IsHeadCollider(Collider col, Vector3 hitPoint)
    {
        if (headshotDamage <= 0f || col == null) return false;

        string colName = col.name.ToLowerInvariant();
        if (headColliderNames != null)
        {
            foreach (string candidate in headColliderNames)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (colName.Contains(candidate.ToLowerInvariant())) return true;
            }
        }

        CharacterController body = col.GetComponentInParent<CharacterController>();
        if (body != null && hitPoint.y >= body.bounds.max.y - headZoneHeight)
            return true;

        return false;
    }

    /// <summary>
    /// Убрать из hitMask слои самого оружия, игрока и дырок от пуль.
    ///
    /// Зачем: луч летит из камеры, а модель оружия висит перед ней. Если её слой
    /// остаётся в маске, первое попадание — в собственный ствол, и выстрел
    /// никогда не доходит до врага. То же с капсулой игрока и BulletHole.
    /// </summary>
    void ExcludeSelfFromHitMask()
    {
        int excluded = 0;

        // Слои модели оружия
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            excluded |= 1 << t.gameObject.layer;

        // Слой игрока
        if (fpsController != null)
            excluded |= 1 << fpsController.gameObject.layer;

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) excluded |= 1 << playerLayer;

        int holeLayer = LayerMask.NameToLayer("BulletHole");
        if (holeLayer >= 0) excluded |= 1 << holeLayer;

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0) excluded |= 1 << ignoreRaycast;

        hitMask &= ~excluded;

        int charLayer = LayerMask.NameToLayer("Characters");
        if (charLayer >= 0 && (hitMask & (1 << charLayer)) == 0)
        {
            hitMask |= 1 << charLayer;
            Debug.LogWarning("[Gun] Слой Characters отсутствовал в Hit Mask — добавлен " +
                             "автоматически, иначе пули пролетали бы сквозь врагов.", this);
        }

        if (hitMask == 0)
        {
            Debug.LogWarning("[Gun] Hit Mask оказалась пустой после исключения своих слоёв — " +
                             "стрелять было бы некуда. Маска сброшена на Default.", this);
            hitMask = 1;   // Default
        }

        WarnAboutUnreachableTargets(excluded);
    }

    /// <summary>
    /// Предупредить, если враг стоит на слое, который мы только что исключили.
    ///
    /// Реальный случай: WeaponHolder и Player лежат на одном слое, и если врага
    /// положили туда же, пули начинают проходить сквозь него молча. Ошибку
    /// в такой ситуации искать почти невозможно, поэтому пишем в консоль сразу.
    /// </summary>
    void WarnAboutUnreachableTargets(int excludedMask)
    {
        var reported = new System.Collections.Generic.HashSet<int>();

        foreach (MonoBehaviour mb in FindObjectsOfType<MonoBehaviour>())
        {
            if (mb is not ZharkoeLeto44.AI.IDamageable) continue;
            if (fpsController != null && mb.transform.IsChildOf(fpsController.transform)) continue;

            int layer = mb.gameObject.layer;
            if ((excludedMask & (1 << layer)) == 0) continue;
            if (!reported.Add(layer)) continue;

            Debug.LogWarning($"[Gun] «{mb.name}» стоит на слое «{LayerMask.LayerToName(layer)}», " +
                             "который исключён из Hit Mask (это слой оружия или игрока). " +
                             "Пули будут проходить сквозь него. Переведи врага на отдельный слой.", mb);
        }
    }

    bool CanPlaceHole(Vector3 point)
    {
        foreach (Collider col in Physics.OverlapSphere(point, holeMinDistance, holeCheckMask))
            if (col.CompareTag("BulletHole")) return false;
        return true;
    }

    /// <summary>
    /// Спавн визуального эффекта в точке попадания пули.
    /// Эффект подбирается по тегу/материалу поверхности, поворачивается
    /// по нормали и уничтожается через impactLifetime.
    /// </summary>
    void SpawnImpactEffect(RaycastHit hit)
    {
        GameObject prefab = FindImpactPrefab(hit);
        if (prefab == null) return;

        Vector3 spawnPos = hit.point + hit.normal * impactOffset;
        Quaternion spawnRot = Quaternion.LookRotation(hit.normal);

        GameObject fx = Instantiate(prefab, spawnPos, spawnRot);

        // ParticlePack stores the actual impact under a "HitEffect" child and
        // keeps a mesh/collider on the prefab root for its demo scene.  When such
        // a prefab is used by a weapon, leave only the visual effect at the hit
        // point so the invisible demo collider cannot affect later shots.
        Transform visualRoot = fx.transform.Find("HitEffect");
        if (visualRoot != null)
        {
            visualRoot.SetParent(null, true);
            visualRoot.SetPositionAndRotation(spawnPos, spawnRot);
            Destroy(fx);
            fx = visualRoot.gameObject;
        }

        Destroy(fx, impactLifetime);
    }

    /// <summary>
    /// Подобрать префаб и звук для поверхности попадания.
    /// Порядок проверки: тег объекта — затем PhysicsMaterial коллайдера.
    /// Если ничего не совпало — возвращается defaultImpactPrefab.
    /// </summary>
    GameObject FindImpactPrefab(RaycastHit hit)
    {
        if (impactEffects != null)
        {
            foreach (ImpactEffectEntry entry in impactEffects)
            {
                if (entry == null) continue;

                bool matches = false;

                if (!string.IsNullOrEmpty(entry.surfaceTag))
                    matches = hit.collider.CompareTag(entry.surfaceTag);
                else if (entry.surfaceMaterial != null)
                    matches = hit.collider.sharedMaterial == entry.surfaceMaterial;

                if (!matches) continue;

                if (entry.impactSound != null && audioSource != null)
                    audioSource.PlayOneShot(entry.impactSound);

                return entry.effectPrefab;
            }
        }

        return defaultImpactPrefab;
    }

    Vector3 GetSpreadDirection(float spreadDegrees)
    {
        Vector3 baseDir = playerCamera.transform.forward;
        float half = spreadDegrees * Mathf.Deg2Rad * 0.5f;
        Vector2 circle = Random.insideUnitCircle * Mathf.Tan(half);
        Vector2 angleDeg = circle * Mathf.Rad2Deg;
        return Quaternion.Euler(angleDeg.y, angleDeg.x, 0f) * baseDir;
    }

    void OnGUI()
    {
        if (initFailed || !drawDebugGUI) return;
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = Color.white }, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(15, 15, 400, 30), $"Патроны: {currentAmmo}/{maxAmmo}  (магазинов: {spareMagazines})" + (isReloading ? " (перезарядка...)" : ""));
        GUI.Label(new Rect(15, 45, 400, 30), "Режим: " + (currentFireMode == FireMode.Auto ? "Авто" : "Одиночный"));
        GUI.Label(new Rect(15, Screen.height - 40, 400, 30), "R - перезарядка | V - режим огня | B - осмотр");
    }

    void OnDisable()
    {
        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }
        if (inspectRoutine != null)
        {
            StopCoroutine(inspectRoutine);
            inspectRoutine = null;
        }
        isReloading = false;
        isCinematicReload = false;
        reloadProgress = 0f;
        isInspecting = false;
        if (started)
        {
            // Смена оружия посреди перезарядки: возвращаем магазины/затвор/рычаг
            // в исходное — как в начале ReloadSequence, иначе останутся висеть в воздухе.
            if (oldMagazine != null)
            {
                oldMagazine.gameObject.SetActive(true);
                oldMagazine.localPosition = oldMagOriginalLocalPos;
                oldMagazine.localRotation = oldMagOriginalLocalRot;
            }
            if (newMagazine != null) newMagazine.gameObject.SetActive(false);
            if (lever != null) lever.localPosition = leverOriginalLocalPos;
            if (bolt != null)
            {
                bolt.localPosition = boltOriginalLocalPos;
                bolt.localRotation = boltOriginalLocalRot;
            }
            if (boltLight != null) boltLight.enabled = false;
        }
        isHolstering = false;
        holsterProgress = 0f;
        equipProgress = 1f;
    }

    // === КИНЕМАТОГРАФИЧНАЯ ПЕРЕЗАРЯДКА ===
    IEnumerator ReloadSequence()
    {
        isReloading = true;
        reloadProgress = 0f;
        isCinematicReload = true;

        if (reloadSound != null && audioSource != null) audioSource.PlayOneShot(reloadSound);

        if (oldMagazine != null)
        {
            oldMagazine.gameObject.SetActive(true);
            oldMagazine.localPosition = oldMagOriginalLocalPos;
            oldMagazine.localRotation = oldMagOriginalLocalRot;
        }
        if (newMagazine != null)
        {
            if (newMagazine.parent != weaponModel) newMagazine.SetParent(weaponModel, false);
            newMagazine.gameObject.SetActive(false);
            newMagazine.localPosition = newMagOriginalLocalPos;
            newMagazine.localRotation = newMagOriginalLocalRot;
        }
        if (lever != null) lever.localPosition = leverOriginalLocalPos;
        if (bolt != null)
        {
            bolt.localPosition = boltOriginalLocalPos;
            bolt.localRotation = boltOriginalLocalRot;
        }

        float reloadFOV = normalFOV - reloadFOVReduction;
        yield return StartCoroutine(FOVChange(reloadFOV, fovTransitionTime));

        float t = 0f;
        Vector3 startWeaponPos = weaponModel.localPosition;
        Quaternion startWeaponRot = weaponModel.localRotation;
        while (t < closeUpTransitionTime)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / closeUpTransitionTime);
            weaponModel.localPosition = Vector3.Lerp(startWeaponPos, reloadStartPosition, progress);
            weaponModel.localRotation = Quaternion.Slerp(startWeaponRot, Quaternion.Euler(reloadStartRotation), progress);
            reloadProgress = Mathf.Clamp01(t / reloadTime);
            yield return null;
        }
        weaponModel.localPosition = reloadStartPosition;
        weaponModel.localRotation = Quaternion.Euler(reloadStartRotation);

        yield return new WaitForSeconds(pauseBeforeMagazineDrop);

        if (lever != null)
        {
            if (magazineReleaseSound != null && audioSource != null) StartCoroutine(PlayDelayedSound(magazineReleaseSound, magazineReleaseSoundDelay));
            Vector3 leverTargetPos = leverOriginalLocalPos + new Vector3(leverMoveOffsetX, 0, 0);
            t = 0f;
            while (t < leverMoveTime)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / leverMoveTime);
                lever.localPosition = Vector3.Lerp(leverOriginalLocalPos, leverTargetPos, progress);
                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + t) / reloadTime);
                yield return null;
            }
            lever.localPosition = leverTargetPos;
        }

        yield return new WaitForSeconds(0.1f);

        if (oldMagazine != null)
        {
            Vector3 dropStartLocalPos = oldMagazine.localPosition;
            Quaternion dropStartLocalRot = oldMagazine.localRotation;

            if (magazineDropSound != null && audioSource != null) StartCoroutine(PlayDelayedSound(magazineDropSound, magazineDropSoundDelay));

            float dropTimer = 0f;
            while (dropTimer < magazineDropTime)
            {
                dropTimer += Time.deltaTime;
                float k = dropTimer / magazineDropTime;
                float dropOffset = magazineDropHeight * k * k;
                float rotOffset = magazineDropRotationZ * k;
                oldMagazine.localPosition = dropStartLocalPos + Vector3.down * dropOffset;
                oldMagazine.localRotation = dropStartLocalRot * Quaternion.Euler(0, 0, rotOffset);
                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + dropTimer) / reloadTime);
                yield return null;
            }
            oldMagazine.gameObject.SetActive(false);
        }

        yield return new WaitForSeconds(pauseBeforeMagazineInsert);

        if (newMagazine != null)
        {
            yield return new WaitForSeconds(newMagazineAppearDelay);

            Vector3 targetLocalPos = newMagazineInsertLocalPos;
            Quaternion targetLocalRot = Quaternion.Euler(newMagazineInsertLocalRot);

            Vector3 startLocalPos = targetLocalPos + newMagazineStartOffset;
            Quaternion startLocalRot = targetLocalRot * Quaternion.Euler(newMagazineStartRotationOffset);

            newMagazine.gameObject.SetActive(true);
            newMagazine.localPosition = startLocalPos;
            newMagazine.localRotation = startLocalRot;

            if (magazineSlideSound != null && audioSource != null) StartCoroutine(PlayDelayedSound(magazineSlideSound, magazineSlideSoundDelay));

            StartCoroutine(CameraShake(cameraShakeDuration, cameraShakeIntensityInsert));

            Vector3 preInsertPos = Vector3.Lerp(startLocalPos, targetLocalPos, newMagazinePreInsertAmount);
            Quaternion preInsertRot = Quaternion.Slerp(startLocalRot, targetLocalRot, newMagazinePreInsertAmount);

            float insertTimer = 0f;
            while (insertTimer < newMagazineMoveTime)
            {
                insertTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(insertTimer / newMagazineMoveTime);
                float curveValue = newMagazineInsertCurve != null ? newMagazineInsertCurve.Evaluate(progress) : Mathf.SmoothStep(0f, 1f, progress);

                newMagazine.localPosition = Vector3.Lerp(startLocalPos, preInsertPos, curveValue);
                newMagazine.localRotation = Quaternion.Slerp(startLocalRot, preInsertRot, curveValue);

                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + insertTimer) / reloadTime);
                yield return null;
            }

            newMagazine.localPosition = preInsertPos;
            newMagazine.localRotation = preInsertRot;

            yield return new WaitForSeconds(newMagazineSnapPause);

            float snapTimer = 0f;
            Vector3 snapStartPos = newMagazine.localPosition;
            Quaternion snapStartRot = newMagazine.localRotation;
            while (snapTimer < 0.1f)
            {
                snapTimer += Time.deltaTime * newMagazineSnapSpeed;
                float k = Mathf.Clamp01(snapTimer);
                newMagazine.localPosition = Vector3.Lerp(snapStartPos, targetLocalPos, k);
                newMagazine.localRotation = Quaternion.Slerp(snapStartRot, targetLocalRot, k);

                float endFactor = k;
                Vector3 tiltRot = insertTiltRotation * endFactor;
                Vector3 inertiaPos = newMagInertiaOffsetPos * endFactor;
                Vector3 inertiaRot = newMagInertiaOffsetRot * endFactor;
                weaponModel.localPosition = reloadStartPosition + inertiaPos;
                weaponModel.localRotation = Quaternion.Euler(reloadStartRotation) * Quaternion.Euler(inertiaRot + tiltRot);

                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + newMagazineMoveTime + newMagazineSnapPause + snapTimer) / reloadTime);
                yield return null;
            }

            newMagazine.localPosition = targetLocalPos;
            newMagazine.localRotation = targetLocalRot;

            if (magazineSnapSound != null && audioSource != null) StartCoroutine(PlayDelayedSound(magazineSnapSound, magazineSnapSoundDelay));

            float returnT2 = 0f;
            Vector3 currentWeaponPos2 = weaponModel.localPosition;
            Quaternion currentWeaponRot2 = weaponModel.localRotation;
            while (returnT2 < inertiaDuration)
            {
                returnT2 += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, returnT2 / inertiaDuration);
                weaponModel.localPosition = Vector3.Lerp(currentWeaponPos2, reloadStartPosition, k);
                weaponModel.localRotation = Quaternion.Slerp(currentWeaponRot2, Quaternion.Euler(reloadStartRotation), k);
                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + newMagazineMoveTime + newMagazineSnapPause + 0.1f + returnT2) / reloadTime);
                yield return null;
            }
            weaponModel.localPosition = reloadStartPosition;
            weaponModel.localRotation = Quaternion.Euler(reloadStartRotation);
        }

        yield return new WaitForSeconds(pauseBeforeBoltPull);

        t = 0f;
        Vector3 boltPullStartPos = weaponModel.localPosition;
        Quaternion boltPullStartRot = weaponModel.localRotation;
        while (t < boltPullTransitionTime)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / boltPullTransitionTime);
            weaponModel.localPosition = Vector3.Lerp(boltPullStartPos, reloadBoltPullPosition, progress);
            weaponModel.localRotation = Quaternion.Slerp(boltPullStartRot, Quaternion.Euler(reloadBoltPullRotation), progress);
            reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + newMagazineMoveTime + newMagazineSnapPause + 0.1f + inertiaDuration + pauseBeforeBoltPull + t) / reloadTime);
            yield return null;
        }
        weaponModel.localPosition = reloadBoltPullPosition;
        weaponModel.localRotation = Quaternion.Euler(reloadBoltPullRotation);

        if (bolt != null)
        {
            if (boltSound != null && audioSource != null) StartCoroutine(PlayDelayedSound(boltSound, boltSoundDelay));

            if (boltParticles != null) boltParticles.Play();
            if (boltLight != null)
            {
                boltLight.enabled = true;
                StartCoroutine(DisableLightAfterDelay(boltLight, 0.1f));
            }
            if (useBoltFOVKick) StartCoroutine(FOVKick(boltFOVKickAmount, boltFOVKickDuration));

            StartCoroutine(CameraShake(cameraShakeDuration, cameraShakeIntensityBolt));

            Vector3 boltPullTarget = boltOriginalLocalPos + new Vector3(-0.49f, 0f, 0f);
            t = 0f;
            while (t < leverMoveTime)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / leverMoveTime);
                bolt.localPosition = Vector3.Lerp(boltOriginalLocalPos, boltPullTarget, progress);

                float boltInertiaFactor = Mathf.Sin(progress * Mathf.PI);
                weaponModel.localPosition = reloadBoltPullPosition + boltPullInertiaOffsetPos * boltInertiaFactor;
                weaponModel.localRotation = Quaternion.Euler(reloadBoltPullRotation) * Quaternion.Euler(boltPullInertiaOffsetRot * boltInertiaFactor);

                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + newMagazineMoveTime + newMagazineSnapPause + 0.1f + inertiaDuration + pauseBeforeBoltPull + boltPullTransitionTime + t) / reloadTime);
                yield return null;
            }
            bolt.localPosition = boltPullTarget;

            yield return new WaitForSeconds(0.08f);

            t = 0f;
            while (t < leverMoveTime)
            {
                t += Time.deltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, t / leverMoveTime);
                bolt.localPosition = Vector3.Lerp(boltPullTarget, boltOriginalLocalPos, progress);

                float returnFactor = Mathf.Sin(progress * Mathf.PI) * 0.6f;
                weaponModel.localPosition = reloadBoltPullPosition + boltPullInertiaOffsetPos * returnFactor;
                weaponModel.localRotation = Quaternion.Euler(reloadBoltPullRotation) * Quaternion.Euler(boltPullInertiaOffsetRot * returnFactor);

                reloadProgress = Mathf.Clamp01((closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + newMagazineMoveTime + newMagazineSnapPause + 0.1f + inertiaDuration + pauseBeforeBoltPull + boltPullTransitionTime + leverMoveTime + 0.08f + t) / reloadTime);
                yield return null;
            }
            bolt.localPosition = boltOriginalLocalPos;
            weaponModel.localPosition = reloadBoltPullPosition;
            weaponModel.localRotation = Quaternion.Euler(reloadBoltPullRotation);
        }

        float elapsed = closeUpTransitionTime + pauseBeforeMagazineDrop + leverMoveTime + 0.1f + magazineDropTime + pauseBeforeMagazineInsert + newMagazineAppearDelay + newMagazineMoveTime + newMagazineSnapPause + 0.1f + inertiaDuration + pauseBeforeBoltPull + boltPullTransitionTime + leverMoveTime + 0.08f + leverMoveTime;
        while (elapsed < reloadTime)
        {
            elapsed += Time.deltaTime;
            reloadProgress = Mathf.Clamp01(elapsed / reloadTime);
            yield return null;
        }

        float targetFOV = isAiming ? aimFOV : normalFOV;
        yield return StartCoroutine(FOVChange(targetFOV, fovTransitionTime));

        t = 0f;
        Vector3 returnPos = Vector3.Lerp(hipPosition, aimPosition, aimBlend);
        Vector3 returnRot = Vector3.Lerp(hipRotation, aimRotation, aimBlend);
        while (t < closeUpTransitionTime)
        {
            t += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, t / closeUpTransitionTime);
            weaponModel.localPosition = Vector3.Lerp(reloadBoltPullPosition, returnPos, progress);
            weaponModel.localRotation = Quaternion.Slerp(Quaternion.Euler(reloadBoltPullRotation), Quaternion.Euler(returnRot), progress);
            reloadProgress = Mathf.Clamp01((elapsed + t) / reloadTime);
            yield return null;
        }

        currentAmmo = maxAmmo;
        ConsumeMagazine();
        OnAmmoChanged?.Invoke(currentAmmo, maxAmmo);
        currentSpread = baseSpread;
        isReloading = false;
        isCinematicReload = false;
        reloadProgress = 0f;
        reloadRoutine = null;
    }

#if UNITY_EDITOR
    [ContextMenu("Баланс: точная стрельба на дистанцию")]
    void ApplyLongRangeBalance()
    {
        // Значения в инспекторе сцены не обновляются сами при смене дефолтов в коде —
        // эта кнопка применяет баланс на конкретный ППШ в один клик.
        baseSpread = 1.5f;
        autoSpreadPerShot = 0.7f;
        maxSpread = 8f;
        aimSpreadMultiplier = 0.25f;
        spreadRecoverySpeed = 18f;
        Debug.Log("[Gun] Применён баланс для дистанции: одиночные в прицеле бьют точно, очередь всё ещё расползается.", this);
    }
#endif
}
