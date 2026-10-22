using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ZharkoeLeto44.AI;

/// <summary>
/// Спавнер немцев по кнопке E.
///
/// Где появятся враги:
///  • задан Spawn Points — каждый немец встаёт РОВНО в свою точку (позиция и
///    поворот точки), лишние — рядом с точкой;
///  • точек нет — по кольцу вокруг куба (старое поведение).
///
/// Точка должна лежать на NavMesh. Если она висит над полом/на столе — её
/// «притянет» к NavMesh в радиусе Spawn NavMesh Snap (по умолчанию 2.5 м).
/// </summary>
public class EnemySpawnCube : MonoBehaviour
{
    [Header("Спавн модели немца")]
    [Tooltip("Префаб немецкого солдата. Пусто — возьмём GermanSoldier.prefab из проекта. " +
             "Спавнится ИМЯ модель: она уже настроена (оружие в руках, анимации, ИИ) — " +
             "скрипт ничего не добавляет.")]
    public GameObject germanPrefab;

    [Tooltip("Сколько немцев выпустить.")]
    public int spawnCount = 3;

    [Header("Точки появления (точное размещение)")]
    [Tooltip("Если заданы — немец №i появляется РОВНО в точке №i: позиция и поворот берутся " +
             "у точки. 0-й — в 0-ю, 1-й — в 1-ю и т.д. Если немцев больше, чем точек, " +
             "лишние встают рядом с точкой (не в той же самой).")]
    public Transform[] spawnPoints = new Transform[0];

    [Tooltip("Насколько точка может «прилипнуть» к NavMesh, м. 0 — не двигать точку вообще. " +
             "2.5 — нормальный запас, если точка стоит на высоте глаз или на столе. " +
             "Если рядом меша нет — немец появится ровно в точке, с предупреждением.")]
    [Min(0f)] public float spawnNavMeshSnap = SpawnPlacement.DefaultSnap;

    [Tooltip("Маска областей NavMesh. По умолчанию — все, кроме «Not Walkable» (индекс 1).")]
    public int spawnNavMeshAreaMask = SpawnPlacement.DefaultAreaMask;

    [Tooltip("Держать позицию: солдаты стоят на точках и осматриваются, а не расходятся " +
             "по местности (в префабе включено блуждание радиусом 10 м). " +
             "Выкл — обычное поведение префаба.")]
    public bool holdPositionAtSpawn = true;

    [Header("Если точки не заданы — кольцо вокруг куба")]
    [Tooltip("Радиус расстановки вокруг куба, м. Немцы встают по разным точкам, " +
             "а не в одной куче (иначе они потом и в бою слипаются в одну точку).")]
    public float spawnRadius = 7f;

    [Tooltip("Минимальная дистанция между спавн-точками, м.")]
    public float spawnSpacing = 4f;

    [Header("Немцы, уже стоящие на сцене (старый способ)")]
    [Tooltip("Выключенные объекты на сцене: если German Prefab не задан, " +
             "включаются эти. Пусто — найдутся сами по имени.")]
    public GameObject[] enemiesToEnable = new GameObject[3];

    [Header("Взаимодействие")]
    public float interactDistance = 3.5f;
    public KeyCode interactKey = KeyCode.E;
    public string hintMessage = "Нажмите E — выпустить немцев (тест)";
    public string doneMessage = "Немцы выпущены!";
    [Tooltip("Сработать только один раз. Выключи, если хочешь включать/выключать повторно.")]
    public bool spawnOnce = true;
    public float doneMessageTime = 3f;

    private GameObject cachedPlayer;
    private bool playerInRange;
    private bool hasSpawned;
    private float doneUntil;
    private float nextPlayerWarnTime;
    private float nextPlayerSearchTime;
    private bool spawnPointsValidated;
    private GUIStyle _hintStyle;
    private readonly List<GameObject> _spawned = new();

    private const string GermanPrefabPath =
        "Assets/All Project/GameData/Prefabs/GermanSoldier.prefab";

    private void Start()
    {
        if (germanPrefab == null) germanPrefab = LoadGermanPrefab();

        if (!HasAssignedEnemies()) AutoFindEnemies();

        if (germanPrefab == null && !HasAssignedEnemies())
            Debug.LogWarning($"[EnemySpawnCube] {name}: не найден префаб немца " +
                             $"({GermanPrefabPath}) и не заданы объекты на сцене.", this);

        ValidateSpawnPoints();
    }

    private static GameObject LoadGermanPrefab()
    {
#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GermanPrefabPath);
        if (prefab != null) return prefab;
#endif
        return Resources.Load<GameObject>("GermanSoldier");
    }

    private void Update()
    {
        if (cachedPlayer == null)
        {
            // Поиск игрока — полный скан сцены, поэтому не каждый кадр:
            // не нашли — ждём полсекунды и пробуем снова.
            if (Time.time >= nextPlayerSearchTime)
            {
                nextPlayerSearchTime = Time.time + 0.5f;
                try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
                catch { cachedPlayer = null; }
            }

            if (cachedPlayer == null)
            {
                if (Time.time >= nextPlayerWarnTime)
                {
                    nextPlayerWarnTime = Time.time + 5f;
                    Debug.LogWarning($"[EnemySpawnCube] {name}: игрок с тегом «Player» не найден — E не сработает.", this);
                }
                playerInRange = false;
                return;
            }
        }

        playerInRange = Vector3.Distance(transform.position, cachedPlayer.transform.position) <= interactDistance;
        if (!playerInRange) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive) return;

        if (spawnOnce && hasSpawned) return;
        if (Input.GetKeyDown(interactKey)) EnableEnemies();
    }

    public void EnableEnemies()
    {
        // Один раз: иначе внешний вызов (сценарий/UI) даст двойной спавн отряда.
        if (spawnOnce && hasSpawned) return;

        if (germanPrefab != null) SpawnGermans();
        else EnableSceneEnemies();

        hasSpawned = true;
        doneUntil = Time.time + doneMessageTime;
    }

    /// <summary>
    /// Спавн моделей немца: по точкам, а если точек нет — по кольцу вокруг куба.
    /// Только модель, уже настроенная в префабе. Никаких дополнительных объектов.
    /// </summary>
    private void SpawnGermans()
    {
        int count = Mathf.Max(1, spawnCount);
        float radius = Mathf.Max(0.5f, spawnRadius);
        float spacing = Mathf.Max(0.8f, spawnSpacing);
        bool usePoints = HasSpawnPoints();

        List<Vector3> used = new List<Vector3>(count + _spawned.Count);
        foreach (GameObject previous in _spawned)
            if (previous != null) used.Add(previous.transform.position);

        int offMesh = 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 point;
            Quaternion rotation;

            if (usePoints)
            {
                Transform source = GetSpawnPoint(i, out bool exact);
                Vector3 desired = source.position;

                if (!exact)
                {
                    // Немцев больше, чем точек: ставим рядом с точкой, чтобы
                    // они не слиплись в одной (иначе NavMeshAgent их расталкивает
                    // и один уезжает неизвестно куда).
                    float angle = (i * 137.5f) * Mathf.Deg2Rad;
                    desired += new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * spacing;
                }

                if (!SpawnPlacement.TrySnap(desired, spawnNavMeshSnap,
                        spawnNavMeshAreaMask, out point))
                {
                    offMesh++;
                    point = desired;
                }

                rotation = source.rotation;
            }
            else
            {
                point = FindRingPoint(i, count, radius, spacing, used, out rotation);
            }

            used.Add(point);

            GameObject instance = Instantiate(germanPrefab, point, rotation);
            instance.name = germanPrefab.name;
            _spawned.Add(instance);
            ApplyHoldPosition(instance);
        }

        if (offMesh > 0)
            Debug.LogWarning($"[EnemySpawnCube] {name}: {offMesh} из {count} точек вне NavMesh — " +
                             "немцы сели ровно туда, куда указано, но ходить не смогут. " +
                             "Запеки NavMesh под точками.", this);

        Debug.Log($"[EnemySpawnCube] {name}: выпущено немцев: {_spawned.Count} " +
                  (usePoints
                      ? $"(точки появления, снап NavMesh {spawnNavMeshSnap:F1} м)."
                      : $"(модель «{germanPrefab.name}», точки разнесены по радиусу {radius:F1} м)."), this);
    }

    /// <summary>
    /// Точка из списка Spawn Points. exact = false, если немцев больше, чем точек:
    /// тогда точку берём по кругу (первую непустую), а врага ставим рядом с ней.
    /// </summary>
    private Transform GetSpawnPoint(int index, out bool exact)
    {
        exact = index >= 0 && index < spawnPoints.Length && spawnPoints[index] != null;
        if (exact) return spawnPoints[index];

        for (int k = 0; k < spawnPoints.Length; k++)
        {
            Transform candidate = spawnPoints[(index + k) % spawnPoints.Length];
            if (candidate != null) return candidate;
        }

        // Вызывается только когда HasSpawnPoints() == true, но подстрахуемся.
        return transform;
    }

    /// <summary>
    /// Точка по кольцу вокруг куба (когда Spawn Points пуст). Всегда возвращает
    /// РЕАЛЬНУЮ точку кольца — не центр куба, иначе отряд появляется в одной куче.
    /// </summary>
    private Vector3 FindRingPoint(int index, int count, float radius, float spacing,
        List<Vector3> used, out Quaternion rotation)
    {
        float baseAngle = index / (float)Mathf.Max(1, count) * 360f + Random.Range(-18f, 18f);
        float ringRadius = radius * Random.Range(0.55f, 1f);

        Vector3 best = transform.position;
        float bestScore = -1f;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = baseAngle + attempt * 37f;
            float r = ringRadius * (1f - attempt * 0.06f);
            Vector3 candidate = transform.position +
                new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * r, 0f,
                            Mathf.Cos(angle * Mathf.Deg2Rad) * r);

            Vector3 point = candidate;
            // Нет NavMesh рядом — оставляем точку кольца как есть (мотор сам
            // сядет на землю), но НЕ подменяем её кубом.
            SpawnPlacement.TrySnap(candidate, spawnNavMeshSnap, spawnNavMeshAreaMask, out point);

            float score = MinDistance(point, used);
            if (score < 0f) score = float.MaxValue;   // ещё никого нет
            if (score >= spacing)
            {
                rotation = SpawnPlacement.LookAt(transform.position, point, transform.forward);
                return point;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = point;
            }
        }

        rotation = SpawnPlacement.LookAt(transform.position, best, transform.forward);
        return best;
    }

    private static float MinDistance(Vector3 point, List<Vector3> used)
    {
        if (used == null || used.Count == 0) return -1f;

        float best = float.MaxValue;
        for (int i = 0; i < used.Count; i++)
            best = Mathf.Min(best, SpawnPlacement.FlatDistance(point, used[i]));
        return best;
    }

    /// <summary>Солдат стоит на точке и осматривается, пока не заметит игрока.</summary>
    private void ApplyHoldPosition(GameObject instance)
    {
        if (!holdPositionAtSpawn || instance == null) return;

        EnemyAI ai = instance.GetComponent<EnemyAI>();
        if (ai == null) ai = instance.GetComponentInChildren<EnemyAI>(true);
        if (ai == null) return;

        ai.SetPatrolRoute(null);
        ai.SetWander(false);
    }

    /// <summary>Один раз на старте: предупредить о точках не на NavMesh и пустых слотах.</summary>
    private void ValidateSpawnPoints()
    {
        if (spawnPointsValidated) return;
        spawnPointsValidated = true;

        if (germanPrefab == null || spawnPoints == null || spawnPoints.Length == 0) return;

        int mask = spawnNavMeshAreaMask != 0
            ? spawnNavMeshAreaMask
            : SpawnPlacement.DefaultAreaMask;
        bool hasNavMesh = NavMesh.SamplePosition(transform.position, out _, 100f, mask);

        int valid = 0;
        int nulls = 0;

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Transform point = spawnPoints[i];
            if (point == null) { nulls++; continue; }
            valid++;

            if (!hasNavMesh) continue;

            if (!SpawnPlacement.TrySnap(point.position, spawnNavMeshSnap,
                    spawnNavMeshAreaMask, out Vector3 snapped))
            {
                Debug.LogWarning($"[EnemySpawnCube] {name}: точка «{point.name}» ({point.position}) " +
                                 $"не на NavMesh и рядом (в {Mathf.Max(0.1f, spawnNavMeshSnap):F1} м) " +
                                 "его нет — немец появится ровно в точке, но ходить не сможет. " +
                                 "Запеки NavMesh под точкой.", point);
                continue;
            }

            float offset = SpawnPlacement.FlatDistance(snapped, point.position);
            if (offset > 0.5f)
                Debug.LogWarning($"[EnemySpawnCube] {name}: точка «{point.name}» стоит в {offset:F1} м " +
                                 "от NavMesh — немец появится не ровно в ней. Подними/опусти точку " +
                                 "на пол или уменьши Spawn NavMesh Snap.", point);
        }

        if (nulls > 0)
            Debug.LogWarning($"[EnemySpawnCube] {name}: в Spawn Points {nulls} пустых слотов — " +
                             "они будут пропущены.", this);

        if (valid < Mathf.Max(1, spawnCount))
            Debug.LogWarning($"[EnemySpawnCube] {name}: точек {valid}, а немцев " +
                             $"{Mathf.Max(1, spawnCount)} — лишние встанут рядом с точками " +
                             $"(радиус {Mathf.Max(0.8f, spawnSpacing):F1} м).", this);
    }

    private bool HasSpawnPoints()
    {
        if (spawnPoints == null) return false;
        for (int i = 0; i < spawnPoints.Length; i++)
            if (spawnPoints[i] != null) return true;
        return false;
    }

    private void EnableSceneEnemies()
    {
        foreach (GameObject go in enemiesToEnable)
        {
            if (go == null || go.activeSelf) continue;
            go.SetActive(true);
        }
    }

    private bool HasAssignedEnemies()
    {
        if (enemiesToEnable == null) return false;
        foreach (GameObject go in enemiesToEnable)
            if (go != null) return true;
        return false;
    }

    private void AutoFindEnemies()
    {
        Transform[] all = FindObjectsOfType<Transform>(true);
        List<GameObject> found = new List<GameObject>();

        string[] wanted =
        {
            "== Enemy Template (клонируй меня) ==",
            "== Enemy Template (клонируй меня) == (1)",
            "== Enemy Template (клонируй меня) == (2)",
        };

        foreach (string w in wanted)
        {
            foreach (Transform t in all)
            {
                if (t.name == w)
                {
                    found.Add(t.gameObject);
                    break;
                }
            }
            if (found.Count >= 3) break;
        }

        if (found.Count < 3)
        {
            foreach (Transform t in all)
            {
                if (t.name.Contains("Enemy Template") && !found.Contains(t.gameObject))
                {
                    found.Add(t.gameObject);
                    if (found.Count >= 3) break;
                }
            }
        }

        enemiesToEnable = found.ToArray();
    }

    private void OnGUI()
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;

        GUIStyle style = _hintStyle;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.alignment = TextAnchor.MiddleCenter;
            _hintStyle = style;
        }

        if (playerInRange && (!spawnOnce || !hasSpawned))
        {
            GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 50, 500, 30), hintMessage, style);
        }
        else if (hasSpawned && Time.time < doneUntil)
        {
            GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 50, 500, 30), doneMessage, style);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, interactDistance);

        if (HasSpawnPoints())
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                Transform point = spawnPoints[i];
                if (point == null) continue;

                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
                Gizmos.DrawWireSphere(point.position, 0.4f);
                Gizmos.DrawLine(transform.position, point.position);
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.35f);
                Gizmos.DrawRay(point.position, point.forward * 1.5f);
            }
        }
        else
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.5f, spawnRadius));
        }

        if (enemiesToEnable == null) return;
        Gizmos.color = Color.yellow;
        foreach (GameObject go in enemiesToEnable)
        {
            if (go != null)
                Gizmos.DrawLine(transform.position, go.transform.position);
        }
    }
}
