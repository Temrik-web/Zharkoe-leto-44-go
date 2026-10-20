using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// Проигрыватель анимаций врага БЕЗ AnimatorController.
    /// Перетащи AnimationClip'ы в инспектор — скрипт сам склеит их через PlayableGraph:
    /// idle/walk/run/sprint смешиваются по скорости, shoot/reload/hit/death — поверх одним слоем.
    /// Управляется из EnemyAI (SetLocomotion / PlayShoot / PlayReload / PlayHit / PlayDeath).
    /// Все клипы необязательны: чего нет — то молча пропускается, ошибок в консоли не будет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnimator : MonoBehaviour
    {
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

        [Header("На какой скорости какой клип (м/с)")]
        [Tooltip("Якорь ходьбы. Держи равным Patrol Speed из EnemyAI.")]
        [SerializeField, Min(0.1f)] private float walkAnchorSpeed = 1.8f;
        [Tooltip("Якорь бега. Держи равным Chase Speed из EnemyAI.")]
        [SerializeField, Min(0.1f)] private float runAnchorSpeed = 3.8f;
        [SerializeField, Min(0.1f)] private float sprintAnchorSpeed = 5f;
        [Tooltip("Ниже этой скорости считаемся стоящими.")]
        [SerializeField, Min(0f)] private float idleMaxSpeed = 0.25f;
        [Tooltip("Как быстро веса догоняют цель. Больше — резче переходы.")]
        [SerializeField, Min(1f)] private float blendResponsiveness = 8f;
        [Tooltip("Если Idle-клипа нет — ставить самую медленную локомоцию на паузу при стоянке " +
                 "(чтобы не было лунной походки на месте). Для теста «run везде» ВЫКЛЮЧИ — " +
                 "тогда run будет играться даже стоя.")]
        [SerializeField] private bool freezeInPlaceWithoutIdle = true;
        [Tooltip("ЗАМОК МАСШТАБА. Принудительно включается в Awake/OnEnable: после оценки графа " +
                 "возвращает костям сохранённый bind-масштаб, если клип или смешивание клипов его " +
                 "изменили. Это страховка для клипов с несовпадающими Scale-кривыми. " +
                 "Галку можно снять только для диагностики.")]
        [SerializeField] private bool lockScales = true;

        [Header("Экшен-клипы (необязательно — можно оставить пустыми)")]
        [SerializeField] private AnimationClip shootClip;
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

        private PlayableGraph _graph;
        private AnimationMixerPlayable _locoMixer;
        private AnimationLayerMixerPlayable _layerMixer;
        private readonly AnimationClipPlayable[] _locoPlayables = new AnimationClipPlayable[4];
        private AnimationClipPlayable _actionPlayable;
        private readonly float[] _weights = new float[4];
        private readonly float[] _targets = new float[4];

        private float _speed;
        private bool _moveIntent;
        private bool _aiming;
        private bool _dead;
        private bool _graphReady;

        private readonly Dictionary<Transform, Vector3> _bindScales = new();
        private bool _warnedScaleDrift;

        private float _actionTime;
        private float _actionLength;
        private float _actionWeight;
        private float _actionTargetWeight;
        private bool _actionHoldAtEnd;

        private float _nextShootTime;
        private float _nextHitTime;
        private bool _tippedOver;
        private AnimationClip _deathClipInUse;
        private bool _warnedNoAnimator;
        private bool _warnedNoClips;
        private bool _warnedController;
        private bool _warnedNoDeathClip;
        private bool _warnedNoGraph;

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

        /// <summary>Вызывается из EnemyAI каждый кадр: текущая скорость м/с + есть ли приказ двигаться.
        /// ДВИЖЕНИЕ = АНИМАЦИЯ: если isMoving=true, idle запрещён — играет slowest locomotion
        /// даже когда замеренная скорость на этом кадре 0 (разгон/доворот/первый кадр).</summary>
        public void SetLocomotion(float speed, bool isMoving)
        {
            _speed = Mathf.Max(0f, speed);
            _moveIntent = isMoving;
        }

        /// <summary>Совместимость: без флага приказа — приказ выводится из порога idle.</summary>
        public void SetLocomotion(float speed)
        {
            SetLocomotion(speed, speed > idleMaxSpeed);
        }

        /// <summary>Вызывается из EnemyAI при смене состояния (пока только запоминается).</summary>
        public void SetAiming(bool aiming)
        {
            _aiming = aiming;
        }

        public void PlayShoot()
        {
            if (_dead || shootClip == null) return;
            if (Time.time < _nextShootTime) return;
            _nextShootTime = Time.time + Mathf.Max(0.05f, shootAnimCooldown);
            StartAction(shootClip, holdAtEnd: false);
        }

        public void PlayReload()
        {
            if (_dead || reloadClip == null) return;
            StartAction(reloadClip, holdAtEnd: false);
        }

        public void PlayHit()
        {
            if (_dead || hitClip == null) return;
            if (Time.time < _nextHitTime) return;
            _nextHitTime = Time.time + Mathf.Max(0.05f, hitAnimCooldown);
            StartAction(hitClip, holdAtEnd: false);
        }

        public void PlayDeath()
        {
            if (_dead) return;
            _dead = true;
            // Запоминаем ВЫБРАННЫЙ клип: если экшен позже потеряется (пересборка графа),
            // долежим ТОТ ЖЕ клип, а не кинем рандом заново (иначе труп дёргается).
            _deathClipInUse = SelectDeathClip();
            if (_deathClipInUse != null)
            {
                StartAction(_deathClipInUse, holdAtEnd: true);
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
            for (int i = 0; i < 4; i++)
            {
                _weights[i] += (_targets[i] - _weights[i]) * k;
                if (Mathf.Abs(_targets[i] - _weights[i]) < 0.001f) _weights[i] = _targets[i];
                _locoMixer.SetInputWeight(i, _dead ? 0f : _weights[i]);

                var p = _locoPlayables[i];
                if (p.IsValid())
                    p.SetSpeed(_dead || (standingWithoutIdle && i == slowest) ? 0f : 1f);
            }

            UpdateAction(dt);

            // БАГФИКС «встаёт после смерти»: если экшен смерти потерян (пересборка графа,
            // вкл/выкл объекта), локомоция с нулевыми весами показывает bind-стойку.
            // Самолечение каждый кадр: долежать ТОТ ЖЕ клип сразу в конце, без переигрывания.
            if (_dead) HoldDeathPose();

            // ЗАМОК МАСШТАБА: после оценки графа вернуть костям сохранённый bind-масштаб
            // до рендера, если клип или смешивание клипов его изменили.
            if (lockScales) RestoreBindScales();
        }

        private void CacheBindScales()
        {
            if (animator == null) return;

            foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || _bindScales.ContainsKey(t)) continue;
                _bindScales[t] = t.localScale;
            }
            // Чистим уничтоженные, чтобы словарь не рос бесконечно.
            if (_bindScales.Count > 0)
            {
                List<Transform> dead = null;
                foreach (var kv in _bindScales)
                {
                    if (kv.Key != null) continue;
                    if (dead == null) dead = new List<Transform>();
                    dead.Add(kv.Key);
                }
                if (dead != null)
                    foreach (Transform d in dead) _bindScales.Remove(d);
            }
        }

        private void RestoreBindScales()
        {
            if (_bindScales.Count == 0) return;
            foreach (var kv in _bindScales)
            {
                Transform t = kv.Key;
                if (t == null) continue;
                Vector3 b = kv.Value;
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
            if (!_actionPlayable.IsValid())
            {
                _layerMixer.SetInputWeight(1, 0f);
                _actionWeight = 0f;
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
                    _layerMixer.SetInputWeight(0, 0f);
                    _layerMixer.SetInputWeight(1, 1f);
                    return;
                }
                _actionTargetWeight = 0f;
                if (_actionWeight <= 0.01f)
                {
                    _graph.Disconnect(_layerMixer, 1);
                    _actionPlayable.Destroy();
                    _actionPlayable = new AnimationClipPlayable();
                    _layerMixer.SetInputWeight(1, 0f);
                    _layerMixer.SetInputWeight(0, 1f);
                    return;
                }
            }

            _actionWeight = Mathf.MoveTowards(
                _actionWeight, _actionTargetWeight, actionFadeSpeed * dt);
            _layerMixer.SetInputWeight(1, _actionWeight);
            _layerMixer.SetInputWeight(0, _dead ? 0f : 1f - _actionWeight);
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
            StartAction(_deathClipInUse, holdAtEnd: true);
            if (!_actionPlayable.IsValid()) return;
            _actionTime = _actionLength;
            _actionPlayable.SetTime(_actionLength);
            _actionPlayable.Pause();
            _actionWeight = 1f;
            _actionTargetWeight = 1f;
            _layerMixer.SetInputWeight(0, 0f);
            _layerMixer.SetInputWeight(1, 1f);
        }

        private void StartAction(AnimationClip clip, bool holdAtEnd)
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
                _graph.Disconnect(_layerMixer, 1);
                _actionPlayable.Destroy();
                _actionPlayable = new AnimationClipPlayable();
            }

            _actionPlayable = AnimationClipPlayable.Create(_graph, clip);
            _actionPlayable.SetTime(0);
            _actionPlayable.Play();
            _graph.Connect(_actionPlayable, 0, _layerMixer, 1);

            _actionTime = 0f;
            _actionLength = Mathf.Max(0.05f, (float)clip.length);
            _actionHoldAtEnd = holdAtEnd;
            _actionTargetWeight = 1f;
            _layerMixer.SetInputWeight(1, _actionWeight);
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

        private void ComputeTargets(float speed, bool forceMove, float[] outW)
        {
            outW[0] = outW[1] = outW[2] = outW[3] = 0f;

            float[] anchors = { 0f, walkAnchorSpeed, runAnchorSpeed, sprintAnchorSpeed };
            AnimationClip[] clips = { idleClip, walkClip, runClip, sprintClip };

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

            int first = -1, last = -1;
            for (int i = 0; i < 4; i++)
                if (clips[i] != null) { if (first < 0) first = i; last = i; }
            if (first < 0) return;

            if (speed <= idleMaxSpeed)
            {
                outW[idleClip != null ? 0 : first] = 1f;
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

            _graph = PlayableGraph.Create("EnemyAnimator_" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _locoMixer = AnimationMixerPlayable.Create(_graph, 4);
            AnimationClip[] clips = { idleClip, walkClip, runClip, sprintClip };
            for (int i = 0; i < 4; i++)
            {
                if (clips[i] != null)
                {
                    var p = AnimationClipPlayable.Create(_graph, clips[i]);
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

            _layerMixer = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_locoMixer, 0, _layerMixer, 0);
            _layerMixer.SetInputWeight(0, 1f);
            _layerMixer.SetInputWeight(1, 0f);

            var output = AnimationPlayableOutput.Create(_graph, "EnemyOutput", animator);
            output.SetSourcePlayable(_layerMixer);

            ComputeTargets(0f, false, _targets);
            for (int i = 0; i < 4; i++) _weights[i] = _targets[i];

            _graph.Play();
            CacheBindScales();
            _graphReady = true;
            // Если пересобрались уже мёртвым — сразу долежать труп, не ждать кадра.
            if (_dead) HoldDeathPose();
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graphReady = false;
            _actionPlayable = new AnimationClipPlayable();
            _actionWeight = 0f;
            _actionTargetWeight = 0f;
            // Бинд-скейлы НЕ чистим: они захвачены чистыми в Awake (до графа) и переживают
            // пересборку — иначе Rebuild посреди погони узаконил бы искажение.
        }

        /// <summary>
        /// БАГ: AnimatorController в поле Controller и PlayableGraph крутят кости
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
