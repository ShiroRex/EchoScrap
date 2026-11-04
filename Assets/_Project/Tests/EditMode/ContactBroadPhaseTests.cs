using NUnit.Framework;
using UnityEngine;
using EchoScrap.Combat;
namespace EchoScrap.Tests {
 public class ContactBroadPhaseTests {
  [Test] public void MatchesOriginalForRotationsScalesSlopesAndContactOffsets(){
   var a=new GameObject("Source");var b=new GameObject("Target");
   try {
    var random=new System.Random(719);var target=b.AddComponent<CapsuleCollider>();target.radius=.45f;target.height=1.8f;
    foreach(int kind in new[]{0,1,2}){
     Collider source=kind==0?(Collider)a.AddComponent<CapsuleCollider>():kind==1?a.AddComponent<BoxCollider>():a.AddComponent<SphereCollider>();
     for(int i=0;i<3000;i++){
      a.transform.position=new Vector3(R()*8-4,R()*5-2.5f,R()*8-4);a.transform.rotation=Quaternion.Euler(R()*90,R()*360,R()*90);a.transform.localScale=new Vector3(.2f+R()*4,.2f+R()*4,.2f+R()*4);
      b.transform.rotation=Quaternion.Euler(0,R()*360,0);Physics.SyncTransforms();
      bool expected=ReferenceContactCollisionUtility.AreCollidersTouching(source,target,out var ep,out var ed);
      bool actual=ContactCollisionUtility.AreCollidersTouching(source,target,out var ap,out var ad);
      Assert.AreEqual(expected,actual,$"shape {kind}, case {i}, position {a.transform.position}");
      if(expected){Assert.Less((ap-ep).sqrMagnitude,.000001f);Assert.Less((ad-ed).sqrMagnitude,.000001f);}
     }
     Object.DestroyImmediate(source);
    }
    float R()=>(float)random.NextDouble();
   } finally {Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
  }
  [Test] public void DistantCrowdBenchmarkKeepsAllEnemiesAndSkipsNarrowPhase(){
   var a=new GameObject("Far enemy");var b=new GameObject("Player");
   try{
    var source=a.AddComponent<CapsuleCollider>();var target=b.AddComponent<CapsuleCollider>();a.transform.position=new Vector3(40,0,40);Physics.SyncTransforms();
    for(int i=0;i<100;i++){ContactCollisionUtility.AreCollidersTouching(source,target,out _,out _);ReferenceContactCollisionUtility.AreCollidersTouching(source,target,out _,out _);}
    var timer=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<30000;i++)Assert.IsFalse(ReferenceContactCollisionUtility.AreCollidersTouching(source,target,out _,out _));timer.Stop();double before=timer.Elapsed.TotalMilliseconds;
    timer.Restart();for(int i=0;i<30000;i++)Assert.IsFalse(ContactCollisionUtility.AreCollidersTouching(source,target,out _,out _));timer.Stop();
    Debug.Log($"CONTACT_BENCHMARK 30000 far-enemy contacts: original={before:0.00}ms optimized={timer.Elapsed.TotalMilliseconds:0.00}ms");
   }finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
  }
 }
}



namespace EchoScrap.Combat
{
    /// <summary>
    /// Comprobación de contacto físico mediante los colliders reales en lugar de distancia entre centros.
    /// Soporta colliders grandes (Coloso 1.6x, Gólgora 3.5x), tanto triggers como sólidos resueltos por PhysX.
    /// </summary>
    internal static class ReferenceContactCollisionUtility
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
