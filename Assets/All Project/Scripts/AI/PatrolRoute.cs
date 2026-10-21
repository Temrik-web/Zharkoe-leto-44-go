using UnityEngine;

namespace ZharkoeLeto44.AI
{
public sealed class PatrolRoute : MonoBehaviour
{
    [SerializeField] private Transform[] points;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool randomOrder;

    /// <summary>
    /// Сколько точек маршрута РЕАЛЬНО задано (пустые слоты в массиве не считаются).
    /// Если 0 — маршрута нет, ИИ должен блуждать, а не стоять на месте.
    /// </summary>
    public int Count => ValidCount;

    public bool Loop => loop;

    /// <summary>Размер массива точек (включая пустые слоты) — только для арифметики индексов.</summary>
    private int SlotCount => points != null ? points.Length : 0;

    private int ValidCount
    {
        get
        {
            if (points == null) return 0;
            int n = 0;
            for (int i = 0; i < points.Length; i++)
                if (points[i] != null) n++;
            return n;
        }
    }

    /// <summary>
    /// Следующая точка маршрута. Пустые слоты пропускаются: иначе враг один раз
    /// упрётся в незаполненную точку и больше не сдвинется.
    /// При randomOrder точки идут случайно, но только в зацикленном маршруте:
    /// без цикла маршрут — прямая линия, и randomOrder его бы никогда не заканчивал.
    /// </summary>
    public int NextIndex(int current)
    {
        int slots = SlotCount;
        if (slots <= 0) return 0;
        if (ValidCount <= 1) return FirstValidIndex(0);

        int candidate;
        if (randomOrder && loop)
        {
            int next = Random.Range(0, slots - 1);
            candidate = next >= current ? next + 1 : next;
        }
        else
        {
            candidate = current + 1 < slots ? current + 1 : (loop ? 0 : slots - 1);
        }

        return SkipToValid(candidate, current);
    }

    public Transform GetPoint(int index)
    {
        int slots = SlotCount;
        if (slots == 0) return null;

        index = Mathf.Clamp(index, 0, slots - 1);
        for (int i = 0; i < slots; i++)
        {
            int slot = (index + i) % slots;
            if (points[slot] != null) return points[slot];
        }

        return null;
    }

    /// <summary>Индекс первой занятой точки начиная с <paramref name="from"/> (циклически).</summary>
    private int FirstValidIndex(int from)
    {
        int slots = SlotCount;
        if (slots == 0) return 0;

        for (int i = 0; i < slots; i++)
        {
            int slot = ((from + i) % slots + slots) % slots;
            if (points[slot] != null) return slot;
        }

        return 0;
    }

    /// <summary>Индекс ближайшей ЗАНЯТОЙ точки от <paramref name="candidate"/> (циклически).</summary>
    private int SkipToValid(int candidate, int fallback)
    {
        int slots = SlotCount;
        if (slots == 0) return 0;

        int start = ((candidate % slots) + slots) % slots;
        for (int i = 0; i < slots; i++)
        {
            int slot = (start + i) % slots;
            if (points[slot] != null) return slot;
        }

        // Все слоты пустые — отдаём прежний индекс, чтобы не выйти за границы.
        return fallback >= 0 && fallback < slots ? fallback : 0;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (points == null || points.Length == 0) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null) continue;

            Gizmos.DrawWireSphere(points[i].position, 0.3f);

            int next = (i + 1) % points.Length;
            if (!loop && next == 0) continue;
            if (points[next] != null)
                Gizmos.DrawLine(points[i].position, points[next].position);
        }
    }
#endif
}
}
