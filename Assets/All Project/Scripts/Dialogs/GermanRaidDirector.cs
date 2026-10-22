using System.Collections;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>Режиссёр налётов: слушает шину диалогов, ждёт паузу, спавнит немцев.</summary>
[DisallowMultipleComponent]
public class GermanRaidDirector : MonoBehaviour
{
    [Header("Что слушаем")]
    [Tooltip("Id события из диалогов. Совпадает с DialogueRaidHook.eventId и CustomEvent.stringParam.")]
    public string raidEventId = "raid_germans";

    [Header("Кого поднимаем")]
    [Tooltip("Префабы немцев (с EnemyAI/CharacterHealth). Пусто — только логика без спавна.")]
    public GameObject[] germanPrefabs = new GameObject[0];

    [Tooltip("Точки появления (пустые GameObject у околицы/леса). Если заданы — немец №i " +
             "появляется РОВНО в точке №i. Пусто — спавн рядом с игроком.")]
    public Transform[] spawnPoints = new Transform[0];

    [Tooltip("Насколько точка может «прилипнуть» к NavMesh, м. 0 — не двигать точку вообще. " +
             "Если рядом меша нет — немец появится ровно в точке, с предупреждением.")]
    [Min(0f)] public float spawnNavMeshSnap = 2.5f;
    [Tooltip("Маска областей NavMesh. По умолчанию — все, кроме «Not Walkable» (индекс 1).")]
    public int spawnNavMeshAreaMask = ~(1 << 1);
    [Tooltip("Дистанция между немцами, если точек меньше, чем немцев, м.")]
    [Min(1f)] public float spawnSpacing = 2.5f;
    [Tooltip("Сразу бросать заспавненных на игрока (налёт = атака). " +
             "Выкл — стоят на точках, пока игрок сам не попадётся на глаза.")]
    public bool aggroOnSpawn = true;
    [Tooltip("Поворачивать к игроку (выкл — поворот спавнпоинта).")]
    public bool facePlayerOnSpawn = false;

    [Tooltip("Сколько немцев поднять за раз.")]
    [Min(1)] public int germansPerRaid = 2;

    [Header("Тайминг")]
    [Tooltip("Пауза между событием из диалога и появлением (дать игроку дойти до укрытия).")]
    [Min(0f)] public float warningDelay = 6f;

    [Tooltip("Текст предупреждения (показывается через FloatingText над игроком, если он есть).")]
    public string warningText = "Тревога! Немцы у околицы!";

    [Header("Мир и квест")]
    [Tooltip("Квест, стартующий с налётом.")]
    public string questOnRaid = "q_raid";

    [Tooltip("Флаг GameState, взводимый на время налёта (ветвление диалогов).")]
    public string activeFlag = "raid_active";

    [Tooltip("Только один налёт за жизнь сцены (повторные события игнорируются).")]
    public bool oncePerScene = false;

    [Tooltip("Считать убитых: закрывать квест, когда все заспавненные мертвы. Выкл — квест висит до ручного закрытия.")]
    public bool completeQuestWhenCleared = true;

    bool raidFired = false;
    readonly System.Collections.Generic.List<GameObject> alive = new System.Collections.Generic.List<GameObject>();

    void OnEnable() => DialogueEventBus.OnEvent += OnDialogueEvent;
    void OnDisable() => DialogueEventBus.OnEvent -= OnDialogueEvent;

    void OnDialogueEvent(string eventId)
    {
        if (eventId != raidEventId) return;
        if (oncePerScene && raidFired) return;
        raidFired = true;
        StartCoroutine(RaidRoutine());
    }

    IEnumerator RaidRoutine()
    {
        GameState.SetFlag(activeFlag, true);
        if (!string.IsNullOrEmpty(questOnRaid))
            QuestSystem.StartQuest(questOnRaid);

        Warn(warningText);
        Debug.Log($"[Raid] Сигнал «{raidEventId}»: немцы будут через {warningDelay} c.", this);

        if (warningDelay > 0f)
            yield return new WaitForSeconds(warningDelay);

        SpawnWave(germansPerRaid);

        if (completeQuestWhenCleared && alive.Count > 0)
            yield return StartCoroutine(WaitCleared());

        GameState.SetFlag(activeFlag, false);
    }

    void Warn(string text)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            try { FloatingText.Show(text, player.transform.position + Vector3.up * 2f, Color.red); }
            catch { Debug.Log($"[Raid] {text}", this); }
        }
        Debug.Log($"[Raid] {text}", this);
    }

    void SpawnWave(int count)
    {
        if (germanPrefabs == null || germanPrefabs.Length == 0)
        {
            Debug.LogWarning("[Raid] Префабы немцев не заданы — спавна нет, но флаг и квест уже подняты. " +
                             "Перетащи префаб врага в GermanRaidDirector.germanPrefabs.", this);
            // Без тел ждать некого — сразу гасим флаг и закрываем квест как
            // «ложную тревогу», иначе квест висит вечно.
            GameState.SetFlag(activeFlag, false);
            if (!string.IsNullOrEmpty(questOnRaid))
                QuestSystem.CompleteQuest(questOnRaid);
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Vector3 playerPos = player != null ? player.transform.position : transform.position;
        Vector3 fallback = player != null
            ? playerPos + player.transform.forward * 18f
            : transform.position;

        int pointCount = spawnPoints != null ? spawnPoints.Length : 0;

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = germanPrefabs[i % germanPrefabs.Length];
            if (prefab == null) continue;

            Transform point = FindSpawnPoint(i, pointCount);

            Vector3 pos;
            Quaternion rot;

            if (point != null)
            {
                Vector3 desired = point.position;

                // Точек меньше, чем немцев: второй круг ставим рядом с точкой
                // (не в ту же самую), иначе двое слипаются.
                int cycle = i / Mathf.Max(1, pointCount);
                if (cycle > 0)
                {
                    float angle = (i * 137.5f) * Mathf.Deg2Rad;
                    desired += new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) *
                               Mathf.Max(1f, spawnSpacing);
                }

                if (!SpawnPlacement.TrySnap(desired, spawnNavMeshSnap,
                        spawnNavMeshAreaMask, out pos))
                    pos = desired;

                rot = facePlayerOnSpawn && player != null
                    ? SpawnPlacement.LookAt(pos, playerPos, point.forward)
                    : point.rotation;
            }
            else
            {
                Vector3 desired = fallback + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                if (!SpawnPlacement.TrySnap(desired, spawnNavMeshSnap,
                        spawnNavMeshAreaMask, out pos))
                    pos = desired;

                rot = facePlayerOnSpawn && player != null
                    ? SpawnPlacement.LookAt(pos, playerPos, Vector3.forward)
                    : Quaternion.identity;
            }

            GameObject g = Instantiate(prefab, pos, rot);
            EnemyAI ai = g.GetComponent<EnemyAI>();
            if (ai == null) ai = g.GetComponentInChildren<EnemyAI>(true);
            if (ai != null)
            {
                ai.SetPatrolRoute(null);
                ai.SetWander(false);
                if (aggroOnSpawn && player != null)
                    ai.ForceTarget(player.transform);
            }

            alive.Add(g);
            Debug.Log($"[Raid] Немец {i + 1}/{count}: {g.name} в {pos}.", g);
        }
    }

    /// <summary>Точка №i; если слот пуст — следующая непустая (по кругу).</summary>
    Transform FindSpawnPoint(int index, int pointCount)
    {
        if (pointCount <= 0 || spawnPoints == null) return null;

        for (int k = 0; k < pointCount; k++)
        {
            Transform t = spawnPoints[(index + k) % pointCount];
            if (t != null) return t;
        }

        Debug.LogWarning("[Raid] Все «Spawn Points» пустые — спавню рядом с игроком.", this);
        return null;
    }

    IEnumerator WaitCleared()
    {
        while (true)
        {
            alive.RemoveAll(g => g == null);
            if (alive.Count == 0) break;
            yield return new WaitForSeconds(1f);
        }
        Debug.Log("[Raid] Налёт отбит — все немцы мертвы.", this);
        if (!string.IsNullOrEmpty(questOnRaid))
            QuestSystem.CompleteQuest(questOnRaid);
    }

    /// <summary>Ручной запуск из инспектора (тест без диалогов).</summary>
    [ContextMenu("Тест: поднять налёт сейчас")]
    public void TestRaidNow() => OnDialogueEvent(raidEventId);
}
