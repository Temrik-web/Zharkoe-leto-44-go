using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инвентарь игрока: наведение на предмет, подбор, хранение со стаками,
/// выбрасывание, использование, открытие/закрытие панели, сохранение.
/// Вешается на игрока (или на объект с камерой).
/// </summary>
[DisallowMultipleComponent]
public class InventorySystem : MonoBehaviour
{
    [Serializable]
    public class Slot
    {
        public ItemData item;
        public int amount;

        public Slot(ItemData item, int amount)
        {
            this.item = item;
            this.amount = amount;
        }

        public bool IsEmpty => item == null || amount <= 0;
        public int FreeSpace => item == null ? 0 : Mathf.Max(0, item.maxStack - amount);
    }

    public static InventorySystem Instance { get; private set; }
    [Header("Ссылки")]
    [Tooltip("Если пусто — возьмётся Camera.main.")]
    public Camera playerCamera;
    [Tooltip("UI-панель инвентаря. Можно оставить пустой — тогда работает только логика.")]
    public GameObject inventoryPanel;
    public AudioSource audioSource;
    [Header("Подбор")]
    public float pickupRange = 3f;
    [Tooltip("По каким слоям искать предметы.")]
    public LayerMask pickupMask = ~0;
    public KeyCode pickupKey = KeyCode.E;
    [Tooltip("Радиус SphereCast. 0 — обычный тонкий луч (сложнее прицелиться).")]
    [Range(0f, 0.5f)] public float pickupCastRadius = 0.15f;

    [Tooltip("Радиус вокруг игрока, в котором предмет считается доступным даже без " +
             "попадания луча. Лечит случай «стою вплотную и подсказка исчезла»: " +
             "SphereCast не видит коллайдер, внутри которого начинается сфера.")]
    [Range(0f, 3f)] public float nearPickupRadius = 1.2f;

    [Tooltip("Максимальный угол от центра экрана, при котором предмет считается " +
             "тем, на который смотрит игрок. Больше — легче навести, но можно " +
             "случайно подобрать предмет сбоку.")]
    [Range(5f, 90f)] public float maxPickupAngle = 35f;

    [Tooltip("Проверять, не закрыт ли предмет стеной. Выключи, если предметы " +
             "лежат внутри сложной геометрии и подсказка мерцает.")]
    public bool requireLineOfSight = true;

    [Tooltip("Слои, которые могут заслонять предмет. Убери отсюда мелкий декор, " +
             "чтобы трава и мусор не перекрывали подсказку.")]
    public LayerMask occlusionMask = ~0;

    [Tooltip("Автоподбор при касании триггера, без нажатия клавиши.")]
    public bool autoPickupOnTouch = false;

    [Header("Инвентарь")]
    public KeyCode toggleKey = KeyCode.Tab;
    [Min(1)] public int maxSlots = 20;
    [Tooltip("Использовать предмет по цифрам 1..9.")]
    public bool useHotkeys = true;
    [Tooltip("Повторное нажатие цифры оружия, которое уже в руках, — убрать его (пустые руки). " +
             "Как переключение оружия в шутерах. G+цифра по-прежнему выбрасывает предмет.")]
    public bool hotkeyTogglesHolster = true;
    [Tooltip("Клавиша сортировки по категориям.")]
    public KeyCode sortKey = KeyCode.R;
    [Tooltip("Автоматически сортировать после каждого подбора.")]
    public bool autoSortOnPickup = false;
    [Header("Выбрасывание")]
    public KeyCode dropKey = KeyCode.G;
    [Tooltip("Универсальный префаб с компонентом Pickup для дропа предметов без своего worldPrefab.")]
    public GameObject genericPickupPrefab;
    public float dropForwardOffset = 1.2f;
    [Tooltip("Высота точки выброса относительно камеры. Чуть ниже прицела — " +
             "отрицательное значение (уровень груди), чтобы не падало «от головы».")]
    public float dropUpOffset = -0.15f;
    public float dropThrowForce = 2.5f;
    [Header("Поведение при открытии")]
    public bool manageCursor = true;
    [Tooltip("Ставить Time.timeScale = 0, пока инвентарь открыт.")]
    public bool pauseGameWhenOpen = false;
    [Header("Отладочный HUD (OnGUI)")]
    [Tooltip("Рисовать подсказку и список через OnGUI. Выключи, когда сделаешь нормальный UI.")]
    public bool drawDebugGUI = true;

    [Header("Всплывающие подписи")]
    [Tooltip("Показывать «+2 Аптечка» в мире при подборе.")]
    public bool showFloatingText = true;
    [Header("Сохранение")]
    public bool autoSaveOnQuit = true;
    public bool autoLoadOnStart = false;
    public const string DefaultSaveKey = "inventory_v1";
    public string saveKey = DefaultSaveKey;
    public List<Slot> slots = new List<Slot>();
    private bool isOpen;
    private Pickup currentTarget;
    // Physics queries run every frame while the inventory is closed. Reuse their
    // storage, but fall back to the unbounded queries in unusually crowded areas.
    private readonly RaycastHit[] pickupHits = new RaycastHit[64];
    private readonly Collider[] nearbyColliders = new Collider[64];
    private readonly RaycastHit[] lineOfSightHits = new RaycastHit[64];
    private static readonly IComparer<RaycastHit> HitDistanceComparer =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
    private float lastSortTime = -1f;
    private Coroutine dropAllRoutine;
    /// <summary>Разброс броска по рысканью для массового сброса (0 — ровно вперёд).</summary>
    private float dropScatterYaw = 0f;

    /// <summary>
    /// Скрытые шаблоны мировых пикапов: «как выглядит предмет в мире».
    /// Снимаются со сцены при старте, пока оригиналы ещё не подобрали.
    /// Нужны, чтобы дроп оружия спавнил настоящую модель Ppsh-41(GR) / нож /
    /// гранату, а не универсальный куб. Ключ — ItemData.Id.
    /// </summary>
    private readonly Dictionary<string, GameObject> worldTemplateCache = new Dictionary<string, GameObject>();

    /// <summary>Открыт ли инвентарь. Другие скрипты читают это, чтобы блокировать ввод.</summary>
    public bool IsOpen => isOpen;

    /// <summary>Предмет, на который сейчас смотрит игрок (может быть null).</summary>
    public Pickup CurrentTarget => currentTarget;

    public int SlotCount => slots.Count;
    public int MaxSlots => maxSlots;

    // ---------- События ----------
    /// <summary>Инвентарь изменился (добавили/убрали/выбросили). Для перерисовки UI.</summary>
    public event Action OnInventoryChanged;
    /// <summary>Инвентарь открыли/закрыли. Параметр — новое состояние.</summary>
    public event Action<bool> OnToggled;
    /// <summary>Цель наведения изменилась (может быть null).</summary>
    public event Action<Pickup> OnTargetChanged;
    /// <summary>Предмет подобран: (предмет, количество).</summary>
    public event Action<ItemData, int> OnItemPickedUp;
    /// <summary>Инвентарь отсортирован — для анимации перестроения UI.</summary>
    public event Action OnSorted;
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[Inventory] На сцене уже есть InventorySystem ({Instance.name}). " +
                             $"Компонент на {name} отключён.");
            enabled = false;
            return;
        }
        Instance = this;

        if (playerCamera == null) playerCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Якщо об'єкт знищили при відкритому інвентарі на паузі —
        // не залишаємо timeScale = 0 на всю гру.
        if (isOpen && pauseGameWhenOpen) Time.timeScale = 1f;
    }

    void Start()
    {
        if (playerCamera == null)
            Debug.LogWarning("[Inventory] Камера не найдена — наведение на предметы работать не будет.");

        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        isOpen = false;

        CacheWorldPickupTemplates();

        if (autoLoadOnStart) Load();

        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Запомнить, как предметы выглядят в мире, пока их не подобрали.
    /// Для каждого ItemData без явного worldPrefab делаем скрытую неактивную
    /// копию первого подходящего Pickup со сцены. После подбора оригинал
    /// уничтожается (Pickup.OnPickedUp), а копия остаётся шаблоном для дропа.
    /// Если на один предмет несколько пикапов (настоящая модель + тестовый куб
    /// из визарда) — выбираем наиболее «настоящий» по эвристике.
    /// </summary>
    void CacheWorldPickupTemplates()
    {
        worldTemplateCache.Clear();

        Pickup[] all;
        try { all = FindObjectsOfType<Pickup>(true); }
        catch { return; }

        var bestScore = new Dictionary<string, int>();

        foreach (Pickup pickup in all)
        {
            if (pickup == null || pickup.item == null) continue;
            // Свои же скрытые шаблоны с прошлого кэширования не кэшируем повторно
            if (pickup.gameObject.name.StartsWith("WorldTemplate_")) continue;
            if ((pickup.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
            // Ручная настройка важнее автоматики
            if (pickup.item.worldPrefab != null) continue;

            string id = pickup.item.Id;
            if (string.IsNullOrEmpty(id)) continue;

            int score = ScoreWorldTemplate(pickup);
            if (worldTemplateCache.TryGetValue(id, out GameObject existing))
            {
                if (existing != null && bestScore.TryGetValue(id, out int old) && old >= score)
                    continue;
                if (existing != null) Destroy(existing);
            }

            GameObject backup = Instantiate(pickup.gameObject);
            backup.name = $"WorldTemplate_{id}";
            backup.SetActive(false);
            backup.hideFlags = HideFlags.HideAndDontSave;
            // Несобираемые триггеры оригинала нам не мешают: объект скрыт.
            // Позицию сбрасываем в начало координат, чтобы не смущала в иерархии.
            backup.transform.position = Vector3.zero;
            backup.transform.rotation = Quaternion.identity;

            worldTemplateCache[id] = backup;
            bestScore[id] = score;
        }

        if (worldTemplateCache.Count > 0)
            Debug.Log($"[Inventory] Запомнено шаблонов мировых моделей: {worldTemplateCache.Count}");
    }

    /// <summary>
    /// Эвристика «настоящести» пикапа. Настоящие модели оружия в сцене
    /// (Ppsh-41(GR) и т.п.) — триггер без Rigidbody и без вращения;
    /// тестовые кубы визарда — наоборот: kinematic-Rigidbody + spin.
    /// </summary>
    static int ScoreWorldTemplate(Pickup pickup)
    {
        int score = 0;
        GameObject go = pickup.gameObject;

        if (pickup.spin) score -= 2;
        if (go.GetComponent<Rigidbody>() == null) score += 2;

        Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            if (c != null && c.isTrigger) { score += 1; break; }

        if (go.GetComponentsInChildren<Renderer>(true).Length > 1) score += 1;
        if (go.name.Contains("(GR)")) score += 2;
        if (go.name.StartsWith("Pickup_") || go.name.Contains("Generic")) score -= 1;

        return score;
    }

    void OnApplicationQuit()
    {
        if (autoSaveOnQuit) Save();
    }

    void Update()
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
        {
            SetTarget(null);
            return;
        }

        if (Input.GetKeyDown(toggleKey))
            ToggleInventory();

        if (isOpen)
        {
            SetTarget(null);
            // R только при открытом инвентаре, иначе конфликт с перезарядкой
            if (Input.GetKeyDown(sortKey)) SortAndStack();

            if (useHotkeys) HandleHotkeys();
            return;
        }

        DetectPickup();

        if (currentTarget != null && Input.GetKeyDown(pickupKey))
            TryPickUp(currentTarget);

        if (useHotkeys) HandleHotkeys();
    }

    /// <summary>
    /// Цифры 1..9 — быстрый доступ. Номер берётся из ItemData.hotbarSlot,
    /// а не из позиции в списке: слот предмета не должен зависеть
    /// от сортировки и порядка подбора.
    /// </summary>
    void HandleHotkeys()
    {
        for (int digit = 1; digit <= 9; digit++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + (digit - 1))) continue;

            int index = FindSlotByHotbar(digit);
            if (index < 0)
            {
                Debug.Log($"[Inventory] В слоте {digit} ничего нет.");
                continue;
            }

            if (Input.GetKey(dropKey)) DropSlot(index);
            else if (!(hotkeyTogglesHolster && ToggleHolsterIfEquipped(index))) UseSlot(index);
            return;
        }
    }

    /// <summary>
    /// Повторная цифра: если это оружие уже в руках — убрать его.
    /// True если убрали (UseSlot вызывать не надо).
    /// </summary>
    bool ToggleHolsterIfEquipped(int index)
    {
        if (!IsValidIndex(index)) return false;

        ItemData item = slots[index].item;
        if (item == null || !item.IsEquippable) return false;
        if (!item.IsCurrentlyEquipped) return false;
        if (WeaponSlotManager.Instance == null) return false;

        WeaponSlotManager.Instance.Holster();
        Debug.Log($"[Inventory] {item.itemName} убрано повторной цифрой.");
        return true;
    }

    public int FindSlotByHotbar(int digit)
    {
        if (digit < 1 || digit > 9) return -1;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty) continue;
            if (slots[i].item.hotbarSlot == digit) return i;
        }
        return -1;
    }

    public ItemData GetHotbarItem(int digit)
    {
        int index = FindSlotByHotbar(digit);
        return index >= 0 ? slots[index].item : null;
    }

    // Каст + сфера вплотную: один SphereCast слепнет внутри коллайдера и перекрывается столешницей
    void DetectPickup()
    {
        if (playerCamera == null)
        {
            SetTarget(null);
            return;
        }

        Transform cam = playerCamera.transform;
        Vector3 origin = cam.position;
        Vector3 dir = cam.forward;

        Pickup best = null;
        float bestScore = float.MaxValue;
        float radius = Mathf.Max(0.01f, pickupCastRadius);
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, dir, pickupHits,
            pickupRange, pickupMask, QueryTriggerInteraction.Collide);
        if (hitCount == pickupHits.Length)
        {
            RaycastHit[] allHits = Physics.SphereCastAll(origin, radius, dir, pickupRange,
                pickupMask, QueryTriggerInteraction.Collide);
            foreach (RaycastHit hit in allHits)
                Consider(hit.collider, cam, ref best, ref bestScore);
        }
        else
        {
            for (int i = 0; i < hitCount; i++)
                Consider(pickupHits[i].collider, cam, ref best, ref bestScore);
        }

        int nearbyCount = Physics.OverlapSphereNonAlloc(origin, nearPickupRadius,
            nearbyColliders, pickupMask, QueryTriggerInteraction.Collide);
        if (nearbyCount == nearbyColliders.Length)
        {
            Collider[] allNearby = Physics.OverlapSphere(origin, nearPickupRadius,
                pickupMask, QueryTriggerInteraction.Collide);
            foreach (Collider col in allNearby)
                Consider(col, cam, ref best, ref bestScore);
        }
        else
        {
            for (int i = 0; i < nearbyCount; i++)
                Consider(nearbyColliders[i], cam, ref best, ref bestScore);
        }

        SetTarget(best);
    }

    // Оценка — угол от центра: прицел всегда бьёт край сферы. Вплотную угол = 0
    void Consider(Collider col, Transform cam, ref Pickup best, ref float bestScore)
    {
        if (col == null) return;

        Pickup pickup = col.GetComponentInParent<Pickup>();
        if (pickup == null || pickup.item == null) return;

        Vector3 point = col.bounds.center;
        Vector3 toTarget = point - cam.position;
        float distance = toTarget.magnitude;

        if (distance > pickupRange) return;
        float angle = distance < nearPickupRadius
            ? 0f
            : Vector3.Angle(cam.forward, toTarget.normalized);
        if (angle > maxPickupAngle) return;
        float score = angle * 10f + distance;
        if (score >= bestScore) return;

        if (requireLineOfSight && !HasLineOfSight(cam.position, pickup, col)) return;

        best = pickup;
        bestScore = score;
    }

    /// <summary>
    /// Виден ли предмет: не закрыт ли он стеной. Собственные коллайдеры предмета
    /// и коллайдеры игрока преградой не считаются.
    /// </summary>
    bool HasLineOfSight(Vector3 eye, Pickup pickup, Collider target)
    {
        Vector3 point = target.bounds.center;
        Vector3 dir = point - eye;
        float distance = dir.magnitude;

        if (distance < 0.05f) return true;

        int hitCount = Physics.RaycastNonAlloc(eye, dir.normalized, lineOfSightHits,
            distance, occlusionMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = lineOfSightHits;
        if (hitCount == lineOfSightHits.Length)
        {
            hits = Physics.RaycastAll(eye, dir.normalized, distance,
                occlusionMask, QueryTriggerInteraction.Ignore);
            hitCount = hits.Length;
        }
        System.Array.Sort(hits, 0, hitCount, HitDistanceComparer);

        Transform pickupRoot = pickup.transform;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null) continue;
            if (hit.collider == target) return true;
            if (hit.collider.transform.IsChildOf(pickupRoot)) return true;

            // Сам игрок и его оружие в руках не заслоняют предмет
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (playerCamera != null && hit.collider.transform.IsChildOf(playerCamera.transform)) continue;

            return false;
        }

        return true;
    }

    void SetTarget(Pickup next)
    {
        if (currentTarget == next) return;

        if (currentTarget != null) currentTarget.SetHighlight(false);
        currentTarget = next;
        if (currentTarget != null) currentTarget.SetHighlight(true);

        OnTargetChanged?.Invoke(currentTarget);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!autoPickupOnTouch) return;
        Pickup p = other.GetComponentInParent<Pickup>();
        if (p != null) TryPickUp(p);
    }
    public void TryPickUp(Pickup pickup)
    {
        if (pickup == null) return;

        if (pickup.item == null)
        {
            Debug.LogWarning($"[Inventory] У {pickup.name} не задан ItemData — подбирать нечего.");
            return;
        }

        int taken = AddItem(pickup.item, pickup.amount);

        if (taken <= 0)
        {
            Debug.Log("[Inventory] Инвентарь полон.");
            return;
        }

        PlayClip(pickup.item.pickupSound);
        Debug.Log($"[Inventory] Подобрано: {pickup.item.itemName} x{taken}");
        OnItemPickedUp?.Invoke(pickup.item, taken);

        if (showFloatingText)
        {
            string label = taken > 1
                ? $"+{taken}  {pickup.item.itemName}"
                : $"+ {pickup.item.itemName}";
            FloatingText.Show(label, pickup.transform.position + Vector3.up * 0.35f,
                              pickup.item.RarityColor);
        }

        if (pickup == currentTarget && taken >= pickup.amount) SetTarget(null);
        pickup.OnPickedUp(taken);

        if (autoSortOnPickup) SortAndStack();
    }

    // Возвращает сколько реально влезло (0 — не влезло ничего)
    public int AddItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        int remaining = amount;
        if (item.stackable)
        {
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                Slot slot = slots[i];
                if (slot.item != item || slot.FreeSpace <= 0) continue;

                int toAdd = Mathf.Min(slot.FreeSpace, remaining);
                slot.amount += toAdd;
                remaining -= toAdd;
            }
        }

        while (remaining > 0 && slots.Count < maxSlots)
        {
            int perSlot = item.stackable ? Mathf.Min(item.maxStack, remaining) : 1;
            slots.Add(new Slot(item, perSlot));
            remaining -= perSlot;
        }

        int added = amount - remaining;
        if (added > 0)
        {
            OnInventoryChanged?.Invoke();
            QuestSystem.NotifyItemAdded(item.Id);
        }
        return added;
    }

    public bool AddItemFull(ItemData item, int amount) =>
        HasSpaceFor(item, amount) && AddItem(item, amount) == amount;
    public bool RemoveItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;
        if (CountItem(item) < amount) return false;

        int remaining = amount;
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (slots[i].item != item) continue;

            int taken = Mathf.Min(slots[i].amount, remaining);
            slots[i].amount -= taken;
            remaining -= taken;
            if (slots[i].amount <= 0) slots.RemoveAt(i);
        }

        if (remaining < amount)
        {
            OnInventoryChanged?.Invoke();
            ValidateEquippedWeapon(item);
        }
        return remaining <= 0;
    }

    /// <summary>Использовать предмет из слота.</summary>
    public void UseSlot(int index)
    {
        if (!IsValidIndex(index)) return;

        Slot slot = slots[index];
        ItemData item = slot.item;
        if (item == null) return;

        if (!item.Use(gameObject)) return;

        PlayClip(item.useSound);

        if (item.consumeOnUse)
        {
            slot.amount--;
            if (slot.amount <= 0) slots.RemoveAt(index);
        }

        OnInventoryChanged?.Invoke();
        ValidateEquippedWeapon(item);
        Debug.Log($"[Inventory] Использовано: {item.itemName}");
    }

    public void DropOne(int index) => Drop(index, 1);
    public void DropSlot(int index)
    {
        if (!IsValidIndex(index)) return;
        Drop(index, slots[index].amount);
    }

    /// <summary>
    /// Секретная фишка: выбросить ВЕСЬ инвентарь в мир.
    /// Вызывается вручную или через контекстное меню компонента.
    /// Предметы вылетают по штуке в кадр, а не стаками и не все разом — иначе всё
    /// спавнится в одной точке и физика разрывает стопку. Руки пустеют сами через Validate.
    /// </summary>
    [ContextMenu("Секрет: выбросить весь инвентарь")]
    public void DropAll()
    {
        if (slots.Count == 0)
        {
            Debug.Log("[Inventory] Инвентарь пуст — выбрасывать нечего.");
            return;
        }
        if (dropAllRoutine != null) return;

        dropAllRoutine = StartCoroutine(DropAllRoutine());
    }

    System.Collections.IEnumerator DropAllRoutine()
    {
        int startSlots = slots.Count;
        int totalUnits = 0;
        // Веер вместо одной кучи: стаки ложатся раздельно, их можно пересчитать.
        dropScatterYaw = 25f;
        Debug.Log($"[Inventory] Секретный сброс: выбрасываю всё ({startSlots} слотов).");

        try
        {
            while (slots.Count > 0)
            {
                int last = slots.Count - 1;
                int before = slots[last].amount;
                DropOne(last);

                // Слот либо уменьшился на штуку, либо исчез целиком.
                // Иначе дроп не удался (нет префаба/шаблона) — дальше не идём,
                // иначе зависнем в бесконечном цикле.
                bool progressed = slots.Count < last + 1 ||
                                  (slots.Count == last + 1 && slots[last].amount < before);
                if (!progressed) break;
                totalUnits += 1;

                yield return null;
            }
        }
        finally
        {
            dropScatterYaw = 0f;
            dropAllRoutine = null;
        }

        Debug.Log($"[Inventory] Секретный сброс готов: слотов {startSlots}, " +
                  $"предметов {totalUnits}. Всё лежит вокруг — количество в стаке " +
                  $"видно на подсказке (xN), при подборе вернётся столько же.");
    }

    /// <summary>Направление броска. При массовом сбросе — веер, иначе ровно вперёд.</summary>
    Vector3 GetThrowDirection(Transform origin)
    {
        if (dropScatterYaw <= 0f) return origin.forward;
        float yaw = UnityEngine.Random.Range(-dropScatterYaw, dropScatterYaw);
        return Quaternion.Euler(0f, yaw, 0f) * origin.forward;
    }

    void Drop(int index, int count)
    {
        if (!IsValidIndex(index)) return;

        Slot slot = slots[index];
        ItemData item = slot.item;
        count = Mathf.Clamp(count, 1, slot.amount);

        if (!SpawnInWorld(item, count)) return;

        slot.amount -= count;
        if (slot.amount <= 0) slots.RemoveAt(index);

        OnInventoryChanged?.Invoke();
        ValidateEquippedWeapon(item);
        Debug.Log($"[Inventory] Выброшено: {item.itemName} x{count}");
    }

    bool SpawnInWorld(ItemData item, int count)
    {
        // Приоритет: 1) явный worldPrefab у предмета,
        // 2) настоящая мировая модель со сцены (Ppsh-41(GR) и т.п.),
        // 3) клон модели из рук (для оружия без мирового пикапа в сцене),
        // 4) универсальный куб.
        if (item.worldPrefab != null)
            return SpawnFromPrefab(item.worldPrefab, item, count);

        if (TrySpawnFromWorldTemplate(item, count))
            return true;

        if (item.IsEquippable && TrySpawnFromHeldModel(item, count))
            return true;

        if (genericPickupPrefab != null)
            return SpawnFromPrefab(genericPickupPrefab, item, count);

        Debug.LogWarning($"[Inventory] Нет префаба для дропа {item.itemName}. " +
                         "Задай Generic Pickup Prefab или World Prefab в ItemData.");
        return false;
    }

    bool SpawnFromPrefab(GameObject prefab, ItemData item, int count)
    {
        Transform origin = playerCamera != null ? playerCamera.transform : transform;
        Vector3 pos = origin.position + origin.forward * dropForwardOffset + Vector3.up * dropUpOffset;
        Vector3 throwDir = GetThrowDirection(origin);

        GameObject obj = Instantiate(prefab, pos, Quaternion.LookRotation(throwDir));

        Pickup p = obj.GetComponent<Pickup>();
        if (p == null) p = obj.GetComponentInChildren<Pickup>();
        Collider pickupCollider = p != null ? p.GetComponentInChildren<Collider>() : null;
        if (p == null || !p.gameObject.activeInHierarchy || pickupCollider == null || !pickupCollider.enabled)
        {
            Debug.LogWarning($"[Inventory] Префаб {prefab.name} не содержит доступный Pickup с коллайдером. Предмет {item.itemName} остался в инвентаре.");
            Destroy(obj);
            return false;
        }
        p.Configure(item, count);
        p.promptText = "";
        p.RecaptureBase();

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // GenericPickup з візарда — kinematic, тому AddForce мовчки нічого не дає
            // і дроп зависає в повітрі. Вимикаємо kinematic, щоб кидок реально працював.
            if (rb.isKinematic) rb.isKinematic = false;
            rb.WakeUp();
            if (dropThrowForce > 0f)
                rb.AddForce(throwDir * dropThrowForce, ForceMode.Impulse);
        }
        return true;
    }

    /// <summary>
    /// Дроп клоном настоящей мировой модели из кэша (такой же, какую поднимали).
    /// Оригиналы из сцены к моменту дропа обычно уже уничтожены подбором,
    /// поэтому клонируем скрытый шаблон из CacheWorldPickupTemplates.
    /// </summary>
    bool TrySpawnFromWorldTemplate(ItemData item, int count)
    {
        if (item == null) return false;
        if (!worldTemplateCache.TryGetValue(item.Id, out GameObject template) || template == null)
            return false;

        Transform origin = playerCamera != null ? playerCamera.transform : transform;
        Vector3 pos = origin.position + origin.forward * dropForwardOffset + Vector3.up * dropUpOffset;

        GameObject obj = Instantiate(template, pos, Quaternion.LookRotation(GetThrowDirection(origin)));
        obj.hideFlags = HideFlags.None;
        obj.name = $"Dropped_{item.itemName}";
        obj.transform.SetParent(null, true);
        obj.transform.position = pos;
        obj.SetActive(true);

        Pickup p = obj.GetComponent<Pickup>();
        if (p == null) p = obj.GetComponentInChildren<Pickup>();
        if (p == null)
        {
            Destroy(obj);
            return false;
        }

        // Мировые пикапы висят триггерами без физики. Для броска включаем
        // твёрдую физику на ВСЕХ коллайдерах (свой коллайдер тоже подхватится).
        // Наведение всё равно работает: DetectPickup бьёт и по триггерам, и по твёрдым.
        Collider[] templateColliders = obj.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in templateColliders)
        {
            if (c == null) continue;
            c.enabled = true;
            c.isTrigger = false;
        }
        if (templateColliders.Length == 0) EnsureBoxCollider(obj);

        p.Configure(item, count);
        p.promptText = "";
        // На земле модель лежит, а не танцует: иначе уснувшая физика
        // отдаст управление bob, и модель прыгнет назад в точку дропа.
        p.spin = false;
        p.bob = false;
        p.RecaptureBase();

        // Падение с физикой и заморозка — DroppedWeapon
        // (как и у оружия, выпавшего из врагов). Без доводок трансформа.
        LaunchWithDropPhysics(obj, origin);

        return true;
    }

    /// <summary>
    /// Запасной путь: в сцене не было мирового пикапа для этого оружия
    /// (например, тестовые кубы визарда) — клонируем визуал модели из рук.
    /// С модели срезаются все боевые скрипты, остаётся только внешность.
    /// </summary>
    bool TrySpawnFromHeldModel(ItemData item, int count)
    {
        WeaponSlotManager mgr = WeaponSlotManager.Instance;
        if (mgr == null) return false;

        EquippableWeapon template = mgr.Find(item.equipWeaponId);
        if (template == null) return false;

        Transform origin = playerCamera != null ? playerCamera.transform : transform;
        Vector3 pos = origin.position + origin.forward * dropForwardOffset + Vector3.up * dropUpOffset;
        Quaternion rot = Quaternion.LookRotation(origin.forward);

        // Клон модели из рук иначе сам перецепится обратно в держатель
        // (HeldItem.Awake -> AttachToHolder). Подавляем на время Instantiate,
        // как это уже делает GrenadeItem для копии-снаряда.
        GameObject obj;
        HeldItem.SuppressAttachOnAwake = true;
        try
        {
            obj = Instantiate(template.gameObject, pos, rot);
        }
        finally
        {
            HeldItem.SuppressAttachOnAwake = false;
        }

        obj.name = $"Dropped_{item.itemName}";
        obj.transform.SetParent(null, true);
        obj.transform.position = pos;
        obj.transform.rotation = rot;

        // Срезаем логику рук/стрельбы: в мире нужна только внешность.
        foreach (Wep w in obj.GetComponentsInChildren<Wep>(true))
        {
            w.enabled = false;
            Destroy(w);
        }
        foreach (HeldItem held in obj.GetComponentsInChildren<HeldItem>(true))
        {
            held.enabled = false;
            Destroy(held);
        }
        foreach (EquippableWeapon eq in obj.GetComponentsInChildren<EquippableWeapon>(true))
        {
            eq.enabled = false;
            Destroy(eq);
        }
        foreach (Pickup old in obj.GetComponentsInChildren<Pickup>(true)) Destroy(old);
        foreach (AudioSource src in obj.GetComponentsInChildren<AudioSource>(true)) Destroy(src);

        // В иерархии рук могут быть свои Rigidbody (у гранат/ножей физика
        // пригашена, но компоненты висят). Оставляем только корневой,
        // иначе Unity ругнётся на несколько тел в одной иерархии.
        Rigidbody rootRb = obj.GetComponent<Rigidbody>();
        foreach (Rigidbody r in obj.GetComponentsInChildren<Rigidbody>(true))
        {
            if (r != null && r != rootRb) Destroy(r);
        }

        SetLayerRecursively(obj, 0);
        foreach (Renderer r in obj.GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = true;

        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
        {
            if (c == null) continue;
            c.enabled = true;
            c.isTrigger = false;
        }
        if (colliders.Length == 0) EnsureBoxCollider(obj);

        obj.SetActive(true);

        // Pickup добавляем последним: его Awake сразу кэширует рендереры
        // уже очищенного объекта и создаёт подсветку.
        Pickup p = obj.AddComponent<Pickup>();
        p.spin = false;
        p.bob = false;
        p.Configure(item, count);
        p.promptText = "";
        p.RecaptureBase();

        LaunchWithDropPhysics(obj, origin);

        return true;
    }

    /// <summary>
    /// Бросок с настоящей физикой: гравитация и заморозка, когда успокоилось.
    /// Тот же DroppedWeapon, что у оружия, выпавшего из врагов (см. EnemyLoadout).
    /// Никаких принудительных доводок трансформа — только физика, иначе
    /// kinematic-движки везут всё, что стоит на оружии.
    /// Время жизни 0 — выброшенное игроком лежит, пока его не подберут.
    /// </summary>
    void LaunchWithDropPhysics(GameObject obj, Transform origin)
    {
        var dropper = obj.AddComponent<ZharkoeLeto44.AI.DroppedWeapon>();
        Vector3 launch = GetThrowDirection(origin) * dropThrowForce + Vector3.up * 0.5f;
        dropper.Initialize(
            gravityDelay: 0.2f,
            launchVelocity: launch,
            // Без вращения вообще: летит ровно, как лежало в руках.
            spin: Vector3.zero,
            groundMask: ~0,
            lifetime: 0f,
            applySpin: false);
    }

    /// <summary>Страховочный твёрдый коллайдер по габаритам модели.</summary>
    static void EnsureBoxCollider(GameObject obj)
    {
        Renderer rend = obj.GetComponentInChildren<Renderer>();
        BoxCollider box = obj.AddComponent<BoxCollider>();
        if (rend != null)
        {
            // bounds мировые — пересчитываем в локальные через потерю масштаба
            Vector3 worldSize = rend.bounds.size;
            Vector3 lossy = obj.transform.lossyScale;
            box.size = new Vector3(
                worldSize.x / Mathf.Max(0.001f, lossy.x),
                worldSize.y / Mathf.Max(0.001f, lossy.y),
                worldSize.z / Mathf.Max(0.001f, lossy.z));
            box.center = obj.transform.InverseTransformPoint(rend.bounds.center);
        }
        else
        {
            box.size = new Vector3(0.6f, 0.2f, 0.2f);
        }
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    public bool HasSpaceFor(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;

        int remaining = amount;

        if (item.stackable)
            foreach (Slot s in slots)
                if (s.item == item) remaining -= s.FreeSpace;

        if (remaining <= 0) return true;

        int freeSlots = maxSlots - slots.Count;
        int perSlot = item.stackable ? item.maxStack : 1;
        return freeSlots * perSlot >= remaining;
    }

    public int CountItem(ItemData item)
    {
        if (item == null) return 0;
        int total = 0;
        foreach (Slot s in slots)
            if (s.item == item) total += s.amount;
        return total;
    }

    public int CountItemById(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        int total = 0;
        foreach (Slot s in slots)
            if (s.item != null && s.item.Id == id) total += s.amount;
        return total;
    }

    public bool HasItem(ItemData item, int amount = 1) => CountItem(item) >= amount;
    public bool HasKey(string keyId) => CountItemByKeyId(keyId) > 0;

    /// <summary>
    /// Есть ли в инвентаре предмет, экипирующий оружие с данным id.
    /// Нужно WeaponSlotManager, чтобы не давать переключиться на неподобранное оружие.
    /// </summary>
    public bool HasWeaponItem(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return false;

        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.equipWeaponId == weaponId) return true;

        return false;
    }

    /// <summary>
    /// Убедиться, что предмет в руках всё ещё есть в инвентаре.
    /// Вызывается после Drop / RemoveItem / Clear / Use / Load.
    /// Без этого выброшенное оружие оставалось видимым в руках,
    /// хотя из инвентаря уже пропадало.
    /// </summary>
    public void ValidateEquippedWeapon() => ValidateEquippedWeapon(null);

    /// <summary>
    /// Точечная проверка: если изменившийся предмет не связан с оружием в руках,
    /// руки не трогаем. Это защищает стартовое оружие (equippedOnStart без
    /// предмета в сумке) от случайного holster при использовании бинта,
    /// выбросе патронов и т.п.
    /// </summary>
    public void ValidateEquippedWeapon(ItemData changedItem)
    {
        WeaponSlotManager mgr = WeaponSlotManager.Instance;
        if (mgr == null) return;

        EquippableWeapon current = mgr.Current;
        if (current == null) return;

        // Изменился конкретный предмет, не связанный с тем, что в руках, — выходим.
        // Сравнение по equipWeaponId, а не по ассету: разные ItemData могут
        // давать одно и то же оружие.
        if (changedItem != null && changedItem.equipWeaponId != current.weaponId) return;

        if (HasWeaponItem(current.weaponId)) return;

        // Бросок гранаты сам списывает последнюю штуку и сам убирает руки
        // в конце анимации. Если убрать руки прямо здесь, OnDisable остановит
        // корутину броска посередине (StopCoroutine в GrenadeItem.OnDisable).
        GrenadeItem throwing = current.GetComponentInChildren<GrenadeItem>();
        if (throwing != null && throwing.IsThrowing) return;

        // Экипированного предмета в сумке больше нет — руки должны опустеть.
        // Если осталось другое оружие, сразу переключаемся на него,
        // иначе уходим в пустые руки.
        var owned = mgr.GetOwnedWeapons();
        if (owned.Count > 0)
            mgr.Equip(owned[0].weaponId);
        else
            mgr.Holster();
    }

    // Граната после броска знает только weaponId — по нему ищем ассет для списания
    public ItemData GetWeaponItem(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;

        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.equipWeaponId == weaponId) return s.item;

        return null;
    }

    public int CountWeaponItem(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return 0;

        int total = 0;
        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.equipWeaponId == weaponId) total += s.amount;

        return total;
    }

    public int CountItemByKeyId(string keyId)
    {
        if (string.IsNullOrEmpty(keyId)) return 0;
        int total = 0;
        foreach (Slot s in slots)
            if (s.item != null && s.item.itemType == ItemType.Key && s.item.keyId == keyId)
                total += s.amount;
        return total;
    }

    public Slot GetSlot(int index) => IsValidIndex(index) ? slots[index] : null;

    bool IsValidIndex(int index) => index >= 0 && index < slots.Count;

    /// <summary>Поменять два слота местами (для drag&drop в UI).</summary>
    public void SwapSlots(int a, int b)
    {
        if (!IsValidIndex(a) || !IsValidIndex(b) || a == b) return;
        Slot tmp = slots[a];
        slots[a] = slots[b];
        slots[b] = tmp;
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Собрать одинаковые предметы в минимум стаков и разложить по категориям.</summary>
    public void SortAndStack()
    {
        var merged = new List<Slot>();

        foreach (Slot s in slots)
        {
            if (s.IsEmpty) continue;

            int remaining = s.amount;
            if (s.item.stackable)
            {
                foreach (Slot m in merged)
                {
                    if (m.item != s.item || m.FreeSpace <= 0) continue;
                    int move = Mathf.Min(m.FreeSpace, remaining);
                    m.amount += move;
                    remaining -= move;
                    if (remaining <= 0) break;
                }
            }
            while (remaining > 0)
            {
                int perSlot = s.item.stackable ? Mathf.Min(s.item.maxStack, remaining) : 1;
                merged.Add(new Slot(s.item, perSlot));
                remaining -= perSlot;
            }
        }

        merged.Sort(CompareSlots);

        slots = merged;
        lastSortTime = Time.unscaledTime;
        OnInventoryChanged?.Invoke();
        OnSorted?.Invoke();
        Debug.Log("[Inventory] Отсортировано по категориям.");
    }

    /// <summary>
    /// Порядок: категория (оружие → патроны → медикаменты → ключи → прочее),
    /// затем редкость (сначала ценное), затем ручной sortOrder, затем имя,
    /// затем крупные стаки выше.
    /// </summary>
    static int CompareSlots(Slot a, Slot b)
    {
        int byCategory = a.item.CategoryOrder.CompareTo(b.item.CategoryOrder);
        if (byCategory != 0) return byCategory;

        int byRarity = ((int)b.item.rarity).CompareTo((int)a.item.rarity);
        if (byRarity != 0) return byRarity;

        int byManual = a.item.sortOrder.CompareTo(b.item.sortOrder);
        if (byManual != 0) return byManual;

        int byName = string.Compare(a.item.itemName, b.item.itemName, StringComparison.CurrentCulture);
        if (byName != 0) return byName;

        return b.amount.CompareTo(a.amount);
    }

    /// <summary>Категории, реально присутствующие в инвентаре, в порядке отображения.</summary>
    public List<ItemType> GetPresentCategories()
    {
        var result = new List<ItemType>();
        foreach (ItemType type in ItemData.DisplayOrder)
        {
            foreach (Slot s in slots)
            {
                if (s.IsEmpty || s.item.itemType != type) continue;
                result.Add(type);
                break;
            }
        }
        return result;
    }

    /// <summary>Индексы слотов данной категории (для фильтра по вкладкам).</summary>
    public List<int> GetSlotIndicesOfCategory(ItemType type)
    {
        var result = new List<int>();
        for (int i = 0; i < slots.Count; i++)
            if (!slots[i].IsEmpty && slots[i].item.itemType == type) result.Add(i);
        return result;
    }

    /// <summary>Сколько слотов занято предметами данной категории.</summary>
    public int CountCategory(ItemType type)
    {
        int total = 0;
        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.itemType == type) total += s.amount;
        return total;
    }

    public void Clear()
    {
        slots.Clear();
        OnInventoryChanged?.Invoke();
        ValidateEquippedWeapon();
    }

    // =====================================================================
    // Открытие / закрытие
    // =====================================================================
    public void ToggleInventory() => SetOpen(!isOpen);

    public void SetOpen(bool open)
    {
        if (isOpen == open) return;
        isOpen = open;

        if (inventoryPanel != null) inventoryPanel.SetActive(isOpen);

        if (manageCursor)
        {
            Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isOpen;
        }

        if (pauseGameWhenOpen)
            Time.timeScale = isOpen ? 0f : 1f;

        if (isOpen) SetTarget(null);

        OnToggled?.Invoke(isOpen);
        OnInventoryChanged?.Invoke();
    }

    void OnDisable()
    {
        // Не оставляем игру на паузе, если компонент выключили при открытом инвентаре
        if (isOpen && pauseGameWhenOpen) Time.timeScale = 1f;
    }

    // =====================================================================
    // Сохранение / загрузка
    // =====================================================================
    [Serializable]
    private class SaveEntry
    {
        public string id;
        public int amount;
    }

    [Serializable]
    private class SaveData
    {
        public List<SaveEntry> entries = new List<SaveEntry>();
    }

    public void Save()
    {
        var data = new SaveData();
        foreach (Slot s in slots)
        {
            if (s.IsEmpty) continue;
            data.entries.Add(new SaveEntry { id = s.item.Id, amount = s.amount });
        }

        PlayerPrefs.SetString(saveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
        Debug.Log($"[Inventory] Сохранено слотов: {data.entries.Count}");
    }

    public void Load()
    {
        if (!PlayerPrefs.HasKey(saveKey))
        {
            Debug.Log("[Inventory] Сохранения нет.");
            return;
        }

        ItemDatabase db = ItemDatabase.Instance;
        if (db == null)
        {
            Debug.LogWarning("[Inventory] ItemDatabase не найден в Resources — загрузка невозможна. " +
                             "Создай ассет Inventory/Item Database и положи его в Assets/Resources/ItemDatabase.asset");
            return;
        }

        SaveData data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(saveKey));
        if (data == null) return;

        slots.Clear();
        foreach (SaveEntry e in data.entries)
        {
            ItemData item = db.GetById(e.id);
            if (item == null)
            {
                Debug.LogWarning($"[Inventory] Предмет с id '{e.id}' не найден в базе — пропущен.");
                continue;
            }
            AddItem(item, e.amount);
        }

        OnInventoryChanged?.Invoke();
        ValidateEquippedWeapon();
        Debug.Log($"[Inventory] Загружено слотов: {slots.Count}");
    }

    public void DeleteSave() => PlayerPrefs.DeleteKey(saveKey);

    // =====================================================================
    // Прочее
    // =====================================================================
    void PlayClip(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource != null) audioSource.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, transform.position);
    }

    void OnGUI()
    {
        if (!drawDebugGUI) return;

        // Подсказка по центру — только когда инвентарь закрыт
        if (!isOpen && currentTarget != null)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f + 30f, 400f, 30f),
                      currentTarget.GetPrompt(), style);
            return;
        }

        // Список содержимого — когда открыт и нет настоящего UI
        if (isOpen && inventoryPanel == null)
        {
            float w = 340f;
            float h = 40f + slots.Count * 22f;
            GUI.Box(new Rect(20f, 90f, w, h), $"Инвентарь ({slots.Count}/{maxSlots})");

            for (int i = 0; i < slots.Count; i++)
            {
                Slot s = slots[i];
                string line = $"{i + 1}. {s.item.itemName} x{s.amount}";
                GUI.Label(new Rect(35f, 115f + i * 22f, w - 30f, 22f), line);
            }

            GUI.Label(new Rect(20f, 90f + h + 4f, 600f, 22f),
                      "1..9 — использовать | G+цифра — выбросить");
        }
    }

    void OnDrawGizmosSelected()
    {
        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null) return;

        Vector3 origin = cam.transform.position;
        Vector3 end = origin + cam.transform.forward * pickupRange;

        // Луч прицела
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, end);
        if (pickupCastRadius > 0f) Gizmos.DrawWireSphere(end, pickupCastRadius);

        // Зона «вплотную»: здесь предмет берётся без попадания луча
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(origin, nearPickupRadius);

        // Конус допустимого угла наведения
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.4f);
        float rad = maxPickupAngle * Mathf.Deg2Rad;
        float coneRadius = Mathf.Tan(rad) * pickupRange;

        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            Vector3 offset = cam.transform.right * Mathf.Cos(a) * coneRadius
                             + cam.transform.up * Mathf.Sin(a) * coneRadius;
            Gizmos.DrawLine(origin, end + offset);
        }
    }
}
