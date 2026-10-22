using System.Collections;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Тонкий контроллер цепочки «дом деда»:
///
///  1. Игрок подбирает ППШ (событие InventorySystem.OnItemPickedUp).
///  2. Через пару секунд за дверью появляется «стоящий немец» (точка SpawnPoint)
///     и слышны три удара в дверь + лёгкая тряска камеры.
///  3. Игрок открывает дверь (DoorController.Opened) — автоматически
///     запускается готовый диалог с немцем (DialogueManager).
///  4. Как только диалог пошёл, модель немца прячется (окно диалога её не должно
///     перекрывать). После диалога она остаётся скрытой при любом исходе:
///     «уходит» — её и так нет, «отказ» — дальше работает засада
///     (PpshGermanEncounter.RaidRoutine).
///
/// Исходы диалога отрабатывает существующий механизм: CustomEvent в диалоге
/// (ppsh_german_agreed / ppsh_german_refused) → шина DialogueEventBus →
/// PpshGermanEncounter. Здесь новых исходов не изобретается.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Flame of History/Story Sequence DEDA")]
public sealed class StorySequence_DEDA : MonoBehaviour
{
    [Header("Триггер: подбор ППШ")]
    [Tooltip("Id предмета, после подбора которого начинается цепочка (ППШ-41 — «ppsh41»).")]
    public string triggerItemId = "ppsh41";
    [Tooltip("Следить за инвентарём, если ППШ выдали не подбором, а диалогом.")]
    public bool watchInventory = true;
    [Tooltip("Как часто проверять инвентарь, сек.")]
    [Min(0.1f)] public float watchInterval = 0.5f;

    [Header("Дверь")]
    [Tooltip("Дверь, открытие которой запускает разговор (DoorPivotDEDA).")]
    public DoorController door;

    [Header("Немец за дверью")]
    [Tooltip("Id точки SpawnPoint, где стоит немец (например GermanBehindDEDA).")]
    public string germanSpawnPointId = "GermanBehindDEDA";
    [Tooltip("Через сколько секунд после подбора ППШ немец появляется за дверью.")]
    [Min(0f)] public float spawnDelay = 1f;
    [Tooltip("Через сколько секунд после открытия двери начинается разговор.")]
    [Min(0f)] public float dialogueDelay = 2f;

    [Header("Стук в дверь")]
    [Tooltip("Сколько ударов в дверь.")]
    [Min(1)] public int knockCount = 3;
    [Tooltip("Пауза между ударами, сек.")]
    [Min(0.1f)] public float knockInterval = 0.45f;
    [Tooltip("Звук удара в дверь.")]
    public AudioClip knockClip;
    [Tooltip("Источник звука. Пусто — AudioSource.PlayClipAtPoint.")]
    public AudioSource knockSource;
    [Tooltip("Трясти камеру при каждом ударе (CameraShake).")]
    public bool shakeOnKnock = true;
    [Tooltip("Длительность тряски, сек.")]
    [Min(0.01f)] public float shakeDuration = 0.22f;
    [Tooltip("Сила тряски.")]
    [Min(0f)] public float shakeMagnitude = 0.12f;
    [Tooltip("Что показать игроку, когда стучат.")]
    public string knockMessage = "Кто-то стучит в дверь...";
    public Color knockMessageColor = new Color(1f, 0.9f, 0.5f);

    [Header("Диалог")]
    [Tooltip("Готовый диалог с немцем. Пусто — берём из Resources.")]
    public DialogueData dialogue;
    [Tooltip("Путь к диалогу в Resources (если поле Dialogue пусто).")]
    public string dialogueResourcePath = "PpshGermanEncounter/PpshGermanDialogue";

    [Header("Одноразово")]
    [Tooltip("Не проигрывать цепочку заново, если она уже отыграна (флаг GameState).")]
    public bool oncePerScene = true;
    [Tooltip("Флаг GameState. Тот же, что у PpshGermanEncounter — новый не заводим.")]
    public string finishedFlag = "ppsh_german_encounter_finished";

    private InventorySystem cachedInventory;
    private DialogueManager cachedManager;
    private Coroutine knockRoutine;
    private Coroutine dialogueRoutine;
    private GameObject cachedPlayer;
    private float nextWatchTime;
    private bool chainStarted;
    private bool dialogueStarted;
    private GameObject german;

    private void OnEnable()
    {
        HookInstances();

        if (cachedInventory != null)
        {
            cachedInventory.OnItemPickedUp -= OnItemPickedUp;
            cachedInventory.OnItemPickedUp += OnItemPickedUp;
        }
        if (cachedManager != null)
        {
            cachedManager.OnDialogueEnded -= OnDialogueEnded;
            cachedManager.OnDialogueEnded += OnDialogueEnded;
        }
        if (door != null)
        {
            door.Opened -= OnDoorOpened;
            door.Opened += OnDoorOpened;
        }
    }

    private void OnDisable()
    {
        if (cachedInventory != null) cachedInventory.OnItemPickedUp -= OnItemPickedUp;
        if (cachedManager != null) cachedManager.OnDialogueEnded -= OnDialogueEnded;
        if (door != null) door.Opened -= OnDoorOpened;
        if (knockRoutine != null)
        {
            StopCoroutine(knockRoutine);
            knockRoutine = null;
        }
        if (dialogueRoutine != null)
        {
            StopCoroutine(dialogueRoutine);
            dialogueRoutine = null;
        }
    }

    private void Start()
    {
        HookInstances();
        ResolvePlayer();

        if (dialogue == null) dialogue = LoadDialogue();

        if (oncePerScene && GameState.GetFlag(finishedFlag))
        {
            Debug.Log($"[StoryDEDA] {name}: цепочка уже отыграна (флаг «{finishedFlag}») — пропускаю.", this);
            enabled = false;
            return;
        }

        if (door == null)
            Debug.LogWarning($"[StoryDEDA] {name}: дверь не назначена — разговор не запустится.", this);
        if (dialogue == null)
            Debug.LogError($"[StoryDEDA] {name}: диалог не найден (ресурс «{dialogueResourcePath}»).", this);
    }

    private void HookInstances()
    {
        if (cachedInventory == null)
        {
            cachedInventory = InventorySystem.Instance;
            if (cachedInventory == null) cachedInventory = FindObjectOfType<InventorySystem>();
            if (cachedInventory != null)
            {
                cachedInventory.OnItemPickedUp -= OnItemPickedUp;
                cachedInventory.OnItemPickedUp += OnItemPickedUp;
            }
        }

        if (cachedManager == null)
        {
            cachedManager = DialogueManager.Instance;
            if (cachedManager == null) cachedManager = FindObjectOfType<DialogueManager>();
            if (cachedManager != null)
            {
                cachedManager.OnDialogueEnded -= OnDialogueEnded;
                cachedManager.OnDialogueEnded += OnDialogueEnded;
            }
        }
    }

    private void Update()
    {
        if (!watchInventory || chainStarted) return;
        if (Time.time < nextWatchTime) return;
        nextWatchTime = Time.time + Mathf.Max(0.1f, watchInterval);

        if (cachedInventory == null)
        {
            cachedInventory = InventorySystem.Instance;
            if (cachedInventory == null) return;
            cachedInventory.OnItemPickedUp -= OnItemPickedUp;
            cachedInventory.OnItemPickedUp += OnItemPickedUp;
        }

        if (cachedInventory.CountItemById(triggerItemId) > 0) BeginChain();
    }

    /// <summary>Игрок подобрал предмет — проверяем, наш ли это триггер.</summary>
    private void OnItemPickedUp(ItemData item, int amount)
    {
        if (item == null || string.IsNullOrEmpty(triggerItemId)) return;
        if (item.Id != triggerItemId) return;
        BeginChain();
    }

    /// <summary>Старт цепочки: появление немца и стук в дверь.</summary>
    public void BeginChain()
    {
        if (chainStarted) return;
        chainStarted = true;
        Debug.Log($"[StoryDEDA] {name}: цепочка начата (подобран «{triggerItemId}»).", this);

        // Край: дверь уже открыта — стучать в открытую дверь нечего.
        // Немец всё равно появляется, разговор начинается через dialogueDelay.
        if (door != null && door.IsOpen)
        {
            if (knockRoutine == null) knockRoutine = StartCoroutine(OpenDoorDialogueRoutine());
            return;
        }

        if (knockRoutine == null) knockRoutine = StartCoroutine(KnockAndSpawnRoutine());
    }

    /// <summary>Дверь уже открыта: ждём dialogueDelay и начинаем разговор.</summary>
    private IEnumerator OpenDoorDialogueRoutine()
    {
        yield return new WaitForSeconds(spawnDelay);
        EnsureGermanSpawned();
        yield return new WaitForSeconds(Mathf.Max(0f, dialogueDelay));
        knockRoutine = null;
        StartDialogue();
    }

    /// <summary>Немец появляется за дверью, потом — три удара и тряска камеры.</summary>
    private IEnumerator KnockAndSpawnRoutine()
    {
        if (spawnDelay > 0f) yield return new WaitForSeconds(spawnDelay);
        if (dialogueStarted) yield break;

        EnsureGermanSpawned();

        int knocks = Mathf.Max(1, knockCount);
        for (int i = 0; i < knocks; i++)
        {
            if (dialogueStarted) yield break;

            PlayKnock();
            ShakeCamera();
            Say(knockMessage, knockMessageColor);

            if (i < knocks - 1) yield return new WaitForSeconds(Mathf.Max(0.1f, knockInterval));
        }

        knockRoutine = null;
    }

    /// <summary>Поднять немца в точке SpawnPoint (один раз).</summary>
    public GameObject EnsureGermanSpawned()
    {
        if (german != null) return german;
        // Немец появляется только после подбора ППШ: иначе он застаивает
        // одноразовую точку и диалог начинается на пустом месте.
        if (!chainStarted) return null;

        german = SpawnPointRegistry.SpawnById(germanSpawnPointId);
        if (german == null)
            Debug.LogWarning($"[StoryDEDA] {name}: точка «{germanSpawnPointId}» не дала немца — " +
                             "проверь, что точка есть в сцене, Id совпадает и префаб назначен.", this);
        return german;
    }

    /// <summary>Дверь открыли игроком — через dialogueDelay начинается разговор.</summary>
    private void OnDoorOpened()
    {
        // Сцена начинается только после подбора ППШ. Раньше диалог стартовал
        // при первом открытии двери (войти в подвал) — немец ещё не появлялся.
        if (!chainStarted) return;
        if (dialogueStarted) return;

        EnsureGermanSpawned();
        if (dialogueRoutine == null) dialogueRoutine = StartCoroutine(DialogueAfterDelayRoutine());
    }

    /// <summary>Ждём, пока дверь распахнётся полностью, затем разговор.</summary>
    private IEnumerator DialogueAfterDelayRoutine()
    {
        // Раньше разговор стартовал по событию открытия, пока полотно ещё
        // двигалось. Теперь ждём конца анимации двери.
        while (door != null && door.IsOpen && !door.IsFullyOpen)
            yield return null;

        if (door != null && !door.IsOpen)
        {
            dialogueRoutine = null;
            yield break;
        }

        if (dialogueDelay > 0f)
            yield return new WaitForSeconds(dialogueDelay);

        dialogueRoutine = null;
        StartDialogue();
    }

    /// <summary>Запустить готовый диалог через существующий DialogueManager.</summary>
    public bool StartDialogue()
    {
        if (dialogueStarted) return false;

        if (dialogue == null) dialogue = LoadDialogue();
        if (dialogue == null)
        {
            Debug.LogError($"[StoryDEDA] {name}: диалог не найден (ресурс «{dialogueResourcePath}»).", this);
            return false;
        }
        if (dialogue.nodes == null || dialogue.nodes.Count == 0)
        {
            Debug.LogError($"[StoryDEDA] {name}: в диалоге «{dialogue.name}» 0 узлов — разговор не начнётся.", this);
            return false;
        }

        DialogueManager manager = cachedManager != null ? cachedManager : DialogueManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[StoryDEDA] DialogueManager в сцене не найден — разговор не запустится.", this);
            return false;
        }
        if (manager.isDialogueActive) return false;

        // Диалог уже пройден целиком — второй раз не начинаем.
        if (DialogueManager.IsDialogueDone(dialogue))
        {
            Debug.Log($"[StoryDEDA] {name}: диалог «{dialogue.dialogueName}» уже пройден — не переигрываю.", this);
            return false;
        }

        string resume = DialogueManager.GetSavedNodeID(dialogue);
        if (!string.IsNullOrEmpty(resume) && dialogue.GetNodeByID(resume) == null) resume = "";

        dialogueStarted = true;
        HideGerman();
        manager.StartDialogue(dialogue, null, resume);

        if (!manager.isDialogueActive)
        {
            dialogueStarted = false;
            Debug.LogError("[StoryDEDA] DialogueManager не взял диалог (нет UI канваса?).", this);
            return false;
        }

        Debug.Log($"[StoryDEDA] {name}: начат разговор «{dialogue.dialogueName}».", this);
        return true;
    }

    /// <summary>Прячем модель немца: в окне диалога она лишняя.</summary>
    public void HideGerman()
    {
        if (german == null) return;
        if (german.activeSelf) german.SetActive(false);
    }

    /// <summary>Разговор закончился: модель остаётся скрытой, цепочка помечена сделанной.</summary>
    private void OnDialogueEnded()
    {
        HideGerman();

        DialogueManager manager = cachedManager != null ? cachedManager : DialogueManager.Instance;
        if (manager == null || dialogue == null) return;
        if (manager.LastFinishedDialogue != dialogue) return;

        if (manager.LastFinishedCompleted)
        {
            if (oncePerScene) GameState.SetFlag(finishedFlag, true);
        }
        else
        {
            // Выход по Esc: разговор можно продолжить — ждём следующего
            // открытия двери, диалог стартует заново.
            dialogueStarted = false;
        }

        Debug.Log($"[StoryDEDA] {name}: разговор окончен " +
                  $"{(manager.LastFinishedCompleted ? "(пройден до конца)" : "(выход по Esc)")}.", this);
    }

    private void PlayKnock()
    {
        if (knockClip == null) return;
        Vector3 position = door != null ? door.transform.position : transform.position;

        if (knockSource != null)
        {
            knockSource.pitch = Random.Range(0.94f, 1.06f);
            knockSource.PlayOneShot(knockClip);
            return;
        }
        AudioSource.PlayClipAtPoint(knockClip, position, 0.9f);
    }

    /// <summary>Тряска камеры от удара в дверь (существующий CameraShake).</summary>
    private void ShakeCamera()
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

    private void Say(string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        GameObject player = ResolvePlayer();
        if (player == null) return;
        try { FloatingText.Show(text, player.transform.position + Vector3.up * 2f, color); }
        catch { Debug.Log($"[StoryDEDA] {text}"); }
    }

    private GameObject ResolvePlayer()
    {
        if (cachedPlayer == null)
        {
            try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
            catch { cachedPlayer = null; }
        }
        return cachedPlayer;
    }

    private DialogueData LoadDialogue()
    {
        if (string.IsNullOrEmpty(dialogueResourcePath)) return null;
        DialogueData data = Resources.Load<DialogueData>(dialogueResourcePath);
        if (data == null)
            Debug.LogError($"[StoryDEDA] {name}: диалог «{dialogueResourcePath}» не найден в Resources.", this);
        return data;
    }

    private void OnDrawGizmosSelected()
    {
        if (door == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
        Gizmos.DrawLine(transform.position, door.transform.position);
        Gizmos.DrawWireSphere(door.transform.position, 0.4f);
    }
}
