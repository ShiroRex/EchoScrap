using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>One shared obstacle field for the horde; no per-enemy path allocations.</summary>
    public sealed class ArenaNavigation : MonoBehaviour
    {
        public static ArenaNavigation Active { get; private set; }
        [SerializeField] private Transform target;
        [SerializeField, Min(0.5f)] private float cellSize = 1.5f;
        [SerializeField, Min(1f)] private float halfExtent = 58f;
        [SerializeField, Min(0.2f)] private float clearance = 0.9f;
        private bool[] open;
        private int[] costs, queue;
        private int width, goal = -1;
        private float nextRefresh;
        private readonly Collider[] overlaps = new Collider[24];
        private static readonly int[] Dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] Dz = { -1, -1, -1, 0, 0, 1, 1, 1 };
        public int ReachableCells { get; private set; }
        public int WalkableCells { get; private set; }
        public Transform Target { get => target; set { target = value; goal = -1; } }

        private void OnEnable() => Active = this;
        private void OnDisable() { if (Active == this) Active = null; }
        private void Start() => Rebuild();

        public void Rebuild()
        {
            width = Mathf.FloorToInt(halfExtent * 2f / cellSize) + 1;
            int length = width * width;
            open = new bool[length]; costs = new int[length]; queue = new int[length];
            WalkableCells = 0; goal = -1;
            Collider floor = ArenaSurface.Active != null ? ArenaSurface.Active.GroundCollider : null;
            Physics.SyncTransforms();
            for (int i = 0; i < length; i++)
            {
                Vector3 p = Position(i); p.y = ArenaSurface.Height(p);
                int hits = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * 0.95f,
                    p + Vector3.up * 1.55f, clearance, overlaps, 1, QueryTriggerInteraction.Ignore);
                bool blocked = hits == overlaps.Length;
                for (int h = 0; h < hits; h++)
                    if (overlaps[h] != floor) { blocked = true; break; }
                open[i] = !blocked && ArenaSurface.Normal(p).y > 0.9f;
                if (open[i]) WalkableCells++;
            }
            Refresh();
        }

        public static Vector3 DirectionOrDirect(Vector3 origin, Vector3 direct)
        {
            if (Active == null || Active.target == null || direct.sqrMagnitude < 0.001f) return direct;
            return Active.Direction(origin, direct);
        }

        public Vector3 Direction(Vector3 origin, Vector3 fallback)
        {
            if (open == null || target == null) return fallback;
            int targetCell = NearestOpen(Cell(target.position));
            if (targetCell != goal || Time.time >= nextRefresh) Refresh(targetCell);
            if ((target.position - origin).sqrMagnitude < cellSize * cellSize) return fallback;
            int cell = Cell(origin);
            if (!open[cell])
            {
                int nearest = NearestOpen(cell);
                Vector3 escape = Position(nearest) - origin; escape.y = 0f;
                return escape.sqrMagnitude > 0.02f ? escape.normalized : fallback;
            }
            int best = cell, bestCost = costs[cell];
            float bestDistance = float.MaxValue;
            for (int d = 0; d < 8; d++)
            {
                int n = Neighbor(cell, d); if (n < 0 || !CanStep(cell, n, d)) continue;
                float distance = (Position(n) - target.position).sqrMagnitude;
                if (costs[n] < bestCost || (costs[n] == bestCost && best != cell && distance < bestDistance))
                { best = n; bestCost = costs[n]; bestDistance = distance; }
            }
            if (best == cell || bestCost == int.MaxValue) return fallback;
            Vector3 direction = Position(best) - origin; direction.y = 0f;
            return direction.normalized;
        }

        private void Refresh(int destination = -1)
        {
            if (open == null || target == null) return;
            goal = destination >= 0 ? destination : NearestOpen(Cell(target.position));
            nextRefresh = Time.time + 0.3f;
            for (int i = 0; i < costs.Length; i++) costs[i] = int.MaxValue;
            int read = 0, write = 0; queue[write++] = goal; costs[goal] = 0;
            while (read < write)
            {
                int cell = queue[read++];
                for (int d = 0; d < 8; d++)
                {
                    int n = Neighbor(cell, d);
                    if (n < 0 || costs[n] != int.MaxValue || !CanStep(cell, n, d)) continue;
                    costs[n] = costs[cell] + 1; queue[write++] = n;
                }
            }
            ReachableCells = write;
        }
        private bool CanStep(int cell, int n, int d)
        {
            if (!open[n]) return false;
            return Dx[d] == 0 || Dz[d] == 0 || (open[cell + Dx[d]] && open[cell + Dz[d] * width]);
        }
        private int Neighbor(int cell, int d)
        {
            int x = cell % width + Dx[d], z = cell / width + Dz[d];
            return x >= 0 && z >= 0 && x < width && z < width ? z * width + x : -1;
        }
        private int Cell(Vector3 p) => Mathf.Clamp(Mathf.RoundToInt((p.z + halfExtent) / cellSize), 0, width - 1) * width
            + Mathf.Clamp(Mathf.RoundToInt((p.x + halfExtent) / cellSize), 0, width - 1);
        private Vector3 Position(int i) => new Vector3(i % width * cellSize - halfExtent, 0f, i / width * cellSize - halfExtent);
        private int NearestOpen(int cell)
        {
            if (open[cell]) return cell;
            int best = cell; float distance = float.MaxValue;
            Vector3 origin = Position(cell);
            for (int z = -5; z <= 5; z++) for (int x = -5; x <= 5; x++)
            {
                int cx = cell % width + x, cz = cell / width + z;
                if (cx < 0 || cz < 0 || cx >= width || cz >= width) continue;
                int n = cz * width + cx;
                float d = (Position(n) - origin).sqrMagnitude;
                if (open[n] && d < distance) { best = n; distance = d; }
            }
            return best;
        }
    }
}
