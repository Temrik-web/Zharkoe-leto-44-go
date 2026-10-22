using UnityEngine;

/// <summary>
/// Отсчёт готовности Степана: игрок подобрал ключ — Степан «занят» и готов
/// принять его через stepanKeyCooldownSeconds секунд.
///
/// Время готовности пишется в GameState (PlayerPrefs) — переживает перезапуск.
/// Условие на кнопке диалога — TimedCondition с тем же ключом.
/// </summary>
[DisallowMultipleComponent]
public class StepanKeyCooldown : MonoBehaviour
{
    [Tooltip("Сколько секунд Степан не готов принимать ключ после его подбора.")]
    [SerializeField] float stepanKeyCooldownSeconds = 60f;

    [Tooltip("Id предмета, подбор которого запускает отсчёт.")]
    [SerializeField] string triggerItemId = "key_cellar";

    [Tooltip("Ключ в GameState: Unix-время, когда Степан готов.")]
    [SerializeField] string readyAtKey = "stepan_key_ready_at";

    private InventorySystem cachedInventory;

    private void OnEnable()
    {
        HookInventory();
    }

    private void OnDisable()
    {
        if (cachedInventory != null) cachedInventory.OnItemPickedUp -= OnItemPickedUp;
    }

    private void Start()
    {
        HookInventory();
    }

    private void HookInventory()
    {
        if (cachedInventory == null)
        {
            cachedInventory = InventorySystem.Instance;
            if (cachedInventory == null) cachedInventory = FindObjectOfType<InventorySystem>();
        }
        if (cachedInventory == null) return;

        cachedInventory.OnItemPickedUp -= OnItemPickedUp;
        cachedInventory.OnItemPickedUp += OnItemPickedUp;
    }

    private void OnItemPickedUp(ItemData item, int amount)
    {
        if (item == null || string.IsNullOrEmpty(triggerItemId)) return;
        if (item.Id != triggerItemId) return;

        int seconds = Mathf.Max(0, Mathf.RoundToInt(stepanKeyCooldownSeconds));
        int readyAt = TimedCondition.NowUnix() + seconds;
        GameState.SetInt(readyAtKey, readyAt);

        Debug.Log($"[StepanCooldown] {name}: ключ подобран — Степан будет готов через {seconds} с.", this);
    }

    /// <summary>Сбросить отсчёт (тест: сразу принять ключ).</summary>
    [ContextMenu("Сбросить отсчёт готовности")]
    public void ResetCooldown()
    {
        GameState.SetInt(readyAtKey, TimedCondition.NowUnix() - 1);
        Debug.Log($"[StepanCooldown] {name}: отсчёт сброшен — Степан готов.", this);
    }
}
