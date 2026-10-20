using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using ZharkoeLeto44.AI;

using CombatEnemyAI = ZharkoeLeto44.AI.EnemyAI;
using CombatTeam = ZharkoeLeto44.AI.Team;

public static class EnemySetupWizard
{
    private const string GameDataFolder = "Assets/GameData";
    private const string PrefabsFolder = "Assets/GameData/Prefabs";
    private const string EnemyPrefabPath = "Assets/GameData/Prefabs/Enemy.prefab";
    private const string EnemyTemplateName = "== Enemy Template (клонируй меня) ==";

    [MenuItem("Tools/Враги/Создать префаб врага", false, 0)]
    public static GameObject CreateEnemyPrefab()
    {
        EnsureFolders();

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (existing != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Префаб врага",
                "Префаб Enemy.prefab уже существует. Пересоздать заново?",
                "Пересоздать", "Оставить как есть");
            if (!overwrite) return existing;
        }

        GameObject root = BuildEnemyObject();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        Debug.Log($"[EnemySetup] Префаб врага создан: {EnemyPrefabPath}");
        return prefab;
    }

    [MenuItem("Tools/Враги/Разместить врага перед игроком", false, 1)]
    public static void SpawnEnemyInFrontOfPlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null)
        {
            prefab = CreateEnemyPrefab();
            if (prefab == null) return;
        }

        GetSpawnBeforePlayer(out Vector3 spawnPos, out Quaternion spawnRot);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = EnemyTemplateName;
        instance.transform.SetPositionAndRotation(spawnPos, spawnRot);

        Undo.RegisterCreatedObjectUndo(instance, "Spawn enemy template");
        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("[EnemySetup] Эталонный враг размещён перед игроком. " +
                  "Выдели его и жми Ctrl+D, чтобы клонировать и расставить по уровню.");
    }

    [MenuItem("Tools/Враги/Создать врага-капсулу на сцене", false, 2)]
    public static void CreateCapsuleEnemyInScene()
    {
        GetSpawnBeforePlayer(out Vector3 spawnPos, out Quaternion spawnRot);

        GameObject root = BuildEnemyObject();
        root.name = "Enemy (капсула)";
        root.transform.SetPositionAndRotation(spawnPos, spawnRot);

        Undo.RegisterCreatedObjectUndo(root, "Create capsule enemy");
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("[EnemySetup] Рабочая капсула создана: EnemyAI + Motor + Health + " +
                  "Loadout + Weapon + Voice + SuppressionReceiver. Жми Play — " +
                  "патрулирует, видит игрока, стреляет.", root);
    }

    private static void GetSpawnBeforePlayer(out Vector3 spawnPos, out Quaternion spawnRot)
    {
        GameObject player = FindPlayer();

        if (player != null)
        {
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            spawnPos = player.transform.position + forward * 5f;
            spawnRot = Quaternion.LookRotation(-forward, Vector3.up);
        }
        else
        {
            spawnPos = Vector3.zero;
            spawnRot = Quaternion.identity;
            Debug.LogWarning("[EnemySetup] Игрок не найден — враг размещён в начале координат.");
        }

        if (NavMesh.SamplePosition(spawnPos, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
            spawnPos = navHit.position;
        else
            Debug.Log("[EnemySetup] NavMesh рядом не найден. Враг будет ходить в режиме " +
                      "EnemyMotor.Fallback (по коллайдерам земли). Для полноценной навигации " +
                      "с обходом препятствий запеки NavMesh.");
    }

    [MenuItem("Tools/Враги/Настроить игрока (свист пуль + тряска)", false, 20)]
    public static void SetupPlayerFeedback()
    {
        GameObject player = FindPlayer();
        if (player == null)
        {
            EditorUtility.DisplayDialog(
                "Настройка игрока",
                "Игрок на сцене не найден.\n\n" +
                "Открой сцену с игроком (CharacterController / камера) и запусти пункт меню снова.",
                "Ок");
            return;
        }

        CharacterHealth playerHealth = player.GetComponent<CharacterHealth>();
        if (playerHealth == null) playerHealth = Undo.AddComponent<CharacterHealth>(player);
        var healthSettings = new SerializedObject(playerHealth);
        healthSettings.FindProperty("team").enumValueIndex = (int)CombatTeam.Allies;
        healthSettings.ApplyModifiedProperties();

        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[EnemySetup] Камера игрока не найдена — обратная связь не настроена.");
            return;
        }

        GameObject camGo = cam.gameObject;

        CameraShake shake = camGo.GetComponent<CameraShake>();
        if (shake == null) Undo.AddComponent<CameraShake>(camGo);

        AudioSource whizz = camGo.GetComponent<AudioSource>();
        if (whizz == null) whizz = Undo.AddComponent<AudioSource>(camGo);
        whizz.playOnAwake = false;
        whizz.spatialBlend = 0f;
        SuppressionReceiver receiver = camGo.GetComponent<SuppressionReceiver>();
        if (receiver == null) receiver = Undo.AddComponent<SuppressionReceiver>(camGo);

        var so = new SerializedObject(receiver);
        so.FindProperty("isPlayer").boolValue = true;
        so.FindProperty("nearMissRadius").floatValue = 2.5f;
        so.FindProperty("whizzSource").objectReferenceValue = whizz;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(camGo);

        Debug.Log($"[EnemySetup] Игрок настроен: CameraShake + SuppressionReceiver на камере «{camGo.name}». " +
                  "Не забудь закинуть звуки свиста в поле Whizz Clips.");
        EditorUtility.DisplayDialog(
            "Игрок настроен",
            $"Камера: {camGo.name}\n\n" +
            "Добавлены:\n" +
            "  • CameraShake — тряска при близких пролётах\n" +
            "  • SuppressionReceiver (isPlayer = true)\n" +
            "  • AudioSource для свиста\n\n" +
            "Осталось вручную: перетащить 2–4 звука свиста/хлопка\n" +
            "в поле «Whizz Clips» у SuppressionReceiver.",
            "Ок");
    }

    [MenuItem("Tools/Враги/Обновить врагов на сцене (ходьба + звуки + оружие)", false, 21)]
    public static void UpgradeSceneEnemies()
    {
        CombatEnemyAI[] enemies = Object.FindObjectsOfType<CombatEnemyAI>(true);
        if (enemies.Length == 0)
        {
            EditorUtility.DisplayDialog("Обновление врагов",
                "На активных сценах не найдено ни одного EnemyAI.", "Ок");
            return;
        }

        int upgraded = 0;

        foreach (CombatEnemyAI ai in enemies)
        {
            GameObject go = ai.gameObject;
            bool changed = false;

            if (go.GetComponent<NavMeshAgent>() == null)
            {
                ConfigureAgent(Undo.AddComponent<NavMeshAgent>(go));
                changed = true;
            }

            if (go.GetComponent<EnemyMotor>() == null)
            {
                ConfigureMotor(Undo.AddComponent<EnemyMotor>(go), go.layer);
                changed = true;
            }

            if (go.GetComponent<EnemyVoice>() == null)
            {
                Undo.AddComponent<EnemyVoice>(go);
                changed = true;
            }

            if (go.GetComponent<EnemyLoadout>() == null)
            {
                Undo.AddComponent<EnemyLoadout>(go);
                changed = true;
            }

            // ДЫРА: враги-капсулы без проигрывателя анимаций. Добавляем молчаливый
            // EnemyAnimator (без клипов ничего не делает) + чиним высоту EyePoint
            // (0.7 = пояс, луч зрения цеплял низкие препятствия) и команду-цель.
            if (go.GetComponent<EnemyAnimator>() == null)
            {
                Undo.AddComponent<EnemyAnimator>(go);
                changed = true;
            }

            Transform eyeT = go.transform.Find("EyePoint");
            if (eyeT != null && eyeT.localPosition.y < 1f)
            {
                Undo.RecordObject(eyeT, "Fix EyePoint height");
                eyeT.localPosition = new Vector3(
                    eyeT.localPosition.x, 1.55f, eyeT.localPosition.z);
                changed = true;
            }

            var aiSo = new SerializedObject(ai);
            SerializedProperty teamProp = aiSo.FindProperty("enemyTeam");
            if (teamProp != null && teamProp.enumValueIndex == (int)CombatTeam.Allies)
            {
                teamProp.enumValueIndex = (int)CombatTeam.Axis;
                aiSo.ApplyModifiedProperties();
                changed = true;
            }

            if (changed)
            {
                upgraded++;
                EditorUtility.SetDirty(go);
            }
        }

        Debug.Log($"[EnemySetup] Обновлено врагов: {upgraded} из {enemies.Length}.");
        EditorUtility.DisplayDialog("Обновление врагов",
            $"Найдено врагов: {enemies.Length}\nДобавлены недостающие компоненты: {upgraded}\n\n" +
            "Осталось вручную:\n" +
            "  • закинуть звуки в EnemyVoice (крики, боль, смерть, шаги)\n" +
            "  • указать префаб оружия в EnemyLoadout → Weapon Prefab",
            "Ок");
    }

    [MenuItem("Tools/Враги/Настроить ВЫБРАННЫЙ объект как врага", false, 3)]
    public static void SetupSelectedAsEnemy()
    {
        GameObject root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Настройка врага",
                "Ничего не выбрано.\n\nВыдели модель врага на сцене (например, Rifle Run) " +
                "и запусти пункт меню снова.",
                "Ок");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root, "Setup enemy");
        int added = 0;

        NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
        if (agent == null) { agent = Undo.AddComponent<NavMeshAgent>(root); ConfigureAgent(agent); added++; }

        CharacterController cc = root.GetComponent<CharacterController>();
        if (cc == null)
        {
            cc = Undo.AddComponent<CharacterController>(root);
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.stepOffset = 0.45f;
            cc.skinWidth = 0.08f;
            added++;
        }

        EnemyMotor motor = root.GetComponent<EnemyMotor>();
        if (motor == null) { motor = Undo.AddComponent<EnemyMotor>(root); ConfigureMotor(motor, root.layer); added++; }

        CharacterHealth health = root.GetComponent<CharacterHealth>();
        if (health == null) { health = Undo.AddComponent<CharacterHealth>(root); added++; }
        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("team").enumValueIndex = (int)CombatTeam.Axis;
        healthSo.ApplyModifiedProperties();

        EnemyVoice voice = root.GetComponent<EnemyVoice>();
        if (voice == null) { voice = Undo.AddComponent<EnemyVoice>(root); added++; }

        EnemyLoadout loadout = root.GetComponent<EnemyLoadout>();
        if (loadout == null) { loadout = Undo.AddComponent<EnemyLoadout>(root); added++; }

        EnemyAnimator enemyAnimator = root.GetComponent<EnemyAnimator>();
        if (enemyAnimator == null) { enemyAnimator = Undo.AddComponent<EnemyAnimator>(root); added++; }

        CombatEnemyAI ai = root.GetComponent<CombatEnemyAI>();
        if (ai == null) { ai = Undo.AddComponent<CombatEnemyAI>(root); added++; }

        SuppressionReceiver receiver = root.GetComponent<SuppressionReceiver>();
        if (receiver == null) { receiver = Undo.AddComponent<SuppressionReceiver>(root); added++; }

        Animator animator = FindAnimatorOn(root);
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(root);
            added++;
        }

        Transform eye = FindDeepChild(root.transform, "EyePoint");
        if (eye == null)
        {
            GameObject eyeGo = new GameObject("EyePoint");
            Undo.RegisterCreatedObjectUndo(eyeGo, "Create EyePoint");
            eyeGo.transform.SetParent(root.transform, false);
            eyeGo.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            eye = eyeGo.transform;
        }
        else if (eye.parent == root.transform &&
                 eye.position.y - root.transform.position.y < 1f)
        {
            Undo.RecordObject(eye, "Fix EyePoint height");
            eye.localPosition = new Vector3(eye.localPosition.x, 1.55f, eye.localPosition.z);
        }

        HitscanWeapon weapon = root.GetComponentInChildren<HitscanWeapon>(true);
        if (weapon == null)
        {
            GameObject weaponGo = new GameObject("Weapon");
            Undo.RegisterCreatedObjectUndo(weaponGo, "Create Weapon");
            weaponGo.transform.SetParent(root.transform, false);
            weaponGo.transform.localPosition = new Vector3(0.25f, 1.1f, 0.3f);
            weaponGo.layer = root.layer;

            GameObject muzzle = new GameObject("Muzzle");
            Undo.RegisterCreatedObjectUndo(muzzle, "Create Muzzle");
            muzzle.transform.SetParent(weaponGo.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, 0.5f);

            AudioSource audio = Undo.AddComponent<AudioSource>(weaponGo);
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;

            weapon = Undo.AddComponent<HitscanWeapon>(weaponGo);
            var wso = new SerializedObject(weapon);
            wso.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
            wso.FindProperty("audioSource").objectReferenceValue = audio;
            wso.ApplyModifiedProperties();
            added++;
        }

        var aiSo = new SerializedObject(ai);
        SetRef(aiSo, "eyePoint", eye);
        SetRef(aiSo, "weapon", weapon);
        SetRef(aiSo, "voice", voice);
        SetRef(aiSo, "loadout", loadout);
        SetRef(aiSo, "animator", animator);
        SetRef(aiSo, "enemyAnimator", enemyAnimator);

        SerializedProperty teamProp = aiSo.FindProperty("enemyTeam");
        if (teamProp != null && teamProp.enumValueIndex == (int)CombatTeam.Allies)
            teamProp.enumValueIndex = (int)CombatTeam.Axis;

        SerializedProperty tm = aiSo.FindProperty("targetMask");
        SerializedProperty vm = aiSo.FindProperty("visibilityMask");
        if (tm != null && tm.intValue == 0) tm.intValue = ~0;
        if (vm != null && vm.intValue == 0) vm.intValue = ~0;

        float patrol = aiSo.FindProperty("patrolSpeed").floatValue;
        float chase = aiSo.FindProperty("chaseSpeed").floatValue;
        aiSo.ApplyModifiedProperties();

        var eSo = new SerializedObject(enemyAnimator);
        eSo.FindProperty("animator").objectReferenceValue = animator;
        eSo.FindProperty("walkAnchorSpeed").floatValue = Mathf.Max(0.5f, patrol);
        eSo.FindProperty("runAnchorSpeed").floatValue = Mathf.Max(patrol + 0.5f, chase);
        SerializedProperty runProp = eSo.FindProperty("runAnchorSpeed");
        SerializedProperty sprintProp = eSo.FindProperty("sprintAnchorSpeed");
        if (sprintProp.floatValue <= runProp.floatValue)
            sprintProp.floatValue = runProp.floatValue + 1f;
        eSo.ApplyModifiedProperties();

        var rSo = new SerializedObject(receiver);
        rSo.FindProperty("isPlayer").boolValue = false;
        rSo.FindProperty("enemyAI").objectReferenceValue = ai;
        rSo.ApplyModifiedProperties();

        EditorUtility.SetDirty(root);

        bool noAvatar = animator.avatar == null;
        string manual = "";
        if (noAvatar)
            manual += "\n• назначить Avatar в Animator (без него Mixamo-клипы не заведутся);";
        manual += "\n• перетащить клипы в EnemyAnimator (Idle / Walk / Run / Sprint);";
        manual += "\n• оружие: префаб в EnemyLoadout → Weapon Prefab (или оставить автосозданное);";
        manual += "\n• звуки в EnemyVoice (крики, боль, смерть, шаги).";

        Debug.Log($"[EnemySetup] «{root.name}»: добавлено компонентов: {added}. " +
            "Связи Eye/Weapon/Voice/Loadout/Animator/EnemyAnimator проставлены, " +
            "якоря скорости синхронизированы с EnemyAI." +
            (noAvatar ? " ВНИМАНИЕ: у Animator нет Avatar!" : ""), root);
        EditorUtility.DisplayDialog("Враг настроен",
            $"Объект: {root.name}\nДобавлено компонентов: {added}\n" +
            "Связи проставлены, якоря скорости синхронизированы.\n\nОсталось вручную:" + manual,
            "Ок");
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null && p.objectReferenceValue == null)
            p.objectReferenceValue = value;
    }

    private static Animator FindAnimatorOn(GameObject root)
    {
        Animator self = root.GetComponent<Animator>();
        if (self != null) return self;

        HitscanWeapon w = root.GetComponentInChildren<HitscanWeapon>(true);
        Transform weaponRoot = w != null ? w.transform : null;

        foreach (Animator cand in root.GetComponentsInChildren<Animator>(true))
        {
            if (cand == null) continue;
            if (weaponRoot != null &&
                (cand.transform == weaponRoot || cand.transform.IsChildOf(weaponRoot)))
                continue;
            return cand;
        }

        return null;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent.name == childName) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindDeepChild(child, childName);
            if (found != null) return found;
        }

        return null;
    }

    private static GameObject BuildEnemyObject()
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "Enemy";

        int charLayer = LayerMask.NameToLayer("Characters");
        if (charLayer >= 0) root.layer = charLayer;
        ConfigureAgent(root.AddComponent<NavMeshAgent>());
        ConfigureMotor(root.AddComponent<EnemyMotor>(), root.layer);
        CharacterHealth health = root.AddComponent<CharacterHealth>();
        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("team").enumValueIndex = (int)CombatTeam.Axis;
        healthSo.FindProperty("maximumHealth").floatValue = 100f;
        healthSo.ApplyModifiedProperties();

        GameObject eye = new GameObject("EyePoint");
        eye.transform.SetParent(root.transform, false);
        eye.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        GameObject weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(root.transform, false);
        weaponGo.transform.localPosition = new Vector3(0.25f, 0.5f, 0.3f);

        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(weaponGo.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 0.5f);

        AudioSource weaponAudio = weaponGo.AddComponent<AudioSource>();
        weaponAudio.playOnAwake = false;
        weaponAudio.spatialBlend = 1f;
        HitscanWeapon weapon = weaponGo.AddComponent<HitscanWeapon>();
        var weaponSo = new SerializedObject(weapon);
        weaponSo.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        weaponSo.FindProperty("audioSource").objectReferenceValue = weaponAudio;
        weaponSo.ApplyModifiedProperties();

        EnemyVoice voice = root.AddComponent<EnemyVoice>();
        CombatEnemyAI ai = root.AddComponent<CombatEnemyAI>();
        var aiSo = new SerializedObject(ai);
        aiSo.FindProperty("eyePoint").objectReferenceValue = eye.transform;
        aiSo.FindProperty("weapon").objectReferenceValue = weapon;
        aiSo.FindProperty("voice").objectReferenceValue = voice;
        aiSo.FindProperty("enemyTeam").enumValueIndex = (int)CombatTeam.Axis;

        int selfLayerMask = charLayer >= 0 ? (1 << charLayer) : 0;

        SerializedProperty targetMask = aiSo.FindProperty("targetMask");
        SerializedProperty visibilityMask = aiSo.FindProperty("visibilityMask");
        targetMask.intValue = selfLayerMask != 0 ? ~selfLayerMask : ~0;
        visibilityMask.intValue = selfLayerMask != 0 ? ~selfLayerMask : ~0;
        aiSo.ApplyModifiedProperties();

        root.AddComponent<EnemyLoadout>();
        SuppressionReceiver receiver = root.AddComponent<SuppressionReceiver>();
        var recSo = new SerializedObject(receiver);
        recSo.FindProperty("isPlayer").boolValue = false;
        recSo.FindProperty("nearMissRadius").floatValue = 3f;
        recSo.FindProperty("enemyAI").objectReferenceValue = ai;
        recSo.ApplyModifiedProperties();

        return root;
    }

    private static void ConfigureAgent(NavMeshAgent agent)
    {
        agent.speed = 4.2f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.2f;
        agent.radius = 0.4f;
        agent.height = 2f;
    }

    private static void ConfigureMotor(EnemyMotor motor, int ownerLayer)
    {
        var so = new SerializedObject(motor);

        so.FindProperty("allowFallbackMovement").boolValue = true;
        so.FindProperty("arriveRadius").floatValue = 1.2f;
        so.FindProperty("bodyRadius").floatValue = 0.4f;
        so.FindProperty("groundOffset").floatValue = 1f;
        int charLayerIdx = LayerMask.NameToLayer("Characters");
        int exclude = charLayerIdx >= 0 ? ~(1 << charLayerIdx) : ~0;
        so.FindProperty("groundMask").intValue = exclude;
        so.FindProperty("obstacleMask").intValue = exclude;

        so.ApplyModifiedProperties();
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(GameDataFolder))
            AssetDatabase.CreateFolder("Assets", "GameData");
        if (!AssetDatabase.IsValidFolder(PrefabsFolder))
            AssetDatabase.CreateFolder(GameDataFolder, "Prefabs");
    }

    private static GameObject FindPlayer()
    {
        var pc = Object.FindObjectOfType<PlayerHealth>();
        if (pc != null) return pc.gameObject;
        GameObject tagged = null;
        try { tagged = GameObject.FindGameObjectWithTag("Player"); }
        catch {  }
        if (tagged != null) return tagged;
        var cc = Object.FindObjectOfType<CharacterController>();
        if (cc != null) return cc.gameObject;

        return null;
    }
}
