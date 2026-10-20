using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }
    private static readonly RaycastHit[] LineOfSightHits = new RaycastHit[64];
    private static readonly IComparer<RaycastHit> HitDistanceComparer =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    [Header("UI элементы")]
    public GameObject dialoguePanel;
    public TextMeshProUGUI speakerNameText;
    public Image speakerPortraitImage;
    public TextMeshProUGUI dialogueText;
    public GameObject choicesPanel;
    public Button choiceButtonPrefab;
    public Transform choicesContainer;

    [Header("Свои кнопки (канвас пользователя)")]
    [Tooltip("Включено — варианты показываются на готовых кнопках сцены (Button1..6), " +
             "а не создаются из шаблона.")]
    public bool useStaticChoiceButtons = false;
    [Tooltip("Кнопки вариантов в порядке Button1, Button2, Button3. Лишние прячутся сами.")]
    public Button[] staticChoiceButtons = new Button[0];
    [Tooltip("Подписи кнопок (тексты 1, 2, 3 внутри). Если пусто — найдутся сами.")]
    public TextMeshProUGUI[] staticChoiceLabels = new TextMeshProUGUI[0];
    [Tooltip("Спрайт заблокированного варианта (UI5). Пусто — обычный спрайт + серый цвет.")]
    public Sprite lockedChoiceSprite;
    [Tooltip("Цвет подписи заблокированного варианта.")]
    public Color lockedChoiceLabelColor = new Color(0.55f, 0.55f, 0.6f, 0.8f);
    public GameObject interactHint;
    public TextMeshProUGUI interactHintText;
    public CanvasGroup dialogueCanvasGroup;

    [Header("Глобальные настройки")]
    [Tooltip("Задержка между символами в секундах. Меньше = быстрее (0.01 = очень быстро)")]
    public float defaultTextSpeed = 0.02f;
    public KeyCode advanceKey = KeyCode.Space;
    public bool pauseGameDuringDialogue = true;
    public bool allowSkipTyping = true;
    public bool showCursorDuringDialogue = true;
    public KeyCode exitDialogueKey = KeyCode.Escape;

    [Header("Анимации")]
    public float fadeInDuration = 0.4f;
    public Vector3 panelStartScale = new Vector3(0.8f, 0.8f, 1f);
    public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Дополнительно")]
    [Tooltip("Задержка перед показом кнопок после завершения текста")]
    public float choicesDelay = 0.5f;
    [Tooltip("Показывать мигающий курсор в конце строки")]
    public bool showTypingCursor = true;
    [Tooltip("Символ курсора")]
    public string cursorSymbol = "_";
    [Tooltip("Скорость мигания курсора")]
    public float cursorBlinkSpeed = 0.5f;
    [Tooltip("Звук печати (необязательно)")]
    public AudioClip typingSound;
    [Range(0f, 1f)]
    public float typingSoundVolume = 0.3f;
    public int typingSoundFrequency = 3;

    [Header("Длинные реплики")]
    [Tooltip("Включено — длинные реплики печатаются чуть меньшим шрифтом, " +
             "чтобы текст не вылезал из окна. Короткие реплики не трогаем.")]
    public bool shrinkLongText = true;
    [Tooltip("Длина реплики в символах, после которой шрифт уменьшается (первая ступень).")]
    public int longTextThreshold = 200;
    [Tooltip("Множитель шрифта для длинных реплик.")]
    [Range(0.5f, 1f)] public float longTextScale = 0.85f;
    [Tooltip("Длина реплики для второй ступени уменьшения.")]
    public int veryLongThreshold = 280;
    [Tooltip("Множитель шрифта для очень длинных реплик.")]
    [Range(0.5f, 1f)] public float veryLongScale = 0.72f;

    [Header("Реплика игрока (эхо выбора)")]
    [Tooltip("Показать выбранный ответ как реплику героя (отдельный шаг с печатью), " +
             "а не перескакивать сразу на ответ NPC. Лечит «фраза героя не видна».")]
    public bool echoPlayerChoice = true;
    [Tooltip("Имя героя для эха выбора (его реплик).")]
    public string playerSpeakerName = "Алесь";
    [Tooltip("Цвет реплики героя. Прозрачный (по умолчанию) = авто-палитра по имени.")]
    public Color playerSpeakerColor = new Color(0f, 0f, 0f, 0f);

    [Header("Имя в тексте")]
    [Tooltip("Включено — имя говорящего дописывается в начало реплики " +
             "(«Степан: ...»). Нужно канвасам БЕЗ отдельного поля имени " +
             "(UserDialogueUI включает сам). Если поле имени привязано — " +
             "префикс не ставится, чтобы не двоилось.")]
    public bool prefixSpeakerNameInText = false;

    private DialogueData currentDialogue;
    private DialogueNode currentNode;
    public bool isDialogueActive = false;
    private bool isTyping = false;
    private Coroutine typingCoroutine;
    private Coroutine autoAdvanceCoroutine;
    private DialogueTrigger currentTrigger;
    private bool previousCursorVisible;
    private CursorLockMode previousLockMode;
    private float previousTimeScale = 1f;
    private Coroutine fadeRoutine;
    private Coroutine cursorBlinkCoroutine;
    private bool isShowingChoices = false;

    // Эхо выбора: currentNode остаётся старым, цель хранится отдельно.
    private bool isShowingEcho = false;
    private DialogueNode pendingEchoTarget;
    private string echoText = "";
    private Coroutine echoCoroutine;

    // Контроллеры, выключенные нами на время диалога.
    private readonly System.Collections.Generic.List<MonoBehaviour> disabledControllersByUs =
        new System.Collections.Generic.List<MonoBehaviour>();

    // Исходные цвета подписей кнопок.
    private readonly System.Collections.Generic.Dictionary<TextMeshProUGUI, Color> labelBaseColors =
        new System.Collections.Generic.Dictionary<TextMeshProUGUI, Color>();

    private int revealedCharacters = 0;
    private string cursorBaseText = "";
    // Rich-text нельзя резать через Substring — режим maxVisibleCharacters.
    private bool textHasRichTags = false;

    private AudioSource typingAudioSource;

    // Базовый размер шрифта для shrinkLongText.
    private float baseFontSize = -1f;

    /// <summary>Идёт ли посимвольная печать прямо сейчас.</summary>
    public bool IsTyping => isTyping;
    /// <summary>Показаны ли варианты ответа.</summary>
    public bool IsShowingChoices => isShowingChoices;
    /// <summary>Текущий узел диалога (может быть null).</summary>
    public DialogueNode CurrentNode => currentNode;
    /// <summary>Текущий диалог (может быть null).</summary>
    public DialogueData CurrentDialogue => currentDialogue;
    /// <summary>Показана ли эхо-реплика героя (выбранный ответ).</summary>
    public bool IsShowingEcho => isShowingEcho;
    /// <summary>Диалог, который только что закрылся (для цепочек/хуков). Null — ни один ещё не закрывался.</summary>
    public DialogueData LastFinishedDialogue { get; private set; }
    /// <summary>Был ли последний закрытый диалог пройден до конца (true) или брошен по Esc (false).</summary>
    public bool LastFinishedCompleted { get; private set; }

    /// <summary>Диалог начался. Для кинематографики, звука, аналитики.</summary>
    public event System.Action OnDialogueStarted;
    /// <summary>Диалог закончился.</summary>
    public event System.Action OnDialogueEnded;
    /// <summary>Перешли к новому узлу.</summary>
    public event System.Action<DialogueNode> OnNodeChanged;

    /// <summary>False — промежуточные сейвы прогресса/выборов пишут только в память
    /// (Set без PlayerPrefs.Save). На диск сбрасывает SaveManager.Checkpoint(). Управляется SaveManager.</summary>
    public static bool EnableDiskWrite = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        GameState.Load();
        QuestSystem.Load();
    }

    void OnApplicationQuit()
    {
        GameState.Save();
        QuestSystem.Save();
        PlayerPrefs.Save();
    }

    void OnDisable()
    {
        // Выключение посреди диалога не должно оставлять следующую сцену на паузе.
        if (isDialogueActive)
        {
            if (pauseGameDuringDialogue) Time.timeScale = previousTimeScale;
            if (showCursorDuringDialogue)
            {
                Cursor.visible = previousCursorVisible;
                Cursor.lockState = previousLockMode;
            }
            isDialogueActive = false;
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (choicesPanel != null) choicesPanel.SetActive(false);
        }
        PlayerInputLock.ReleaseAll(this);
        if (!PlayerInputLock.MovementLocked)
        {
            foreach (var fps in disabledControllersByUs)
                if (fps != null) fps.enabled = true;
        }
        disabledControllersByUs.Clear();
    }
    const string ProgressPrefix = "flame_dlg_";

    /// <summary>Гарантия интерфейса перед стартом.</summary>
    void EnsureUserInterface()
    {
        if (dialogueText != null) return;

        UserDialogueUI ui = ExistingAdapter();
        if (ui == null)
        {
            TryAutoWireUserCanvas();
            return;
        }
        ui.RetryWire();
    }

    /// <summary>Адаптер в сцене (включая скрытые и выключенные).</summary>
    static UserDialogueUI ExistingAdapter()
    {
        foreach (UserDialogueUI ui in FindObjectsOfType<UserDialogueUI>(true))
        {
            if (ui != null) return ui;
        }
        return null;
    }

    /// <summary>Сам находит канвас пользователя по Button1 и вешает адаптер.</summary>
    void TryAutoWireUserCanvas()
    {
        if (dialogueText != null) return;
        if (ExistingAdapter() != null) return;

        Transform btn = FindInSceneByName("Button1");
        if (btn == null) return;

        // Хост — верхний предок кнопки, никогда сама кнопка.
        Transform top = btn;
        while (top.parent != null) top = top.parent;

        if (top.GetComponent<UserDialogueUI>() != null) return;
        top.gameObject.AddComponent<UserDialogueUI>();
        Debug.Log($"[DialogueManager] Нашёл твой канвас ({top.gameObject.name}) — адаптер подключён сам.", this);
        foreach (Canvas c in FindObjectsOfType<Canvas>(true))
        {
            if (c != null && c.gameObject.name == "DialogueCanvas" && c.gameObject != top.gameObject)
                c.gameObject.SetActive(false);
        }
    }

    static Transform FindInSceneByName(string name)
    {
        foreach (Transform t in FindObjectsOfType<Transform>(true))
        {
            if (t != null && t.name == name) return t;
        }
        return null;
    }

    /// <summary>Спрятать фиксированные кнопки поштучно (панель целиком не трогаем).</summary>
    void HideStaticButtons()
    {
        ClearSelection();
        if (staticChoiceButtons == null) return;
        foreach (Button b in staticChoiceButtons)
        {
            if (b != null && b.gameObject.activeSelf)
                b.gameObject.SetActive(false);
        }
    }

    static string DialogueKey(DialogueData dialogue)
    {
        if (dialogue == null) return "";
        return string.IsNullOrEmpty(dialogue.name) ? dialogue.dialogueName : dialogue.name;
    }

    /// <summary>Узел, на котором остановились в прошлый раз ("" — нет сохранения).</summary>
    public static string GetSavedNodeID(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return "";
        return PlayerPrefs.GetString(ProgressPrefix + "node_" + key, "");
    }

    public static bool IsDialogueDone(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return false;
        return PlayerPrefs.GetInt(ProgressPrefix + "done_" + key, 0) == 1;
    }

    public static void MarkDialogueDone(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.SetString(ProgressPrefix + "node_" + key, "");
        PlayerPrefs.SetInt(ProgressPrefix + "done_" + key, 1);
        ClearUsedChoices(dialogue);
        if (EnableDiskWrite) PlayerPrefs.Save();
    }

    static void SaveDialogueProgress(DialogueData dialogue, string nodeID)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(nodeID)) return;
        PlayerPrefs.SetString(ProgressPrefix + "node_" + key, nodeID);
        if (EnableDiskWrite) PlayerPrefs.Save();
    }

    static void ClearDialogueProgress(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.SetString(ProgressPrefix + "node_" + key, "");
        if (EnableDiskWrite) PlayerPrefs.Save();
    }

    /// <summary>Сбросить прогресс и прохождение одного диалога (для тестов/новой игры).</summary>
    public static void ResetDialogue(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.DeleteKey(ProgressPrefix + "node_" + key);
        PlayerPrefs.DeleteKey(ProgressPrefix + "done_" + key);
        ClearUsedChoices(dialogue);
        // Сброс — явное действие (новая игра/тесты), пишем на диск всегда.
        PlayerPrefs.Save();
    }

    // Одноразовые кнопки: "nodeID\nchoiceText" в PlayerPrefs, показ блокировкой, а не скрытием.
    const string UsedPrefix = "flame_dlg_used_";

    static string MakeUsedKey(string nodeID, string choiceText) =>
        (nodeID ?? "") + "\n" + (choiceText ?? "");

    /// <summary>Все использованные выборы диалога.</summary>
    public static System.Collections.Generic.HashSet<string> GetUsedChoices(DialogueData dialogue)
    {
        var set = new System.Collections.Generic.HashSet<string>();
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return set;
        string raw = PlayerPrefs.GetString(UsedPrefix + key, "");
        if (string.IsNullOrEmpty(raw)) return set;
        foreach (string e in raw.Split(new[] { "\u001f" }, System.StringSplitOptions.RemoveEmptyEntries))
            set.Add(e);
        return set;
    }

    static void SaveUsedChoices(DialogueData dialogue,
        System.Collections.Generic.HashSet<string> set)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.SetString(UsedPrefix + key,
            string.Join("\u001f", new System.Collections.Generic.List<string>(set).ToArray()));
        if (EnableDiskWrite) PlayerPrefs.Save();
    }

    /// <summary>Пометить выбор использованным (кнопка гаснет навсегда).</summary>
    public static void MarkChoiceUsed(DialogueData dialogue, string nodeID, string choiceText)
    {
        if (dialogue == null) return;
        var set = GetUsedChoices(dialogue);
        if (set.Add(MakeUsedKey(nodeID, choiceText)))
            SaveUsedChoices(dialogue, set);
    }

    /// <summary>Нажимался ли уже этот выбор.</summary>
    public static bool IsChoiceUsed(DialogueData dialogue, string nodeID, string choiceText)
    {
        if (dialogue == null) return false;
        return GetUsedChoices(dialogue).Contains(MakeUsedKey(nodeID, choiceText));
    }

    /// <summary>Одноразовый ли выбор (гаснет после нажатия).
    /// Анти-фарм: одноразовые только выборы с реальным эффектом —
    /// команды (выдача/квесты/флаги), требования предмета/квеста.
    /// Чистая навигация («спросить ещё», хаб-возвраты N3&lt;-&gt;N4) и выходы
    /// (endDialogue) остаются доступны повторно. Иначе игрок, вышедший через
    /// «(уйти)» до выполнения квеста, в следующий раз видит обе кнопки
    /// заблокированными — софтлок.</summary>
    static bool IsSingleUseChoice(DialogueChoice choice)
    {
        if (choice == null) return false;
        if (choice.endDialogue) return false;
        if (!string.IsNullOrEmpty(choice.requiredItemId)) return true;
        if (choice.requiredItems != null && choice.requiredItems.Count > 0) return true;
        if (!string.IsNullOrEmpty(choice.requiredQuestId)) return true;
        if (choice.onSelectCommands != null)
        {
            foreach (var cmd in choice.onSelectCommands)
            {
                if (cmd == null) continue;
                if (!string.IsNullOrEmpty(cmd.stringParam)) return true;
                if (cmd.gameObjectParam != null) return true;
                if (cmd.clipParam != null) return true;
                if (cmd.spriteParam != null) return true;
            }
        }
        return false;
    }

    /// <summary>Нажимался ли уже ОДНОРАЗОВЫЙ выбор (для блокировки кнопки).</summary>
    static bool IsChoiceSpent(DialogueData dialogue, string nodeID, DialogueChoice choice)
    {
        if (dialogue == null || choice == null) return false;
        if (!IsSingleUseChoice(choice)) return false;
        return IsChoiceUsed(dialogue, nodeID, choice.choiceText);
    }

    static void ClearUsedChoices(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.DeleteKey(UsedPrefix + key);
    }

    /// <summary>С какой реплики продолжать после Esc: с той же, что была показана.
    /// Раньше возвращался СЛЕДУЮЩИЙ узел (показанное пропускалось), из-за чего
    /// повторный разговор начинался «со второго диалога». Теперь — повтор.</summary>
    static string ComputeResumeNodeID(DialogueNode echoTarget, DialogueNode lastNode)
    {
        if (echoTarget != null) return echoTarget.nodeID;
        if (lastNode == null) return null;
        return lastNode.nodeID;
    }


    // =====================================================================
    // Говорящие: цвет имени + портрет (или заглушка, пока нет арта).
    // =====================================================================
    /// <summary>Цвет имени: явный из узла или авто-палитра по имени.</summary>
    public static Color ResolveSpeakerColor(DialogueNode node)
    {
        if (node == null) return Color.white;
        if (node.speakerColor.a > 0.01f) return node.speakerColor;
        return SpeakerPortrait.GetSpeakerColor(node.speakerName);
    }

    /// <summary>Цвет реплики героя (эхо выбора): явный или авто-палитра по имени.</summary>
    public Color ResolvePlayerColor()
    {
        if (playerSpeakerColor.a > 0.01f) return playerSpeakerColor;
        if (string.IsNullOrEmpty(playerSpeakerName)) return Color.white;
        return SpeakerPortrait.GetSpeakerColor(playerSpeakerName);
    }

    /// <summary>
    /// Совпал ли статус квеста с требованием выбора:
    /// 0 — взят (активен или выполнен), 1 — активен, 2 — выполнен, 3 — провален.
    /// </summary>
    public static bool IsQuestStateMatch(string questId, int requiredState)
    {
        if (string.IsNullOrEmpty(questId)) return true;
        int s = QuestSystem.GetState(questId);
        if (requiredState == 0) return s == QuestSystem.StateActive || s == QuestSystem.StateDone;
        return s == requiredState;
    }

    /// <summary>
    /// Держит ли квест-гейт диалог: задан completionQuestId, а квест не выполнен.
    /// Пока держит — диалог не засчитывается и цепочка дальше не идёт.
    /// </summary>
    public static bool IsQuestGateBlocking(DialogueData dialogue)
    {
        if (dialogue == null || string.IsNullOrEmpty(dialogue.completionQuestId)) return false;
        return !QuestSystem.IsDone(dialogue.completionQuestId);
    }

    /// <summary>Суффикс подсказки E, если диалог упёрт в квест-гейт.</summary>
    public static string GetQuestGateHint(DialogueData dialogue)
    {
        if (!IsQuestGateBlocking(dialogue)) return "";
        return " — нужен: " + QuestSystem.GetTitle(dialogue.completionQuestId);
    }

    /// <summary>Виден ли игрок с точки (рейкаст с высоты глаз, триггеры игнорятся).</summary>
    public static bool HasLineOfSight(Vector3 from, GameObject playerGO,
        GameObject selfGO, LayerMask blockMask)
    {
        if (playerGO == null) return false;
        Vector3 target = playerGO.transform.position + Vector3.up * 1.3f;
        Vector3 dir = target - from;
        float dist = dir.magnitude;
        if (dist <= 0.05f) return true;
        dir /= dist;

        Ray ray = new Ray(from, dir);
        int hitCount = Physics.RaycastNonAlloc(
            ray, LineOfSightHits, dist, blockMask.value, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = LineOfSightHits;
        if (hitCount == LineOfSightHits.Length)
        {
            hits = Physics.RaycastAll(ray, dist, blockMask.value, QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
        }
        System.Array.Sort(hits, 0, hitCount, HitDistanceComparer);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null) continue;
            GameObject go = h.collider.gameObject;
            if (IsSelfOrChild(go, selfGO)) continue;
            if (IsSelfOrChild(go, playerGO)) return true;
            return false;
        }
        return true;
    }
    static bool IsSelfOrChild(GameObject go, GameObject root)
    {
        if (go == null || root == null) return false;
        Transform t = go.transform;
        while (t != null)
        {
            if (t.gameObject == root) return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>Текст для показа: при префиксе и без поля имени — «Имя: реплика».</summary>
    string WithSpeaker(string text, string speakerName)
    {
        if (!prefixSpeakerNameInText || speakerNameText != null) return text ?? "";
        if (string.IsNullOrEmpty(speakerName)) return text ?? "";
        return speakerName + ": " + (text ?? "");
    }

    /// <summary>
    /// Текст узла с учётом немецкого акцента: реплика немца звучит с выговором.
    /// Seed считаем от nodeID — одна и та же реплика читается одинаково.
    /// </summary>
    public static string NodeText(DialogueNode node)
    {
        if (node == null) return "";
        if (!node.germanAccent) return node.dialogueText ?? "";
        return GermanAccent.Apply(node.dialogueText, StableHash(node.nodeID));
    }

    /// <summary>Стабильный хэш строки (System.String.GetHashCode меняется между запусками).</summary>
    static int StableHash(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int h = 17;
        foreach (char c in s) h = unchecked(h * 31 + c);
        return h;
    }

    /// <summary>Портрет: спрайт из узла или круглая заглушка в цвете персонажа.</summary>
    public static Sprite ResolveSpeakerPortrait(DialogueNode node)
    {
        if (node == null) return null;
        if (node.speakerPortrait != null) return node.speakerPortrait;
        if (string.IsNullOrEmpty(node.speakerName)) return null;
        return SpeakerPortrait.GetPlaceholder(node.speakerName);
    }

    void Start()
    {
        TryAutoWireUserCanvas();

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
            if (dialogueCanvasGroup == null)
                dialogueCanvasGroup = dialoguePanel.GetComponent<CanvasGroup>();
            if (dialogueCanvasGroup == null)
                dialogueCanvasGroup = dialoguePanel.AddComponent<CanvasGroup>();
            dialogueCanvasGroup.alpha = 0f;
            dialoguePanel.transform.localScale = panelStartScale;
        }
        if (choicesPanel != null) choicesPanel.SetActive(false);
        if (interactHint != null) interactHint.SetActive(false);
    }

    void Update()
    {
        if (!isDialogueActive) return;

        if (Input.GetKeyDown(exitDialogueKey))
        {
            EndDialogue(false);
            return;
        }

        // Пока выборы на экране — пробел не двигает диалог, только мышь.
        if (isShowingChoices) return;

        if (isTyping && allowSkipTyping && Input.GetKeyDown(advanceKey))
        {
            CompleteTyping();
        }
        else if (!isTyping && Input.GetKeyDown(advanceKey))
        {
            AdvanceDialogue();
        }
    }

    /// <summary>Сбросить фокус: иначе пробел сработает как клик по кнопке.</summary>
    static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Запретить кнопке фокус с клавиатуры: выбор только мышкой.</summary>
    static void DisableKeyboardFocus(Button button)
    {
        if (button == null) return;
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
    }

    /// <summary>Начать диалог. startNodeID — продолжить с узла, пусто — с начала.</summary>
    public void StartDialogue(DialogueData dialogue, DialogueTrigger trigger, string startNodeID = null)
    {
        if (isDialogueActive) return;
        if (dialogue == null)
        {
            Debug.LogError("[DialogueManager] StartDialogue: dialogue = null.", this);
            return;
        }

        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (dialoguePanel != null && dialogueCanvasGroup != null)
        {
            dialoguePanel.SetActive(true);
            dialogueCanvasGroup.alpha = 0f;
            dialoguePanel.transform.localScale = panelStartScale;
        }

        // Курсор
        if (showCursorDuringDialogue)
        {
            previousCursorVisible = Cursor.visible;
            previousLockMode = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        PlayerInputLock.SetWeaponLock(this, true);
        PlayerInputLock.SetMovementLock(this, true);
        disabledControllersByUs.Clear();
        foreach (var fps in FindObjectsOfType<EasyPeasyFirstPersonController.FirstPersonController>())
        {
            if (fps != null && fps.enabled)
            {
                fps.enabled = false;
                disabledControllersByUs.Add(fps);
            }
        }

        currentDialogue = dialogue;
        currentTrigger = trigger;
        isDialogueActive = true;
        isShowingChoices = false;
        HideStaticButtons();

        EnsureUserInterface();
        if (dialogueText != null && !dialogueText.enableAutoSizing)
        {
            if (baseFontSize <= 0f)
                baseFontSize = dialogueText.fontSize;
            else
                dialogueText.fontSize = baseFontSize;
        }

        if (dialogueText == null)
            Debug.LogError("[DialogueManager] Нет текста реплики (dialogueText пуст): канвас не открывается. " +
                "Выдели свой канвас в Hierarchy и нажми Tools -> Диалоги -> Подключить мой канвас.", this);

        var start = !string.IsNullOrEmpty(startNodeID) ? dialogue.GetNodeByID(startNodeID) : null;
        if (start == null) start = dialogue.GetStartNode();
        if (start != null && !string.IsNullOrEmpty(startNodeID) && start.nodeID == startNodeID)
            Debug.Log($"[DialogueManager] Продолжаем «{dialogue.dialogueName}» с узла {start.nodeID} (сейв).", this);

        if (dialogueCanvasGroup == null && dialoguePanel != null)
            dialogueCanvasGroup = dialoguePanel.GetComponent<CanvasGroup>();
        if (dialogueCanvasGroup == null && dialoguePanel != null)
            dialogueCanvasGroup = dialoguePanel.AddComponent<CanvasGroup>();

        if (dialoguePanel != null && dialogueCanvasGroup != null)
        {
            fadeRoutine = StartCoroutine(FadeAndScale(dialoguePanel.transform, dialogueCanvasGroup, Vector3.one, 1f, fadeInDuration, fadeInCurve));
        }

        if (interactHint != null) interactHint.SetActive(false);
        if (pauseGameDuringDialogue)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        OnDialogueStarted?.Invoke();

        MoveToNode(start);
    }

    /// <summary>Закончить диалог. true — прошли до конца, false — ручной выход с сохранением прогресса.</summary>
    public void EndDialogue(bool completed = true)
    {
        if (!isDialogueActive) return;

        DialogueData finishedDialogue = currentDialogue;
        DialogueNode lastNode = currentNode;
        DialogueNode echoTarget = pendingEchoTarget;
        DialogueTrigger finishedTrigger = currentTrigger;

        isDialogueActive = false;
        currentNode = null;
        currentDialogue = null;
        currentTrigger = null;
        isShowingChoices = false;
        isShowingEcho = false;
        pendingEchoTarget = null;
        echoText = "";
        isTyping = false;
        revealedCharacters = 0;
        cursorBaseText = "";
        ClearSelection();

        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }
        if (echoCoroutine != null) { StopCoroutine(echoCoroutine); echoCoroutine = null; }
        if (autoAdvanceCoroutine != null) { StopCoroutine(autoAdvanceCoroutine); autoAdvanceCoroutine = null; }
        if (cursorBlinkCoroutine != null) { StopCoroutine(cursorBlinkCoroutine); cursorBlinkCoroutine = null; }
        if (fadeRoutine != null) { StopCoroutine(fadeRoutine); fadeRoutine = null; }

        if (pauseGameDuringDialogue) Time.timeScale = previousTimeScale;
        if (showCursorDuringDialogue)
        {
            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousLockMode;
        }

        PlayerInputLock.ReleaseAll(this);
        if (!PlayerInputLock.MovementLocked)
        {
            for (int i = 0; i < disabledControllersByUs.Count; i++)
            {
                if (disabledControllersByUs[i] != null)
                    disabledControllersByUs[i].enabled = true;
            }
        }
        disabledControllersByUs.Clear();

        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
            dialoguePanel.transform.localScale = panelStartScale;
            if (dialogueCanvasGroup != null)
                dialogueCanvasGroup.alpha = 0f;
        }
        // Возвращаем обычный шрифт: следующий диалог начнётся с базового размера.
        if (dialogueText != null && baseFontSize > 0f && !dialogueText.enableAutoSizing)
            dialogueText.fontSize = baseFontSize;
        if (choicesPanel != null) choicesPanel.SetActive(false);
        HideStaticButtons();

        // Квест-гейт: без выполненного квеста done не ставим, прогресс сохраняем.
        if (completed && IsQuestGateBlocking(finishedDialogue))
        {
            Debug.Log($"[DialogueManager] «{DialogueKey(finishedDialogue)}» не засчитан: " +
                      $"сначала выполни «{QuestSystem.GetTitle(finishedDialogue.completionQuestId)}».", this);
            completed = false;
            string gateResume = ComputeResumeNodeID(echoTarget, lastNode);
            if (string.IsNullOrEmpty(gateResume))
                gateResume = echoTarget != null ? echoTarget.nodeID
                    : (lastNode != null ? lastNode.nodeID : null);
            if (!string.IsNullOrEmpty(gateResume))
                SaveDialogueProgress(finishedDialogue, gateResume);
        }

        // Сначала сейв/done, потом события — иначе хуки видят старое состояние.
        LastFinishedDialogue = finishedDialogue;
        LastFinishedCompleted = completed;
        if (finishedDialogue != null)
        {
            if (completed)
            {
                ClearDialogueProgress(finishedDialogue);
                MarkDialogueDone(finishedDialogue);
            }
            else
            {
                // Esc: показанное не повторяем, узел с выбором держим.
                string progressNodeID = ComputeResumeNodeID(echoTarget, lastNode);
                if (!string.IsNullOrEmpty(progressNodeID))
                    SaveDialogueProgress(finishedDialogue, progressNodeID);
            }
            GameState.Save();
            QuestSystem.Save();
        }

        if (finishedTrigger != null)
            finishedTrigger.OnDialogueEnded();

        OnDialogueEnded?.Invoke();
    }

    void MoveToNode(DialogueNode node)
    {
        MoveToNodeInternal(node, 0);
    }

    /// <summary>Есть ли у узла выборы (null считаем отсутствием).</summary>
    static bool HasChoices(DialogueNode node)
    {
        return node != null && node.choices != null && node.choices.Count > 0;
    }

    void MoveToNodeInternal(DialogueNode node, int skipDepth)
    {
        if (node == null)
        {
            Debug.LogError("[DialogueManager] Переход в null-узел (битая ссылка nextNodeID?) — " +
                           "диалог завершён без отметки прохождения, чтобы его можно было пройти заново.", this);
            EndDialogue(false);
            return;
        }

        // Пустой сервисный узел: команды выполняем, текст не показываем, идём дальше.
        if (string.IsNullOrEmpty(node.dialogueText) && !HasChoices(node))
        {
            if (skipDepth > 100)
            {
                Debug.LogError($"[DialogueManager] Цепочка пустых узлов длиннее 100 в " +
                               $"«{DialogueKey(currentDialogue)}» (зацикливание?) — диалог остановлен.", this);
                EndDialogue(false);
                return;
            }
            DialogueNode skippedFrom = currentNode;
            currentNode = node;
            isShowingChoices = false;
            isShowingEcho = false;
            pendingEchoTarget = null;
            HideStaticButtons();

            if (currentDialogue != null)
                SaveDialogueProgress(currentDialogue, node.nodeID);

            if (skippedFrom != null) skippedFrom.onNodeExit?.Invoke();
            if (node.onEnterCommands != null)
                foreach (var cmd in node.onEnterCommands)
                    cmd.Execute();
            node.onNodeEnter?.Invoke();
            OnNodeChanged?.Invoke(node);

            if (!string.IsNullOrEmpty(node.nextNodeID) && currentDialogue != null)
            {
                DialogueNode next = currentDialogue.GetNodeByID(node.nextNodeID);
                if (next == null)
                {
                    Debug.LogError($"[DialogueManager] Узел «{node.nodeID}»: nextNodeID " +
                                   $"«{node.nextNodeID}» не найден — диалог завершён. Проверь связи в DialogueData.", this);
                    EndDialogue(false);
                    return;
                }
                MoveToNodeInternal(next, skipDepth + 1);
            }
            else
            {
                EndDialogue();
            }
            return;
        }

        DialogueNode previousNode = currentNode;
        currentNode = node;
        isShowingChoices = false;
        isShowingEcho = false;
        pendingEchoTarget = null;
        HideStaticButtons();

        if (currentDialogue != null)
            SaveDialogueProgress(currentDialogue, node.nodeID);

        DialogueHistory history = GetComponent<DialogueHistory>();
        if (history == null) history = gameObject.AddComponent<DialogueHistory>();
        history.Record(DialogueKey(currentDialogue), node.speakerName, NodeText(node), ResolveSpeakerColor(node));

        if (choicesPanel != null) choicesPanel.SetActive(false);

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }

        if (previousNode != null) previousNode.onNodeExit?.Invoke();
        if (node.onEnterCommands != null)
            foreach (var cmd in node.onEnterCommands)
                cmd.Execute();

        node.onNodeEnter?.Invoke();
        OnNodeChanged?.Invoke(node);

        if (speakerNameText != null)
        {
            if (!string.IsNullOrEmpty(node.speakerName))
            {
                speakerNameText.text = node.speakerName;
                speakerNameText.color = ResolveSpeakerColor(node);
                speakerNameText.gameObject.SetActive(true);
            }
            else
            {
                speakerNameText.gameObject.SetActive(false);
            }
        }

        if (speakerPortraitImage != null)
        {
            Sprite portrait = ResolveSpeakerPortrait(node);
            if (portrait != null)
            {
                speakerPortraitImage.sprite = portrait;
                speakerPortraitImage.gameObject.SetActive(true);
            }
            else
            {
                speakerPortraitImage.gameObject.SetActive(false);
            }
        }

        cursorBaseText = WithSpeaker(NodeText(node), node.speakerName);
        FitTextSize(cursorBaseText);
        // Текста нет, а выборы есть: окно не показываем, выборы сразу.
        if (string.IsNullOrEmpty(NodeText(node)) && HasChoices(node))
        {
            isTyping = false;
            revealedCharacters = 0;
            if (dialogueText != null)
            {
                dialogueText.text = "";
                dialogueText.maxVisibleCharacters = int.MaxValue;
            }
            ShowChoices();
            return;
        }

        float speed = node.textSpeed > 0 ? node.textSpeed : defaultTextSpeed;
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(
            WithSpeaker(NodeText(node), node.speakerName), speed));

        if (autoAdvanceCoroutine != null) StopCoroutine(autoAdvanceCoroutine);
        if (node.autoAdvanceDelay > 0 && !HasChoices(node))
        {
            autoAdvanceCoroutine = StartCoroutine(AutoAdvance(node.autoAdvanceDelay));
        }
    }

    IEnumerator TypeText(string text, float speed)
    {
        isTyping = true;
        if (text == null) text = "";

        if (dialogueText == null)
        {
            isTyping = false;
            typingCoroutine = null;
            revealedCharacters = text.Length;
            if (!isShowingEcho && HasChoices(currentNode) && !isShowingChoices)
                StartCoroutine(ShowChoicesAfterDelay(choicesDelay));
            yield break;
        }

        revealedCharacters = 0;
        textHasRichTags = text.IndexOf('<') >= 0;

        // Курсор несовместим с rich-text: обрезка посреди тега ломает разметку.
        bool useCursor = showTypingCursor && !string.IsNullOrEmpty(cursorSymbol) && !textHasRichTags;

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }

        int visibleLength = 0;
        if (textHasRichTags)
        {
            dialogueText.text = text;
            dialogueText.maxVisibleCharacters = 0;
            visibleLength = GetVisibleLength(text);
        }
        else
        {
            dialogueText.text = "";
            dialogueText.maxVisibleCharacters = int.MaxValue;
        }

        int soundCounter = 0;

        if (textHasRichTags)
        {
            for (int i = 1; i <= visibleLength; i++)
            {
                revealedCharacters = i;
                dialogueText.maxVisibleCharacters = i;

                if (typingSound != null && ++soundCounter % Mathf.Max(1, typingSoundFrequency) == 0)
                    PlayTypingSound();

                yield return new WaitForSecondsRealtime(speed);
            }

            revealedCharacters = visibleLength;
        }
        else
        {
            // Не рвём суррогатные пары и комбинируемые символы.
            int[] cuts = GetCutPoints(text);
            foreach (int cut in cuts)
            {
                revealedCharacters = cut;
                dialogueText.text = text.Substring(0, cut);

                if (typingSound != null && ++soundCounter % Mathf.Max(1, typingSoundFrequency) == 0)
                    PlayTypingSound();

                yield return new WaitForSecondsRealtime(speed);
            }

            revealedCharacters = text.Length;
        }

        if (textHasRichTags)
        {
            dialogueText.maxVisibleCharacters = visibleLength;
        }
        else
        {
            dialogueText.text = text;
            dialogueText.maxVisibleCharacters = int.MaxValue;
        }

        isTyping = false;
        typingCoroutine = null;
        if (useCursor)
            cursorBlinkCoroutine = StartCoroutine(BlinkCursor());

        // Во время эха героя выборы старого узла не показываем: их уже выбрали.
        if (!isShowingEcho && HasChoices(currentNode))
        {
            yield return new WaitForSecondsRealtime(choicesDelay);
            if (isDialogueActive && !isTyping)
                ShowChoices();
        }
    }

    /// <summary>Позиции разреза строки по границам текстовых элементов.</summary>
    static int[] GetCutPoints(string text)
    {
        if (string.IsNullOrEmpty(text)) return new int[0];
        var cuts = new System.Collections.Generic.List<int>(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int next = i + 1;
            if (char.IsHighSurrogate(text[i]) && next < text.Length && char.IsLowSurrogate(text[next]))
                next++;
            // Диакритика, вариационные селекторы и ZWJ-цепочки тянутся за символом.
            while (next < text.Length &&
                   (text[next] == '\u200D' || text[next] == '\uFE0F' ||
                    char.GetUnicodeCategory(text[next]) == System.Globalization.UnicodeCategory.NonSpacingMark ||
                    char.GetUnicodeCategory(text[next]) == System.Globalization.UnicodeCategory.SpacingCombiningMark ||
                    char.GetUnicodeCategory(text[next]) == System.Globalization.UnicodeCategory.EnclosingMark))
                next++;
            i = next;
            cuts.Add(i);
        }
        return cuts.ToArray();
    }

    /// <summary>Число печатаемых символов без учёта rich-text тегов.</summary>
    static int GetVisibleLength(string text)
    {
        int count = 0;
        bool inTag = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) count++;
        }
        return count;
    }

    void PlayTypingSound()
    {
        if (typingSound == null) return;

        if (typingAudioSource == null)
        {
            typingAudioSource = GetComponent<AudioSource>();
            if (typingAudioSource == null)
            {
                typingAudioSource = gameObject.AddComponent<AudioSource>();
                typingAudioSource.playOnAwake = false;
                typingAudioSource.spatialBlend = 0f;
            }
        }
        typingAudioSource.pitch = Random.Range(0.94f, 1.06f);
        typingAudioSource.PlayOneShot(typingSound, typingSoundVolume);
    }

    IEnumerator BlinkCursor()
    {
        string baseText = cursorBaseText ?? "";
        if (baseText == null) baseText = "";

        int safeCount = Mathf.Clamp(revealedCharacters, 0, baseText.Length);
        string shown = baseText.Substring(0, safeCount);

        bool show = true;
        while (true)
        {
            if (dialogueText == null) yield break;

            dialogueText.text = show ? shown + cursorSymbol : shown;
            dialogueText.maxVisibleCharacters = int.MaxValue;
            show = !show;

            yield return new WaitForSecondsRealtime(cursorBlinkSpeed);
        }
    }

    /// <summary>Мгновенно допечатать реплику (пробел/клик).</summary>
    public void CompleteTyping()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        if (echoCoroutine != null)
        {
            StopCoroutine(echoCoroutine);
            echoCoroutine = null;
        }
        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }

        // Допечатать эхо героя.
        if (isShowingEcho)
        {
            string echoShown = WithSpeaker(echoText, playerSpeakerName);
            if (dialogueText == null)
            {
                isTyping = false;
                revealedCharacters = echoShown.Length;
                return;
            }

            dialogueText.text = echoShown;
            dialogueText.maxVisibleCharacters = int.MaxValue;
            revealedCharacters = echoShown.Length;
            isTyping = false;

            if (showTypingCursor && !string.IsNullOrEmpty(cursorSymbol) && echoShown.IndexOf('<') < 0)
                cursorBlinkCoroutine = StartCoroutine(BlinkCursor());
            return;
        }

        if (currentNode == null) return;

        if (dialogueText == null)
        {
            isTyping = false;
            revealedCharacters = WithSpeaker(NodeText(currentNode), currentNode.speakerName).Length;
            if (currentNode.choices.Count > 0 && !isShowingChoices)
                StartCoroutine(ShowChoicesAfterDelay(0f));
            return;
        }

        string full = WithSpeaker(NodeText(currentNode), currentNode.speakerName);
        dialogueText.text = full;
        dialogueText.maxVisibleCharacters = int.MaxValue;
        revealedCharacters = full.Length;
        isTyping = false;

        if (showTypingCursor && !string.IsNullOrEmpty(cursorSymbol) && !textHasRichTags)
            cursorBlinkCoroutine = StartCoroutine(BlinkCursor());

        if (currentNode.choices.Count > 0 && !isShowingChoices)
            StartCoroutine(ShowChoicesAfterDelay(0f));
    }

    /// <summary>Двигает диалог дальше (эхо, следующий узел, выборы).</summary>
    public void AdvanceDialogue()
    {
        if (isTyping) return;
        if (isShowingChoices) return;
        if (isShowingEcho)
        {
            DialogueNode target = pendingEchoTarget;
            isShowingEcho = false;
            pendingEchoTarget = null;
            echoText = "";
            MoveToNode(target);
            return;
        }

        if (currentNode == null) { EndDialogue(); return; }

        if (HasChoices(currentNode))
        {
            ShowChoices();
            return;
        }

        if (!string.IsNullOrEmpty(currentNode.nextNodeID))
        {
            DialogueNode next = currentDialogue != null
                ? currentDialogue.GetNodeByID(currentNode.nextNodeID)
                : null;
            if (next == null)
            {
                Debug.LogError($"[DialogueManager] Узел «{currentNode.nodeID}»: nextNodeID " +
                               $"«{currentNode.nextNodeID}» не найден — диалог завершён. Проверь связи в DialogueData.", this);
                EndDialogue(false);
                return;
            }
            MoveToNode(next);
        }
        else
        {
            EndDialogue();
        }
    }

    IEnumerator AutoAdvance(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        autoAdvanceCoroutine = null;
        if (isDialogueActive && !isTyping && !isShowingChoices && !isShowingEcho)
        {
            AdvanceDialogue();
        }
    }

    IEnumerator ShowChoicesAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (isDialogueActive && !isTyping)
            ShowChoices();
    }

    void ShowChoices()
    {
        if (isShowingChoices) return;
        if (currentNode == null) return;
        if (currentNode.choices == null) return;
        if (!useStaticChoiceButtons && (choicesContainer == null || choiceButtonPrefab == null)) return;
        isShowingChoices = true;
        ClearSelection();

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
            if (dialogueText != null && currentNode != null)
                dialogueText.text = WithSpeaker(NodeText(currentNode), currentNode.speakerName);
        }

        if (choicesPanel != null) choicesPanel.SetActive(true);

        // Скрытые выпадают из списка, showWhenLocked и использованные — блокировкой.
        var entries = new System.Collections.Generic.List<ChoiceEntry>();
        foreach (var choice in currentNode.choices)
        {
            bool used = choice != null && currentDialogue != null &&
                        IsChoiceSpent(currentDialogue, currentNode.nodeID, choice);
            bool available = !used && IsChoiceAvailable(choice);
            if (!available && !used && (choice == null || !choice.showWhenLocked))
                continue;
            entries.Add(new ChoiceEntry(choice, available, used));
        }

        if (useStaticChoiceButtons)
            ShowStaticChoices(entries);
        else
            ShowTemplateChoices(entries);
    }

    /// <summary>Вариант + доступен ли он + нажимался ли уже (одноразовый).</summary>
    private struct ChoiceEntry
    {
        public DialogueChoice choice;
        public bool available;
        public bool used;
        public ChoiceEntry(DialogueChoice choice, bool available, bool used)
        {
            this.choice = choice;
            this.available = available;
            this.used = used;
        }
        /// <summary>Нажать нельзя: недоступен или уже использован.</summary>
        public bool Locked => !available || used;
        /// <summary>Кнопка видна, но только как spent/locked (не прятать).</summary>
        public bool ForceShowLocked => used;
    }

    /// <summary>Проверка условий варианта: ассет-условие, предмет, квест.</summary>
    static bool IsChoiceAvailable(DialogueChoice choice)
    {
        if (choice == null) return false;
        if (choice.condition != null && !choice.condition.Evaluate())
            return false;
        if (!string.IsNullOrEmpty(choice.requiredItemId) &&
            !DialogueCommand.PlayerHasItem(choice.requiredItemId, Mathf.Max(1, choice.requiredItemCount)))
            return false;
        if (choice.requiredItems != null)
        {
            foreach (var req in choice.requiredItems)
            {
                if (req == null || string.IsNullOrEmpty(req.itemId)) continue;
                if (!DialogueCommand.PlayerHasItem(req.itemId, Mathf.Max(1, req.count)))
                    return false;
            }
        }
        if (!string.IsNullOrEmpty(choice.requiredQuestId) &&
            !IsQuestStateMatch(choice.requiredQuestId, choice.requiredQuestState))
            return false;
        return true;
    }

    /// <summary>Варианты на готовых кнопках сцены.</summary>
    void ShowStaticChoices(System.Collections.Generic.List<ChoiceEntry> entries)
    {
        // Слоты сортируем по Y: нумерация в сцене может не совпадать с порядком.
        var slots = new System.Collections.Generic.List<Button>();
        if (staticChoiceButtons != null)
        {
            foreach (Button b in staticChoiceButtons)
            {
                if (b != null) slots.Add(b);
            }
        }
        slots.Sort((a, b) => SlotY(b).CompareTo(SlotY(a)));

        if (slots.Count == 0 && entries.Count > 0)
        {
            Debug.LogWarning("[DialogueManager] Есть варианты, но нет кнопок (staticChoiceButtons пуст). " +
                "Добавь UserDialogueUI на свой канвас: Tools -> Диалоги -> Подключить мой канвас.", this);
            isShowingChoices = false;
            EndDialogue(false);
            return;
        }
        for (int i = 0; i < slots.Count; i++)
        {
            Button button = slots[i];

            if (i < entries.Count)
            {
                ChoiceEntry entry = entries[i];
                DisableKeyboardFocus(button);
                TextMeshProUGUI label = GetStaticLabel(StaticButtonIndex(button), button);
                if (label != null)
                {
                    label.text = entry.choice.choiceText;
                    label.gameObject.SetActive(true);
                    RememberLabelColor(label);
                    label.color = entry.Locked ? lockedChoiceLabelColor : labelBaseColors[label];
                }
                button.onClick.RemoveAllListeners();
                if (!entry.Locked)
                {
                    DialogueChoice capturedChoice = entry.choice;
                    button.onClick.AddListener(() => OnChoiceSelected(capturedChoice));
                    button.interactable = true;
                }
                else
                {
                    button.interactable = false;
                }
                ApplyLockedStyle(button);
                if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
            }
            else
            {
                button.gameObject.SetActive(false);
            }
        }

        if (entries.Count == 0)
        {
            if (choicesPanel != null) choicesPanel.SetActive(false);
            isShowingChoices = false;
            Debug.LogError($"[DialogueManager] Узел «{currentNode?.nodeID}»: все варианты скрыты " +
                            "условиями (и ни один не showWhenLocked) — выборы показать нечего. " +
                            "Диалог закрыт БЕЗ отметки прохождения, иначе цепочка перепрыгнет дальше.", this);
            EndDialogue(false);
        }
        else if (entries.Count > slots.Count)
        {
            Debug.LogWarning($"[DialogueManager] Вариантов {entries.Count}, а кнопок {slots.Count} — " +
                               "показаны первые, остальные ветки НЕДОСТУПНЫ игроку! Продублируй последнюю " +
                               $"кнопку на канвасе как Button{slots.Count + 1} (внутри текст \"{slots.Count + 1}\") — " +
                               "адаптер подхватит её сам.", this);
        }
    }

    /// <summary>Y слота (чем больше, тем выше).</summary>
    static float SlotY(Button button)
    {
        if (button == null) return 0f;
        RectTransform rect = button.GetComponent<RectTransform>();
        return rect != null ? rect.anchoredPosition.y : 0f;
    }

    /// <summary>Индекс кнопки в staticChoiceButtons.</summary>
    int StaticButtonIndex(Button button)
    {
        if (staticChoiceButtons == null || button == null) return -1;
        for (int i = 0; i < staticChoiceButtons.Length; i++)
        {
            if (staticChoiceButtons[i] == button) return i;
        }
        return -1;
    }

    /// <summary>Запомнить исходный цвет подписи.</summary>
    void RememberLabelColor(TextMeshProUGUI label)
    {
        if (label != null && !labelBaseColors.ContainsKey(label))
            labelBaseColors[label] = label.color;
    }

    /// <summary>Спрайт UI5 на кнопку (если задан и у стиля его ещё нет).</summary>
    void ApplyLockedStyle(Button button)
    {
        if (button == null || lockedChoiceSprite == null) return;
        DialogueCanvasButton style = button.GetComponent<DialogueCanvasButton>();
        if (style != null && style.disabledSprite == null)
            style.disabledSprite = lockedChoiceSprite;
    }

    TextMeshProUGUI GetStaticLabel(int index, Button button)
    {
        if (index >= 0 && staticChoiceLabels != null && index < staticChoiceLabels.Length &&
            staticChoiceLabels[index] != null)
            return staticChoiceLabels[index];
        return button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
    }

    /// <summary>Варианты из шаблона. Заблокированные — нажать нельзя.</summary>
    void ShowTemplateChoices(System.Collections.Generic.List<ChoiceEntry> entries)
    {
        if (choicesContainer == null || choiceButtonPrefab == null) return;

        foreach (Transform child in choicesContainer)
            Destroy(child.gameObject);

        foreach (var entry in entries)
        {
            Button button = Instantiate(choiceButtonPrefab, choicesContainer);
            DisableKeyboardFocus(button);
            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = entry.choice.choiceText;
                if (entry.Locked)
                    buttonText.color = lockedChoiceLabelColor;
            }

            if (!entry.Locked)
            {
                DialogueChoice capturedChoice = entry.choice;
                button.onClick.AddListener(() => OnChoiceSelected(capturedChoice));
                button.interactable = true;
            }
            else
            {
                button.interactable = false;
            }
            ApplyLockedStyle(button);
        }

        if (choicesContainer.childCount == 0)
        {
            if (choicesPanel != null) choicesPanel.SetActive(false);
            isShowingChoices = false;
            Debug.LogError($"[DialogueManager] Узел «{currentNode?.nodeID}»: все варианты скрыты " +
                            "условиями — закрыто БЕЗ отметки прохождения.", this);
            EndDialogue(false);
        }
    }

    void OnChoiceSelected(DialogueChoice choice)
    {
        if (choicesPanel != null) choicesPanel.SetActive(false);
        HideStaticButtons();
        isShowingChoices = false;
        if (choice != null && currentDialogue != null && currentNode != null && IsSingleUseChoice(choice))
            MarkChoiceUsed(currentDialogue, currentNode.nodeID, choice.choiceText);

        if (choice.onSelectCommands != null)
            foreach (var cmd in choice.onSelectCommands)
                cmd.Execute();

        choice.onSelected?.Invoke();

        if (choice.endDialogue)
        {
            EndDialogue();
            return;
        }

        DialogueNode target = null;
        if (!string.IsNullOrEmpty(choice.nextNodeID))
        {
            target = currentDialogue != null ? currentDialogue.GetNodeByID(choice.nextNodeID) : null;
            if (target == null)
            {
                Debug.LogError($"[DialogueManager] Выбор «{choice.choiceText}»: nextNodeID " +
                               $"«{choice.nextNodeID}» не найден — диалог завершён. Проверь связи в DialogueData.", this);
                EndDialogue(false);
                return;
            }
        }

        // Эхо: ответ показываем репликой героя, иначе фраза видна только на кнопке.
        if (echoPlayerChoice && target != null && !string.IsNullOrEmpty(choice.choiceText))
        {
            ShowEcho(choice.choiceText, target);
            return;
        }

        if (target != null)
        {
            MoveToNode(target);
        }
        else
        {
            EndDialogue();
        }
    }

    /// <summary>Показать ответ как реплику героя. Дальше — пробел/клик в целевой узел.</summary>
    void ShowEcho(string text, DialogueNode target)
    {
        pendingEchoTarget = target;
        echoText = text ?? "";
        isShowingEcho = true;
        isShowingChoices = false;

        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }
        if (echoCoroutine != null) { StopCoroutine(echoCoroutine); echoCoroutine = null; }
        if (autoAdvanceCoroutine != null) { StopCoroutine(autoAdvanceCoroutine); autoAdvanceCoroutine = null; }
        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }
        if (choicesPanel != null) choicesPanel.SetActive(false);
        HideStaticButtons();
        DialogueHistory history = GetComponent<DialogueHistory>();
        if (history == null) history = gameObject.AddComponent<DialogueHistory>();
        history.Record(DialogueKey(currentDialogue), playerSpeakerName, echoText, ResolvePlayerColor());

        if (speakerNameText != null)
        {
            if (!string.IsNullOrEmpty(playerSpeakerName))
            {
                speakerNameText.text = playerSpeakerName;
                speakerNameText.color = ResolvePlayerColor();
                if (!speakerNameText.gameObject.activeSelf)
                    speakerNameText.gameObject.SetActive(true);
            }
            else
            {
                speakerNameText.gameObject.SetActive(false);
            }
        }

        if (speakerPortraitImage != null)
            speakerPortraitImage.gameObject.SetActive(false);
        OnNodeChanged?.Invoke(new DialogueNode
        {
            speakerName = playerSpeakerName,
            speakerColor = playerSpeakerColor
        });

        float speed = defaultTextSpeed;
        if (currentNode != null && currentNode.textSpeed > 0)
            speed = currentNode.textSpeed;
        string echoShown = WithSpeaker(echoText, playerSpeakerName);
        cursorBaseText = echoShown;
        FitTextSize(echoShown);
        echoCoroutine = StartCoroutine(EchoTypeText(echoShown, speed));
    }

    IEnumerator EchoTypeText(string text, float speed)
    {
        isTyping = true;
        if (text == null) text = "";
        revealedCharacters = 0;
        bool rich = text.IndexOf('<') >= 0;
        int soundCounter = 0;

        if (dialogueText == null)
        {
            isTyping = false;
            echoCoroutine = null;
            revealedCharacters = text.Length;
            yield break;
        }

        if (rich)
        {
            dialogueText.text = text;
            dialogueText.maxVisibleCharacters = 0;
            int length = GetVisibleLength(text);
            for (int i = 1; i <= length; i++)
            {
                revealedCharacters = i;
                dialogueText.maxVisibleCharacters = i;
                if (typingSound != null && ++soundCounter % Mathf.Max(1, typingSoundFrequency) == 0)
                    PlayTypingSound();
                yield return new WaitForSecondsRealtime(speed);
            }
            revealedCharacters = length;
            dialogueText.maxVisibleCharacters = length;
        }
        else
        {
            dialogueText.text = "";
            dialogueText.maxVisibleCharacters = int.MaxValue;
            foreach (int cut in GetCutPoints(text))
            {
                revealedCharacters = cut;
                dialogueText.text = text.Substring(0, cut);
                if (typingSound != null && ++soundCounter % Mathf.Max(1, typingSoundFrequency) == 0)
                    PlayTypingSound();
                yield return new WaitForSecondsRealtime(speed);
            }
            revealedCharacters = text.Length;
            dialogueText.text = text;
            dialogueText.maxVisibleCharacters = int.MaxValue;
        }

        isTyping = false;
        echoCoroutine = null;

        if (showTypingCursor && !string.IsNullOrEmpty(cursorSymbol) && !rich)
            cursorBlinkCoroutine = StartCoroutine(BlinkCursor());
    }

    /// <summary>Уменьшить шрифт под длинную реплику. Автосайз уважаем, не трогаем.</summary>
    void FitTextSize(string text)
    {
        if (!shrinkLongText || dialogueText == null || dialogueText.enableAutoSizing) return;
        if (baseFontSize <= 0f) baseFontSize = dialogueText.fontSize;
        int len = string.IsNullOrEmpty(text) ? 0 : text.Length;
        float scale = 1f;
        if (len >= veryLongThreshold) scale = veryLongScale;
        else if (len >= longTextThreshold) scale = longTextScale;
        dialogueText.fontSize = baseFontSize * scale;
    }

    public void SetBackground(Sprite bg)
    {
        // TODO: свой фон
    }

    IEnumerator FadeAndScale(Transform target, CanvasGroup cg, Vector3 targetScale, float targetAlpha, float duration, AnimationCurve curve, System.Action onComplete = null)
    {
        float elapsed = 0f;
        Vector3 startScale = target.localScale;
        float startAlpha = cg.alpha;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curvedT = curve.Evaluate(t);

            target.localScale = Vector3.Lerp(startScale, targetScale, curvedT);
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, curvedT);
            yield return null;
        }

        target.localScale = targetScale;
        cg.alpha = targetAlpha;
        onComplete?.Invoke();
    }
}
