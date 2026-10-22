using System;
using UnityEngine;

/// <summary>
/// Условие «прошло ли время»: в GameState лежит Unix-время, когда действие
/// станет доступным. Используется как condition у варианта ответа — диалог
/// сам прячет или блокирует кнопку, без правок DialogueManager.
/// </summary>
[CreateAssetMenu(fileName = "TimedCondition", menuName = "Dialogue System/Conditions/Timed Condition")]
public class TimedCondition : DialogueCondition
{
    [Tooltip("Ключ в GameState, где лежит Unix-время готовности.")]
    public string flagKey = "stepan_key_ready_at";

    [Tooltip("true — условие выполняется, пока отсчёт НЕ истёк.")]
    public bool invert = false;

    public override bool Evaluate()
    {
        int readyAt = GameState.GetInt(flagKey, 0);
        bool ready = readyAt > 0 && readyAt <= NowUnix();
        return invert ? !ready : ready;
    }

    public override string Description
    {
        get
        {
            if (string.IsNullOrEmpty(flagKey)) return "Таймер не задан";
            int readyAt = GameState.GetInt(flagKey, 0);
            if (readyAt <= 0) return "Отсчёт не запущен";
            int left = readyAt - NowUnix();
            if (left > 0) return $"Осталось {left} с";
            return invert ? "Ожидание" : "Готово";
        }
    }

    /// <summary>Текущее Unix-время в секундах.</summary>
    public static int NowUnix() => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
