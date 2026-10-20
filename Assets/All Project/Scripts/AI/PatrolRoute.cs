using UnityEngine;

namespace ZharkoeLeto44.AI
{
public sealed class PatrolRoute : MonoBehaviour
{
    [SerializeField] private Transform[] points;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool randomOrder;
    public int Count => points != null ? points.Length : 0;
    public bool Loop => loop;
    public int NextIndex(int current)
    {
        if (Count <= 1) return 0;
        if (randomOrder)
        {
            int next = Random.Range(0, Count - 1);
            return next >= current ? next + 1 : next;
        }
        return current + 1 < Count ? current + 1 : (loop ? 0 : Count - 1);
    }

    public Transform GetPoint(int index)
    {
        if (points == null || points.Length == 0) return null;
        index = Mathf.Clamp(index, 0, points.Length - 1);
        return points[index];
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (points == null || points.Length == 0) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null) continue;
            Gizmos.DrawWireSphere(points[i].position, 0.3f);

            int next = (i + 1) % points.Length;
            if (!loop && next == 0) continue;
            if (points[next] != null)
                Gizmos.DrawLine(points[i].position, points[next].position);
        }
    }
#endif
}
}
