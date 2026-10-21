using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Мастер сцены «Немец за ППШ»: собирает объект сценария, точки появления засады,
/// префаб немца из выбранной модели и подбираемый ППШ в подвале.
/// </summary>
public static class PpshGermanEncounterWizard
{
    const string MenuRoot = "Tools/Диалоги/Немец за ППШ/";
    const string DialoguePath = "Assets/All Project/Resources/PpshGermanEncounter/PpshGermanDialogue.asset";
    const string GermanPrefabPath = "Assets/All Project/GameData/Prefabs/GermanSoldier.prefab";
    const string FallbackEnemyPrefabPath = "Assets/All Project/GameData/Prefabs/Enemy.prefab";
    const string PpshItemPath = "Assets/All Project/GameData/Items/Item_Ppsh41.asset";
    const string RootName = "Немец за ППШ (сценарий)";

    // ------------------------------------------------------------------
    // 0. Всё сразу
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Собрать всё (одной кнопкой)", false, -1)]
    public static void SetupEverything()
    {
        SetupScene();
        MakePpshPickable();
        PlaceFoodNearPpsh();
        Validate();
    }

    // ------------------------------------------------------------------
    // 1. Объект сценария
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Настроить сцену", false, 0)]
    public static void SetupScene()
    {
        PpshGermanEncounter existing = Object.FindObjectOfType<PpshGermanEncounter>(true);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[PpshGerman] Объект сценария уже есть в сцене — выделен он.", existing);
            return;
        }

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Ppsh German encounter");

        PpshGermanEncounter encounter = root.AddComponent<PpshGermanEncounter>();
        DialogueData dialogue = AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
        if (dialogue == null)
            Debug.LogError($"[PpshGerman] Диалог не найден: {DialoguePath}", root);
        else
        {
            var so = new SerializedObject(encounter);
            so.FindProperty("dialogue").objectReferenceValue = dialogue;
            so.ApplyModifiedProperties();
        }

        GameObject germanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GermanPrefabPath);
        if (germanPrefab == null) germanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackEnemyPrefabPath);
        if (germanPrefab == null)
            Debug.LogWarning("[PpshGerman] Префаб немца не найден. Собери его: " +
                             "«Собрать префаб немца из выбранного объекта» (выдели модель в сцене).", root);

        DoorController door = FindDoorNearPpsh();

        var so2 = new SerializedObject(encounter);
        so2.FindProperty("germanPrefab").objectReferenceValue = germanPrefab;
        so2.FindProperty("raiderPrefabs").arraySize = germanPrefab != null ? 1 : 0;
        if (germanPrefab != null)
        {
            so2.FindProperty("raiderPrefabs").GetArrayElementAtIndex(0).objectReferenceValue = germanPrefab;
        }
        so2.FindProperty("door").objectReferenceValue = door;
        so2.ApplyModifiedProperties();

        // Сторона двери, где снаружи — та, где игрока нет.
        Vector3 basePos = door != null ? door.transform.position : root.transform.position;
        Vector3 side = door != null ? -door.transform.forward : Vector3.forward;
        GameObject player = FindPlayer();
        if (player != null && door != null)
        {
            Vector3 toPlayer = player.transform.position - basePos;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f) side = -toPlayer.normalized;
        }
        side.Normalize();

        // Объект сценария ставим к двери: точки — его дети, в локальных
        // координатах. Тогда перетаскивание объекта не уносит их за собой.
        root.transform.position = basePos;

        // Откуда немец выходит на улицу.
        Transform approach = CreateChild(root.transform, "Откуда подходит немец", side * 9f);
        // Где он встанет и будет стучать.
        Transform stand = CreateChild(root.transform, "Немец у двери", side * 1.4f);
        // Точки засады на улице.
        Transform[] points = new Transform[6];
        Vector3 right = door != null ? door.transform.right : Vector3.right;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 local = right * (i - 2.5f) * 2.2f + side * 12f;
            points[i] = CreateChild(root.transform, $"Точка засады {i + 1}", local);
        }

        var so3 = new SerializedObject(encounter);
        so3.FindProperty("germanStandPoint").objectReferenceValue = stand;
        so3.FindProperty("germanApproachPoint").objectReferenceValue = approach;
        so3.FindProperty("spawnPoints").arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
            so3.FindProperty("spawnPoints").GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        so3.ApplyModifiedProperties();

        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        Debug.Log("[PpshGerman] Сценарий создан. Осталось руками:\n" +
                  "  • «Немец у двери» — точка, где он встанет и будет стучать;\n" +
                  "  • «Откуда подходит немец» — точка на улице, откуда он выйдет;\n" +
                  "  • расставить «Точки засады» по улице (там появятся шестеро);\n" +
                  "  • перетащить модель немца в German Prefab (или собрать префаб мастером);\n" +
                  "  • закинуть звуки: стук, свист, шаги немца.\n" +
                  "  • рядом с ППШ положить хлеб и флягу — «Положить хлеб и флягу рядом с ППШ»;\n" +
                  "  • сделать ППШ подбираемым — «Сделать ППШ подбираемым в подвале».", root);
    }

    /// <summary>Дочерний объект сцены. Позиция — локальная (относительно родителя).</summary>
    static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    /// <summary>
    /// Дверь, в которую стучит немец: та, рядом с которой игрок поднял ППШ.
    /// Если ППШ не нашли — дверь подвала, иначе первая попавшаяся.
    /// </summary>
    static DoorController FindDoorNearPpsh()
    {
        DoorController[] doors = Object.FindObjectsOfType<DoorController>(true);
        if (doors.Length == 0) return null;

        GameObject ppsh = FindPpshObject();
        if (ppsh != null)
        {
            DoorController best = null;
            float bestDist = float.MaxValue;
            foreach (DoorController d in doors)
            {
                if (d == null) continue;
                float dist = Vector3.Distance(d.transform.position, ppsh.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = d;
                }
            }
            if (best != null) return best;
        }

        foreach (DoorController d in doors)
        {
            if (d == null) continue;
            if (d.questToCompleteOnOpen == "q_cellar_door") return d;
            if (d.autoUnlockQuestId == "q_cellar_door") return d;
            if (d.requiredKeyId == "cellar") return d;
        }
        return doors[0];
    }

    static GameObject FindPlayer()
    {
        try
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) return tagged;
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------------------
    // 2. Префаб немца из выбранной модели
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Собрать префаб немца из выбранного объекта", false, 1)]
    public static void BuildGermanPrefab()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Префаб немца",
                "Выдели модель солдата в сцене (например «superman») и запусти пункт снова.", "Ок");
            return;
        }

        // Ставим на модель весь боевой набор скриптов.
        EnemySetupWizard.SetupSelectedAsEnemy();

        string folder = System.IO.Path.GetDirectoryName(GermanPrefabPath);
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/All Project"))
                AssetDatabase.CreateFolder("Assets", "All Project");
            if (!AssetDatabase.IsValidFolder("Assets/All Project/GameData"))
                AssetDatabase.CreateFolder("Assets/All Project", "GameData");
            if (!AssetDatabase.IsValidFolder("Assets/All Project/GameData/Prefabs"))
                AssetDatabase.CreateFolder("Assets/All Project/GameData", "Prefabs");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(selected, GermanPrefabPath);
        AssetDatabase.SaveAssets();

        if (prefab != null)
        {
            Debug.Log($"[PpshGerman] Префаб немца собран: {GermanPrefabPath}. " +
                      "Теперь запусти «Настроить сцену» — префаб подставится сам.", prefab);
        }
    }

    // ------------------------------------------------------------------
    // 3. ППШ в подвале должен подбираться
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Сделать ППШ подбираемым в подвале", false, 2)]
    public static void MakePpshPickable()
    {
        GameObject ppsh = FindPpshObject();
        if (ppsh == null)
        {
            EditorUtility.DisplayDialog("ППШ в подвале",
                "Объект «forPPSH-41» не найден в сцене.\n" +
                "Выдели модель ППШ и запусти пункт снова.", "Ок");
            return;
        }

        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(PpshItemPath);
        if (item == null)
        {
            EditorUtility.DisplayDialog("ППШ в подвале",
                $"Ассет предмета не найден: {PpshItemPath}", "Ок");
            return;
        }

        if (ppsh.GetComponentInParent<Pickup>() == null)
        {
            Undo.RecordObject(ppsh, "Add pickup");
            EnsureCollider(ppsh);
            Pickup pickup = ppsh.AddComponent<Pickup>();
            pickup.item = item;
            pickup.amount = 1;
            pickup.promptText = "E — ППШ-41";
            pickup.spin = false;
            pickup.bob = false;
            pickup.createGlowLight = false;
            EditorUtility.SetDirty(ppsh);
        }
        else
        {
            Pickup pickup = ppsh.GetComponentInParent<Pickup>();
            Undo.RecordObject(pickup, "Set pickup item");
            pickup.item = item;
            pickup.promptText = "E — ППШ-41";
            EditorUtility.SetDirty(pickup);
        }

        Debug.Log($"[PpshGerman] ППШ в подвале подбирается: {ppsh.name}. " +
                  "Теперь после подбора начнётся сценарий (немец стучит в дверь).", ppsh);
    }

    /// <summary>Коллайдер по габаритам модели, если своего нет — иначе подобрать нечем.</summary>
    static void EnsureCollider(GameObject go)    {
        if (go.GetComponentInChildren<Collider>(true) != null) return;

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            go.AddComponent<BoxCollider>();
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

        BoxCollider box = go.AddComponent<BoxCollider>();
        Vector3 size = bounds.size;
        Vector3 center = bounds.center - go.transform.position;
        box.size = size;
        box.center = new Vector3(center.x, center.y, center.z);
    }

    static GameObject FindPpshObject()
    {
        // Настоящий подбираемый ППШ — объект с Pickup и предметом ppsh41.
        foreach (Pickup pickup in Object.FindObjectsOfType<Pickup>(true))
        {
            if (pickup != null && pickup.item != null && pickup.item.Id == "ppsh41")
                return pickup.gameObject;
        }

        GameObject[] all = Object.FindObjectsOfType<GameObject>(true);

        // Модель у двери подвала (не пустышка «forPPSH-41» в пустоте).
        foreach (GameObject go in all)
        {
            if (go != null && go.name == "Ppsh-41(GR)") return go;
        }
        foreach (GameObject go in all)
        {
            if (go != null && go.name == "forPPSH-41") return go;
        }
        foreach (GameObject go in all)
        {
            if (go == null) continue;
            if (go.name.IndexOf("PPSH", System.StringComparison.OrdinalIgnoreCase) >= 0) return go;
            if (go.name.IndexOf("ППШ", System.StringComparison.OrdinalIgnoreCase) >= 0) return go;
        }
        return null;
    }

    /// <summary>Дверь подвала: DoorPivotDEDA, иначе по ключу «cellar» или квесту.</summary>
    static DoorController FindCellarDoor()
    {
        foreach (DoorController d in Object.FindObjectsOfType<DoorController>(true))
        {
            if (d == null) continue;
            if (d.name.IndexOf("DEDA", System.StringComparison.OrdinalIgnoreCase) >= 0) return d;
            if (!string.IsNullOrEmpty(d.requiredKeyId) && d.requiredKeyId == "cellar") return d;
            if (!string.IsNullOrEmpty(d.autoUnlockQuestId) && d.autoUnlockQuestId == "q_cellar_door") return d;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // 4. Хлеб и вода: без них немец не получит еду
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Положить хлеб и флягу рядом с ППШ", false, 3)]
    public static void PlaceFoodNearPpsh()
    {
        GameObject ppsh = FindPpshObject();
        if (ppsh == null)
        {
            EditorUtility.DisplayDialog("Хлеб и фляга",
                "ППШ в сцене не найден.\nВыдели модель и запусти пункт снова.", "Ок");
            return;
        }

        ItemData bread = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/All Project/GameData/Items/Item_Bread.asset");
        ItemData water = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/All Project/GameData/Items/Item_WaterFlask.asset");

        int placed = 0;
        if (bread != null) placed += PlaceOrMovePickup(ppsh.transform, "Хлеб (в подвале)", bread, new Vector3(0.4f, 0.05f, 0.3f));
        if (water != null) placed += PlaceOrMovePickup(ppsh.transform, "Фляга (в подвале)", water, new Vector3(-0.4f, 0.05f, 0.3f));
        if (placed == 0)
            Debug.LogWarning("[PpshGerman] Нечего ставить: ассеты Item_Bread / Item_WaterFlask не найдены.", ppsh);
        else
            Debug.Log($"[PpshGerman] Хлеб и фляга теперь рядом с ППШ «{ppsh.name}».", ppsh);
    }

    static int PlaceOrMovePickup(Transform ppsh, string name, ItemData item, Vector3 offset)
    {
        Vector3 world = ppsh.position + offset;

        // Если такой предмет уже есть в сцене (пусть и в пустоте) — переносим его.
        foreach (Pickup p in Object.FindObjectsOfType<Pickup>(true))
        {
            if (p == null || p.item != item) continue;

            Undo.SetTransformParent(p.transform, null, "Move pickup");
            Undo.RecordObject(p.transform, "Move pickup");
            p.transform.position = world;
            p.transform.localScale = Vector3.one;
            p.gameObject.SetActive(true);
            EditorUtility.SetDirty(p);
            return 1;
        }

        return PlacePickup(name, item, world);
    }

    static int PlacePickup(string name, ItemData item, Vector3 world)
    {
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.position = world;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.45f, 0.45f, 0.45f);

        Pickup pickup = go.AddComponent<Pickup>();
        pickup.item = item;
        pickup.amount = 1;
        pickup.promptText = $"E — {item.itemName}";
        pickup.spin = false;
        pickup.bob = true;
        pickup.bobAmplitude = 0.05f;
        pickup.createGlowLight = false;
        pickup.highlightRenderers = new Renderer[0];

        return 1;
    }

    // ------------------------------------------------------------------
    // Починка точек (если объект сценария двигали — точки уехали)
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Починить точки (прибить к двери)", false, 5)]
    public static void FixPoints()
    {
        PpshGermanEncounter encounter = Object.FindObjectOfType<PpshGermanEncounter>(true);
        if (encounter == null)
        {
            EditorUtility.DisplayDialog("Починка точек", "Объекта сценария в сцене нет.", "Ок");
            return;
        }

        GameObject ppsh = FindPpshObject();
        DoorController door = encounter.door;
        bool doorTooFar = door != null && ppsh != null &&
                          Vector3.Distance(door.transform.position, ppsh.transform.position) > encounter.maxDoorDistanceFromPpsh;
        if (door == null || doorTooFar)
        {
            DoorController cellar = FindCellarDoor();
            door = cellar != null ? cellar : FindDoorNearPpsh();

            var so = new SerializedObject(encounter);
            so.FindProperty("door").objectReferenceValue = door;
            so.ApplyModifiedProperties();

            if (door != null)
                Debug.Log($"[PpshGerman] Дверь сценария: «{door.name}».", encounter);
        }
        if (door == null)
        {
            EditorUtility.DisplayDialog("Починка точек", "Дверь не найдена — чинить нечего.", "Ок");
            return;
        }

        Transform root = encounter.transform;
        GameObject player = FindPlayer();
        Vector3 basePos = door.transform.position;
        if (player != null) basePos.y = player.transform.position.y + 0.05f;

        Vector3 side = -door.transform.forward;
        if (player != null)
        {
            Vector3 toPlayer = player.transform.position - basePos;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f) side = -toPlayer.normalized;
        }
        side.Normalize();
        Vector3 right = door.transform.right;

        Undo.RecordObject(root, "Fix encounter points");
        root.position = basePos;

        if (encounter.germanApproachPoint != null)
            SetLocal(encounter.germanApproachPoint, side * 9f);
        if (encounter.germanStandPoint != null)
            SetLocal(encounter.germanStandPoint, side * 1.7f);

        for (int i = 0; i < encounter.spawnPoints.Length; i++)
        {
            Transform t = encounter.spawnPoints[i];
            if (t == null) continue;
            SetLocal(t, right * (i - 2.5f) * 2.2f + side * 12f);
        }

        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[PpshGerman] Точки прибиты к двери «{door.name}» ({basePos}). " +
                  "Теперь немец дойдёт до двери и начнёт разговор.", encounter);
    }

    static void SetLocal(Transform t, Vector3 local)
    {
        Undo.RecordObject(t, "Fix point");
        t.localPosition = local;
        EditorUtility.SetDirty(t);
    }

    /// <summary>Сброс прогресса диалога и флага сценария — чтобы проверить заново.</summary>
    [MenuItem(MenuRoot + "Сбросить прогресс сценария (для теста)", false, 6)]
    public static void ResetProgress()
    {
        DialogueData dialogue = AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
        if (dialogue != null) DialogueManager.ResetDialogue(dialogue);
        GameState.SetFlag("ppsh_german_encounter_finished", false);
        Debug.Log("[PpshGerman] Прогресс диалога и флаг сценария сброшены. " +
                  "Теперь ППШ снова запускает сцену.", dialogue);
    }

    // ------------------------------------------------------------------
    // 4. Проверка
    // ------------------------------------------------------------------

    [MenuItem(MenuRoot + "Проверить готовность", false, 20)]
    public static void Validate()
    {
        var log = new System.Text.StringBuilder();
        int errors = 0;

        PpshGermanEncounter encounter = Object.FindObjectOfType<PpshGermanEncounter>(true);
        if (encounter == null)
        {
            log.AppendLine("[ОШИБКА] Объекта сценария в сцене нет. Запусти «Настроить сцену».");
            errors++;
        }
        else
        {
            var so = new SerializedObject(encounter);
            if (so.FindProperty("germanPrefab").objectReferenceValue == null)
            {
                log.AppendLine("[ОШИБКА] Не задан префаб немца (German Prefab).");
                errors++;
            }
            if (so.FindProperty("dialogue").objectReferenceValue == null)
            {
                log.AppendLine("[ОШИБКА] Не задан диалог (Dialogue).");
                errors++;
            }
            if (so.FindProperty("door").objectReferenceValue == null)
            {
                log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] Не указана дверь (Door) — сцена начнётся сразу после стука.");
            }
            else
            {
                var doorProp = so.FindProperty("door").objectReferenceValue as DoorController;
                GameObject ppshCheck = FindPpshObject();
                if (doorProp != null && ppshCheck != null)
                {
                    float d = Vector3.Distance(doorProp.transform.position, ppshCheck.transform.position);
                    if (d > encounter.maxDoorDistanceFromPpsh)
                        log.AppendLine($"[ОШИБКА] Дверь «{doorProp.name}» в {d:F0} м от ППШ — " +
                                       "жми «Починить точки (прибить к двери)».");
                }
            }
            if (so.FindProperty("germanApproachPoint").objectReferenceValue == null)
            {
                log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] Нет точки «Откуда подходит немец» — " +
                               "он появится в 9 м от двери по ту сторону, где игрока нет.");
            }
            if (so.FindProperty("germanStandPoint").objectReferenceValue == null)
            {
                log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] Нет точки «Немец у двери» — " +
                               "он встанет в 1.4 м от двери напротив игрока.");
            }
            else
            {
                var stand = so.FindProperty("germanStandPoint").objectReferenceValue as Transform;
                var doorT = so.FindProperty("door").objectReferenceValue as Component;
                if (stand != null && doorT != null)
                {
                    float d = Vector3.Distance(stand.position, doorT.transform.position);
                    if (d > 5f)
                        log.AppendLine($"[ОШИБКА] Точка «Немец у двери» в {d:F0} м от двери — " +
                                       "передвинь её или жми «Починить точки».");
                }
            }
            if (so.FindProperty("spawnPoints").arraySize == 0)
            {
                log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] Нет точек засады (Spawn Points) — немцы появятся кольцом вокруг игрока.");
            }
            if (so.FindProperty("raiderPrefabs").arraySize == 0)
            {
                log.AppendLine("[ОШИБКА] Не заданы префабы засады (Raider Prefabs).");
                errors++;
            }
        }

        DialogueData dialogue = AssetDatabase.LoadAssetAtPath<DialogueData>(DialoguePath);
        if (dialogue == null)
        {
            log.AppendLine($"[ОШИБКА] Ассет диалога не найден: {DialoguePath}");
            errors++;
        }
        else
        {
            int n = dialogue.nodes != null ? dialogue.nodes.Count : 0;
            if (n == 0)
            {
                log.AppendLine("[ОШИБКА] В диалоге 0 узлов.");
                errors++;
            }
            bool hasAgree = false, hasRefuse = false;
            foreach (var node in dialogue.nodes)
            {
                if (node?.choices == null) continue;
                foreach (var c in node.choices)
                {
                    if (c?.onSelectCommands == null) continue;
                    foreach (var cmd in c.onSelectCommands)
                    {
                        if (cmd == null || cmd.type != DialogueCommand.CommandType.CustomEvent) continue;
                        if (cmd.stringParam == "ppsh_german_agreed") hasAgree = true;
                        if (cmd.stringParam == "ppsh_german_refused") hasRefuse = true;
                    }
                }
            }
            if (!hasAgree) { log.AppendLine("[ОШИБКА] В диалоге нет события ppsh_german_agreed."); errors++; }
            if (!hasRefuse) { log.AppendLine("[ОШИБКА] В диалоге нет события ppsh_german_refused."); errors++; }
        }

        GameObject ppsh = FindPpshObject();
        if (ppsh == null)
        {
            log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] Подбираемый ППШ в сцене не найден.");
        }
        else if (ppsh.GetComponentInParent<Pickup>() == null)
        {
            log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] На ППШ нет Pickup — подобрать его нечем. " +
                           "Запусти «Сделать ППШ подбираемым в подвале».");
        }

        bool hasBread = false, hasWater = false;
        foreach (Pickup p in Object.FindObjectsOfType<Pickup>(true))
        {
            if (p == null || p.item == null) continue;
            if (p.item.Id == "bread") hasBread = true;
            if (p.item.Id == "water") hasWater = true;
        }
        if (!hasBread || !hasWater)
        {
            log.AppendLine("[ПРЕДУПРЕЖДЕНИЕ] В сцене нет хлеба или фляги — немец не получит еду. " +
                           "Жми «Положить хлеб и флягу рядом с ППШ».");
        }

        log.AppendLine(errors == 0 ? "Готово: сценарий можно проверять в Play." : $"Ошибок: {errors}.");
        Debug.Log("[PpshGerman] Проверка:\n" + log);
        EditorUtility.DisplayDialog("Проверка сценария", log.ToString(), "Ок");
    }
}
