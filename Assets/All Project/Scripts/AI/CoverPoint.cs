using UnityEngine;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// МАРКЕР УКРЫТИЯ на сцене. Ставится на месте укрытия (ящик, стена, окоп).
    /// Координатор отряда выбирает свободные укрытия рядом с целью и распределяет
    /// их между солдатами: один залег — один выглянул.
    /// Если маркеров на сцене нет — тактика работает без укрытий (роль «Укрытие»
    /// превращается в обычную перебежку).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoverPoint : MonoBehaviour
    {
        [Header("Тип укрытия")]
        [Tooltip("Высокое укрытие: за ним не видно, нужно выглядывать сбоку. " +
                 "Низкое — можно стрелять поверх, лишь слегка наклонившись.")]
        [SerializeField] private bool highCover;
        [Tooltip("Откуда безопасно смотреть из укрытия. Пусто — берётся направление " +
                 "от цели (укрытие «смотрит» на врага).")]
        [SerializeField] private Transform facingOverride;
        [Tooltip("Радиус, в котором укрытие считается занятым, м.")]
        [SerializeField, Min(0.5f)] private float claimRadius = 1.5f;
        [Tooltip("Максимальная дистанция от укрытия до цели, м. Дальние укрытия " +
                 "не предлагаются — из них не видно.")]
        [SerializeField, Min(1f)] private float maxTargetDistance = 45f;

        public bool HighCover => highCover;
        public float ClaimRadius => claimRadius;
        public float MaxTargetDistance => maxTargetDistance;
        public Vector3 Position => transform.position;
        public Transform FacingOverride => facingOverride;

        /// <summary>Солдат, занявший укрытие (null — свободно).</summary>
        public EnemyAI Occupant { get; private set; }

        public bool IsFree => Occupant == null;

        private static readonly System.Collections.Generic.List<CoverPoint> s_points = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => s_points.Clear();

        private void OnEnable() => Register(this);
        private void OnDisable() => Unregister(this);

        private static void Register(CoverPoint point)
        {
            if (point == null) return;
            if (!s_points.Contains(point)) s_points.Add(point);
        }

        private static void Unregister(CoverPoint point)
        {
            if (point == null) return;
            s_points.Remove(point);
        }

        internal static int Count => s_points.Count;

        internal static CoverPoint GetAt(int index) =>
            index >= 0 && index < s_points.Count ? s_points[index] : null;

        /// <summary>Занять укрытие. false — уже занято другим солдатом.</summary>
        public bool TryClaim(EnemyAI soldier)
        {
            if (Occupant != null && Occupant != soldier) return false;
            Occupant = soldier;
            return true;
        }

        /// <summary>Освободить укрытие (солдат погиб или сменил роль).</summary>
        public void Release(EnemyAI soldier)
        {
            if (Occupant == soldier) Occupant = null;
        }

        /// <summary>Направление, в котором безопасно смотреть из укрытия.</summary>
        public Vector3 GetFacingDirection(Vector3 targetPosition)
        {
            if (facingOverride != null)
            {
                Vector3 dir = facingOverride.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) return dir.normalized;
            }

            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return transform.forward;
            return toTarget.normalized;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = highCover ? new Color(0.2f, 0.6f, 1f) : new Color(0.3f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, claimRadius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);
        }
#endif
    }
}
