using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZharkoeLeto44.AI
{
    /// <summary>Как точка появления включается.</summary>
    public enum SpawnActivation
    {
        /// <summary>Только по вызову из кода: SpawnPointRegistry.SpawnById(id).</summary>
        Manual,
        /// <summary>По событию шины диалогов (поле Event Id).</summary>
        OnEvent,
        /// <summary>Когда игрок зашёл в триггер точки (нужен trigger-коллайдер).</summary>
        OnTriggerEnter
    }

    /// <summary>Что делать с ИИ заспавненного врага.</summary>
    public enum SpawnAiState
    {
        /// <summary>Ничего не трогаем — префаб такой, какой есть.</summary>
        PrefabDefault,
        /// <summary>Стоит, не патрулирует, не слышит, не атакует (ИИ выключен).</summary>
        IdleNonHostile,
        /// <summary>ИИ жив, но стоит на точке: без патруля и блуждания.</summary>
        HoldPosition,
        /// <summary>Сразу бросается на игрока.</summary>
        Hostile
    }

    /// <summary>
    /// Точка появления врага/события: ставится прямо в сцене, настраивается
    /// в инспекторе, спавнится по Id или по группе через SpawnPointRegistry.
    ///
    /// Правила:
    ///  • Id уникален в сцене (дубли — ошибка, в логе и в валидаторе);
    ///  • Group — свободная группа: ActivateGroup("Story") поднимает всё;
    ///  • OneShot — точка срабатывает один раз;
    ///  • без Find в рантайме: реестр сам держит список точек (OnEnable/OnDisable).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Flame of History/Spawn Point")]
    public sealed class SpawnPoint : MonoBehaviour
    {
        [Header("Идентификация")]
        [Tooltip("Уникальный id точки в сцене. По нему спавнит SpawnPointRegistry.SpawnById.")]
        public string Id = "";
        [Tooltip("Группа точек. ActivateGroup(\"Story\") поднимает все точки группы.")]
        public string Group = "";

        [Header("Что спавнить")]
        [Tooltip("Префаб, который поднимаем в точке. Пусто — берём Scene Object.")]
        public GameObject prefab;
        [Tooltip("Объект из сцены: не создаём новый, а включаем и ставим в точку.")]
        public GameObject sceneObject;
        [Tooltip("Смещение от точки в мире (метры).")]
        public Vector3 positionOffset = Vector3.zero;
        [Tooltip("Насколько точка может прилипнуть к NavMesh, м. 0 — не двигать точку.")]
        [Min(0f)] public float navMeshSnap = SpawnPlacement.DefaultSnap;
        [Tooltip("Маска областей NavMesh. По умолчанию — все, кроме «Not Walkable».")]
        public int navMeshAreaMask = SpawnPlacement.DefaultAreaMask;
        [Tooltip("Поворачивать заспавненного к игроку (выкл — поворот точки).")]
        public bool facePlayerOnSpawn = false;

        [Header("Активация")]
        [Tooltip("Точка выключена — спавн не идёт (включается из кода: SpawnPoint.SpawnEnabled).")]
        public bool spawnEnabled = true;
        [Tooltip("Как точка включается.")]
        public SpawnActivation activation = SpawnActivation.Manual;
        [Tooltip("Спавнить только один раз. Повторные вызовы игнорируются.")]
        public bool oneShot = true;
        [Tooltip("Id события для OnEvent (из DialogueEventBus), например raid_germans.")]
        public string eventId = "";
        [Tooltip("Радиус триггера для OnTriggerEnter, м.")]
        [Min(0.1f)] public float triggerRadius = 1.5f;

        [Header("Начальное состояние")]
        [Tooltip("Что сделать с ИИ заспавненного. PrefabDefault — ничего.")]
        public SpawnAiState aiState = SpawnAiState.PrefabDefault;
        [Tooltip("Спрятать оружие у «мирного» врага (Idle Non Hostile).")]
        public bool hideWeapon = true;

        /// <summary>Точка уже сработала.</summary>
        public bool HasSpawned { get; private set; }
        /// <summary>Что подняли в точке (null — ещё не спавнили).</summary>
        public GameObject SpawnedInstance { get; private set; }

        private void OnEnable()
        {
            SpawnPointRegistry.Register(this);
            if (activation == SpawnActivation.OnEvent && !string.IsNullOrEmpty(eventId))
                DialogueEventBus.OnEvent += OnBusEvent;
        }

        private void OnDisable()
        {
            DialogueEventBus.OnEvent -= OnBusEvent;
            SpawnPointRegistry.Unregister(this);
        }

        private void OnBusEvent(string busEventId)
        {
            if (string.IsNullOrEmpty(busEventId)) return;
            if (!string.IsNullOrEmpty(eventId) && busEventId != eventId) return;
            Activate();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (activation != SpawnActivation.OnTriggerEnter) return;
            if (other == null) return;
            Activate();
        }

        /// <summary>Включить точку (Manual/OnEvent/OnTriggerEnter).</summary>
        public void Activate()
        {
            Spawn(out _);
        }

        /// <summary>
        /// Поднять объект в точке. false — точка выключена, уже сработала
        /// (OneShot) или нечего спавнить.
        /// </summary>
        public bool Spawn(out GameObject instance, Transform faceTarget = null)
        {
            instance = null;
            if (!spawnEnabled) return false;
            if (oneShot && HasSpawned) return false;

            GameObject source = prefab != null ? prefab : sceneObject;
            if (source == null)
            {
                Debug.LogWarning($"[SpawnPoint] {name}: не задан Prefab и Scene Object — спавнить нечего.", this);
                return false;
            }

            Vector3 position = transform.position + positionOffset;
            Quaternion rotation = transform.rotation;

            // Точка на NavMesh: враг сможет ходить. Если меша рядом нет —
            // остаёмся ровно там, где поставили (и предупреждаем).
            bool snapped = SpawnPlacement.TrySnap(position, navMeshSnap, navMeshAreaMask, out Vector3 meshPoint);
            if (snapped) position = meshPoint;

            if (facePlayerOnSpawn && faceTarget != null)
                rotation = SpawnPlacement.LookAt(position, faceTarget.position, transform.forward);

            if (prefab != null)
            {
                instance = Instantiate(prefab, position, rotation);
                if (string.IsNullOrEmpty(instance.name)) instance.name = prefab.name;
            }
            else
            {
                // Объект из сцены: не создаём новый, а поднимаем и ставим в точку.
                instance = sceneObject;
                instance.transform.SetPositionAndRotation(position, rotation);
                if (!instance.activeSelf) instance.SetActive(true);
            }

            ApplyAiState(instance, faceTarget);

            SpawnedInstance = instance;
            HasSpawned = true;

            if (!snapped && prefab != null)
                Debug.LogWarning($"[SpawnPoint] {name}: точка ({position}) не на NavMesh и рядом его нет — " +
                                 "объект появится ровно здесь, но ходить не сможет. Запеки NavMesh.", this);

            Debug.Log($"[SpawnPoint] {name}: поднят «{instance.name}» в {position} " +
                      $"(точка «{Id}», группа «{Group}»).", this);
            return true;
        }

        /// <summary>Применить выбранное начальное состояние ИИ.</summary>
        private void ApplyAiState(GameObject instance, Transform faceTarget)
        {
            if (instance == null) return;

            switch (aiState)
            {
                case SpawnAiState.IdleNonHostile:
                    MakeIdleNonHostile(instance);
                    break;

                case SpawnAiState.HoldPosition:
                {
                    EnemyAI ai = instance.GetComponent<EnemyAI>();
                    if (ai == null) ai = instance.GetComponentInChildren<EnemyAI>(true);
                    if (ai != null)
                    {
                        ai.SetPatrolRoute(null);
                        ai.SetWander(false);
                    }
                    else
                    {
                        Debug.LogWarning($"[SpawnPoint] {name}: у «{instance.name}» нет EnemyAI — " +
                                         "Hold Position не применить.", this);
                    }
                    break;
                }

                case SpawnAiState.Hostile:
                {
                    Transform target = faceTarget;
                    if (target == null) target = FindPlayerTransform();
                    EnemyAI ai = instance.GetComponent<EnemyAI>();
                    if (ai == null) ai = instance.GetComponentInChildren<EnemyAI>(true);
                    if (ai != null && target != null) ai.ForceTarget(target);
                    break;
                }
            }
        }

        /// <summary>
        /// «Просто стоит»: ИИ выключен, оружие спрятано, мотор остановлен.
        /// Враг не патрулирует, не слышит игрока и не атакует, пока его
        /// обратно не включат (GermanBeggar.GoHostile / EnemyAI.enabled).
        /// </summary>
        private void MakeIdleNonHostile(GameObject instance)
        {
            EnemyAI ai = instance.GetComponent<EnemyAI>();
            if (ai == null) ai = instance.GetComponentInChildren<EnemyAI>(true);
            if (ai != null)
            {
                ai.enabled = false;
            }
            else
            {
                // Visual-only персонаж: ИИ на модели нет, глушить нечего.
                Debug.Log($"[SpawnPoint] {name}: на «{instance.name}» нет EnemyAI — это visual-only персонаж, " +
                          "ИИ отключать не нужно.", this);
            }

            HitscanWeapon weapon = instance.GetComponentInChildren<HitscanWeapon>(true);
            if (weapon != null)
            {
                weapon.enabled = false;
                if (hideWeapon && weapon.gameObject != instance && weapon.gameObject.activeSelf)
                    weapon.gameObject.SetActive(false);
            }

            SuppressionReceiver suppression = instance.GetComponentInChildren<SuppressionReceiver>(true);
            if (suppression != null) suppression.enabled = false;

            EnemyMotor motor = instance.GetComponent<EnemyMotor>();
            if (motor == null) motor = instance.GetComponentInChildren<EnemyMotor>(true);
            if (motor != null) motor.Stop();
        }

        private static Transform FindPlayerTransform()
        {
            GameObject player = null;
            try { player = GameObject.FindGameObjectWithTag("Player"); }
            catch { player = null; }
            return player != null ? player.transform : null;
        }

        private void OnValidate()
        {
            if (Id != null) Id = Id.Trim();
            if (Group != null) Group = Group.Trim();
            if (activation == SpawnActivation.OnEvent && string.IsNullOrEmpty(eventId))
                Debug.LogWarning($"[SpawnPoint] {name}: активация OnEvent, но Event Id пуст — точка не сработает.", this);
            if (activation == SpawnActivation.OnTriggerEnter && GetComponent<Collider>() == null)
                Debug.LogWarning($"[SpawnPoint] {name}: активация OnTriggerEnter, но на объекте нет коллайдера — " +
                                 "добавь trigger-коллайдер.", this);
            if (prefab == null && sceneObject == null)
                Debug.LogWarning($"[SpawnPoint] {name}: не задан Prefab и Scene Object.", this);
        }

        private void OnDrawGizmos()
        {
            Color color = Color.yellow;
            switch (activation)
            {
                case SpawnActivation.OnEvent: color = new Color(0.2f, 0.9f, 1f, 0.9f); break;
                case SpawnActivation.OnTriggerEnter: color = new Color(1f, 0.6f, 0.1f, 0.9f); break;
            }
            if (!spawnEnabled) color.a = 0.35f;
            if (HasSpawned && oneShot) color = new Color(0.4f, 0.4f, 0.4f, 0.6f);

            Vector3 position = transform.position + positionOffset;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(position, 0.6f);
            Gizmos.DrawLine(position, position + transform.forward * 1.4f);

            // Стрелка направления (куда смотрит заспавненный).
            Vector3 tip = position + transform.forward * 1.4f;
            Vector3 right = transform.right * 0.25f;
            Gizmos.DrawLine(tip, tip - transform.forward * 0.4f + right);
            Gizmos.DrawLine(tip, tip - transform.forward * 0.4f - right);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 position = transform.position + positionOffset;
            string label = string.IsNullOrEmpty(Id) ? "(без Id)" : Id;
            if (!string.IsNullOrEmpty(Group)) label += " / " + Group;
            if (HasSpawned) label += "  [использована]";
            Handles.color = Color.white;
            Handles.Label(position + Vector3.up * 1.1f, label);

            if (!string.IsNullOrEmpty(eventId))
                Handles.Label(position + Vector3.up * 0.8f, "event: " + eventId);
        }
#endif
    }
}
