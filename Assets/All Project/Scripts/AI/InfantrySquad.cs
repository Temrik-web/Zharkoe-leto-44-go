using System.Collections.Generic;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// Отрядная расстановка: место в строю, мягкое расхождение и подход к цели
    /// с разных сторон. Задача — солдаты НЕ стоят в одной точке, а держат
    /// разброс по местности, при этом перемещение остаётся плавным.
    /// </summary>
    internal static class InfantrySquad
    {
        // Черновики без аллокаций: считаются каждый кадр для каждого солдата.
        private static readonly List<EnemyAI> s_members = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_members.Clear();

        internal static EnemyAI FindNearestAlly(EnemyAI self, out float distance)
        {
            distance = float.MaxValue;
            EnemyAI nearest = null;
            float radius = self.SquadAwarenessRadius;
            List<EnemyAI> soldiers = EnemyAI.RegisteredSoldiers;

            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == self) continue;
                if (!other.IsSquadAlive || other.SquadTeam != self.SquadTeam) continue;

                float candidate = FlatDistance(self.transform.position, other.transform.position);
                if (candidate > radius || candidate >= distance) continue;

                distance = candidate;
                nearest = other;
            }

            return nearest;
        }

        /// <summary>
        /// Своё место в строю относительно цели.
        /// right — ось «вбок» от цели (в системе координат строя).
        /// lateral — сдвиг вбок, м; depth — сдвиг по глубине (ближе/дальше), м.
        /// Порядок определяется текущим боковым положением солдат: левый остаётся
        /// слевым, правый — правым, поэтому никто не перебегает через строй.
        /// Учитываются ТОЛЬКО союзники по отряду: живые, в пределах
        /// squadAwarenessRadius и находящиеся в погоне/бою. Раньше считались все
        /// солдаты уровня — при 20 солдатах строй «схлопывался» в точку.
        /// </summary>
        internal static void ComputeFormationSlot(EnemyAI self, Vector3 center, Vector3 right,
            out float lateral, out float depth)
        {
            lateral = 0f;
            depth = 0f;

            List<EnemyAI> soldiers = EnemyAI.RegisteredSoldiers;
            s_members.Clear();
            float radius = self.SquadAwarenessRadius;
            Vector3 selfPosition = self.transform.position;
            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == self) continue;
                if (!other.IsSquadAlive || other.SquadTeam != self.SquadTeam) continue;
                if (!other.IsInEngagement) continue;
                if (FlatDistance(selfPosition, other.transform.position) > radius) continue;
                s_members.Add(other);
            }

            int count = s_members.Count + 1;
            if (count <= 1) return;

            Vector3 selfRelative = self.transform.position - center;
            selfRelative.y = 0f;
            float selfLateral = Vector3.Dot(selfRelative, right);

            int rank = 0;
            for (int i = 0; i < s_members.Count; i++)
            {
                Vector3 relative = s_members[i].transform.position - center;
                relative.y = 0f;
                if (Vector3.Dot(relative, right) < selfLateral) rank++;
            }

            // Минимальный интервал — не меньше дистанции расхождения. Иначе при
            // пяти солдатах на 9 м строя шаг 2.25 м, и отряд выглядит кучей.
            float spacing = Mathf.Max(self.SquadSeparationDistance,
                self.SquadLateralSpread / Mathf.Max(1, count - 1));
            lateral = (rank - (count - 1) * 0.5f) * spacing;
            depth = (rank % 2 == 0 ? -0.5f : 0.5f) * self.SquadDepthSpread;
        }

        /// <summary>
        /// Мягкое расхождение: если сосед слишком близко — точка сдвигается вбок
        /// от него. Не рывок, а плавное смещение (якорь едет с ограниченной скоростью).
        /// </summary>
        internal static Vector3 ApplySeparation(EnemyAI self, Vector3 anchor)
        {
            EnemyAI nearest = FindNearestAlly(self, out float distance);
            if (nearest == null) return anchor;

            float minDistance = self.SquadSeparationDistance;
            if (distance >= minDistance) return anchor;

            Vector3 away = self.transform.position - nearest.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = self.transform.right;
            away.Normalize();

            float push = (minDistance - distance) * self.SquadSeparationStrength;
            return anchor + away * push;
        }

        /// <summary>Слишком близко к соседям по отряду — такую точку не берём.</summary>
        internal static bool IsCrowded(EnemyAI self, Vector3 point, float minDistance)
        {
            if (minDistance <= 0f) return false;

            float radius = self.SquadAwarenessRadius;
            List<EnemyAI> soldiers = EnemyAI.RegisteredSoldiers;
            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == self) continue;
                if (!other.IsSquadAlive || other.SquadTeam != self.SquadTeam) continue;
                // Сосед по отряду — только тот, кто рядом. Солдат на другом краю
                // уровня не мешает выбрать точку.
                if (FlatDistance(point, other.transform.position) > radius) continue;
                if (FlatDistance(point, other.transform.position) < minDistance) return true;
            }

            return false;
        }

        /// <summary>
        /// Куда идти в погоне: не в одну точку, а к своей стороне цели —
        /// солдаты подходят с разных направлений и не слипаются в кучу.
        /// </summary>
        internal static Vector3 AdjustChaseGoal(EnemyAI self, Vector3 destination)
        {
            Vector3 position = self.transform.position;
            Vector3 toTarget = destination - position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.5f) return destination;

            Vector3 forward = toTarget.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            ComputeFormationSlot(self, destination, right, out float lateral, out _);

            // Чем дальше цель, тем заметнее разброс по подходу.
            float distance = toTarget.magnitude;
            float scale = Mathf.Clamp(distance / Mathf.Max(1f, self.SquadLateralSpread), 0.35f, 1f);
            return destination + right * (lateral * scale);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
