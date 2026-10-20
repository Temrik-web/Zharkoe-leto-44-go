using System.Collections.Generic;
using UnityEngine;

namespace ZharkoeLeto44.AI
{
    internal static class InfantrySquad
    {
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

        internal static bool IsAnchor(EnemyAI self, Vector3 aimPoint)
        {
            if (!self.EvaluateSquadLine(aimPoint, out _)) return false;

            EnemyAI best = self;
            float bestScore = LineScore(self, aimPoint);
            int bestId = self.GetInstanceID();
            float radius = self.SquadAwarenessRadius;
            List<EnemyAI> soldiers = EnemyAI.RegisteredSoldiers;

            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == self) continue;
                if (!other.IsSquadAlive || other.SquadTeam != self.SquadTeam) continue;
                if (other.State != EnemyState.Combat || other.SquadTarget != self.SquadTarget) continue;
                if (FlatDistance(self.transform.position, other.transform.position) > radius) continue;
                if (!other.EvaluateSquadLine(aimPoint, out _)) continue;

                float score = LineScore(other, aimPoint);
                bool better = score > bestScore + 0.001f ||
                    (Mathf.Abs(score - bestScore) <= 0.001f &&
                     other.GetInstanceID() < bestId);
                if (!better) continue;

                best = other;
                bestScore = score;
                bestId = other.GetInstanceID();
            }

            return best == self;
        }

        internal static int ChooseBypassSide(EnemyAI self, Vector3 targetPosition)
        {
            if (Time.time < self.SquadSideUntil && self.SquadSide != 0)
                return self.SquadSide;

            int left = 0;
            int right = 0;
            int count = 1;
            float radius = self.SquadAwarenessRadius;
            Vector3 aimPoint = targetPosition + Vector3.up * 1.4f;
            List<EnemyAI> soldiers = EnemyAI.RegisteredSoldiers;

            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == self) continue;
                if (!other.IsSquadAlive || other.SquadTeam != self.SquadTeam) continue;
                if (other.State != EnemyState.Combat || other.SquadTarget != self.SquadTarget) continue;
                if (FlatDistance(self.transform.position, other.transform.position) > radius)
                    continue;

                if (other.EvaluateSquadLine(aimPoint, out _)) continue;

                bool committed = Time.time < other.SquadSideUntil && other.SquadSide != 0;
                if (committed)
                {
                    if (other.SquadSide < 0) left++;
                    else right++;
                }

                count++;
            }

            int largeSide = left + right > 0
                ? (left >= right ? -1 : 1)
                : StableLargeSide(targetPosition);
            int smallCount = Mathf.Max(1, Mathf.FloorToInt(count * 0.3f));
            int largeCount = Mathf.Max(1, count - smallCount);
            int committedOnLarge = largeSide < 0 ? left : right;
            return committedOnLarge < largeCount ? largeSide : -largeSide;
        }

        internal static bool TryBuildSidestepPoint(EnemyAI self, EnemyAI nearest,
            Vector3 targetPosition, out Vector3 point)
        {
            point = self.transform.position;
            if (nearest == null) return false;

            Vector3 position = self.transform.position;
            Vector3 toTarget = targetPosition - position;
            toTarget.y = 0f;
            Vector3 forward = toTarget.sqrMagnitude > 0.01f
                ? toTarget.normalized
                : self.transform.forward;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;
            float step = Random.Range(1.4f, 2.2f);

            Vector3 first = position + lateral * step + forward * 0.35f;
            Vector3 second = position - lateral * step + forward * 0.35f;
            bool firstOk = self.Motor.SampleReachablePoint(first, 2.5f, out Vector3 firstPoint);
            bool secondOk = self.Motor.SampleReachablePoint(second, 2.5f, out Vector3 secondPoint);

            if (!firstOk && !secondOk) return false;

            if (firstOk && secondOk)
            {
                float firstClearance = MinAllyDistance(self, firstPoint);
                float secondClearance = MinAllyDistance(self, secondPoint);
                if (Mathf.Abs(firstClearance - secondClearance) < 0.05f)
                    point = StableSide(self) > 0 ? firstPoint : secondPoint;
                else
                    point = firstClearance >= secondClearance ? firstPoint : secondPoint;
            }
            else
            {
                point = firstOk ? firstPoint : secondPoint;
            }

            return FlatDistance(position, point) > 0.1f;
        }

        internal static bool TryBuildSpacingAdjustPoint(EnemyAI self, EnemyAI nearest,
            Vector3 targetPosition, bool pullCloser, out Vector3 point)
        {
            point = self.transform.position;
            if (nearest == null) return false;

            Vector3 position = self.transform.position;
            Vector3 toTarget = targetPosition - position;
            toTarget.y = 0f;
            Vector3 forward = toTarget.sqrMagnitude > 0.01f
                ? toTarget.normalized
                : self.transform.forward;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;
            float step = Random.Range(0.9f, Mathf.Max(1f, self.SquadMicroAdjustDistance));

            Vector3 candidate;
            if (pullCloser)
            {
                Vector3 toAlly = nearest.transform.position - position;
                toAlly.y = 0f;
                if (toAlly.sqrMagnitude < 0.01f) return false;

                Vector3 direction = toAlly.normalized;
                if (Vector3.Dot(direction, forward) < -0.2f)
                {
                    float side = Vector3.Dot(direction, lateral);
                    if (Mathf.Abs(side) < 0.05f) return false;
                    direction = lateral * Mathf.Sign(side);
                }

                candidate = position + direction * step;
            }
            else
            {
                Vector3 away = position - nearest.transform.position;
                away.y = 0f;
                float side = Vector3.Dot(away, lateral);
                int sign = Mathf.Abs(side) >= 0.05f
                    ? (side >= 0f ? 1 : -1)
                    : StableSide(self);
                candidate = position + lateral * (sign * step) + forward * 0.15f;
            }

            if (!self.Motor.SampleReachablePoint(candidate, 2f, out point)) return false;
            if (FlatDistance(position, point) < 0.1f) return false;
            return MinAllyDistance(self, point) >= self.SquadMinimumSpacing;
        }

        internal static bool TryBuildFlankAdjustPoint(EnemyAI self, EnemyAI nearest,
            Vector3 targetPosition, out Vector3 point)
        {
            point = self.transform.position;
            if (nearest == null) return false;

            Vector3 position = self.transform.position;
            Vector3 toTarget = targetPosition - nearest.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return false;

            Vector3 forward = toTarget.normalized;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;
            float offset = Vector3.Dot(position - nearest.transform.position, lateral);
            float maxOffset = Mathf.Clamp(self.SquadComfortMin * 0.35f, 1.5f, 2.5f);
            float room = maxOffset - Mathf.Abs(offset);
            if (room < 0.4f) return false;

            int sign = Mathf.Abs(offset) > 0.1f
                ? (offset > 0f ? 1 : -1)
                : StableSide(self);
            float step = Random.Range(Mathf.Min(0.9f, room), room);
            Vector3 candidate = position + lateral * (sign * step);
            if (!self.Motor.SampleReachablePoint(candidate, 2f, out Vector3 reachable))
                return false;
            if (FlatDistance(position, reachable) < 0.1f) return false;

            float before = MinAllyDistance(self, position);
            float after = MinAllyDistance(self, reachable);
            if (after < before - 0.25f && after < self.SquadMinimumSpacing) return false;

            point = reachable;
            return true;
        }

        internal static bool TryBuildBypassPoint(EnemyAI self, int side, Vector3 targetPosition,
            out Vector3 point)
        {
            point = self.transform.position;

            Vector3 position = self.transform.position;
            Vector3 toTarget = targetPosition - position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return false;

            Vector3 forward = toTarget.normalized;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized *
                (side < 0 ? -1f : 1f);
            float min = self.SquadBypassMinDistance;
            float max = self.SquadBypassMaxDistance;

            for (int i = 0; i < 3; i++)
            {
                float step = Random.Range(min, max);
                float advance = Random.Range(0.2f, 1f);
                Vector3 candidate = position + lateral * step + forward * advance;
                if (!self.Motor.SampleReachablePoint(candidate, 3f, out Vector3 reachable))
                    continue;
                if (FlatDistance(position, reachable) < 0.6f) continue;

                point = reachable;
                return true;
            }

            Vector3 fallback = position + lateral * Mathf.Max(1f, min * 0.6f);
            if (!self.Motor.SampleReachablePoint(fallback, 2.5f, out Vector3 fallbackPoint))
                return false;

            point = fallbackPoint;
            return FlatDistance(position, point) > 0.1f;
        }

        internal static Vector3 AdjustChaseGoal(EnemyAI self, Vector3 destination)
        {
            Vector3 position = self.transform.position;
            Vector3 toTarget = destination - position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.5f) return destination;

            Vector3 forward = toTarget.normalized;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;
            int side = StableSide(self);
            float offset = 1.7f + Mathf.Abs(self.GetInstanceID() % 7) * 0.15f;

            EnemyAI nearest = FindNearestAlly(self, out float nearestDistance);
            if (nearest != null && nearestDistance < self.SquadMinimumSpacing)
            {
                Vector3 away = position - nearest.transform.position;
                away.y = 0f;
                float dot = Vector3.Dot(away, lateral);
                if (Mathf.Abs(dot) >= 0.05f) side = dot >= 0f ? 1 : -1;
                offset += Mathf.Min(2f, self.SquadMinimumSpacing - nearestDistance);
            }

            return destination + lateral * (side * offset);
        }

        internal static int StableSide(EnemyAI self)
        {
            return (self.GetInstanceID() & 1) == 0 ? -1 : 1;
        }

        private static int StableLargeSide(Vector3 target)
        {
            int x = Mathf.FloorToInt(target.x * 0.25f);
            int z = Mathf.FloorToInt(target.z * 0.25f);
            int hash = x * 73856093 ^ z * 19349663;
            return (hash & 1) == 0 ? -1 : 1;
        }

        private static float LineScore(EnemyAI soldier, Vector3 aimPoint)
        {
            Vector3 eye = soldier.SquadEye != null
                ? soldier.SquadEye.position
                : soldier.transform.position;
            return 1000f - Vector3.Distance(eye, aimPoint);
        }

        private static float MinAllyDistance(EnemyAI self, Vector3 position)
        {
            float minimum = float.MaxValue;
            List<EnemyAI> soldiers = EnemyAI.RegisteredSoldiers;

            for (int i = 0; i < soldiers.Count; i++)
            {
                EnemyAI other = soldiers[i];
                if (other == null || other == self) continue;
                if (!other.IsSquadAlive || other.SquadTeam != self.SquadTeam) continue;

                float distance = FlatDistance(position, other.transform.position);
                if (distance < minimum) minimum = distance;
            }

            return minimum;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
