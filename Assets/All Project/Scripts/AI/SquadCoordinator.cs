using UnityEngine;

namespace ZharkoeLeto44.AI
{
    /// <summary>Роль солдата в отряде (решает координатор, исполняет солдат).</summary>
    internal enum SquadRole
    {
        Fire = 0,     // стоит и стреляет
        Maneuver = 1, // перебежка 8–12 м, не стреляет
        Cover = 2,    // укрытие: выглянул — короткая очередь — спрятался
        Flank = 3     // обход с фланга
    }

    /// <summary>
    /// КООРДИНАТОР ОТРЯДА. Собирает живых союзников в отряды (пересчёт 0.5 с),
    /// раздаёт роли и жетоны атаки (план 0.25 с) и фиксирует решение минимум
    /// на commitTime (1.5 с) — выбора не дрожит.
    /// Солдаты только исполняют решение, ничего не решают сами.
    /// </summary>
    internal static class SquadCoordinator
    {
        private const int MaxSquads = 8;
        private const int MaxMembers = 24;

        private const float PlanInterval = 0.25f;
        private const float ClusterInterval = 0.5f;
        private const float CommitTime = 1.5f;
        private const int MaxShootersLimit = 3;
        // Жетон нельзя держать вечно: иначе первые стрелки всегда стреляют,
        // а остальные всегда бегают в манёвре/фланге и никогда не открывают огонь.
        private const float TokenHoldTime = 4f;

        internal sealed class SquadPlan
        {
            public readonly EnemyAI[] Members = new EnemyAI[MaxMembers];
            public readonly SquadRole[] Roles = new SquadRole[MaxMembers];
            public readonly bool[] Tokens = new bool[MaxMembers];
            public readonly Vector3[] Goals = new Vector3[MaxMembers];
            public int Count;
            public float LastChangeTime = float.NegativeInfinity;
            public bool Valid;
        }

        private static readonly SquadPlan[] s_plans = CreatePlans();
        private static readonly int[] s_candidate = new int[MaxMembers];
        private static readonly float[] s_candidateWait = new float[MaxMembers];
        private static int s_stamp;

        private static float s_nextPlanTime = float.NegativeInfinity;
        private static float s_nextClusterTime = float.NegativeInfinity;

        private static SquadPlan[] CreatePlans()
        {
            var plans = new SquadPlan[MaxSquads];
            for (int i = 0; i < MaxSquads; i++) plans[i] = new SquadPlan();
            return plans;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_stamp = 0;
            s_nextPlanTime = float.NegativeInfinity;
            s_nextClusterTime = float.NegativeInfinity;

            for (int i = 0; i < s_plans.Length; i++)
            {
                SquadPlan plan = s_plans[i];
                plan.Count = 0;
                plan.Valid = false;
                plan.LastChangeTime = float.NegativeInfinity;

                for (int j = 0; j < MaxMembers; j++)
                {
                    plan.Members[j] = null;
                    plan.Roles[j] = SquadRole.Fire;
                    plan.Tokens[j] = false;
                    plan.Goals[j] = default;
                }
            }
        }

        /// <summary>Тик координатора. Вызывают солдаты, но работает он раз в 0.25 с.</summary>
        internal static void Tick(float now)
        {
            if (now < s_nextPlanTime) return;
            s_nextPlanTime = now + PlanInterval;

            if (now >= s_nextClusterTime)
            {
                s_nextClusterTime = now + ClusterInterval;
                RebuildSquads();
            }

            PlanAll(now);
        }

        // ─────────────────────────── Группировка ───────────────────────────

        private static void RebuildSquads()
        {
            s_stamp++;
            int stamp = s_stamp;
            int planIndex = 0;

            var soldiers = EnemyAI.RegisteredSoldiers;
            for (int i = 0; i < soldiers.Count && planIndex < MaxSquads; i++)
            {
                EnemyAI seed = soldiers[i];
                if (seed == null || !seed.IsSquadAlive || !seed.IsInEngagement) continue;
                if (seed.SquadStamp == stamp) continue;

                SquadPlan plan = s_plans[planIndex++];
                plan.Count = 0;
                plan.Valid = true;

                float radius = seed.SquadAwarenessRadius;
                Vector3 origin = seed.transform.position;

                // Обход по индексу: список солдат может измениться (кто-то
                // зарегистрировался) — итерация по List бросила бы исключение.
                for (int j = 0; j < soldiers.Count; j++)
                {
                    EnemyAI other = soldiers[j];
                    if (other == null || other == seed) continue;
                    if (other.SquadStamp == stamp) continue;
                    if (!other.IsSquadAlive || other.SquadTeam != seed.SquadTeam) continue;
                    if (!other.IsInEngagement) continue;
                    if (FlatDistance(origin, other.transform.position) > radius) continue;

                    other.SquadStamp = stamp;
                    if (plan.Count < MaxMembers) plan.Members[plan.Count++] = other;
                }

                seed.SquadStamp = stamp;
                if (plan.Count < MaxMembers) plan.Members[plan.Count++] = seed;
            }

            for (int i = planIndex; i < MaxSquads; i++)
            {
                s_plans[i].Valid = false;
                s_plans[i].Count = 0;
            }
        }

        // ─────────────────────────── Планирование ───────────────────────────

        private static void PlanAll(float now)
        {
            for (int i = 0; i < MaxSquads; i++)
            {
                SquadPlan plan = s_plans[i];
                if (!plan.Valid || plan.Count == 0) continue;
                PlanSquad(plan, now);
            }
        }

        private static void PlanSquad(SquadPlan plan, float now)
        {
            int n = plan.Count;
            if (n <= 0) return;

            // Лидер — самый «в теме» (наибольшая осведомлённость).
            EnemyAI leader = plan.Members[0];
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                EnemyAI m = plan.Members[i];
                if (m == null) continue;
                float score = m.Awareness;
                if (score > bestScore) { bestScore = score; leader = m; }
            }
            if (leader == null) return;

            Vector3 targetPos = leader.SquadTarget != null
                ? leader.SquadTarget.position
                : leader.transform.position;

            Vector3 toTarget = targetPos - leader.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) toTarget = Vector3.forward;
            toTarget.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, toTarget);

            // Стреляет весь отряд: жетоны не ограничиваем (min(maxShooters, N/2)
            // оставлял половину солдат без огня — они только бегали).
            int maxShooters = Mathf.Clamp(Mathf.Max(leader.MaxShooters, n), 1, n);

            // 1. Жетоны: мёртв, потерял цель, заблокирован союзником — жетон отпускается.
            int holders = 0;
            for (int i = 0; i < n; i++)
            {
                EnemyAI m = plan.Members[i];
                if (m == null) { plan.Tokens[i] = false; continue; }

                if (!plan.Tokens[i]) continue;

                bool valid = m.IsSquadAlive && m.IsInEngagement &&
                            m.SquadTarget != null && !m.IsBlockedByAlly && m.CanTakeFireToken;
                if (!valid)
                {
                    plan.Tokens[i] = false;
                    m.HasFireToken = false;
                    m.LastTokenTime = now;
                    m.WantsReposition = false;
                    continue;
                }
                holders++;
            }

            bool canChange = now - plan.LastChangeTime >= Mathf.Max(0.1f, leader.SquadCommitTime);

            if (canChange)
            {
                // 2. Ротация: если есть кому стрелять, самый долго держащий жетон
                // уступает место ждущему (не все сразу — по одному за commit).
                int waiters = 0;
                for (int i = 0; i < n; i++)
                {
                    EnemyAI m = plan.Members[i];
                    if (m == null || plan.Tokens[i]) continue;
                    if (!m.IsSquadAlive || !m.IsInEngagement) continue;
                    if (!m.CanTakeFireToken) continue;
                    waiters++;
                }

                if (waiters > 0)
                {
                    int rotateIndex = -1;
                    float longestHold = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        if (!plan.Tokens[i]) continue;
                        EnemyAI m = plan.Members[i];
                        if (m == null) continue;
                        float held = now - m.LastTokenTime;
                        if (held < TokenHoldTime || held <= longestHold) continue;
                        longestHold = held;
                        rotateIndex = i;
                    }

                    if (rotateIndex >= 0)
                    {
                        EnemyAI rotated = plan.Members[rotateIndex];
                        plan.Tokens[rotateIndex] = false;
                        rotated.HasFireToken = false;
                        rotated.LastTokenTime = now;
                        holders--;
                    }
                }

                // 3. Кто получает жетон. Кандидаты: живые, в бою, с патронами.
                int candidates = 0;
                for (int i = 0; i < n; i++)
                {
                    EnemyAI m = plan.Members[i];
                    if (m == null || plan.Tokens[i]) continue;
                    if (!m.IsSquadAlive || !m.IsInEngagement) continue;
                    if (!m.CanTakeFireToken) continue;

                    s_candidate[candidates] = i;
                    s_candidateWait[candidates] = now - m.LastTokenTime;
                    candidates++;
                }

                // Сортировка выбора: кто дольше без жетона — тот первым.
                for (int k = 0; k < candidates; k++)
                {
                    int best = k;
                    for (int j = k + 1; j < candidates; j++)
                        if (s_candidateWait[j] > s_candidateWait[best]) best = j;
                    if (best != k)
                    {
                        (s_candidate[k], s_candidate[best]) = (s_candidate[best], s_candidate[k]);
                        (s_candidateWait[k], s_candidateWait[best]) = (s_candidateWait[best], s_candidateWait[k]);
                    }
                }

                int wanted = Mathf.Min(maxShooters, candidates + holders);
                // Лишние жетоны снимаем (кто-то мог остаться без надобности).
                for (int i = 0; i < n && holders > wanted; i++)
                {
                    if (!plan.Tokens[i]) continue;
                    plan.Tokens[i] = false;
                    plan.Members[i].HasFireToken = false;
                    plan.Members[i].LastTokenTime = now;
                    holders--;
                }
                // Недостающие — выдаём.
                for (int k = 0; k < candidates && holders < wanted; k++)
                {
                    int index = s_candidate[k];
                    plan.Tokens[index] = true;
                    plan.Members[index].HasFireToken = true;
                    plan.Members[index].LastTokenTime = now;
                    holders++;
                }

                // Все держат строй и стреляют с места. Никаких постоянных
                // перебежек «в манёвр/фланг» — отряд дерётся, а не бегает кучей.
                for (int i = 0; i < n; i++)
                    plan.Roles[i] = SquadRole.Fire;

                plan.LastChangeTime = now;
            }

            // 4. Применяем решение к солдатам и обновляем их цели.
            for (int i = 0; i < n; i++)
            {
                EnemyAI m = plan.Members[i];
                if (m == null) continue;

                SquadRole role = plan.Roles[i];
                m.WantsReposition = false;

                // Укрытие держим только пока роль «Укрытие».
                if (role != SquadRole.Cover && m.AssignedCover != null)
                {
                    m.AssignedCover.Release(m);
                    m.AssignedCover = null;
                }

                m.ApplySquadRole(role, plan.Tokens[i]);

                switch (role)
                {
                    case SquadRole.Maneuver:
                        plan.Goals[i] = PickManeuverGoal(m, leader, plan, toTarget, right, targetPos);
                        break;
                    case SquadRole.Flank:
                        plan.Goals[i] = PickFlankGoal(m, leader, plan, toTarget, right, targetPos);
                        break;
                    case SquadRole.Cover:
                        plan.Goals[i] = PickCoverGoal(m, leader, plan, toTarget, right, targetPos);
                        break;
                }

                m.SquadGoal = plan.Goals[i];
            }
        }

        // ─────────────────────────── Выбор позиций ───────────────────────────

        /// <summary>
        /// Перебежка: точка впереди-набок ОТ ЦЕЛИ (не от текущей позиции солдата —
        /// иначе цель «убегала» бы от него и перебежка не заканчивалась).
        /// </summary>
        private static Vector3 PickManeuverGoal(EnemyAI self, EnemyAI leader, SquadPlan plan,
            Vector3 toTarget, Vector3 right, Vector3 targetPos)
        {
            float side = ((self.GetInstanceID() & 1) == 0) ? 1f : -1f;
            float dash = Mathf.Max(4f, leader.ManeuverDistance * 0.7f);
            float range = Mathf.Max(4f, leader.SquadPreferredCombatDistance * 0.6f);

            Vector3 desired = targetPos - toTarget * range + right * side * dash;
            return ResolveGoal(self, desired, plan, targetPos, 4f);
        }

        /// <summary>Фланг: позиция сбоку от цели на боевой дистанции.</summary>
        private static Vector3 PickFlankGoal(EnemyAI self, EnemyAI leader, SquadPlan plan,
            Vector3 toTarget, Vector3 right, Vector3 targetPos)
        {
            float side = ((self.GetInstanceID() & 1) == 0) ? 1f : -1f;
            float flankDistance = Mathf.Max(8f, leader.SquadPreferredCombatDistance);
            Vector3 desired = targetPos + right * side * flankDistance - toTarget * 3f;
            return ResolveGoal(self, desired, plan, targetPos, 5f);
        }

        /// <summary>Укрытие: свободный CoverPoint рядом с целью (если есть).</summary>
        private static Vector3 PickCoverGoal(EnemyAI self, EnemyAI leader, SquadPlan plan,
            Vector3 toTarget, Vector3 right, Vector3 targetPos)
        {
            CoverPoint best = null;
            float bestScore = float.MaxValue;

            int count = CoverPoint.Count;
            for (int i = 0; i < count; i++)
            {
                CoverPoint point = CoverPoint.GetAt(i);
                if (point == null) continue;
                if (!point.IsFree) continue;

                float distance = Vector3.Distance(point.Position, targetPos);
                if (distance > point.MaxTargetDistance) continue;
                if (Vector3.Distance(point.Position, self.transform.position) > 30f) continue;

                // Не втыкаем укрытие прямо в линию «союзник — цель».
                if (IsOnAllyLine(plan, self, point.Position, targetPos)) continue;

                float score = distance + Vector3.Distance(point.Position, self.transform.position) * 0.4f;
                if (score >= bestScore) continue;

                bestScore = score;
                best = point;
            }

            if (best != null && best.TryClaim(self))
            {
                self.AssignedCover = best;
                return best.Position;
            }

            // Укрытий нет — отходим вбок от цели (иначе «стоим столбом»).
            self.AssignedCover = null;
            float side = ((self.GetInstanceID() & 1) == 0) ? 1f : -1f;
            float range = Mathf.Max(4f, leader.SquadPreferredCombatDistance);
            Vector3 desired = targetPos - toTarget * range + right * side * 5f;
            return ResolveGoal(self, desired, plan, targetPos, 4f);
        }

        private static Vector3 ResolveGoal(EnemyAI self, Vector3 desired, SquadPlan plan,
            Vector3 targetPos, float searchRadius)
        {
            EnemyMotor motor = self.Motor;
            if (motor == null) return desired;

            if (motor.SampleReachablePoint(desired, searchRadius, out Vector3 point, true) &&
                IsPositionSuitable(self, plan, point, targetPos))
                return point;

            // Подходим с другой стороны: точка могла оказаться за углом/стеной.
            for (int i = 0; i < 3; i++)
            {
                float offset = (i == 0) ? 5f : (i == 1 ? -5f : 9f);
                Vector3 alt = desired + self.transform.right * offset;
                if (motor.SampleReachablePoint(alt, searchRadius, out point, true) &&
                    IsPositionSuitable(self, plan, point, targetPos))
                    return point;
            }

            // Подходящей точки нет — держим текущую позицию (в стену не идём).
            return self.transform.position;
        }

        /// <summary>
        /// Проверки позиции: не ближе X м к цели, не ближе separation к союзникам,
        /// не на линии «союзник — цель».
        /// </summary>
        private static bool IsPositionSuitable(EnemyAI self, SquadPlan plan, Vector3 point,
            Vector3 targetPos)
        {
            if (Vector3.Distance(point, targetPos) < self.TacticMinTargetDistance) return false;

            float separation = self.SquadSeparationDistance;
            for (int i = 0; i < plan.Count; i++)
            {
                EnemyAI other = plan.Members[i];
                if (other == null || other == self) continue;
                if (FlatDistance(point, other.transform.position) < separation) return false;
            }

            return !IsOnAllyLine(plan, self, point, targetPos);
        }

        private static bool IsOnAllyLine(SquadPlan plan, EnemyAI self, Vector3 point,
            Vector3 targetPos)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                EnemyAI other = plan.Members[i];
                if (other == null || other == self) continue;

                Vector3 a = other.transform.position;
                Vector3 ab = targetPos - a;
                ab.y = 0f;
                float lengthSq = ab.sqrMagnitude;
                if (lengthSq < 0.01f) continue;

                Vector3 ap = point - a;
                ap.y = 0f;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / lengthSq);
                Vector3 closest = a + ab * t;
                if (Vector3.Distance(point, closest) < 1.5f) return true;
            }
            return false;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
