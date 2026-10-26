using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>Apoyo en los peldaños reales del modelo, conservando barandillas sólidas.</summary>
    public sealed class WalkableStairs : MonoBehaviour
    {
        public static WalkableStairs Active { get; private set; }
        public MeshCollider Surface;
        private void OnEnable() { Active=this; if(Surface==null) Surface=GetComponentInChildren<MeshCollider>(); }
        private void OnDisable() { if(Active==this) Active=null; }
        public bool TryHeight(Vector3 position,out float height)
        {
            height=0;
            if(Surface==null) return false;
            var bounds=Surface.bounds;
            if(Mathf.Abs(position.x-bounds.center.x)>bounds.size.x*0.36f) return false;
            if(position.x<bounds.min.x || position.x>bounds.max.x || position.z<bounds.min.z || position.z>bounds.max.z) return false;
            if(!Surface.Raycast(new Ray(new Vector3(position.x,bounds.max.y+2,position.z),Vector3.down),out var hit,bounds.size.y+4)) return false;
            height=hit.point.y; return true;
        }
    }
}
