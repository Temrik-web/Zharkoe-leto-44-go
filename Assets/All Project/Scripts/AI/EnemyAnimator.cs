using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// Проигрыватель анимаций врага БЕЗ AnimatorController.
    /// Перетащи AnimationClip'ы в инспектор — скрипт сам склеит их через PlayableGraph:
    /// idle/walk/run/sprint смешиваются по скорости, shoot/reload/hit/death — поверх отдельным слоем.
    /// Верхний слой умеет работать по AvatarMask (только верх тела), есть прицельные позы
    /// (aimIdle/aimWalk) и опциональные стрейф-клипы.
    /// Управляется из EnemyAI (SetLocomotion / SetAiming / PlayShoot / PlayReload / PlayHit / PlayDeath).
    /// Все клипы необязательны: чего нет — то молча пропускается, ошибок в консоли не будет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnimator : MonoBehaviour
    {
        private enum StrafeKind { None, Left, Right, Back }

        private const int LocoSlots = 7;   // idle, walk, run, sprint, strafeL, strafeR, back
        private const int ActionLayer = 1; // слой: выстрел/перезарядка/попадание (по маске)
        private const int DeathLayer = 2;  // слой: смерть (всё тело)
        private const int AimIdleLayer = 3;// слой: прицельная поза стоя (по маске)
        private const int AimWalkLayer = 4;// слой: прицельная поза на ходу (по маске)
        private const int StandLayer = 5;  // слой: поза «стоит и ждёт» (клип из инспектора)
        private const int LayerCount = 6;

        [Header("Ссылки")]
        [Tooltip("Animator модели врага. ТОЛЬКО ВРУЧНУЮ через инспектор — автопоиск ОТКЛЮЧЁН. " +
                 "Перетащи сюда Animator именно модели. " +
                 "ВАЖНО: поле Controller у этого Animator должно быть пустым (None) — " +
                 "клипы задаются ниже в Локомоции. Если там висит контроллер — скрипт сам его " +
                 "отключит в рантайме, иначе контроллер будет сам играть свои анимации.")]
        [SerializeField] private Animator animator;
        [Tooltip("Выключить Root Motion: движением владеет EnemyMotor/NavMeshAgent, " +
                 "иначе модель и навмеш тянут тело в разные стороны.")]
        [SerializeField] private bool disableRootMotion = true;
        [Tooltip("Писать предупреждения (нет Animator, нет клипов и т.п.).")]
        [SerializeField] private bool verbose = true;

        [Header("Локомоция — перетащи сюда свои клипы")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip runClip;
        [SerializeField] private AnimationClip sprintClip;

        [Header("Опциональные клипы стрейфа")]
        [Tooltip("Клип шага влево. Без него боковой шаг не используется.")]
        [SerializeField] private AnimationClip strafeLeftClip;
        [Tooltip("Клип шага вправо. Без него боковой шаг не используется.")]
        [SerializeField] private AnimationClip strafeRightClip;
        [Tooltip("Клип шага назад. Без него шаг назад не используется.")]
        [SerializeField] private AnimationClip walkBackClip;

        [Header("Прицельные позы (верх тела)")]
        [Tooltip("Поза прицеливания стоя. Играет, пока солдат в бою. Без неё — обычное idle.")]
        [SerializeField] private AnimationClip aimIdleClip;
        [Tooltip("Поза прицеливания на ходу. Без неё во время хода — обычная локомоция.")]
        [SerializeField] private AnimationClip aimWalkClip;

        [Header("Маска верхнего тела")]
        [Tooltip("AvatarMask для клипов выстрела/перезарядки/попадания и прицельных поз. " +
                 "Если задан — эти клипы играют только верхом тела, ноги продолжают локомоцию. " +
                 "Смерть всегда играет всем телом.")]
        [SerializeField] private AvatarMask upperBodyMask;

        [Header("На какой скорости какой клип (м/с)")]
        [Tooltip("Якорь ходьбы. Задаётся из EnemyAI (ConfigureSpeedAnchors) по реальной скорости патруля.")]
        [SerializeField, Min(0.1f)] private float walkAnchorSpeed = 1.8f;
        [Tooltip("Якорь бега. Задаётся из EnemyAI по реальной скорости погони.")]
        [SerializeField, Min(0.1f)] private float runAnchorSpeed = 3.8f;
        [Tooltip("Якорь спринта. Задаётся из EnemyAI по реальной скорости перебежки.")]
        [SerializeField, Min(0.1f)] private float sprintAnchorSpeed = 5f;
        [Tooltip("Ниже этой скорости считаемся стоящими.")]
        [SerializeField, Min(0f)] private float idleMaxSpeed = 0.25f;
        [Tooltip("Как быстро веса догоняют цель. Больше — резче переходы.")]
        [SerializeField, Min(1f)] private float blendResponsiveness = 8f;
        [Tooltip("Если Idle-клипа нет — ставить самую медленную локомоцию на паузу при стоянке " +
                 "(чтобы не было лунной походки на месте). Для теста «run везде» ВЫКЛЮЧИ — " +
                 "тогда run будет играться даже стоя.")]
        [SerializeField] private bool freezeInPlaceWithoutIdle = true;
        [Tooltip("ЗАМОК МАСШТАБА. Принудительно включается в Awake/OnEnable (галка в " +
                 "инспекторе не влияет): после оценки графа возвращает костям сохранённый " +
                 "bind-масштаб, если клип или смешивание клипов его изменили. Это страховка " +
                 "для клипов с несовпадающими Scale-кривыми.")]
        [SerializeField] private bool lockScales = true;

        [Header("Экшен-клипы (необязательно — можно оставить пустыми)")]
        [SerializeField] private AnimationClip shootClip;
        [Tooltip("Короткая отдача во время очереди. Если задан — выстрел играет один раз " +
                 "за очередь, а повторные выстрелы дают короткий отскок. Без него клип " +
                 "выстрела не перезапускается во время очереди (прицельная поза держится).")]
        [SerializeField] private AnimationClip shootRecoilClip;
        [SerializeField] private AnimationClip reloadClip;
        [SerializeField] private AnimationClip hitClip;

        [Tooltip("Клипы смерти. При смерти случайно выбирается один из непустых клипов.")]
        [SerializeField] private AnimationClip[] deathClips;

        [SerializeField, HideInInspector] private AnimationClip deathClip;
        [Tooltip("Скорость появления/затухания экшен-слоя.")]
        [SerializeField, Min(1f)] private float actionFadeSpeed = 8f;
        [Tooltip("Не чаще этого проигрывать анимацию выстрела (выстрелов в секунду больше).")]
        [SerializeField, Min(0f)] private float shootAnimCooldown = 0.28f;
        [Tooltip("Не чаще этого проигрывать анимацию попадания.")]
        [SerializeField, Min(0f)] private float hitAnimCooldown = 0.45f;
        [Tooltip("Если Death-клипа нет — завалить тело на бок, иначе труп останется стоять.")]
        [SerializeField] private bool tipOverOnDeathWithoutClip = true;

        public Animator Animator => animator;
        public bool IsDead => _dead;

        public bool HasLocomotion =>
            idleClip != null || walkClip != null || runClip != null || sprintClip != null;

        /// <summary>Есть ли клипы бокового/заднего шага (без них боковой шаг запрещён).</summary>
        public bool HasStrafeClips =>
            strafeLeftClip != null || strafeRightClip != null || walkBackClip != null;

        /// <summary>Есть ли прицельные позы (иначе SetAiming ничего не меняет).</summary>
        public bool HasAimClips => aimIdleClip != null || aimWalkClip != null;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _locoMixer;
        private AnimationLayerMixerPlayable _layerMixer;
        private readonly AnimationClipPlayable[] _locoPlayables = new AnimationClipPlayable[LocoSlots];
        private AnimationClipPlayable _actionPlayable;
        private AnimationClipPlayable _aimIdlePlayable;
        private AnimationClipPlayable _aimWalkPlayable;
        private readonly float[] _weights = new float[LocoSlots];
        private readonly float[] _targets = new float[LocoSlots];

        // Слой позы ожидания (стоит и ждёт): клип из инспектора поверх локомоции.
        private AnimationClipPlayable _standPlayable;
        private AnimationClip _standClip;
        private float _standWeight;
        private float _standTargetWeight;
        private float _standTime;
        private float _standLength;

        // Кеш якорей и клипов локомоции: раньше ComputeTargets создавал два массива
        // НА КАЖДЫЙ КАДР (2 аллокации на солдата на кадр).
        private readonly float[] _anchorCache = new float[LocoSlots];
        private readonly AnimationClip[] _clipCache = new AnimationClip[LocoSlots];

        private float _speed;
        private bool _moveIntent;
        private bool _aiming;
        private bool _dead;
        private bool _graphReady;
        private StrafeKind _strafe = StrafeKind.None;

        // Гистерезис idle <-> move: без него микрошаги мерцали idle/walk.
        private bool _movingState;
        private float _moveStateHoldUntil;

        // Кеш bind-масштабов в массивах: Dictionary обходился каждый LateUpdate.
        private Transform[] _bindTransforms = new Transform[64];
        private Vector3[] _bindScales = new Vector3[64];
        private int _bindCount;
        private bool _warnedScaleDrift;

        private float _actionTime;
        private float _actionLength;
        private float _actionWeight;
        private float _actionTargetWeight;
        private bool _actionHoldAtEnd;
        private int _actionLayer = ActionLayer;
        private AnimationClip _actionClipInUse;

        private float _aimWeight;
        private float _aimIdleWeight;
        private float _aimWalkWeight;

        private float _nextShootTime;
        private float _nextRecoilTime;
        private float _nextHitTime;
        private bool _tippedOver;
        private AnimationClip _deathClipInUse;
        private bool _warnedNoAnimator;
        private bool _warnedNoClips;
        private bool _warnedController;
        private bool _warnedNoDeathClip;
        private bool _warnedNoGraph;
        private bool _warnedNoUpperBodyMask;

        private void Awake()
        {
            // Автопоиск Animator УДАЛЁН по требованию: только инспектор.
            // Пусто — значит анимаций нет, в консоль предупреждение.
            if (animator == null && verbose && !_warnedNoAnimator)
            {
                _warnedNoAnimator = true;
                Debug.LogWarning($"[EnemyAnimator] {name}: Animator не назначен в инспекторе — " +
                    "анимаций не будет. Перетащи Animator модели вручную.", this);
            }
            StripForeignController();
            WarnIfControllerConflicts();
            if (animator != null && disableRootMotion)
                animator.applyRootMotion = false;
            // БАГФИКС «враг уменьшается на бегу»: старые сцены сохранены без поля lockScales —
            // Unity десериализует его как false и замок молча выключен. Включаем принудительно:
            // анимация не имеет права менять размер костей ни при каких клипах.
            lockScales = true;
            // Чистый бинд ДО первого кадра графа (граф ещё не играл — масштабы не искажены).
            CacheBindScales();
            if (runAnchorSpeed <= walkAnchorSpeed) runAnchorSpeed = walkAnchorSpeed + 0.5f;
            if (sprintAnchorSpeed <= runAnchorSpeed) sprintAnchorSpeed = runAnchorSpeed + 0.5f;
            RefreshClipCache();
            WarnIfUpperBodyMaskMissing();
        }

        /// <summary>
        /// Без AvatarMask слой экшена (выстрел/перезарядка/попадание) и слои
        /// прицельных поз перекрывают локомоцию ЦЕЛИКОМ — ноги замирают.
        /// Само поведение не меняем (это сломало быDeath-слой и текущие сцены),
        /// но предупреждаем: маску надо назначить.
        /// </summary>
        private void WarnIfUpperBodyMaskMissing()
        {
            if (!verbose || _warnedNoUpperBodyMask) return;
            if (upperBodyMask != null) return;
            bool hasActionOrAim = shootClip != null || shootRecoilClip != null ||
                                  reloadClip != null || hitClip != null ||
                                  aimIdleClip != null || aimWalkClip != null;
            if (!hasActionOrAim) return;

            _warnedNoUpperBodyMask = true;
            Debug.LogWarning($"[EnemyAnimator] {name}: не задана AvatarMask верхнего тела — " +
                "слои выстрела/перезарядки/попадания и прицельные позы перекроют локомоцию " +
                "целиком (ноги замирают). Назначь Upper Body Mask.", this);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (animator == null)
            {
                if (verbose && !_warnedNoAnimator)
                {
                    _warnedNoAnimator = true;
                    Debug.LogWarning($"[EnemyAnimator] {name}: Animator не назначен в инспекторе — " +
                        "анимаций не будет. Перетащи Animator модели вручную (автопоиск отключён).", this);
                }
                return;
            }
            if (disableRootMotion) animator.applyRootMotion = false;
            StripForeignController();
            WarnIfControllerConflicts();
            // См. Awake: замок всегда включён (старые сцены иначе грузят false).
            lockScales = true;
            if (HasLocomotion || HasDeathAnimation)
                BuildGraph();
            else if (verbose && !_warnedNoClips)
            {
                _warnedNoClips = true;
                Debug.LogWarning($"[EnemyAnimator] {name}: ни одного клипа не задано — граф не построен, " +
                    "анимаций не будет.", this);
            }
        }

        private void OnDisable()
        {
            DestroyGraph();
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }

        /// <summary>Пересобрать граф (например, если клипы поменяли в рантайме).</summary>
        public void Rebuild()
        {
            if (!Application.isPlaying) return;
            DestroyGraph();
            if (animator != null && (HasLocomotion || HasDeathAnimation))
                BuildGraph();
        }

        /// <summary>
        /// ЕДИНЫЙ ИСТОЧНИК СКОРОСТЕЙ: EnemyAI передаёт реальные скорости своих
        /// состояний (патруль / погоня / перебежка), и якоря анимации совпадают с ними.
        /// Без этого клип бега крутился на скорости патруля — ноги скользили.
        /// </summary>
        public void ConfigureSpeedAnchors(float walkSpeed, float runSpeed, float sprintSpeed)
        {
            walkAnchorSpeed = Mathf.Max(0.1f, walkSpeed);
            runAnchorSpeed = Mathf.Max(walkAnchorSpeed + 0.1f, runSpeed);
            sprintAnchorSpeed = Mathf.Max(runAnchorSpeed + 0.1f, sprintSpeed);
            RefreshClipCache();
        }

        /// <summary>Вызывается из EnemyAI каждый кадр: текущая скорость м/с + есть ли приказ двигаться.
        /// ДВИЖЕНИЕ = АНИМАЦИЯ: если isMoving=true, idle запрещён — играет locomotion
        /// даже когда замеренная скорость на этом кадре 0 (разгон/доворот/первый кадр).</summary>
        public void SetLocomotion(float speed, bool isMoving)
        {
            _strafe = StrafeKind.None;
            ApplyLocomotion(speed, isMoving);
        }

        /// <summary>
        /// Перегрузка с направлением движения: по мировой скорости определяется
        /// боковой/задний шаг (если есть соответствующие клипы). Без клипов стрейфа
        /// поведение не отличается от обычного SetLocomotion.
        /// </summary>
        public void SetLocomotion(Vector3 worldVelocity, bool isMoving, float speed)
        {
            _strafe = StrafeKind.None;
            float magnitude = speed;
            if (isMoving && magnitude > 0.2f && HasStrafeClips)
            {
                Vector3 local = transform.InverseTransformDirection(worldVelocity);
                if (local.z < -0.4f * magnitude) _strafe = StrafeKind.Back;
                else if (local.x > 0.4f * magnitude) _strafe = StrafeKind.Right;
                else if (local.x < -0.4f * magnitude) _strafe = StrafeKind.Left;
            }
            ApplyLocomotion(magnitude, isMoving);
        }

        private void ApplyLocomotion(float speed, bool isMoving)
        {
            _speed = Mathf.Max(0f, speed);
            _moveIntent = isMoving;
            UpdateMoveState(_speed, isMoving);
        }

        /// <summary>
        /// Гистерезис idle <-> move: порог входа в «иду» и порог выхода в «стою» разные,
        /// плюс короткая задержка. Без этого микрошаги (0.2–0.3 м/с) мерцали
        /// idle/walk каждый кадр.
        /// </summary>
        private void UpdateMoveState(float speed, bool isMoving)
        {
            float enter = idleMaxSpeed * 0.8f;
            float exit = idleMaxSpeed * 1.35f;

            if (isMoving || speed > enter)
            {
                if (!_movingState)
                {
                    _movingState = true;
                    _moveStateHoldUntil = Time.time + 0.2f;
                }
                return;
            }

            if (_movingState && speed < exit && Time.time >= _moveStateHoldUntil)
                _movingState = false;
        }

        /// <summary>Вызывается из EnemyAI при смене состояния: пока солдат в бою —
        /// держим прицельную позу (если есть клипы aimIdle/aimWalk).</summary>
        public void SetAiming(bool aiming)
        {
            _aiming = aiming;
        }

        /// <summary>
        /// Поза «стоит и ждёт»: клип играет отдельным слоем поверх локомоции, пока
        /// поза не снята (ClearStandPose). Клип с Loop Time играет по кругу, без Loop Time —
        /// замирает на последнем кадре. Нужен для сцен, где солдат стоит и ждёт
        /// (немец у двери), но ИИ выключен и локомоция не управляется.
        /// </summary>
        public bool PlayStandPose(AnimationClip clip)
        {
            if (clip == null) { ClearStandPose(); return false; }
            if (_dead) return false;
            if (_standClip == clip && _standPlayable.IsValid()) return true;

            DestroyStandPlayable();

            _standClip = clip;
            _standTargetWeight = 1f;

            if (!_graphReady || !_graph.IsValid())
            {
                // Граф ещё не собран (нет клипов локомоции) — клип подхватится при сборке.
                return false;
            }

            CreateStandPlayable();
            return _standPlayable.IsValid();
        }

        /// <summary>Снять позу ожидания: снова обычная локомоция.</summary>
        public void ClearStandPose()
        {
            _standTargetWeight = 0f;
            _standClip = null;
        }

        /// <summary>Идёт ли сейчас поза ожидания.</summary>
        public bool HasStandPose => _standClip != null;

        public void PlayShoot()
        {
            if (_dead || shootClip == null) return;
            if (Time.time < _nextShootTime) return;
            _nextShootTime = Time.time + Mathf.Max(0.05f, shootAnimCooldown);
            StartAction(shootClip, holdAtEnd: false, layer: ActionLayer);
        }

        /// <summary>
        /// Повторный выстрел в очереди: полную анимацию выстрела НЕ перезапускаем
        /// (иначе она дёргалась каждые 0.28 с и «ломала» прицельную позу).
        /// Если задан отдельный клип отдачи — играем его, иначе не делаем ничего.
        /// </summary>
        public void PlayShootRecoil()
        {
            if (_dead) return;

            if (shootRecoilClip != null)
            {
                if (Time.time < _nextRecoilTime) return;
                _nextRecoilTime = Time.time + Mathf.Max(0.05f, shootAnimCooldown);
                StartAction(shootRecoilClip, holdAtEnd: false, layer: ActionLayer);
                return;
            }

            // Клипа отдачи нет — удерживаем текущую анимацию выстрела, не перезапуская её.
            if (_actionPlayable.IsValid() && _actionClipInUse == shootClip) return;
        }

        public void PlayReload()
        {
            if (_dead || reloadClip == null) return;
            StartAction(reloadClip, holdAtEnd: false, layer: ActionLayer);
        }

        public void PlayHit()
        {
            if (_dead || hitClip == null) return;
            if (Time.time < _nextHitTime) return;
            _nextHitTime = Time.time + Mathf.Max(0.05f, hitAnimCooldown);
            StartAction(hitClip, holdAtEnd: false, layer: ActionLayer);
        }

        public void PlayDeath()
        {
            if (_dead) return;
            _dead = true;
            _aiming = false;
            // Прицельные позы гасим сразу: иначе ещё ~0.25 с после смерти
            // видно смесь прицела и трупа.
            _aimWeight = 0f;
            _aimIdleWeight = 0f;
            _aimWalkWeight = 0f;
            if (_layerMixer.IsValid())
            {
                _layerMixer.SetInputWeight(AimIdleLayer, 0f);
                _layerMixer.SetInputWeight(AimWalkLayer, 0f);
            }
            // Запоминаем ВЫБРАННЫЙ клип: если экшен позже потеряется (пересборка графа),
            // долежим ТОТ ЖЕ клип, а не кинем рандом заново (иначе труп дёргается).
            _deathClipInUse = SelectDeathClip();
            if (_deathClipInUse != null)
            {
                StartAction(_deathClipInUse, holdAtEnd: true, layer: DeathLayer);
                return;
            }
            if (tipOverOnDeathWithoutClip && !_tippedOver)
            {
                _tippedOver = true;
                transform.Rotate(Vector3.right, -85f, Space.Self);
            }
            if (verbose && !_warnedNoDeathClip)
            {
                _warnedNoDeathClip = true;
                Debug.Log($"[EnemyAnimator] {name}: Death-клип не задан — " +
                    "тело завалено на бок заглушкой. Подставь deathClip для нормальной смерти.", this);
            }
        }

        private void LateUpdate()
        {
            if (!_graphReady || !_graph.IsValid()) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bool forceMove = _moveIntent && !_dead;
            ComputeTargets(_speed, forceMove, _targets);

            bool standingWithoutIdle = freezeInPlaceWithoutIdle && !forceMove &&
                _speed <= idleMaxSpeed && idleClip == null;
            int slowest = SlowestAvailableIndex();

            float k = Mathf.Min(1f, blendResponsiveness * dt);
            for (int i = 0; i < LocoSlots; i++)
            {
                _weights[i] += (_targets[i] - _weights[i]) * k;
                if (Mathf.Abs(_targets[i] - _weights[i]) < 0.001f) _weights[i] = _targets[i];
                _locoMixer.SetInputWeight(i, _dead ? 0f : _weights[i]);

                var p = _locoPlayables[i];
                if (!p.IsValid()) continue;

                // Масштаб проигрывания = фактическая скорость / якорь клипа (0.8–1.2):
                // ноги не скользят, когда солдат идёт быстрее «своего» клипа.
                float scale = 1f;
                if (i >= 1 && _anchorCache[i] > 0.01f)
                    scale = Mathf.Clamp(_speed / _anchorCache[i], 0.8f, 1.2f);
                if (standingWithoutIdle && i == slowest) scale = 0f;
                // Клип, который уходит в ноль, не должен «топтаться» на месте:
                // раньше ноги продолжали идти (0.8×) ещё 0.3–0.5 с после остановки,
                // пока вес догасал (лунная походка).
                if (_targets[i] <= 0.001f) scale = 0f;

                p.SetSpeed(_dead ? 0f : scale);
            }

            UpdateAction(dt);
            UpdateAim(dt);
            UpdateStand(dt);

            // БАГФИКС «встаёт после смерти»: если экшен смерти потерян (пересборка графа,
            // вкл/выкл объекта), локомоция с нулевыми весами показывает bind-стойку.
            // Самолечение каждый кадр: долежать ТОТ ЖЕ клип сразу в конце, без переигрывания.
            if (_dead) HoldDeathPose();

            // ЗАМОК МАСШТАБА: после оценки графа вернуть костям сохранённый bind-масштаб
            // до рендера, если клип или смешивание клипов его изменили.
            if (lockScales) RestoreBindScales();
        }

        /// <summary>Прицельная поза: вес нарастает, пока солдат в бою (SetAiming).</summary>
        private void UpdateAim(float dt)
        {
            if (!_layerMixer.IsValid()) return;

            bool aiming = _aiming && !_dead && HasAimClips;
            bool moving = _movingState || _moveIntent;

            float target = aiming ? (_actionWeight > 0.01f ? 0.5f : 1f) : 0f;
            _aimWeight = Mathf.MoveTowards(_aimWeight, target, actionFadeSpeed * dt);
            _aimIdleWeight = Mathf.MoveTowards(_aimIdleWeight,
                aiming && !moving ? _aimWeight : 0f, actionFadeSpeed * dt);
            _aimWalkWeight = Mathf.MoveTowards(_aimWalkWeight,
                aiming && moving ? _aimWeight : 0f, actionFadeSpeed * dt);

            _layerMixer.SetInputWeight(AimIdleLayer, _aimIdleWeight);
            _layerMixer.SetInputWeight(AimWalkLayer, _aimWalkWeight);
            ApplyLayerWeights();
        }

        /// <summary>Слой позы ожидания: вес, проигрывание клипа, замирание в конце.</summary>
        private void UpdateStand(float dt)
        {
            if (!_layerMixer.IsValid()) return;

            if (_standWeight <= 0.001f && _standTargetWeight <= 0f)
            {
                if (_standPlayable.IsValid()) DestroyStandPlayable();
                _layerMixer.SetInputWeight(StandLayer, 0f);
                return;
            }

            _standWeight = Mathf.MoveTowards(_standWeight, _standTargetWeight, actionFadeSpeed * dt);
            _layerMixer.SetInputWeight(StandLayer, _standWeight);

            if (!_standPlayable.IsValid() || _standWeight <= 0.01f) return;

            _standTime += dt;
            // Клип без Loop Time: замираем на последнем кадре, а не теряем позу.
            if (_standClip != null && !_standClip.isLooping && _standTime >= _standLength)
            {
                _standPlayable.SetTime(_standLength);
                _standPlayable.Pause();
            }
        }

        private void CreateStandPlayable()
        {
            if (_standClip == null) return;
            // _graphReady на этом этапе ещё false (граф собирается) — проверяем сам граф.
            if (!_graph.IsValid() || !_layerMixer.IsValid()) return;
            if (_standPlayable.IsValid()) return;

            _standPlayable = AnimationClipPlayable.Create(_graph, _standClip);
            _standPlayable.SetTime(0);
            _standPlayable.Play();
            _graph.Connect(_standPlayable, 0, _layerMixer, StandLayer);
            _standTime = 0f;
            _standLength = Mathf.Max(0.05f, (float)_standClip.length);
            _layerMixer.SetInputWeight(StandLayer, _standWeight);
        }

        private void DestroyStandPlayable()
        {
            if (_standPlayable.IsValid())
            {
                if (_graph.IsValid() && _layerMixer.IsValid())
                    _graph.Disconnect(_layerMixer, StandLayer);
                _standPlayable.Destroy();
            }
            _standPlayable = new AnimationClipPlayable();
        }

        private void CacheBindScales()
        {
            if (animator == null) return;

            Transform[] all = animator.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null) continue;
                if (IndexOfBind(t) >= 0) continue;
                AppendBind(t, t.localScale);
            }

            // Чистим уничтоженные, чтобы массив не рос бесконечно.
            int write = 0;
            for (int i = 0; i < _bindCount; i++)
            {
                if (_bindTransforms[i] == null) continue;
                _bindTransforms[write] = _bindTransforms[i];
                _bindScales[write] = _bindScales[i];
                write++;
            }
            for (int i = write; i < _bindCount; i++) _bindTransforms[i] = null;
            _bindCount = write;
        }

        private int IndexOfBind(Transform t)
        {
            for (int i = 0; i < _bindCount; i++)
                if (_bindTransforms[i] == t) return i;
            return -1;
        }

        private void AppendBind(Transform t, Vector3 scale)
        {
            if (_bindCount >= _bindTransforms.Length)
            {
                int newSize = _bindTransforms.Length * 2;
                var newTransforms = new Transform[newSize];
                var newScales = new Vector3[newSize];
                for (int i = 0; i < _bindCount; i++)
                {
                    newTransforms[i] = _bindTransforms[i];
                    newScales[i] = _bindScales[i];
                }
                _bindTransforms = newTransforms;
                _bindScales = newScales;
            }

            _bindTransforms[_bindCount] = t;
            _bindScales[_bindCount] = scale;
            _bindCount++;
        }

        private void RestoreBindScales()
        {
            if (_bindCount == 0) return;
            for (int i = 0; i < _bindCount; i++)
            {
                Transform t = _bindTransforms[i];
                if (t == null) continue;
                Vector3 b = _bindScales[i];
                Vector3 cur = t.localScale;
                if (cur.x != b.x || cur.y != b.y || cur.z != b.z)
                {
                    if (verbose && !_warnedScaleDrift)
                    {
                        _warnedScaleDrift = true;
                        Debug.LogWarning($"[EnemyAnimator] {name}: клип пытался изменить bind-масштаб " +
                            $"кости «{t.name}» {cur} -> возвращён {b} (LockScales). " +
                            "Проверь Scale-кривые и соответствие рига в используемых клипах.", this);
                    }
                    t.localScale = b;
                }
            }
        }

        private void UpdateAction(float dt)
        {
            if (!_layerMixer.IsValid()) return;

            if (!_actionPlayable.IsValid())
            {
                _layerMixer.SetInputWeight(_actionLayer, 0f);
                _actionWeight = 0f;
                ApplyLayerWeights();
                return;
            }

            _actionTime += dt;
            if (_actionTime >= _actionLength)
            {
                if (_actionHoldAtEnd)
                {
                    _actionPlayable.SetTime(_actionLength);
                    _actionPlayable.Pause();
                    _actionWeight = 1f;
                    _layerMixer.SetInputWeight(_actionLayer, 1f);
                    ApplyLayerWeights();
                    return;
                }
                _actionTargetWeight = 0f;
                if (_actionWeight <= 0.01f)
                {
                    _graph.Disconnect(_layerMixer, _actionLayer);
                    _actionPlayable.Destroy();
                    _actionPlayable = new AnimationClipPlayable();
                    _layerMixer.SetInputWeight(_actionLayer, 0f);
                    _actionClipInUse = null;
                    ApplyLayerWeights();
                    return;
                }
            }

            _actionWeight = Mathf.MoveTowards(
                _actionWeight, _actionTargetWeight, actionFadeSpeed * dt);
            _layerMixer.SetInputWeight(_actionLayer, _actionWeight);
            ApplyLayerWeights();
        }

        private void ApplyLayerWeights()
        {
            if (!_layerMixer.IsValid()) return;

            // Базовый слой — вся локомоция. Слои сверху накладываются по весу.
            // Смерть перекрывает всё (вес 1, без маски).
            _layerMixer.SetInputWeight(0, _dead ? 0f : 1f);
        }

        /// <summary>
        /// Самолечение позы трупа: враг мёртв, а экшена смерти нет (потерян при пересборке
        /// графа) — долежать запомненный клип СРАЗУ В КОНЦЕ, без переигрывания падения.
        /// Иначе локомоция с нулевыми весами показывает bind-стойку: труп «встаёт»,
        /// стоит и потом пропадает по corpseLifetime.
        /// </summary>
        private void HoldDeathPose()
        {
            if (!_dead || !_graphReady || !_graph.IsValid()) return;
            if (_actionPlayable.IsValid()) return;
            if (_deathClipInUse == null) _deathClipInUse = SelectDeathClip();
            if (_deathClipInUse == null) return; // tip-over: поворот уже на трансформе
            StartAction(_deathClipInUse, holdAtEnd: true, layer: DeathLayer);
            if (!_actionPlayable.IsValid()) return;
            _actionTime = _actionLength;
            _actionPlayable.SetTime(_actionLength);
            _actionPlayable.Pause();
            _actionWeight = 1f;
            _actionTargetWeight = 1f;
            _layerMixer.SetInputWeight(DeathLayer, 1f);
            ApplyLayerWeights();
        }

        private void StartAction(AnimationClip clip, bool holdAtEnd, int layer)
        {
            if (clip == null) return;
            if (!_graphReady || !_graph.IsValid())
            {
                if (verbose && !_warnedNoGraph)
                {
                    _warnedNoGraph = true;
                    Debug.LogWarning($"[EnemyAnimator] {name}: нет графа (нет Animator/клипов) — " +
                        $"экшен «{clip.name}» пропущен.", this);
                }
                return;
            }

            if (_actionPlayable.IsValid())
            {
                _graph.Disconnect(_layerMixer, _actionLayer);
                _actionPlayable.Destroy();
                _actionPlayable = new AnimationClipPlayable();
                _layerMixer.SetInputWeight(_actionLayer, 0f);
            }

            _actionLayer = layer;
            _actionClipInUse = clip;
            _actionPlayable = AnimationClipPlayable.Create(_graph, clip);
            _actionPlayable.SetTime(0);
            _actionPlayable.Play();
            _graph.Connect(_actionPlayable, 0, _layerMixer, _actionLayer);

            _actionTime = 0f;
            _actionLength = Mathf.Max(0.05f, (float)clip.length);
            _actionHoldAtEnd = holdAtEnd;
            _actionTargetWeight = 1f;
            // Новый экшен стартует с нуля: раньше он наследовал вес предыдущего
            // (выстрел → перезарядка) и «всплывал» с чужим весом.
            _actionWeight = 0f;
            _layerMixer.SetInputWeight(_actionLayer, _actionWeight);
            ApplyLayerWeights();
        }

        private int SlowestAvailableIndex()
        {
            if (walkClip != null) return 1;
            if (runClip != null) return 2;
            if (sprintClip != null) return 3;
            if (idleClip != null) return 0;
            return -1;
        }

        private bool HasDeathAnimation
        {
            get
            {
                if (deathClip != null) return true;
                if (deathClips == null) return false;
                for (int i = 0; i < deathClips.Length; i++)
                    if (deathClips[i] != null) return true;
                return false;
            }
        }

        private AnimationClip SelectDeathClip()
        {
            if (deathClips != null)
            {
                int validClipCount = 0;
                for (int i = 0; i < deathClips.Length; i++)
                    if (deathClips[i] != null) validClipCount++;

                if (validClipCount > 0)
                {
                    int selectedIndex = Random.Range(0, validClipCount);
                    for (int i = 0; i < deathClips.Length; i++)
                    {
                        AnimationClip clip = deathClips[i];
                        if (clip == null) continue;
                        if (selectedIndex-- == 0) return clip;
                    }
                }
            }

            return deathClip;
        }

        private void RefreshClipCache()
        {
            _anchorCache[0] = 0f;
            _anchorCache[1] = walkAnchorSpeed;
            _anchorCache[2] = runAnchorSpeed;
            _anchorCache[3] = sprintAnchorSpeed;
            _anchorCache[4] = walkAnchorSpeed;
            _anchorCache[5] = walkAnchorSpeed;
            _anchorCache[6] = walkAnchorSpeed;

            _clipCache[0] = idleClip;
            _clipCache[1] = walkClip;
            _clipCache[2] = runClip;
            _clipCache[3] = sprintClip;
            _clipCache[4] = strafeLeftClip;
            _clipCache[5] = strafeRightClip;
            _clipCache[6] = walkBackClip;
        }

        private void ComputeTargets(float speed, bool forceMove, float[] outW)
        {
            for (int i = 0; i < LocoSlots; i++) outW[i] = 0f;

            float[] anchors = _anchorCache;
            AnimationClip[] clips = _clipCache;

            // Боковой/задний шаг: если клип задан — он перебивает обычную локомоцию.
            // Если клипа нет — шага вбок не будет вовсе (солдат идёт прямо).
            if (_strafe != StrafeKind.None)
            {
                int slot = _strafe == StrafeKind.Left ? 4 : (_strafe == StrafeKind.Right ? 5 : 6);
                if (clips[slot] != null)
                {
                    outW[slot] = 1f;
                    return;
                }
            }

            // ДВИЖЕНИЕ = АНИМАЦИЯ: есть приказ двигаться и есть чем (walk/run/sprint) —
            // idle запрещён, вес уходит на locomotion даже при скорости ~0.
            if (forceMove && (walkClip != null || runClip != null || sprintClip != null))
            {
                int mFirst = -1, mLast = -1;
                for (int i = 1; i < 4; i++)
                    if (clips[i] != null) { if (mFirst < 0) mFirst = i; mLast = i; }
                if (mFirst < 0) { outW[0] = 1f; return; }

                float eff = Mathf.Max(speed, anchors[mFirst]);
                if (eff <= anchors[mFirst]) { outW[mFirst] = 1f; return; }
                if (eff >= anchors[mLast]) { outW[mLast] = 1f; return; }

                int mLo = mFirst;
                for (int i = mFirst; i < mLast; i++)
                {
                    if (clips[i] == null) continue;
                    int next = -1;
                    for (int j = i + 1; j <= mLast; j++)
                        if (clips[j] != null) { next = j; break; }
                    if (next < 0) break;
                    if (eff >= anchors[i] && eff <= anchors[next]) { mLo = i; break; }
                }

                int mHi = -1;
                for (int j = mLo + 1; j <= mLast; j++)
                    if (clips[j] != null) { mHi = j; break; }
                if (mHi < 0) { outW[mLo] = 1f; return; }

                float mSpan = Mathf.Max(0.001f, anchors[mHi] - anchors[mLo]);
                float mT = Mathf.Clamp01((eff - anchors[mLo]) / mSpan);
                outW[mLo] = 1f - mT;
                outW[mHi] = mT;
                return;
            }

            // Локомоция — слоты 1..3 (walk/run/sprint). IDLE (слот 0) в смешивание
            // НЕ входит: раньше он попадал в диапазон [first..last], и при
            // anchors[0] == 0 солдат на 1 м/с играл 45% idle + 55% walk —
            // ноги в полу-стойке. Idle — только отдельная поза покоя.
            int first = -1, last = -1;
            for (int i = 1; i < 4; i++)
                if (clips[i] != null) { if (first < 0) first = i; last = i; }

            // Локомоции нет вовсе — играем idle (или ничего).
            if (first < 0)
            {
                if (clips[0] != null) outW[0] = 1f;
                return;
            }

            // Гистерезис: пока «идём» (даже на микрошаге) — не падаем в idle.
            if (speed <= idleMaxSpeed && !_movingState)
            {
                // Idle, если он есть; иначе — самая медленная локомоция
                // (её замораживает freezeInPlaceWithoutIdle — без лунной походки).
                outW[clips[0] != null ? 0 : first] = 1f;
                return;
            }

            if (speed <= anchors[first]) { outW[first] = 1f; return; }
            if (speed >= anchors[last]) { outW[last] = 1f; return; }

            int lo = first;
            for (int i = first; i < last; i++)
            {
                if (clips[i] == null) continue;
                int next = -1;
                for (int j = i + 1; j <= last; j++)
                    if (clips[j] != null) { next = j; break; }
                if (next < 0) break;
                if (speed >= anchors[i] && speed <= anchors[next]) { lo = i; break; }
            }

            int hi = -1;
            for (int j = lo + 1; j <= last; j++)
                if (clips[j] != null) { hi = j; break; }
            if (hi < 0) { outW[lo] = 1f; return; }

            float span = Mathf.Max(0.001f, anchors[hi] - anchors[lo]);
            float t = Mathf.Clamp01((speed - anchors[lo]) / span);
            outW[lo] = 1f - t;
            outW[hi] = t;
        }

        private void BuildGraph()
        {
            DestroyGraph();
            if (animator == null) return;

            // БАГФИКС патруля («ходьба не всегда проигрывается»): в walkClip лежит ТОТ ЖЕ
            // ассет, что в idleClip (статика без шагов) — на патруле враг скользит статуей,
            // а ноги видно только на скорости бега. Лечим в рантайме: повышаем run/sprint
            // до walk. Сцену не правим (её перезаписывает открытый редактор), чиним тут —
            // переживает любые перезаписи сцены.
            if (walkClip != null && walkClip == idleClip)
            {
                AnimationClip promo = runClip != null && runClip != idleClip ? runClip
                    : sprintClip != null && sprintClip != idleClip ? sprintClip : null;
                if (promo != null)
                {
                    if (verbose)
                        Debug.LogWarning($"[EnemyAnimator] {name}: walkClip совпадает с idleClip " +
                            $"«{walkClip.name}» (статика!) — патруль скользил бы без шагов. " +
                            $"Подменён на «{promo.name}». Поправь клип в инспекторе, чтобы убрать варнинг.", this);
                    walkClip = promo;
                }
            }

            RefreshClipCache();

            _graph = PlayableGraph.Create("EnemyAnimator_" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _locoMixer = AnimationMixerPlayable.Create(_graph, LocoSlots);
            for (int i = 0; i < LocoSlots; i++)
            {
                AnimationClip clip = _clipCache[i];
                if (clip != null)
                {
                    var p = AnimationClipPlayable.Create(_graph, clip);
                    p.Play();
                    _locoPlayables[i] = p;
                    _graph.Connect(p, 0, _locoMixer, i);
                    _locoMixer.SetInputWeight(i, 0f);
                }
                else
                {
                    _locoPlayables[i] = new AnimationClipPlayable();
                    _locoMixer.SetInputWeight(i, 0f);
                }
            }

            // Верхние слои: экшены (по маске), смерть (без маски), прицельные позы (по маске).
            _layerMixer = AnimationLayerMixerPlayable.Create(_graph, LayerCount);
            _graph.Connect(_locoMixer, 0, _layerMixer, 0);
            for (int i = 0; i < LayerCount; i++) _layerMixer.SetInputWeight(i, 0f);
            _layerMixer.SetInputWeight(0, 1f);

            _aimIdlePlayable = ConnectLayerClip(aimIdleClip, AimIdleLayer);
            _aimWalkPlayable = ConnectLayerClip(aimWalkClip, AimWalkLayer);

            // Поза ожидания могла остаться от прошлого графа — переподключаем тот же клип.
            if (_standClip != null) CreateStandPlayable();

            if (upperBodyMask != null)
            {
                _layerMixer.SetLayerMaskFromAvatarMask(ActionLayer, upperBodyMask);
                _layerMixer.SetLayerMaskFromAvatarMask(AimIdleLayer, upperBodyMask);
                _layerMixer.SetLayerMaskFromAvatarMask(AimWalkLayer, upperBodyMask);
            }

            var output = AnimationPlayableOutput.Create(_graph, "EnemyOutput", animator);
            output.SetSourcePlayable(_layerMixer);

            ComputeTargets(0f, false, _targets);
            for (int i = 0; i < LocoSlots; i++) _weights[i] = _targets[i];

            _graph.Play();
            CacheBindScales();
            _graphReady = true;
            // Если пересобрались уже мёртвым — сразу долежать труп, не ждать кадра.
            if (_dead) HoldDeathPose();
        }

        private AnimationClipPlayable ConnectLayerClip(AnimationClip clip, int layer)
        {
            if (clip == null) return new AnimationClipPlayable();
            var p = AnimationClipPlayable.Create(_graph, clip);
            p.Play();
            _graph.Connect(p, 0, _layerMixer, layer);
            _layerMixer.SetInputWeight(layer, 0f);
            return p;
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graphReady = false;
            _actionPlayable = new AnimationClipPlayable();
            _actionLayer = ActionLayer;
            _actionWeight = 0f;
            _actionTargetWeight = 0f;
            _aimWeight = 0f;
            _aimIdleWeight = 0f;
            _aimWalkWeight = 0f;
            _aimIdlePlayable = new AnimationClipPlayable();
            _aimWalkPlayable = new AnimationClipPlayable();
            _actionClipInUse = null;
            // Плейабл позы ожидания: клип (_standClip) сохраняем — он переподключится
            // при следующей сборке графа. Сам плейабл чистим вместе с графом.
            _standPlayable = new AnimationClipPlayable();
            _standWeight = 0f;
            // Бинд-скейлы НЕ чистим: они захвачены чистыми в Awake (до графа) и переживают
            // пересборку — иначе Rebuild посреди погони узаконил бы искажение.
        }

        /// <summary>
        /// БУГ: AnimatorController в поле Controller и PlayableGraph крутят кости
        /// одновременно — переход дёргается и проигрывается дважды. Клипы должны
        /// жить только в полях ниже (Локомоция/Экшен), Controller — пустой (None).
        /// </summary>
        private void WarnIfControllerConflicts()
        {
            if (_warnedController || animator == null) return;
            if (animator.runtimeAnimatorController == null) return;
            _warnedController = true;
            Debug.LogWarning($"[EnemyAnimator] {name}: у Animator задан Controller " +
                $"«{animator.runtimeAnimatorController.name}» — он и граф EnemyAnimator " +
                "проигрывают анимацию одновременно (двойной переход с рывком). " +
                "Убери Controller (поставь None), клипы уже заданы в полях ниже.", this);
        }

        /// <summary>
        /// Чужие анимации (AnimatorController) — главная причина эффекта
        /// «скрипт сам берёт какие-то анимации»: контроллер играет свой Default State
        /// поверх/вместо графа. По требованию «только инспектор» — отцепляем его,
        /// остаются только клипы из полей ниже.
        /// </summary>
        private void StripForeignController()
        {
            if (animator == null) return;
            if (animator.runtimeAnimatorController == null) return;
            if (verbose)
                Debug.LogWarning($"[EnemyAnimator] {name}: внешний Controller " +
                    $"«{animator.runtimeAnimatorController.name}» отключён — " +
                    "играют только клипы из инспектора EnemyAnimator.", this);
            animator.runtimeAnimatorController = null;
        }

        private void OnValidate()
        {
            if (runAnchorSpeed <= walkAnchorSpeed) runAnchorSpeed = walkAnchorSpeed + 0.5f;
            if (sprintAnchorSpeed <= runAnchorSpeed) sprintAnchorSpeed = runAnchorSpeed + 0.5f;
        }
    }
}
