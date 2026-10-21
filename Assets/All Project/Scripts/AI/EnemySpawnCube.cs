using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemySpawnCube : MonoBehaviour
{
    [Header("Спавн модели немца")]
    [Tooltip("Префаб немецкого солдата. Пусто — возьмём GermanSoldier.prefab из проекта. " +
             "Спавнится ИМЯ модель: она уже настроена (оружие в руках, анимации, ИИ) — " +
             "скрипт ничего не добавляет.")]
    public GameObject germanPrefab;

    [Tooltip("Сколько немцев выпустить.")]
    public int spawnCount = 3;

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
    /// Спавн моделей немца по разным точкам вокруг куба: только модель,
    /// уже настроенная в префабе. Никаких дополнительных объектов.
    /// </summary>
    private void SpawnGermans()
    {
        int count = Mathf.Max(1, spawnCount);
        float radius = Mathf.Max(0.5f, spawnRadius);

        for (int i = 0; i < count; i++)
        {
            Vector3 point = FindSpawnPoint(i, count, radius);
            Quaternion rot = Quaternion.LookRotation(FlatDirectionTo(point), Vector3.up);

            GameObject instance = Instantiate(germanPrefab, point, rot);
            instance.name = germanPrefab.name;
            _spawned.Add(instance);
        }

        Debug.Log($"[EnemySpawnCube] {name}: выпущено немцев: {_spawned.Count} " +
                  $"(модель «{germanPrefab.name}», точки разнесены по радиусу {radius:F1} м).", this);
    }

    /// <summary>
    /// Точка спавна: равномерно по кольцу + разброс, и рядом не было спавнов.
    /// Так немцы сразу стоят по местности, а не в одной точке.
    /// </summary>
    private Vector3 FindSpawnPoint(int index, int count, float radius)
    {
        float baseAngle = index / (float)Mathf.Max(1, count) * 360f + Random.Range(-18f, 18f);
        float ringRadius = radius * Random.Range(0.55f, 1f);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = baseAngle + attempt * 37f;
            float r = ringRadius * (1f - attempt * 0.08f);
            Vector3 candidate = transform.position +
                new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * r, 0f, Mathf.Cos(angle * Mathf.Deg2Rad) * r);

            Vector3 result = candidate;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                result = hit.position;

            bool crowded = false;
            foreach (GameObject go in _spawned)
            {
                if (go == null) continue;
                Vector3 a = go.transform.position;
                Vector3 b = result;
                a.y = 0f;
                b.y = 0f;
                if (Vector3.Distance(a, b) < spawnSpacing) { crowded = true; break; }
            }

            if (!crowded) return result;
        }

        Vector3 fallback = transform.position;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit fallbackHit, 4f, NavMesh.AllAreas))
            fallback = fallbackHit.position;
        return fallback;
    }

    private Vector3 FlatDirectionTo(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        return dir.normalized;
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

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.5f, spawnRadius));

        if (enemiesToEnable == null) return;
        Gizmos.color = Color.yellow;
        foreach (GameObject go in enemiesToEnable)
        {
            if (go != null)
                Gizmos.DrawLine(transform.position, go.transform.position);
        }
    }
}
