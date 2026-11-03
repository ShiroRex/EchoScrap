using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Fixed tripod, 32 cannon headings, lit normal maps and a shared muzzle pivot.</summary>
    public sealed class ScrapTurretView : MonoBehaviour
    {
        [Serializable] private sealed class ArtData { public Vector2[] muzzles; }
        private static ArtData art;
        private static readonly int FrameRect = Shader.PropertyToID("_FrameRect");
        private MaterialPropertyBlock properties;
        private Transform cannon;
        private MeshRenderer baseRenderer, cannonRenderer;
        private float recoil;
        public int DirectionFrame { get; private set; }
        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public MeshRenderer BaseRenderer => baseRenderer;
        public MeshRenderer CannonRenderer => cannonRenderer;
        public Vector3 MuzzleWorldPosition
        {
            get
            {
                Vector2 p = art.muzzles[DirectionFrame];
                return cannon.TransformPoint(new Vector3((p.x-32f)/32f,(62f-p.y)/32f,-.025f));
            }
        }
        public void Initialize(Mesh quad)
        {
            if(cannon!=null)return;
            properties=new MaterialPropertyBlock();
            if(art==null)art=JsonUtility.FromJson<ArtData>(Resources.Load<TextAsset>("Upgrades/Vfx/Turret/Art").text);
            baseRenderer=Part("Fixed tripod",quad,"Base");
            cannonRenderer=Part("Rotating cannon",quad,"Cannon");
            cannon=cannonRenderer.transform;cannon.localPosition=Vector3.back*.012f;
            ResetPose();
        }
        private MeshRenderer Part(string label,Mesh quad,string material)
        {
            var part=new GameObject(label);part.transform.SetParent(transform,false);
            part.AddComponent<MeshFilter>().sharedMesh=quad;
            var renderer=part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=Resources.Load<Material>("Upgrades/Vfx/Turret/"+material);
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            return renderer;
        }
        public void ResetPose()
        {
            recoil=0;AimDirection=Vector3.forward;DirectionFrame=-1;AimAt(transform.position+Vector3.forward);Step(0);
        }
        public void AimAt(Vector3 target)
        {
            Vector3 direction=Vector3.ProjectOnPlane(target-transform.position,Vector3.up);
            if(direction.sqrMagnitude<.0001f)return;
            AimDirection=direction.normalized;
            float angle=Mathf.Repeat(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,360);
            int frame=Mathf.RoundToInt(angle/360f*32)%32;
            if(frame==DirectionFrame)return;
            DirectionFrame=frame;
            properties.SetVector(FrameRect,new Vector4(frame%8/8f,1f-(frame/8+1)/4f,1f/8f,1f/4f));
            cannonRenderer.SetPropertyBlock(properties);
        }
        public void Fire() => recoil=.11f;
        public void Step(float deltaTime)
        {
            recoil=Mathf.Max(0,recoil-deltaTime);
            Vector3 projected=new Vector3(AimDirection.x,AimDirection.z*.574f,0);
            cannon.localPosition=Vector3.back*.012f-projected*(.055f*recoil/.11f);
        }
    }
}
