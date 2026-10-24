using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Explosion-owned animation, independent of the enemy already returned to its pool.</summary>
    public sealed class MothBurstSprite : MonoBehaviour
    {
        private MothSpriteSet sprites;
        private MeshRenderer pixels;
        private MaterialPropertyBlock properties;
        private float age;
        public static MothBurstSprite Create(Transform parent)
        {
            var go = new GameObject("Pooled Reactor Burst"); go.transform.SetParent(parent, false);
            var burst = go.AddComponent<MothBurstSprite>();
            burst.sprites = MothSpriteSet.Load();
            if (burst.sprites != null)
                burst.pixels = MothSpriteView.CreateRenderer("Reactor Burst Pixels", go.transform, burst.sprites.mesh, burst.sprites.material);
            burst.properties = new MaterialPropertyBlock();
            go.SetActive(false); return burst;
        }
        public void Play(Vector3 origin)
        {
            if (pixels == null) return;
            age = 0f; transform.SetPositionAndRotation(origin + Vector3.up * 0.65f, Quaternion.identity);
            transform.localScale = Vector3.one * 0.85f; gameObject.SetActive(true); Step(0f);
        }
        public void Step(float delta)
        {
            age += delta;
            if (age >= sprites.Burst.Duration) { gameObject.SetActive(false); return; }
            properties.SetVector("_FrameRect", sprites.Burst.frames[sprites.Burst.FrameAt(age)].uvRect);
            properties.SetFloat("_GlowStrength", 2.2f);
            pixels.SetPropertyBlock(properties);
        }
    }
}
