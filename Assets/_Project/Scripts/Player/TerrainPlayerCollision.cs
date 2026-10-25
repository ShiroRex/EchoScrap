using EchoScrap.Core;
using UnityEngine;

namespace EchoScrap.Player
{
    /// <summary>Follows relief through a swept capsule instead of teleporting its height into scenery.</summary>
    internal sealed class TerrainPlayerCollision
    {
        private const float Skin = 0.015f;
        private const int Mask = 1 << PhysicsLayers.DefaultLayer;
        private readonly Rigidbody body;
        private readonly CapsuleCollider capsule;
        private readonly Collider[] overlaps = new Collider[128];
        private readonly RaycastHit[] hits = new RaycastHit[128];
        private float radius, segment;
        private Vector3 centerOffset, lastSafe;
        private Collider ground;
        private bool hasSafe;

        public TerrainPlayerCollision(Rigidbody body, CapsuleCollider capsule)
        { this.body = body; this.capsule = capsule; }

        public bool Step(float dt, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = hitNormal = Vector3.zero;
            if (ArenaSurface.Active == null || dt <= 0f) return false;
            ground = ArenaSurface.Active.GroundCollider;
            Vector3 scale = capsule.transform.lossyScale;
            radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            segment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
            centerOffset = body.rotation * Vector3.Scale(capsule.center, scale);
            body.constraints &= ~RigidbodyConstraints.FreezePositionY;

            Vector3 start = Grounded(body.position);
            // Heavy bodies can push the player between physics ticks. Recover the previous
            // nearby clear pose before the height correction can force it inside a low branch.
            if (Blocked(start, out _))
            {
                if (hasSafe && (lastSafe - start).sqrMagnitude < 9f && !Blocked(lastSafe, out _)) start = lastSafe;
                else
                {
                    bool found = false;
                    for (int ring = 1; ring <= 12 && !found; ring++) for (int i = 0; i < 16; i++)
                    {
                        float angle = i * Mathf.PI / 8f;
                        Vector3 candidate = Grounded(start + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 0.1f));
                        if (Blocked(candidate, out _)) continue;
                        start = candidate; found = true; break;
                    }
                    if (!found) { body.linearVelocity = Vector3.zero; return false; }
                }
            }
            body.position = start;
            lastSafe = start; hasSafe = true;
            Vector3 velocity = body.linearVelocity; velocity.y = 0f;
            Vector3 remaining = velocity * dt, position = start;
            bool collided = false;
            for (int slide = 0; slide < 3 && remaining.sqrMagnitude > 0.0000001f; slide++)
            {
                Vector3 target = Grounded(position + remaining);
                if (!Trace(position, target, out float fraction, out Vector3 normal, out Vector3 point))
                { position = target; break; }
                if (!collided) { hitPoint = point; hitNormal = normal; collided = true; }
                position = Grounded(position + remaining * fraction);
                remaining *= 1f - fraction;
                normal.y = 0f;
                // A ceiling with insufficient headroom blocks entry too; do not push upwards.
                if (normal.sqrMagnitude < 0.001f) normal = -remaining.normalized;
                remaining = Vector3.ProjectOnPlane(remaining, normal.normalized);
            }
            // Physics integrates one straight velocity segment; never cut through the inside
            // of a corner when a sliding plan contained several segments.
            if (Trace(start, position, out float safeFraction, out _, out _))
                position = Grounded(Vector3.Lerp(start, position, safeFraction));
            body.linearVelocity = (position - start) / dt;
            return collided;
        }

        public void RecoverAfterPhysics()
        {
            if (!hasSafe || ArenaSurface.Active == null || (body.position - lastSafe).sqrMagnitude >= 9f) return;
            if (!Blocked(body.position, out _) && !Blocked(Grounded(body.position), out _)) return;
            if (Blocked(lastSafe, out _)) return;
            // Contact impulses from heavy enemies run after FixedUpdate. Do not let their
            // correction leave the capsule embedded in a compound scenery collider.
            body.position = lastSafe;
            body.linearVelocity = Vector3.zero;
        }

        private Vector3 Grounded(Vector3 point)
        { point.y = ArenaSurface.SupportHeight(point, radius) + 1f; return point; }

        private void Ends(Vector3 root, out Vector3 bottom, out Vector3 top)
        {
            Vector3 center = root + centerOffset;
            bottom = center - Vector3.up * segment; top = center + Vector3.up * segment;
        }

        private bool Solid(Collider other) => other != null && other != ground;

        private bool Blocked(Vector3 root, out Vector3 normal)
        {
            normal = Vector3.zero;
            Ends(root, out Vector3 bottom, out Vector3 top);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + Skin, overlaps, Mask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return true;
            float deepest = 0f;
            for (int i = 0; i < count; i++)
            {
                Collider other = overlaps[i];
                if (!Solid(other)) continue;
                if (Physics.ComputePenetration(capsule, root, body.rotation, other, other.transform.position,
                    other.transform.rotation, out Vector3 direction, out float depth) && depth > deepest)
                { deepest = depth; normal = direction; }
            }
            return deepest > 0.002f;
        }

        private bool Trace(Vector3 from, Vector3 to, out float fraction, out Vector3 normal, out Vector3 point)
        {
            fraction = 1f; normal = Vector3.zero; point = to;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.00001f) return false;
            Ends(from, out Vector3 bottom, out Vector3 top);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, delta / distance, hits, distance,
                Mask, QueryTriggerInteraction.Ignore);
            bool hit = count == hits.Length;
            float closest = hit ? 0f : distance;
            for (int i = 0; i < count; i++)
            {
                if (!Solid(hits[i].collider) || hits[i].distance >= closest) continue;
                // PhysX reports zero-distance casts for touching shapes even when moving
                // away. Only keep these contacts if a short forward probe enters the shape.
                if (hits[i].distance <= 0.0001f)
                {
                    Vector3 probe = Grounded(from + delta * Mathf.Min(1f, 0.02f / distance));
                    Collider other = hits[i].collider;
                    if (!Physics.ComputePenetration(capsule, probe, body.rotation, other,
                        other.transform.position, other.transform.rotation, out _, out float depth) || depth <= 0.002f)
                        continue;
                }
                closest = hits[i].distance; normal = hits[i].normal; point = hits[i].point; hit = true;
            }
            if (hit) fraction = Mathf.Max(0f, closest - Skin) / distance;
            // Capsule casts ignore initial overlaps. A destination overlap also catches any
            // height adjustment that would put the head beneath an intersecting low branch.
            if (Blocked(to, out Vector3 overlapNormal))
            {
                float low = 0f, high = fraction;
                for (int i = 0; i < 12; i++)
                {
                    float mid = (low + high) * 0.5f;
                    if (Blocked(Grounded(Vector3.Lerp(from, to, mid)), out _)) high = mid; else low = mid;
                }
                fraction = Mathf.Max(0f, low - Skin / distance);
                if (!hit) { normal = overlapNormal; point = Vector3.Lerp(from, to, high); }
                hit = true;
            }
            return hit;
        }
    }
}
