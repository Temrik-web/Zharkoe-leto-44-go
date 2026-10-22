using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZharkoeLeto44.AI;

/// <summary>
/// Инструменты точек появления: создание из меню, проверка сцены, инспектор.
/// Только редактор — в рантайме точки работают через SpawnPointRegistry.
/// </summary>
public static class SpawnPointEditor
{
    const string CreateMenu = "GameObject/Flame of History/Create SpawnPoint";
    const string ValidateMenu = "Tools/Flame of History/Проверить точки спавна";

    [MenuItem(CreateMenu, false, 10)]
    public static void CreateSpawnPoint(MenuCommand command)
    {
        GameObject go = new GameObject("SpawnPoint");
        Undo.RegisterCreatedObjectUndo(go, "Create SpawnPoint");

        SpawnPoint point = Undo.AddComponent<SpawnPoint>(go);

        // Ставим точку туда, где смотрит редактор (или перед камерой сцены).
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            Ray ray = view.camera.ScreenPointToRay(new Vector3(view.camera.pixelWidth * 0.5f,
                                                               view.camera.pixelHeight * 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                go.transform.position = hit.point;
            else
                go.transform.position = ray.origin + ray.direction * 5f;
        }

        go.transform.rotation = Quaternion.identity;
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log($"[SpawnPoint] Создана точка «{go.name}». Задай Id (уникальный в сцене), Group, Prefab " +
                  "и, если нужно, состояние ИИ.", go);
    }

    [MenuItem(ValidateMenu, false, 0)]
    public static void ValidateAll()
    {
        SpawnPoint[] points = Object.FindObjectsOfType<SpawnPoint>(true);
        if (points == null || points.Length == 0)
        {
            Debug.Log("[SpawnPoint] В сцене нет ни одной точки появления.");
            return;
        }

        var byId = new Dictionary<string, SpawnPoint>();
        int errors = 0;
        int warnings = 0;
        var log = new System.Text.StringBuilder();
        log.AppendLine($"Точек появления в сцене: {points.Length}");

        foreach (SpawnPoint point in points)
        {
            if (point == null) continue;
            string path = FullPath(point.gameObject);

            if (string.IsNullOrEmpty(point.Id))
            {
                errors++;
                log.AppendLine($"[ОШИБКА] {path}: пустой Id — точку не найти по имени.");
            }
            else if (byId.TryGetValue(point.Id, out SpawnPoint other))
            {
                errors++;
                log.AppendLine($"[ОШИБКА] Дубль Id «{point.Id}»: {FullPath(other.gameObject)} и {path}. " +
                               "Id должны быть уникальны.");
            }
            else
            {
                byId[point.Id] = point;
            }

            if (point.prefab == null && point.sceneObject == null)
            {
                errors++;
                log.AppendLine($"[ОШИБКА] {path}: не задан Prefab и Scene Object — спавнить нечего.");
            }

            if (point.activation == SpawnActivation.OnEvent && string.IsNullOrEmpty(point.eventId))
            {
                errors++;
                log.AppendLine($"[ОШИБКА] {path}: активация OnEvent, но Event Id пуст.");
            }

            if (point.activation == SpawnActivation.OnTriggerEnter && point.GetComponent<Collider>() == null)
            {
                warnings++;
                log.AppendLine($"[ВНИМАНИЕ] {path}: OnTriggerEnter, но нет коллайдера — точка не сработает.");
            }

            if (point.oneShot && point.activation == SpawnActivation.Manual)
            {
                warnings++;
                log.AppendLine($"[ВНИМАНИЕ] {path}: Manual + OneShot — точка сработает только один раз, " +
                               "из кода (SpawnById).");
            }

            if (point.HasSpawned)
                log.AppendLine($"[Инфо] {path}: уже использована.");

            Collider inside = PointInsideCollider(point);
            if (inside != null)
            {
                warnings++;
                log.AppendLine($"[ВНИМАНИЕ] {path}: точка внутри коллайдера «{FullPath(inside.gameObject)}» — " +
                               "враг появится в стене/в полу. Сдвинь точку.");
            }
        }

        if (errors > 0) log.AppendLine($"Ошибок: {errors}. Вниманий: {warnings}.");
        else if (warnings > 0) log.AppendLine($"Ошибок нет. Вниманий: {warnings}.");
        else log.AppendLine("Всё чисто.");

        Debug.Log($"[SpawnPoint] Проверка точек появления:\n{log}");
    }

    /// <summary>Коллайдер, внутрь которого попала точка (null — таких нет).</summary>
    static Collider PointInsideCollider(SpawnPoint point)
    {
        Vector3 position = point.transform.position + point.positionOffset;
        Collider[] hits = Physics.OverlapSphere(position, 0.35f, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider collider in hits)
        {
            if (collider == null) continue;
            if (collider.transform.IsChildOf(point.transform)) continue;
            return collider;
        }
        return null;
    }

    static string FullPath(GameObject go)
    {
        if (go == null) return "?";
        string path = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}

/// <summary>Инспектор точки: кнопки «спавнить сейчас» и «проверить дубли».</summary>
[CustomEditor(typeof(SpawnPoint))]
public class SpawnPointInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SpawnPoint point = (SpawnPoint)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Действия", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!point.spawnEnabled || (point.oneShot && point.HasSpawned)))
        {
            if (GUILayout.Button("Спавнить сейчас"))
            {
                if (point.Spawn(out GameObject instance))
                    EditorGUIUtility.PingObject(instance);
            }
        }

        if (GUILayout.Button("Найти точки с тем же Id"))
        {
            List<SpawnPoint> same = new List<SpawnPoint>();
            foreach (SpawnPoint other in Object.FindObjectsOfType<SpawnPoint>(true))
            {
                if (other == null || other == point) continue;
                if (!string.IsNullOrEmpty(point.Id) && other.Id == point.Id) same.Add(other);
            }
            if (same.Count == 0) Debug.Log($"[SpawnPoint] Дублей Id «{point.Id}» нет.");
            else
            {
                foreach (SpawnPoint other in same)
                    Debug.LogWarning($"[SpawnPoint] Дубль Id «{point.Id}»: {other.name}", other);
                Selection.objects = same.ToArray();
            }
        }
    }
}
