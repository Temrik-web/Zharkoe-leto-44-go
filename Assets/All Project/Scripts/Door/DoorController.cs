using UnityEngine;
using System.Collections;

public class DoorController : MonoBehaviour
{
    [Header("Настройки двери")]
    public float openAngle = 90f; // знак = сторона открытия
    public float openSpeed = 2f;
    public float closeSpeed = 2f;
    [Header("Взаимодействие")]
    public float interactDistance = 3f;
    public LayerMask interactMask = 1 << 8;
    public bool showHint = true;

    [Header("Блокировка")]
    public bool isLocked = false;
    [Tooltip("Id ключа из инвентаря (ItemData.keyId, например cellar). Пусто — ключ не нужен.")]
    public string requiredKeyId = "";
    [Tooltip("Что писать при наведении на ЗАКРЫТУЮ дверь вместо «Нажмите E».")]
    public string lockedHintMessage = "Дверь закрыта — найдите ключ";
    [Tooltip("Квест, при котором замок снимается сам (например q_cellar_door " +
             "стартует в момент подбора ключа — дверь деда откроется). Пусто — только ключом.")]
    public string autoUnlockQuestId = "";
    [Tooltip("При каком статусе квеста снимать замок: 0 — взят (активен или выполнен), " +
             "1 — активен, 2 — выполнен, 3 — провален.")]
    public int autoUnlockQuestState = 0;
    [Tooltip("Квест, который закроется при первом открытии (например q_cellar_door).")]
    public string questToCompleteOnOpen = "";
    private bool questReported = false;

    [Header("Автозакрытие за игроком")]
    [Tooltip("Один раз закрыть дверь за игроком, когда он войдёт внутрь и отвернётся от неё.")]
    public bool autoCloseBehindPlayer = false;
    [Tooltip("Коллайдер пола дома (FloorDEDA). Пока игрок над ним — он внутри.")]
    public Collider autoCloseInsideArea;
    [Tooltip("На сколько метров игрок должен зайти за дверь, чтобы это считалось входом, а не порогом.")]
    public float autoCloseDepth = 2.5f;
    [Tooltip("Сколько секунд игрок должен провести внутри отвернувшись, прежде чем дверь закроется.")]
    public float autoCloseDelay = 0.35f;
    [Tooltip("Закрывать только когда игрок смотрит в сторону от двери.")]
    public bool autoCloseOnlyFacingAway = true;
    [Tooltip("Угол между взглядом игрока и дверью, после которого считаем, что он отвернулся.")]
    [Range(90f, 180f)] public float autoCloseFacingAngle = 100f;

    [Header("Мгновенное закрытие при подборе")]
    [Tooltip("ID предмета, при подборе которого дверь мгновенно закрывается (например: rgd33).")]
    public string snapClosedOnPickupItemId = "";

    [Header("Звуки")]
    public AudioClip openSound;
    public AudioClip closeSound;
    public AudioClip lockedSound;
    public AudioSource audioSource;

    [Header("Физический коллайдер проёма")]
    public Collider physicalCollider;

    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private bool isOpen = false;
    private bool isAnimating = false;
    private bool isLookingAtDoor = false;
    private bool autoCloseArmed = false;
    private bool autoCloseUsed = false;
    private Vector3 autoCloseInsideDir;
    private float autoCloseTimer = 0f;
    private Transform cachedPlayer;
    private InventorySystem pickupInventory;
    private Coroutine moveRoutine;

    /// <summary>Дверь открылась (сценарии могут подписаться).</summary>
    public event System.Action Opened;

    /// <summary>Открыта ли дверь сейчас (цель открытия, даже пока полотно движется).</summary>
    public bool IsOpen => isOpen;

    /// <summary>Дверь распахнулась и уже не двигается — можно начинать разговор.</summary>
    public bool IsFullyOpen => isOpen && !isAnimating;

    /// <summary>Заперта ли дверь сейчас.</summary>
    public bool IsLocked => isLocked;

    void Start()
    {
        closedRotation = transform.rotation;
        targetRotation = closedRotation;
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        if (physicalCollider == null)
        {
            Collider[] cols = GetComponentsInChildren<Collider>();
            foreach (Collider col in cols)
            {
                if (!col.isTrigger)
                {
                    physicalCollider = col;
                    break;
                }
            }
        }
        if (physicalCollider != null)
            physicalCollider.enabled = !isOpen;
        HookPickupEvent();
        CloseIfPickupAlreadyOwned();
    }

    void Update()
    {
        if (pickupInventory == null)
            HookPickupEvent();
        // Замок снимается состоянием квеста, не вводом — проверка до всех return.
        if (isLocked && !string.IsNullOrEmpty(autoUnlockQuestId) &&
            DialogueManager.IsQuestStateMatch(autoUnlockQuestId, autoUnlockQuestState))
        {
            isLocked = false;
            Debug.Log($"[Door] {name}: замок снят по квесту '{autoUnlockQuestId}'.", this);
        }
        isLookingAtDoor = false;
        if (Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, interactDistance, interactMask))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                {
                    isLookingAtDoor = true;
                }
            }
        }
        if (!isAnimating)
        {
            float speed = isOpen ? openSpeed : closeSpeed;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * speed);
            if (Quaternion.Angle(transform.rotation, targetRotation) < 0.01f)
                transform.rotation = targetRotation;
        }
        UpdateAutoClose();
        // E во время диалога принадлежит диалогу, иначе дверь дёрнется в том же кадре.
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;
        if (isLookingAtDoor && Input.GetKeyDown(KeyCode.E))
        {
            if (isAnimating) return;
            if (isLocked)
            {
                if (TryUnlockWithKey())
                {
                    Debug.Log($"[Door] {name}: открыто ключом '{requiredKeyId}'.", this);
                }
                else
                {
                    PlaySound(lockedSound);
                    return;
                }
            }
            ToggleDoor();
        }
    }
    public void ToggleDoor()
    {
        if (isAnimating || isLocked) return;
        if (isOpen) Close();
        else Open();
    }
    public void Open()
    {
        if (isAnimating || isOpen || isLocked) return;
        // Сторону «внутри дома» запоминаем при первом открытии: игрок стоит
        // снаружи, значит внутрь — в противоположную сторону от него.
        if (autoCloseBehindPlayer && !autoCloseArmed && !autoCloseUsed)
            ArmAutoClose();
        if (!questReported && !string.IsNullOrEmpty(questToCompleteOnOpen))
        {
            questReported = true;
            QuestSystem.CompleteQuest(questToCompleteOnOpen);
        }
        moveRoutine = StartCoroutine(MoveDoor(true));
        try { Opened?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }
    public void Close()
    {
        if (isAnimating || !isOpen) return;
        moveRoutine = StartCoroutine(MoveDoor(false));
    }
    private IEnumerator MoveDoor(bool opening)
    {
        isAnimating = true;
        isOpen = opening; // временно для расчёта targetRotation
        SetTargetRotation();
        PlaySound(opening ? openSound : closeSound);
        if (physicalCollider != null)
            physicalCollider.enabled = !opening;
        Quaternion startRot = transform.rotation;
        Quaternion endRot = targetRotation;
        float speed = opening ? openSpeed : closeSpeed;
        float distance = Quaternion.Angle(startRot, endRot);
        if (distance < 0.01f)
        {
            isAnimating = false;
            isOpen = opening;
            moveRoutine = null;
            yield break;
        }
        float progress = 0f;
        while (progress < 1f)
        {
            progress += Time.deltaTime * speed / distance;
            transform.rotation = Quaternion.Slerp(startRot, endRot, Mathf.Clamp01(progress));
            yield return null;
        }
        transform.rotation = endRot;
        isOpen = opening;
        isAnimating = false;
        moveRoutine = null;
    }

    public void SnapClosed()
    {
        if (!isOpen && !isAnimating) return;

        bool wasOpen = isOpen;
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        isAnimating = false;
        isOpen = false;
        targetRotation = closedRotation;
        transform.rotation = closedRotation;
        if (physicalCollider != null)
            physicalCollider.enabled = true;

        autoCloseUsed = true;
        autoCloseTimer = 0f;

        if (wasOpen)
            PlaySound(closeSound);

        Debug.Log($"[Door] {name}: дверь мгновенно закрыта после подбора '{snapClosedOnPickupItemId}'.", this);
    }

    private void HookPickupEvent()
    {
        if (string.IsNullOrEmpty(snapClosedOnPickupItemId) || pickupInventory != null) return;

        InventorySystem inventory = InventorySystem.Instance;
        if (inventory == null)
            inventory = FindObjectOfType<InventorySystem>();
        if (inventory == null) return;

        pickupInventory = inventory;
        pickupInventory.OnItemPickedUp += HandleItemPickedUp;
    }

    private void HandleItemPickedUp(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return;
        if (item.itemId != snapClosedOnPickupItemId) return;
        if (isOpen || isAnimating)
            SnapClosed();
    }

    private void CloseIfPickupAlreadyOwned()
    {
        if (string.IsNullOrEmpty(snapClosedOnPickupItemId)) return;

        InventorySystem inventory = pickupInventory != null ? pickupInventory : InventorySystem.Instance;
        if (inventory == null) return;
        if (inventory.CountItemById(snapClosedOnPickupItemId) > 0 && isOpen)
            SnapClosed();
    }

    private void OnDestroy()
    {
        if (pickupInventory != null)
            pickupInventory.OnItemPickedUp -= HandleItemPickedUp;
    }

    private void SetTargetRotation()
    {
        targetRotation = isOpen ? closedRotation * Quaternion.AngleAxis(openAngle, Vector3.up) : closedRotation;
    }
    public bool TryUnlockWithKey()
    {
        if (!isLocked) return true;
        if (!PlayerHasRequiredKey()) return false;
        isLocked = false;
        return true;
    }

    /// <summary>Есть ли нужный ключ в инвентаре (не снимая замок).</summary>
    public bool PlayerHasRequiredKey()
    {
        if (string.IsNullOrEmpty(requiredKeyId)) return false;
        InventorySystem inv = InventorySystem.Instance;
        if (inv == null) inv = FindObjectOfType<InventorySystem>();
        return inv != null && inv.HasKey(requiredKeyId);
    }

    /// <summary>
    /// Снять замок снаружи (по сценарию: немец отпер дверь и постучал).
    /// </summary>
    public void Unlock(bool playSound = true)
    {
        if (!isLocked) return;
        isLocked = false;
        if (playSound) PlaySound(openSound);
        Debug.Log($"[Door] {name}: замок снят сценарием.", this);
    }

    /// <summary>Запомнить, с какой стороны игрок открыл дверь: внутрь — наоборот.</summary>
    private void ArmAutoClose()
    {
        Transform player = ResolvePlayer();
        if (player == null) return;

        if (autoCloseInsideArea == null)
        {
            // Пол дома может быть вложен в префаб и не назначен вручную —
            // ищем по имени, которое пользователь дал объекту FloorDEDA.
            GameObject floor = GameObject.Find("FloorDEDA");
            if (floor != null)
            {
                autoCloseInsideArea = floor.GetComponent<Collider>();
                if (autoCloseInsideArea == null)
                    autoCloseInsideArea = floor.GetComponentInChildren<Collider>();
            }
        }

        Vector3 toPlayer = Flat(player.position - transform.position);
        if (toPlayer.sqrMagnitude < 0.01f) return;

        autoCloseInsideDir = -toPlayer.normalized;
        autoCloseArmed = true;

        Debug.Log($"[Door] {name}: автозакрытие взведено. Пол дома: " +
                  $"{(autoCloseInsideArea != null ? autoCloseInsideArea.name : "не найден")}.", this);
    }

    /// <summary>
    /// Один раз закрыть дверь за игроком: он должен зайти глубоко внутрь
    /// (не стоять на пороге) и отвернуться от двери.
    /// </summary>
    private void UpdateAutoClose()
    {
        if (!autoCloseArmed || autoCloseUsed) return;

        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
        {
            autoCloseTimer = 0f;
            return;
        }

        // Закрываем только после того, как полотно доехало до конца.
        if (!isOpen || isAnimating)
        {
            autoCloseTimer = 0f;
            return;
        }

        Transform player = ResolvePlayer();
        if (player == null)
        {
            autoCloseTimer = 0f;
            return;
        }

        Vector3 toPlayer = Flat(player.position - transform.position);

        // Если пол дома известен, «внутри» — это над полом. Иначе — сторона,
        // противоположная той, откуда игрок открыл дверь.
        bool inside;
        if (autoCloseInsideArea != null)
        {
            Bounds area = autoCloseInsideArea.bounds;
            Vector3 p = player.position;
            inside = p.x >= area.min.x - 0.1f && p.x <= area.max.x + 0.1f &&
                     p.z >= area.min.z - 0.1f && p.z <= area.max.z + 0.1f;
        }
        else
        {
            inside = Vector3.Dot(toPlayer, autoCloseInsideDir) >= autoCloseDepth;
        }

        // Не на пороге: игрок должен отойти от двери вглубь дома.
        if (!inside || toPlayer.magnitude < autoCloseDepth)
        {
            autoCloseTimer = 0f;
            return;
        }

        if (autoCloseOnlyFacingAway && !IsPlayerFacingAway(player))
        {
            autoCloseTimer = 0f;
            return;
        }

        autoCloseTimer += Time.deltaTime;
        if (autoCloseTimer < Mathf.Max(0f, autoCloseDelay)) return;

        autoCloseUsed = true;
        autoCloseTimer = 0f;
        Close();
        Debug.Log($"[Door] {name}: закрыта автоматически за вошедшим игроком.", this);
    }

    private bool IsPlayerFacingAway(Transform player)
    {
        Transform view = Camera.main != null ? Camera.main.transform : player;
        Vector3 toDoor = Flat(transform.position - player.position);
        if (toDoor.sqrMagnitude < 0.01f) return false;
        return Vector3.Angle(view.forward, toDoor) >= autoCloseFacingAngle;
    }

    private Transform ResolvePlayer()
    {
        if (cachedPlayer != null) return cachedPlayer;

        GameObject player = null;
        try { player = GameObject.FindGameObjectWithTag("Player"); }
        catch { player = null; }
        if (player == null && Camera.main != null)
            player = Camera.main.transform.root.gameObject;
        if (player != null) cachedPlayer = player.transform;
        return cachedPlayer;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }
    // Во время диалога прячем — E занята разговором.
    void OnGUI()
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;
        if (showHint && isLookingAtDoor && !isAnimating)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.alignment = TextAnchor.MiddleCenter;
            // Ключ уже в сумке — замок снимется на E, подсказка не должна
            // отправлять игрока искать то, что у него есть.
            string text = isLocked && !PlayerHasRequiredKey() && !string.IsNullOrEmpty(lockedHintMessage)
                ? lockedHintMessage
                : "Нажмите E";
            GUI.Label(new Rect(Screen.width / 2 - 160, Screen.height / 2 + 50, 320, 30), text, style);
        }
    }
    void OnDrawGizmosSelected()
    {
        if (Camera.main != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(Camera.main.transform.position, Camera.main.transform.forward * interactDistance);
        }
        if (physicalCollider != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(physicalCollider.bounds.center, physicalCollider.bounds.size);
        }
    }
}