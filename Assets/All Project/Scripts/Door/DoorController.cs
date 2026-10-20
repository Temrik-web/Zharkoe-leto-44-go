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

    /// <summary>Дверь открылась (сценарии могут подписаться).</summary>
    public event System.Action Opened;

    /// <summary>Открыта ли дверь сейчас.</summary>
    public bool IsOpen => isOpen;

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
    }

    void Update()
    {
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
        if (!questReported && !string.IsNullOrEmpty(questToCompleteOnOpen))
        {
            questReported = true;
            QuestSystem.CompleteQuest(questToCompleteOnOpen);
        }
        StartCoroutine(MoveDoor(true));
        try { Opened?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e, this); }
    }
    public void Close()
    {
        if (isAnimating || !isOpen) return;
        StartCoroutine(MoveDoor(false));
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
    }
    private void SetTargetRotation()
    {
        targetRotation = isOpen ? closedRotation * Quaternion.AngleAxis(openAngle, Vector3.up) : closedRotation;
    }
    public bool TryUnlockWithKey()
    {
        if (!isLocked) return true;
        if (string.IsNullOrEmpty(requiredKeyId)) return false;
        InventorySystem inv = InventorySystem.Instance;
        if (inv == null) inv = FindObjectOfType<InventorySystem>();
        if (inv != null && inv.HasKey(requiredKeyId))
        {
            isLocked = false;
            return true;
        }
        return false;
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
            string text = isLocked && !string.IsNullOrEmpty(lockedHintMessage)
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