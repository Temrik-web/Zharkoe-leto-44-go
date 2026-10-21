using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Сценарий «Немец за дверью» (после подбора ППШ в подвале).
///
/// Порядок:
///  1. Игрок подбирает ППШ — за дверью появляется немец и стучит.
///  2. Игрок открывает дверь — начинается разговор.
///  3. Немец требует хлеб и воду.
///     Если игрок отказывает — немец свистит и убегает (засада отключена).
///  4. Если игрок даёт еду — немец уходит и пропадает.
///
/// Настройки — в инспекторе; префабы немцев и точки появления перетаскиваются мышью.
/// </summary>
[DisallowMultipleComponent]
public class PpshGermanEncounter : MonoBehaviour
{
    public enum EncounterPhase
    {
        /// <summary>Ждём, пока игрок возьмёт ППШ.</summary>
        WaitingTrigger,
        /// <summary>Немец за дверью, стучит.</summary>
        Knocking,
        /// <summary>Ждём, пока игрок откроет дверь.</summary>
        WaitingDoor,
        /// <summary>Идёт разговор.</summary>
        Talking,
        /// <summary>Разговор закончен: бой или немец ушёл.</summary>
        Resolved
    }

    [Header("Триггер: подбор ППШ")]
    [Tooltip("Id предмета, после подбора которого начинается сцена. " +
             "ППШ-41 — «ppsh41».")]
    public string triggerItemId = "ppsh41";
    [Tooltip("Следить за инвентарём, если ППШ выдали не подбором, а диалогом.")]
    public bool watchInventory = true;
    [Tooltip("Как часто проверять инвентарь, сек.")]
    [Min(0.1f)] public float watchInterval = 0.5f;
    [Tooltip("Не запускать сцена второй раз за сессию.")]
    public bool oncePerScene = true;

    [Header("Автопривязка (чинит кривую сцену)")]
    [Tooltip("На старте сам находит дверь подвала, переносит точки и еду к настоящему ППШ.")]
    public bool autoFixWiring = true;
    [Tooltip("Если назначенная дверь дальше этого расстояния от подбираемого ППШ — берём дверь подвала, м.")]
    [Min(1f)] public float maxDoorDistanceFromPpsh = 40f;
    [Tooltip("Id ключа двери подвала (поле Required Key Id у DoorController).")]
    public string cellarDoorKeyId = "cellar";
    [Tooltip("Id квеста, открывающего дверь подвала (поле Auto Unlock Quest Id).")]
    public string cellarDoorQuestId = "q_cellar_door";

    [Header("Немец за дверью")]
    [Tooltip("Префаб немца (с EnemyAI/CharacterHealth). Обязателен.")]
    public GameObject germanPrefab;
    [Tooltip("Откуда немец подходит к двери (улица). Пусто — поставим сами.")]
    public Transform germanApproachPoint;
    [Tooltip("Где он встанет и будет стучать. Пусто — напротив двери.")]
    public Transform germanStandPoint;
    [Tooltip("Дверь, которую открывает игрок. Пусто — сцена пойдёт сразу после стука.")]
    public DoorController door;
    [Tooltip("Через сколько секунд после подбора ППШ немец начинает идти к двери.")]
    [Min(0f)] public float knockDelay = 2f;
    [Tooltip("Сколько ждать, пока он дойдёт, прежде чем бить в дверь, сек.")]
    [Min(0f)] public float knockWalkTimeout = 12f;
    [Tooltip("Пауза между ударами, сек.")]
    [Min(0.5f)] public float knockInterval = 2.2f;
    [Tooltip("Сколько раз постучит, пока игрок не откроет.")]
    [Min(1)] public int knockCount = 3;
    [Tooltip("Звук удара в дверь.")]
    public AudioClip knockClip;
    [Tooltip("Что показать игроку, когда стучат.")]
    public string knockMessage = "Кто-то стучит в дверь...";
    [Tooltip("Снять замок с двери, когда немец постучит (он её отпер).")]
    public bool unlockDoorOnKnock = true;
    [Tooltip("Анимация, когда немец стоит у двери и ждёт (клип из инспектора). " +
             "Пусто — обычный idle. Передаётся компоненту German Beggar.")]
    public AnimationClip beggarStandAnimation;
    [Tooltip("Трясти камеру при каждом ударе.")]
    public bool shakeOnKnock = true;
    [Tooltip("Длительность тряски, сек.")]
    [Min(0.01f)] public float shakeDuration = 0.22f;
    [Tooltip("Сила тряски.")]
    [Min(0f)] public float shakeMagnitude = 0.12f;

    [Header("Диалог")]
    [Tooltip("Диалог с немцем. Пусто — возьмём из Resources.")]
    public DialogueData dialogue;
    [Tooltip("Путь к диалогу в Resources (если поле Dialogue пусто).")]
    public string dialogueResourcePath = "PpshGermanEncounter/PpshGermanDialogue";
    [Tooltip("На каком расстоянии от немца можно начать разговор, м.")]
    [Min(1f)] public float talkDistance = 4.5f;
    [Tooltip("На каком расстоянии от открытой двери разговор начинается, м.")]
    [Min(1f)] public float doorTalkDistance = 6f;
    [Tooltip("Страховка: если дверь открыта, немец дошёл, а игрок рядом — говорим. М.")]
    [Min(1f)] public float forceTalkDistance = 20f;
    [Tooltip("Клавиша для повторного разговора (если первый закрыли по Esc).")]
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Подсказка при повторном разговоре.")]
    public string interactMessage = "Нажмите E — поговорить с немцем";
    [Tooltip("Событие «согласился» из диалога (CustomEvent).")]
    public string agreeEventId = "ppsh_german_agreed";
    [Tooltip("Событие «отказал» из диалога (CustomEvent).")]
    public string refuseEventId = "ppsh_german_refused";

    [Header("Засада: шестеро немцев")]
    [Tooltip("Префабы немцев для засады. Можно один и тот же.")]
    public GameObject[] raiderPrefabs = new GameObject[0];
    [Tooltip("Точки появления на улице. Если пусто — кольцо вокруг игрока.")]
    public Transform[] spawnPoints = new Transform[0];
    [Tooltip("Сколько немцев поднять.")]
    [Min(1)] public int raiderCount = 6;
    [Tooltip("Пауза между появлениями, сек.")]
    [Min(0f)] public float raiderSpawnInterval = 0.35f;
    [Tooltip("Через сколько после свиста немцы бросаются на игрока, сек.")]
    [Min(0f)] public float raiderAggroDelay = 1.2f;
    [Tooltip("Радиус кольца вокруг игрока, если спавнпоинтов нет, м.")]
    [Min(3f)] public float fallbackSpawnRadius = 11f;
    [Tooltip("Поднять немцев ЗА спиной игрока (вне поля зрения), если спавнпоинтов нет.")]
    public bool fallbackBehindPlayer = true;

    [Header("Свист")]
    [Tooltip("Звук свиста. Пусто — свистнет сам немец (поле German Beggar).")]
    public AudioClip whistleClip;
    [Tooltip("Радиус шума от свиста: кто услышал — тот идёт сюда, м.")]
    [Min(1f)] public float whistleRadius = 70f;
    [Tooltip("Все немцы в сцене бросаются на игрока (а не только новые).")]
    public bool alertAllEnemies = true;
    [Tooltip("Сам немец тоже бросается в бой после свиста.")]
    public bool beggarFights = true;

    [Header("Если дал еду — уходит")]
    [Tooltip("Куда уходит немец: на расстояние от игрока, м.")]
    [Min(5f)] public float leaveDistance = 25f;
    [Tooltip("Скорость, с которой уходит, м/с.")]
    [Min(0.5f)] public float leaveSpeed = 2.4f;

    [Header("Отладка")]
    public bool debugLogging = false;

    public EncounterPhase Phase { get; private set; } = EncounterPhase.WaitingTrigger;

    private GameObject cachedPlayer;
    private Transform playerTransform;
    private GameObject german;
    private GermanBeggar beggar;
    private DialogueManager cachedManager;
    private InventorySystem cachedInventory;
    private float nextWatchTime;
    private float nextKnockTime;
    private int knocksDone;
    private bool agreed;
    private bool refused;
    private bool talked;
    private bool waitingForInteract;
    private bool hintShown;
    private bool knockStarted;
    private float walkDeadline;
    private DoorController hookedDoor;
    private bool spawnFailed;
    private Pickup triggerPickup;
    private Vector3 ppshHome;
    private Vector3 outsideSide;
    private bool outsideSideReady;

    void OnEnable()
    {
        DialogueEventBus.OnEvent += OnDialogueEvent;
        HookInstances();
    }

    void OnDisable()
    {
        DialogueEventBus.OnEvent -= OnDialogueEvent;
        if (cachedManager != null) cachedManager.OnDialogueEnded -= OnDialogueEnded;
        if (cachedInventory != null) cachedInventory.OnItemPickedUp -= OnItemPickedUp;
        if (hookedDoor != null) hookedDoor.Opened -= OnDoorOpened;
    }

    /// <summary>Подписка на диалог и инвентарь (менеджеры ищутся позже, если их ещё нет).</summary>
    void HookInstances()
    {
        if (cachedManager == null)
        {
            cachedManager = DialogueManager.Instance;
            if (cachedManager == null) cachedManager = FindObjectOfType<DialogueManager>();
        }
        if (cachedManager != null)
        {
            cachedManager.OnDialogueEnded -= OnDialogueEnded;
            cachedManager.OnDialogueEnded += OnDialogueEnded;
        }

        if (cachedInventory == null)
        {
            cachedInventory = InventorySystem.Instance;
            if (cachedInventory == null) cachedInventory = FindObjectOfType<InventorySystem>();
        }
        if (cachedInventory != null)
        {
            cachedInventory.OnItemPickedUp -= OnItemPickedUp;
            cachedInventory.OnItemPickedUp += OnItemPickedUp;
        }
    }

    void Start()
    {
        HookInstances();
        ResolvePlayer();
        ResolveTriggerPickup();
        ResolveDoor();
        if (autoFixWiring) AutoFixWiring();
        HookDoor();
        if (germanPrefab == null)
            Debug.LogWarning($"[PpshGerman] {name}: префаб немца не задан — сцена не сработает. " +
                             "Перетащи модель немца в поле German Prefab.", this);
        if (dialogue == null) dialogue = LoadDialogue();
        if (dialogue == null)
            Debug.LogError($"[PpshGerman] {name}: диалог не найден (ресурс «{dialogueResourcePath}»). " +
                           "Без него разговор не начнётся.", this);
    }

    /// <summary>Дверь не назначена — берём дверь подвала, иначе ближайшую к ППШ.</summary>
    void ResolveDoor()
    {
        if (door != null) return;

        DoorController[] doors = FindObjectsOfType<DoorController>(true);
        if (doors.Length == 0)
        {
            Debug.LogWarning($"[PpshGerman] {name}: в сцене нет ни одной двери — сцена идёт без двери, " +
                             "немец просто выйдет перед игроком.", this);
            return;
        }

        Vector3 anchor = PpshPosition();
        DoorController cellar = FindCellarDoor(doors);
        if (cellar != null && Vector3.Distance(cellar.transform.position, anchor) <= maxDoorDistanceFromPpsh)
        {
            door = cellar;
            Debug.Log($"[PpshGerman] {name}: дверь подвала — «{door.name}» " +
                      $"({Vector3.Distance(door.transform.position, anchor):F1} м от ППШ).", this);
            return;
        }

        door = NearestDoor(doors, anchor);
        if (door != null)
            Debug.Log($"[PpshGerman] {name}: дверь подобрана автоматически — «{door.name}» " +
                      $"({Vector3.Distance(door.transform.position, anchor):F1} м от ППШ).", this);
    }

    /// <summary>Подбираемый ППШ (он же — якорь для двери и еды).</summary>
    void ResolveTriggerPickup()
    {
        if (string.IsNullOrEmpty(triggerItemId)) return;

        Pickup best = null;
        float bestDist = float.MaxValue;
        Vector3 anchor = PlayerPosition();
        foreach (Pickup p in FindObjectsOfType<Pickup>(true))
        {
            if (p == null || p.item == null) continue;
            if (p.item.Id != triggerItemId) continue;
            float dist = (p.transform.position - anchor).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; best = p; }
        }

        triggerPickup = best;
        if (best != null)
        {
            ppshHome = best.transform.position;
            if (debugLogging)
                Debug.Log($"[PpshGerman] ППШ для привязки: «{best.name}» {ppshHome} " +
                          $"({Mathf.Sqrt(bestDist):F1} м от игрока).", this);
        }
    }

    Vector3 PpshPosition()
    {
        if (triggerPickup != null) return triggerPickup.transform.position;
        if (ppshHome != Vector3.zero) return ppshHome;
        if (playerTransform != null) return playerTransform.position;
        return transform.position;
    }

    DoorController FindCellarDoor(DoorController[] doors = null)
    {
        if (doors == null) doors = FindObjectsOfType<DoorController>(true);
        DoorController byKey = null;
        foreach (DoorController d in doors)
        {
            if (d == null) continue;
            if (d.name.IndexOf("DEDA", System.StringComparison.OrdinalIgnoreCase) >= 0) return d;
            if (byKey != null) continue;
            if (!string.IsNullOrEmpty(cellarDoorKeyId) && d.requiredKeyId == cellarDoorKeyId) byKey = d;
            else if (!string.IsNullOrEmpty(cellarDoorQuestId) && d.autoUnlockQuestId == cellarDoorQuestId) byKey = d;
        }
        return byKey;
    }

    static DoorController NearestDoor(DoorController[] doors, Vector3 anchor)
    {
        DoorController best = null;
        float bestDist = float.MaxValue;
        foreach (DoorController d in doors)
        {
            if (d == null) continue;
            float dist = Vector3.Distance(d.transform.position, anchor);
            if (dist < bestDist) { bestDist = dist; best = d; }
        }
        return best;
    }

    /// <summary>
    /// Чинит сцену на ходу: дверь подвала, точки у двери, хлеб и фляга у ППШ.
    /// Нужно, потому что объект сценария могли собрать вокруг пустышки «forPPSH-41».
    /// </summary>
    void AutoFixWiring()
    {
        if (door == null) ResolveDoor();

        Vector3 anchor = PpshPosition();
        if (door != null && Vector3.Distance(door.transform.position, anchor) > maxDoorDistanceFromPpsh)
        {
            DoorController cellar = FindCellarDoor();
            if (cellar != null && cellar != door)
            {
                Debug.Log($"[PpshGerman] Автопривязка: дверь «{door.name}» была в " +
                          $"{Vector3.Distance(door.transform.position, anchor):F0} м от ППШ — берём «{cellar.name}».", this);
                door = cellar;
            }
            else
            {
                DoorController nearest = NearestDoor(FindObjectsOfType<DoorController>(true), anchor);
                if (nearest != null && nearest != door)
                {
                    Debug.Log($"[PpshGerman] Автопривязка: дверь «{door.name}» далеко — берём ближайшую «{nearest.name}».", this);
                    door = nearest;
                }
            }
        }

        if (door == null) return;

        Vector3 side = OutsideSide();
        Vector3 doorPos = door.transform.position;
        Vector3 stand = OnGround(doorPos + side * 1.7f);
        Vector3 approach = OnGround(doorPos + side * 9f);

        if (germanStandPoint != null && Vector3.Distance(germanStandPoint.position, stand) > 3f)
        {
            germanStandPoint.position = stand;
            Debug.Log($"[PpshGerman] Автопривязка: точка «Немец у двери» перенесена к двери ({stand}).", this);
        }
        if (germanApproachPoint != null && Vector3.Distance(germanApproachPoint.position, approach) > 8f)
        {
            germanApproachPoint.position = approach;
            Debug.Log($"[PpshGerman] Автопривязка: точка «Откуда идёт немец» перенесена к двери ({approach}).", this);
        }

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Vector3 right = Vector3.Cross(Vector3.up, side).normalized;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Transform t = spawnPoints[i];
                if (t == null) continue;
                if (Vector3.Distance(t.position, doorPos) <= 60f) continue;
                float offset = (i - (spawnPoints.Length - 1) * 0.5f) * 2.6f;
                t.position = OnGround(doorPos + side * 13f + right * offset);
            }
        }

        PlaceFoodNearPpsh();

        if (debugLogging)
            Debug.Log($"[PpshGerman] Автопривязка готова: дверь «{door.name}» {doorPos}, ППШ {anchor}, " +
                      $"улица {side}, немец встанет {stand}.", this);
    }

    /// <summary>
    /// Сторона двери, где улица. Считаем один раз на старте: игрок начинает
    /// внутри (у ППШ), значит улица — с противоположной стороны двери.
    /// </summary>
    Vector3 OutsideSide()
    {
        if (outsideSideReady) return outsideSide;

        Vector3 toPlayer = Flat(PlayerPosition() - DoorPosition());
        if (toPlayer.sqrMagnitude > 0.5f)
        {
            outsideSide = -toPlayer.normalized;
        }
        else if (door != null)
        {
            outsideSide = Flat(door.transform.forward);
            if (outsideSide.sqrMagnitude < 0.01f) outsideSide = Vector3.forward;
            outsideSide.Normalize();
        }
        else
        {
            outsideSide = Vector3.forward;
        }

        outsideSideReady = true;
        return outsideSide;
    }

    /// <summary>Опустить точку на пол/землю (высота двери — это петля, а не ноги).</summary>
    Vector3 OnGround(Vector3 point)
    {
        float startY = Mathf.Max(point.y, PlayerPosition().y) + 1.5f;
        if (Physics.Raycast(new Vector3(point.x, startY, point.z), Vector3.down,
                            out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            return new Vector3(point.x, hit.point.y + 0.05f, point.z);

        point.y = PlayerPosition().y;
        return point;
    }

    /// <summary>Хлеб и флягу — к настоящему ППШ (а не к пустышке в пустоте).</summary>
    void PlaceFoodNearPpsh()
    {
        if (triggerPickup == null) return;
        Vector3 ppshPos = PpshPosition();

        Vector3 toPlayer = Flat(PlayerPosition() - ppshPos);
        if (toPlayer.sqrMagnitude < 0.01f) toPlayer = -OutsideSide();
        toPlayer.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, toPlayer).normalized;

        MoveFoodPickup("bread", ppshPos + toPlayer * 0.8f + right * 0.4f,
                       new Vector3(0.34f, 0.16f, 0.22f), new Color(0.62f, 0.40f, 0.18f), true);
        MoveFoodPickup("water", ppshPos + toPlayer * 0.8f - right * 0.4f,
                       new Vector3(0.15f, 0.12f, 0.15f), new Color(0.28f, 0.36f, 0.32f), false);
    }

    void MoveFoodPickup(string idPart, Vector3 pos, Vector3 scale, Color color, bool bread)
    {
        foreach (Pickup p in FindObjectsOfType<Pickup>(true))
        {
            if (p == null || p.item == null || p == triggerPickup) continue;
            string id = p.item.Id ?? "";
            string title = p.item.name ?? "";
            if (id.IndexOf(idPart, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                title.IndexOf(idPart, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (p.gameObject.activeInHierarchy == false) p.gameObject.SetActive(true);

            if (Vector3.Distance(p.transform.position, PpshPosition()) >= 1.5f)
            {
                Vector3 place = pos;
                if (Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 3f,
                                    ~0, QueryTriggerInteraction.Ignore))
                    place = hit.point + Vector3.up * 0.1f;

                p.transform.position = place;
                p.RecaptureBase();
                Debug.Log($"[PpshGerman] «{p.item.name}» перенесён к ППШ: {place}.", this);
            }

            EnsureFoodVisual(p, scale, color, bread);
        }
    }

    /// <summary>У хлеба и фляги нет моделей — делаем простые, чтобы их было видно.</summary>
    static void EnsureFoodVisual(Pickup p, Vector3 scale, Color color, bool bread)
    {
        if (p == null) return;

        Collider rootCollider = p.GetComponent<Collider>();
        if (rootCollider != null) rootCollider.isTrigger = true;

        if (p.GetComponentInChildren<Renderer>(true) != null) return;

        GameObject vis = GameObject.CreatePrimitive(bread ? PrimitiveType.Cube : PrimitiveType.Cylinder);
        vis.name = "Модель (авто)";
        Collider col = vis.GetComponent<Collider>();
        if (col != null) Destroy(col);

        vis.transform.SetParent(p.transform, false);
        vis.transform.localPosition = new Vector3(0f, bread ? scale.y * 0.5f : scale.y, 0f);
        vis.transform.localRotation = bread ? Quaternion.Euler(0f, 25f, 4f) : Quaternion.identity;
        vis.transform.localScale = bread ? scale : new Vector3(scale.x, scale.y * 0.5f, scale.z);

        Renderer r = vis.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = MakeMaterial(color);
    }

    static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("HDRP/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
        mat.color = color;
        return mat;
    }

    void HookDoor()
    {
        if (hookedDoor != null)
        {
            hookedDoor.Opened -= OnDoorOpened;
            hookedDoor = null;
        }
        if (door == null) return;
        hookedDoor = door;
        hookedDoor.Opened += OnDoorOpened;
    }

    /// <summary>Игрок открыл дверь — начинаем разговор.</summary>
    void OnDoorOpened()
    {
        if (Phase == EncounterPhase.Resolved || Phase == EncounterPhase.WaitingTrigger) return;
        Debug.Log("[PpshGerman] Дверь открыли — начинаем разговор.", this);
        Phase = EncounterPhase.WaitingDoor;
    }

    void Update()
    {
        switch (Phase)
        {
            case EncounterPhase.WaitingTrigger:
                UpdateTrigger();
                break;

            case EncounterPhase.Knocking:
                UpdateKnocking();
                break;

            case EncounterPhase.WaitingDoor:
                if (IsDoorOpen()) Phase = EncounterPhase.Talking;
                break;

            case EncounterPhase.Talking:
                UpdateTalking();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Триггер
    // ------------------------------------------------------------------

    void UpdateTrigger()
    {
        if (Time.time < nextWatchTime) return;
        nextWatchTime = Time.time + Mathf.Max(0.1f, watchInterval);
        if (!watchInventory) return;

        if (cachedInventory == null)
        {
            cachedInventory = InventorySystem.Instance;
            if (cachedInventory == null) cachedInventory = FindObjectOfType<InventorySystem>();
        }
        if (cachedInventory == null) return;
        if (cachedInventory.CountItemById(triggerItemId) > 0) BeginEncounter();
    }

    void OnItemPickedUp(ItemData item, int amount)
    {
        if (item == null || string.IsNullOrEmpty(triggerItemId)) return;
        if (item.Id != triggerItemId) return;
        BeginEncounter();
    }
    /// <summary>Запустить сценарий вручную (кнопка в инспекторе или UnityEvent).</summary>
    public void BeginEncounter()
    {
        if (Phase != EncounterPhase.WaitingTrigger) return;
        if (oncePerScene && GameState.GetFlag(FinishedFlag)) return;

        Phase = EncounterPhase.Knocking;
        nextKnockTime = Time.time + Mathf.Max(0f, knockDelay);
        knocksDone = 0;
        knockStarted = false;

        // Дверь отперта сразу: игрок не должен ломиться в запертую.
        if (autoFixWiring) AutoFixWiring();
        else ResolveDoor();
        HookDoor();
        UnlockDoor();

        Debug.Log($"[PpshGerman] Сцена начат: игрок поднял «{triggerItemId}». " +
                  $"Дверь: {(door != null ? door.name : "нет")}" +
                  (door != null
                      ? $" (в {Vector3.Distance(door.transform.position, PlayerPosition()):F1} м от игрока, " +
                        $"замок {(door.IsLocked ? "заперт" : "открыт")})"
                      : "") + ".", this);
    }

    // ------------------------------------------------------------------
    // Стук и дверь
    // ------------------------------------------------------------------

    void UpdateKnocking()
    {
        // 1. Немец появился в стороне улицы и идёт к двери.
        if (german == null)
        {
            if (Time.time < nextKnockTime) return;
            SpawnGerman();
            return;
        }

        // 2. Ждём, пока дойдёт. Не дождался — стучим всё равно.
        if (!knockStarted)
        {
            bool arrived = beggar != null && beggar.ArrivedAtTarget;
            if (!arrived && Time.time < walkDeadline) return;
            knockStarted = true;
            nextKnockTime = Time.time;
            Debug.Log($"[PpshGerman] Немец у двери, стучит. Дверь открыта: {IsDoorOpen()}.", this);
        }

        if (Time.time < nextKnockTime) return;

        // Больше knockCount раз не стучим — иначе стук бесконечный.
        if (knocksDone >= Mathf.Max(1, knockCount))
        {
            nextKnockTime = float.MaxValue;
            Say("Тук... тишина. Он ждёт у двери.", new Color(1f, 0.9f, 0.5f));
            return;
        }

        nextKnockTime = Time.time + Mathf.Max(0.5f, knockInterval);
        knocksDone++;

        PlayKnock();
        ShakeCamera();
        Say(knockMessage, Color.yellow);

        // Замок снят с первого удара — игрок может открыть дверь.
        if (knocksDone == 1) UnlockDoor();

        if (IsDoorOpen())
        {
            Phase = EncounterPhase.WaitingDoor;
            return;
        }
    }

    bool IsDoorOpen()
    {
        if (door == null) return true; // двери нет — сцена идёт сразу
        return door.IsOpen;
    }

    void PlayKnock()
    {
        if (knockClip == null) return;
        Vector3 pos = door != null ? door.transform.position : transform.position;
        AudioSource.PlayClipAtPoint(knockClip, pos, 0.9f);
    }

    /// <summary>Тряска камеры от удара в дверь.</summary>
    void ShakeCamera()
    {
        if (!shakeOnKnock || shakeMagnitude <= 0f) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject player = ResolvePlayer();
            if (player != null) cam = player.GetComponentInChildren<Camera>();
        }
        if (cam == null) return;

        CameraShake shake = cam.GetComponent<CameraShake>();
        if (shake == null) shake = cam.gameObject.GetComponentInChildren<CameraShake>();
        if (shake == null) shake = cam.gameObject.AddComponent<CameraShake>();
        shake.Shake(shakeDuration, shakeMagnitude);
    }

    /// <summary>Немец отпер дверь снаружи — замок снят.</summary>
    void UnlockDoor()
    {
        if (!unlockDoorOnKnock || door == null) return;
        door.Unlock();
    }

    void SpawnGerman()
    {
        if (germanPrefab == null)
        {
            if (!spawnFailed)
            {
                spawnFailed = true;
                Debug.LogError($"[PpshGerman] {name}: нет префаба немца — сцена встала. " +
                               "Перетащи модель в поле German Prefab.", this);
            }
            return;
        }
        spawnFailed = false;

        Vector3 approach = ResolveApproachPoint();
        Vector3 lookDir = Flat(PlayerPosition() - approach);
        if (lookDir.sqrMagnitude < 0.01f && door != null) lookDir = Flat(door.transform.forward);
        Quaternion rot = lookDir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
            : Quaternion.identity;

        german = Instantiate(germanPrefab, approach, rot);
        german.name = "Немец за дверью";
        beggar = german.GetComponent<GermanBeggar>();
        if (beggar == null) beggar = german.AddComponent<GermanBeggar>();
        beggar.SetPeaceful();
        // Поза ожидания у двери: клип выбирается здесь, в инспекторе сценария.
        if (beggarStandAnimation != null) beggar.standAnimation = beggarStandAnimation;

        // Идёт к двери и встаёт напротив.
        Vector3 stand = ResolveStandPoint();
        beggar.WalkTo(stand);
        if (!beggar.CanNavigate)
        {
            Debug.LogWarning("[PpshGerman] Немец появился вне NavMesh — сразу ставлю его к двери.", this);
            beggar.TeleportTo(stand);
        }
        walkDeadline = Time.time + Mathf.Max(1f, knockWalkTimeout);

        if (debugLogging)
            Debug.Log($"[PpshGerman] Немец идёт к двери: {approach} → {stand}.", this);
    }

    /// <summary>Откуда немец выходит — с улицы, с другой стороны двери.</summary>
    Vector3 ResolveApproachPoint()
    {
        if (germanApproachPoint != null)
        {
            // Точка должна быть рядом с дверью. Если объект сценария двигали —
            // точки уехали, ставим по-своему.
            float dist = Vector3.Distance(germanApproachPoint.position, DoorPosition());
            if (dist <= 20f) return germanApproachPoint.position;
            Debug.LogWarning($"[PpshGerman] Точка «Откуда подходит немец» уехала от двери на {dist:F0} м — " +
                             "используем расчётную. Передвинь её к двери вручную.", this);
        }

        Vector3 side = DoorSide();
        if (door != null) return door.transform.position + side * 9f;

        // Двери нет — выходит из-за спины игрока.
        return PlayerPosition() + side * 8f;
    }

    /// <summary>Точка у двери — напротив игрока, по ту сторону двери.</summary>
    Vector3 ResolveStandPoint()
    {
        if (germanStandPoint != null)
        {
            float dist = Vector3.Distance(germanStandPoint.position, DoorPosition());
            if (dist <= 5f) return germanStandPoint.position;
            Debug.LogWarning($"[PpshGerman] Точка «Немец у двери» уехала от двери на {dist:F0} м — " +
                             "используем расчётную. Передвинь её к двери вручную.", this);
        }

        Vector3 side = DoorSide();
        if (door != null) return door.transform.position + side * 1.4f;

        // Двери нет — встаёт перед игроком.
        return PlayerPosition() + side * 2.5f;
    }

    /// <summary>Где дверь (или точка, от которой считаем).</summary>
    Vector3 DoorPosition() =>
        door != null ? door.transform.position :
        (germanStandPoint != null ? germanStandPoint.position : PlayerPosition());

    /// <summary>
    /// Сторона двери, где снаружи (там, где нет игрока).
    /// Если двери нет — просто перед игроком.
    /// </summary>
    Vector3 DoorSide()
    {
        if (door != null) return OutsideSide();

        Vector3 fwd = playerTransform != null ? Flat(playerTransform.forward) : Flat(Vector3.forward);
        if (fwd.sqrMagnitude < 0.01f) fwd = Flat(Vector3.forward);
        return fwd.normalized;
    }

    // ------------------------------------------------------------------
    // Разговор
    // ------------------------------------------------------------------

    void UpdateTalking()
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive) return;
        if (talked) return;

        if (ResolvePlayer() == null) return;

        // Немец только что подошёл к двери — постучит, даже если игрок
        // открыл дверь раньше, чем он дошёл.
        if (!knockStarted && beggar != null && !beggar.IsWalking)
        {
            knockStarted = true;
            knocksDone = 1;
            PlayKnock();
            ShakeCamera();
        }

        if (!CanTalk())
        {
            // Игрок далеко — немец зовёт.
            if (Time.time >= nextKnockTime)
            {
                nextKnockTime = Time.time + 6f;
                Say("Погоди! Не уходи!", Color.yellow);
            }
            HideHint();
            return;
        }

        // Разговор закрыли по Esc — заново только по E, иначе диалог
        // открывался бы сам к себе, игрок бы зациклился на Esc.
        if (waitingForInteract)
        {
            ShowHint();
            if (Input.GetKeyDown(interactKey)) StartTalk();
            return;
        }

        StartTalk();
    }

    /// <summary>Можно говорить: игрок рядом с немцем или стоит у открытой двери.</summary>
    bool CanTalk()
    {
        if (german == null) return false;

        float dist = Vector3.Distance(playerTransform.position, german.transform.position);
        if (dist <= talkDistance) return true;

        if (door == null || !door.IsOpen) return false;

        // Немец ещё идёт к двери — разговор ждёт его.
        if (beggar != null && beggar.IsWalking) return false;

        // Игрок у открытой двери, немец у той же двери — говорим через проём.
        float doorDist = Vector3.Distance(playerTransform.position, door.transform.position);
        if (doorDist <= doorTalkDistance) return true;

        // Страховка: дверь открыта, немец дошёл, игрок рядом — начинаем разговор.
        bool germanArrived = knockStarted || (beggar != null && !beggar.IsWalking);
        if (germanArrived && dist <= forceTalkDistance)
        {
            Debug.Log($"[PpshGerman] Игрок в {dist:F1} м от немца — начинаем разговор по страховке.", this);
            return true;
        }
        return false;
    }

    void ShowHint()
    {
        DialogueManager m = DialogueManager.Instance;
        if (m == null || m.interactHint == null) return;
        m.interactHint.SetActive(true);
        if (m.interactHintText != null) m.interactHintText.text = interactMessage;
        hintShown = true;
    }

    void HideHint()
    {
        if (!hintShown) return;
        DialogueManager m = DialogueManager.Instance;
        if (m != null && m.interactHint != null) m.interactHint.SetActive(false);
        hintShown = false;
    }

    void StartTalk()
    {
        if (dialogue == null) dialogue = LoadDialogue();
        if (dialogue == null)
        {
            Debug.LogError($"[PpshGerman] {name}: диалог не найден (ресурс «{dialogueResourcePath}»).", this);
            talked = false;
            return;
        }
        if (dialogue.nodes == null || dialogue.nodes.Count == 0)
        {
            Debug.LogError($"[PpshGerman] В диалоге «{dialogue.name}» 0 узлов — импорт сломан. " +
                           "Переимпортируй ассет (Tools -> Диалоги -> Переимпортировать диалоги) " +
                           "или удали папку Library.", this);
            talked = false;
            return;
        }
        if (DialogueManager.Instance == null)
        {
            Debug.LogError("[PpshGerman] DialogueManager в сцене не найден — разговор не запустится.", this);
            talked = false;
            return;
        }
        if (DialogueManager.Instance.isDialogueActive) return;

        talked = true;
        waitingForInteract = false;
        HideHint();
        string resume = DialogueManager.GetSavedNodeID(dialogue);
        if (!string.IsNullOrEmpty(resume) && dialogue.GetNodeByID(resume) == null) resume = "";

        DialogueManager.Instance.StartDialogue(dialogue, null, resume);
        if (!DialogueManager.Instance.isDialogueActive)
        {
            talked = false;
            Debug.LogError("[PpshGerman] DialogueManager не взял диалог (нет UI канваса?).", this);
        }
    }

    void OnDialogueEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return;
        if (eventId == agreeEventId) agreed = true;
        else if (eventId == refuseEventId) refused = true;
    }

    void OnDialogueEnded()
    {
        HideHint();
        if (Phase != EncounterPhase.Talking && Phase != EncounterPhase.WaitingDoor) return;

        // Чужой диалог (с соседом, с отцом) — не наш разговор.
        DialogueManager m = DialogueManager.Instance;
        if (m != null && m.LastFinishedDialogue != null && dialogue != null &&
            m.LastFinishedDialogue != dialogue) return;

        if (refused)
        {
            Phase = EncounterPhase.Resolved;
            // Немец свистит и зовёт своих: засада выбегает на игрока.
            StartCoroutine(RaidRoutine());
            return;
        }

        if (agreed)
        {
            Phase = EncounterPhase.Resolved;
            StartCoroutine(LeaveRoutine());
            return;
        }

        // Разговор закрыли, ничего не выбрав: дальше — только по E.
        talked = false;
        waitingForInteract = true;
    }

    // ------------------------------------------------------------------
    // Согласился — немец уходит
    // ------------------------------------------------------------------

    IEnumerator LeaveRoutine()
    {
        GameState.SetFlag(FinishedFlag, true);
        if (beggar == null) yield break;

        Vector3 away = Flat(beggar.transform.position - PlayerPosition());
        if (away.sqrMagnitude < 0.01f) away = Flat(-playerTransform.forward);
        away.Normalize();

        Vector3 exit = beggar.transform.position + away * leaveDistance;
        beggar.walkSpeed = leaveSpeed;
        beggar.Leave(exit);

        float timeout = Time.time + 12f;
        while (beggar != null && !beggar.ReachedExit && Time.time < timeout)
        {
            yield return null;
        }

        if (beggar != null) Destroy(beggar.gameObject);
        Debug.Log("[PpshGerman] Немец получил еду и ушёл.", this);
    }

    // ------------------------------------------------------------------
    // Отказ — свист и засада
    // ------------------------------------------------------------------

    /// <summary>
    /// Игрок отказался: немец свистит, зовёт своих — на улице появляются немцы
    /// и бросаются на игрока. Они сразу бегут к игроку и раскладываются по фронту.
    /// </summary>
    IEnumerator RaidRoutine()
    {
        GameState.SetFlag(FinishedFlag, true);
        var spawned = new List<GameObject>();

        // Свист: немец зовёт своих.
        if (beggar != null)
        {
            beggar.Whistle(whistleRadius);
            if (beggarFights)
            {
                yield return new WaitForSeconds(0.4f);
                beggar.GoHostile(playerTransform);
            }
        }
        else
        {
            PlayWhistleAtSelf();
        }

        // Шум — чтобы услышали и те, кто уже в сцене.
        NoiseSystem.Emit(PositionOfGerman(), whistleRadius, german, 1f);

        Say("Свист! Свист!", Color.red);

        // Немцы на спавнпоинтах.
        for (int i = 0; i < raiderCount; i++)
        {
            GameObject raider = SpawnRaider(i);
            if (raider != null) spawned.Add(raider);
            if (raiderSpawnInterval > 0f) yield return new WaitForSeconds(raiderSpawnInterval);
        }

        if (raiderAggroDelay > 0f) yield return new WaitForSeconds(raiderAggroDelay);

        // Засада бросается на игрока.
        if (playerTransform != null)
        {
            foreach (GameObject raider in spawned)
            {
                if (raider == null) continue;
                EnemyAI ai = raider.GetComponent<EnemyAI>();
                if (ai != null) ai.ForceTarget(playerTransform);
            }
        }

        // Все немцы в сцене — на игрока.
        if (alertAllEnemies)
        {
            EnemyAI[] all = FindObjectsOfType<EnemyAI>(true);
            int forced = 0;
            foreach (EnemyAI ai in all)
            {
                if (ai == null) continue;
                if (german != null && ai.gameObject == german) continue;
                if (playerTransform == null) break;
                ai.ForceTarget(playerTransform);
                forced++;
            }
            Debug.Log($"[PpshGerman] Свист: на игрока брошено немцев — {forced}.", this);
        }
    }

    GameObject SpawnRaider(int index)
    {
        if (raiderPrefabs == null || raiderPrefabs.Length == 0)
        {
            if (index == 0)
                Debug.LogWarning($"[PpshGerman] {name}: префабы немцев не заданы — засады не будет. " +
                                 "Перетащи модель в поле Raider Prefabs.", this);
            return null;
        }

        GameObject prefab = raiderPrefabs[index % raiderPrefabs.Length];
        if (prefab == null) return null;

        Vector3 pos = ResolveSpawnPoint(index);
        Quaternion rot = Quaternion.LookRotation(Flat(PlayerPosition() - pos), Vector3.up);

        GameObject raider = Instantiate(prefab, pos, rot);
        raider.name = $"Немец (засада {index + 1})";
        return raider;
    }

    Vector3 ResolveSpawnPoint(int index)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Transform t = spawnPoints[(index + i) % spawnPoints.Length];
                if (t == null) continue;

                // Точка засады должна быть на улице рядом с домом. Уехала — ставим сами.
                float dist = Vector3.Distance(t.position, DoorPosition());
                if (dist <= 60f) return t.position;
            }
            Debug.LogWarning("[PpshGerman] Точки засады уехали от двери — берём засаду по-своему. " +
                             "Передвинь «Точки засады» ближе к дому.", this);
        }

        // Запасной вариант: кольцо вокруг игрока.
        Vector3 center = PlayerPosition();
        Vector3 forward = Flat(playerTransform != null ? playerTransform.forward : Vector3.forward);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();

        float angle = (index / (float)Mathf.Max(1, raiderCount)) * 360f;
        Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
        if (fallbackBehindPlayer && index % 2 == 1) dir = -dir;
        dir.Normalize();

        return center + dir * fallbackSpawnRadius;
    }

    void PlayWhistleAtSelf()
    {
        if (whistleClip == null) return;
        AudioSource.PlayClipAtPoint(whistleClip, PositionOfGerman(), 1f);
    }

    Vector3 PositionOfGerman() => german != null ? german.transform.position : transform.position;

    // ------------------------------------------------------------------
    // Служебное
    // ------------------------------------------------------------------

    void Say(string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        GameObject player = ResolvePlayer();
        if (player == null) return;
        try { FloatingText.Show(text, player.transform.position + Vector3.up * 2f, color); }
        catch { Debug.Log($"[PpshGerman] {text}"); }
    }

    GameObject ResolvePlayer()
    {
        if (cachedPlayer == null)
        {
            try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
            catch { cachedPlayer = null; }
            if (cachedPlayer == null) return null;
            playerTransform = cachedPlayer.transform;
        }
        return cachedPlayer;
    }

    Vector3 PlayerPosition() =>
        playerTransform != null ? playerTransform.position : transform.position;

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    DialogueData LoadDialogue()
    {
        if (string.IsNullOrEmpty(dialogueResourcePath)) return null;
        DialogueData data = Resources.Load<DialogueData>(dialogueResourcePath);
        if (data == null)
            Debug.LogError($"[PpshGerman] Диалог «{dialogueResourcePath}» не найден в Resources.", this);
        return data;
    }

    const string FinishedFlag = "ppsh_german_encounter_finished";

    /// <summary>Ручной запуск сценария (тест без подбора ППШ).</summary>
    [ContextMenu("Тест: начать сценарий сейчас")]
    public void TestBegin() => BeginEncounter();

    /// <summary>Тест: немец уже у двери и разговор начинается немедленно.</summary>
    [ContextMenu("Тест: немец у двери + разговор")]
    public void TestTalkNow()
    {
        if (Phase == EncounterPhase.WaitingTrigger) BeginEncounter();
        if (german == null)
        {
            ResolveDoor();
            SpawnGerman();
            if (beggar != null) beggar.TeleportTo(ResolveStandPoint());
        }
        knockStarted = true;
        knocksDone = Mathf.Max(1, knockCount);
        Phase = EncounterPhase.WaitingDoor;
        UpdateTalking();
    }

    /// <summary>Сбросить сценарий (новое прохождение).</summary>
    [ContextMenu("Сбросить сценарий")]
    public void ResetEncounter()
    {
        GameState.SetFlag(FinishedFlag, false);
        Phase = EncounterPhase.WaitingTrigger;
        agreed = false;
        refused = false;
        talked = false;
        waitingForInteract = false;
        knocksDone = 0;
        knockStarted = false;
        outsideSideReady = false;
        if (german != null) Destroy(german.gameObject);
        german = null;
        beggar = null;
    }

    void OnDrawGizmosSelected()
    {
        if (germanStandPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(germanStandPoint.position, 0.5f);
        }
        if (spawnPoints != null)
        {
            Gizmos.color = Color.red;
            foreach (Transform t in spawnPoints)
            {
                if (t == null) continue;
                Gizmos.DrawWireSphere(t.position, 0.6f);
            }
        }
    }
}
