using System.Collections.Generic;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// Реестр точек появления: SpawnById("GermanBehindDEDA"), ActivateGroup("Story").
    ///
    /// Точки сами регистрируются в OnEnable/OnDisable — в рантайме нет ни
    /// одного FindObjectsOfType. Дубли Id — ошибка: побеждает первая точка,
    /// вторая игнорируется (и пишется в лог).
    /// </summary>
    public static class SpawnPointRegistry
    {
        private static readonly List<SpawnPoint> All = new List<SpawnPoint>();
        private static readonly Dictionary<string, SpawnPoint> ById = new Dictionary<string, SpawnPoint>();
        private static readonly Dictionary<string, List<SpawnPoint>> ByGroup = new Dictionary<string, List<SpawnPoint>>();

        /// <summary>Все зарегистрированные точки (живые).</summary>
        public static IReadOnlyList<SpawnPoint> Points => All;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            bool had = All.Count > 0;
            All.Clear();
            ById.Clear();
            ByGroup.Clear();
            if (had)
                Debug.LogWarning("[SpawnPointRegistry] При входе в Play Mode статики сброшены, а сцена не перезагружалась " +
                                 "(Edit → Project Settings → Editor → Enter Play Mode Options). Точки должны зарегистрироваться " +
                                 "в OnEnable — если сцена не перезагружается, вызови Register вручную.");
        }

        /// <summary>Точка появилась/исчезла — обновляем кэш.</summary>
        public static void Register(SpawnPoint point)
        {
            if (point == null) return;
            if (!All.Contains(point)) All.Add(point);
            RebuildCache();
        }

        /// <summary>Точка ушла со сцены.</summary>
        public static void Unregister(SpawnPoint point)
        {
            if (point == null) return;
            All.Remove(point);
            RebuildCache();
        }

        private static void RebuildCache()
        {
            ById.Clear();
            ByGroup.Clear();

            for (int i = All.Count - 1; i >= 0; i--)
            {
                SpawnPoint point = All[i];
                if (point == null)
                {
                    All.RemoveAt(i);
                    continue;
                }

                string id = point.Id;
                if (string.IsNullOrEmpty(id)) continue;

                if (ById.ContainsKey(id))
                {
                    // Дубль Id: первая зарегистрированная побеждает.
                    Debug.LogError($"[SpawnPointRegistry] Дубль Id «{id}»: «{ById[id].name}» и «{point.name}». " +
                                   "Id точек должны быть уникальны в сцене — переименуй одну.", point);
                    continue;
                }
                ById[id] = point;

                string group = point.Group;
                if (string.IsNullOrEmpty(group)) continue;
                if (!ByGroup.TryGetValue(group, out List<SpawnPoint> list))
                {
                    list = new List<SpawnPoint>();
                    ByGroup[group] = list;
                }
                list.Add(point);
            }
        }

        /// <summary>Найти точку по Id.</summary>
        public static bool TryGetPoint(string id, out SpawnPoint point)
        {
            point = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (ById.Count == 0 && All.Count > 0) RebuildCache();
            return ById.TryGetValue(id, out point) && point != null;
        }

        /// <summary>Все точки группы (пусто — группа неизвестна).</summary>
        public static List<SpawnPoint> GetGroup(string group)
        {
            if (string.IsNullOrEmpty(group)) return new List<SpawnPoint>();
            if (ByGroup.Count == 0 && All.Count > 0) RebuildCache();
            return ByGroup.TryGetValue(group, out List<SpawnPoint> list)
                ? new List<SpawnPoint>(list)
                : new List<SpawnPoint>();
        }

        /// <summary>
        /// Поднять объект в точке с указанным Id.
        /// null — точки нет, она выключена, уже сработала (OneShot) или нечего спавнить.
        /// </summary>
        public static GameObject SpawnById(string id, Transform faceTarget = null)
        {
            if (!TryGetPoint(id, out SpawnPoint point))
            {
                Debug.LogWarning($"[SpawnPointRegistry] Точка «{id}» не найдена в сцене.");
                return null;
            }

            if (!point.Spawn(out GameObject instance, faceTarget))
            {
                if (point.HasSpawned)
                    Debug.Log($"[SpawnPointRegistry] Точка «{id}» уже использована (OneShot) — повторный спавн пропущен.");
                return null;
            }
            return instance;
        }

        /// <summary>Поднять все точки группы. Возвращает, сколько поднялось.</summary>
        public static int ActivateGroup(string group, Transform faceTarget = null)
        {
            List<SpawnPoint> points = GetGroup(group);
            if (points.Count == 0)
            {
                Debug.LogWarning($"[SpawnPointRegistry] Группа «{group}» пуста — спавнить нечего.");
                return 0;
            }

            int count = 0;
            foreach (SpawnPoint point in points)
            {
                if (point == null) continue;
                if (point.Spawn(out _, faceTarget)) count++;
            }
            Debug.Log($"[SpawnPointRegistry] Группа «{group}»: поднято точек — {count} из {points.Count}.");
            return count;
        }

        /// <summary>Сбросить реестр (тесты).</summary>
        public static void Clear()
        {
            All.Clear();
            ById.Clear();
            ByGroup.Clear();
        }
    }
}
