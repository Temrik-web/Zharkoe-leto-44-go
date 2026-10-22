using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// Общая расстановка спавнпоинтов: привязка к NavMesh и поворот.
    /// Все спавнеры (EnemySpawnCube, PpshGermanEncounter, GermanRaidDirector)
    /// используют ОДНИ правила, чтобы враг появлялся там, где поставлена точка.
    /// </summary>
    public static class SpawnPlacement
    {
        /// <summary>Все области NavMesh, кроме «Not Walkable» (индекс 1).</summary>
        public const int DefaultAreaMask = ~(1 << 1);

        /// <summary>Запас привязки точки к NavMesh по умолчанию, м.</summary>
        public const float DefaultSnap = 2.5f;

        /// <summary>Допуск «точка уже на NavMesh», м.</summary>
        public const float OnMeshTolerance = 0.15f;

        /// <summary>
        /// Поставить точку на NavMesh в радиусе snapRadius.
        /// true  — меш найден, position лежит на нём;
        /// false — рядом меша нет, position остаётся РОВНО заданной (спавнер сам
        /// решает, что делать: подменять точку кольцом/кубом нельзя — это и есть
        /// «враги не там, где сказали»).
        /// </summary>
        public static bool TrySnap(Vector3 desired, float snapRadius, int areaMask, out Vector3 position)
        {
            position = desired;
            if (areaMask == 0) areaMask = DefaultAreaMask;

            float radius = snapRadius > 0f ? snapRadius : OnMeshTolerance;
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, radius, areaMask))
                return false;

            position = hit.position;
            return true;
        }

        /// <summary>
        /// Поворот лицом к цели по горизонтали. Если цель совпала с точкой —
        /// берём fallbackForward: Quaternion.LookRotation(Vector3.zero) ругается.
        /// </summary>
        public static Quaternion LookAt(Vector3 from, Vector3 to, Vector3 fallbackForward)
        {
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = fallbackForward;
                dir.y = 0f;
            }
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>Расстояние по горизонтали: точка может висеть над полом.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Точка не ближе spacing к уже занятым (для «лишних» немцев).</summary>
        public static bool IsFree(Vector3 point, List<Vector3> used, float spacing)
        {
            if (used == null) return true;
            for (int i = 0; i < used.Count; i++)
                if (FlatDistance(point, used[i]) < spacing) return false;
            return true;
        }
    }
}
