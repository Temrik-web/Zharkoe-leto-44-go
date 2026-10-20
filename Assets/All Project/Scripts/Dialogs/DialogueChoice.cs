using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Требование к предмету для варианта ответа.</summary>
[System.Serializable]
public class ItemRequirement
{
    [Tooltip("Id предмета из ItemDatabase (например bread).")]
    public string itemId = "";
    [Min(1)] public int count = 1;
}

[System.Serializable]
public class DialogueChoice
{
    [Header("Текст кнопки")]
    public string choiceText;

    [Header("Условие показа (может быть пустым)")]
    public DialogueCondition condition;

    [Header("Требования без ассетов (пусто = не проверять)")]
    [Tooltip("Id предмета из ItemDatabase (например key_cellar). Кнопка видна, только если предмет есть.")]
    public string requiredItemId;
    [Tooltip("Сколько штук предмета нужно.")]
    public int requiredItemCount = 1;
    [Tooltip("Несколько предметов сразу: нужны ВСЕ перечисленные. " +
             "Например: хлеб + вода. Пустой список = не проверять.")]
    public List<ItemRequirement> requiredItems = new List<ItemRequirement>();
    [Tooltip("Id квеста (например q_cart_key). Кнопка видна, только если статус совпал.")]
    public string requiredQuestId;
    [Tooltip("0 — любой взятый (активен или выполнен), 1 — активен, 2 — выполнен, 3 — провален.")]
    public int requiredQuestState = 1;

    [Header("Недоступный вид")]
    [Tooltip("Включено — если условия не выполнены, вариант показывается заблокированным " +
             "(стиль UI5, нажать нельзя). Выключено — вариант скрывается, а нижние кнопки " +
             "сдвигаются вверх, чтобы не было пробелов.")]
    public bool showWhenLocked = false;

    [Header("Переход")]
    public string nextNodeID;
    public bool endDialogue = false;

    [Header("Команды при выборе")]
    public List<DialogueCommand> onSelectCommands = new List<DialogueCommand>();

    [Header("Событие Unity")]
    public UnityEvent onSelected;
}