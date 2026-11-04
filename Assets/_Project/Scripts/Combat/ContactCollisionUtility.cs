using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Comprobación de contacto físico mediante los colliders reales en lugar de distancia entre centros.
    /// Soporta colliders grandes (Coloso 1.6x, Gólgora 3.5x), tanto triggers como sólidos resueltos por PhysX.
    /// </summary>
    public static class ContactCollisionUtility
    {
        public static bool AreCollidersTouching(
            Collider sourceCollider,
            Collider targetCollider,
            out Vector3 contactPoint,
            out Vector3 hitDirection,
            float skinTolerance = 0.05f)
        {
            contactPoint = Vector3.zero;
            hitDirection = Vector3.forward;

            if (sourceCollider == null || targetCollider == null)
                return false;
            if (!sourceCollider.enabled || !targetCollider.enabled)
                return false;
            if (!sourceCollider.gameObject.activeInHierarchy || !targetCollider.gameObject.activeInHierarchy)
                return false;

            Transform sourceTransform = sourceCollider.transform;
            Transform targetTransform = targetCollider.transform;

            Vector3 toTarget = targetTransform.position - sourceTransform.position;
            toTarget.y = 0f;
            hitDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : sourceTransform.forward;

            // A conservative broad phase avoids narrow-phase PhysX calls for distant
            // enemies. Keep the vertical tolerance used by the planar contact rule.
            Bounds sourceBounds = sourceCollider.bounds;
            Bounds targetBounds = targetCollider.bounds;
            float tolerance = Mathf.Abs(skinTolerance);
            Vector3 gap = sourceBounds.center - targetBounds.center;
            Vector3 extent = sourceBounds.extents + targetBounds.extents;
            if (Mathf.Abs(gap.x) > extent.x + tolerance ||
                Mathf.Abs(gap.z) > extent.z + tolerance ||
                Mathf.Abs(gap.y) > extent.y + Mathf.Max(1.25f, tolerance))
                return false;

            // 1. Solapamiento directo entre volúmenes de colisión
            if (Physics.ComputePenetration(
                sourceCollider, sourceTransform.position, sourceTransform.rotation,
                targetCollider, targetTransform.position, targetTransform.rotation,
                out Vector3 penetrationDir, out float penetrationDist) && penetrationDist > 0f)
            {
                contactPoint = targetCollider.ClosestPoint(sourceCollider.bounds.center);
                Vector3 planarDir = new Vector3(-penetrationDir.x, 0f, -penetrationDir.z);
                if (planarDir.sqrMagnitude > 0.0001f)
                    hitDirection = planarDir.normalized;
                return true;
            }

            // 2. Contacto superficial (incluye cuerpos sólidos separados por el contactOffset de PhysX)
            Vector3 closestOnSource = sourceCollider.ClosestPoint(targetCollider.bounds.center);
            Vector3 closestOnTarget = targetCollider.ClosestPoint(closestOnSource);
            Vector3 refinedOnSource = sourceCollider.ClosestPoint(closestOnTarget);

            Vector3 delta = closestOnTarget - refinedOnSource;
            float sqrDist3D = delta.sqrMagnitude;
            float sqrDistXZ = delta.x * delta.x + delta.z * delta.z;
            float maxSqr = skinTolerance * skinTolerance;

            if (sqrDist3D <= maxSqr || (sqrDistXZ <= maxSqr && Mathf.Abs(delta.y) <= 1.25f))
            {
                contactPoint = closestOnTarget;
                return true;
            }

            return false;
        }
    }
}
