using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>Superficie del vertedero. La malla y la locomoción comparten el mismo perfil.</summary>
    public sealed class ArenaSurface : MonoBehaviour
    {
        public const float ReferenceSize = 120f;
        [SerializeField, Min(20f)] private float worldSize = ReferenceSize;
        public static ArenaSurface Active { get; private set; }
        public float WorldSize { get => worldSize; set => worldSize = Mathf.Max(20f, value); }
        public float HorizontalScale => worldSize / ReferenceSize;
        public Collider GroundCollider => GetComponent<Collider>();
        private void OnEnable() => Active = this;
        private void OnDisable() { if (Active == this) Active = null; }

        public static float Profile(float x, float z)
        {
            return Profile(x, z, Active != null ? Active.WorldSize : ReferenceSize);
        }
        // Editor authoring has no active runtime surface; bake with the explicit world size.
        public static float Profile(float x, float z, float size)
        {
            float scale = Mathf.Max(20f, size) / ReferenceSize;
            x /= scale; z /= scale;
            float height = Hill(x,z,-24,3,5.5f,19,22) + Hill(x,z,23,29,4.5f,21,19)
                + Hill(x,z,-12,-39,4,20,18) + Hill(x,z,-37,50,7,24,19)
                + Hill(x,z,46,-8,3.5f,18,23);
            float center = Mathf.SmoothStep(0,1,Mathf.InverseLerp(11,19,new Vector2(x,z).magnitude));
            return height * center;
        }
        public static Vector3 ReferenceToWorld(Vector3 position)
        {
            float scale = Active != null ? Active.HorizontalScale : 1f;
            position.x *= scale; position.z *= scale;
            return position;
        }
        private static float Hill(float x,float z,float cx,float cz,float height,float rx,float rz)
        {
            float r = (x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz);
            return height * Mathf.SmoothStep(0,1,Mathf.Clamp01(1-r));
        }
        public static float Height(Vector3 position) => Active != null ? Profile(position.x,position.z) : 0f;
        public static Vector3 OnGround(Vector3 position, float offset = 0f)
        { position.y = Height(position)+offset; return position; }
        public static Vector3 Normal(Vector3 position)
        {
            if (Active == null) return Vector3.up;
            const float d=0.1f;
            return new Vector3(Profile(position.x-d,position.z)-Profile(position.x+d,position.z),2*d,
                Profile(position.x,position.z-d)-Profile(position.x,position.z+d)).normalized;
        }
        public static float SupportHeight(Vector3 position, float radius)
        {
            float h=Height(position);
            float sample=radius*0.6f, sphereRise=radius*0.2f;
            h=Mathf.Max(h,Mathf.Max(Height(position+Vector3.right*sample),Height(position-Vector3.right*sample))-sphereRise);
            return Mathf.Max(h,Mathf.Max(Height(position+Vector3.forward*sample),Height(position-Vector3.forward*sample))-sphereRise);
        }
        public static void Follow(Rigidbody body, float radius, float rootHeight, float deltaTime)
        {
            if (Active == null || body == null) return;
            // FreezePositionY conserva la altura de la arena plana incluso tras
            // escribir body.position. Liberarla permite apoyar el cuerpo en el relieve.
            body.constraints &= ~RigidbodyConstraints.FreezePositionY;
            Vector3 velocity=body.linearVelocity;
            velocity.y=0f;
            body.linearVelocity=velocity;
            Vector3 position=body.position;
            Vector3 ahead=position + body.linearVelocity*deltaTime;
            position.y=SupportHeight(ahead,radius)+rootHeight;
            body.position=position;
        }
    }
}
