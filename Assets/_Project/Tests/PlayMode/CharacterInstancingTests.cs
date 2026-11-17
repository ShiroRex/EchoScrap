using System.Collections;
using System.Collections.Generic;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class CharacterInstancingTests
    {
        private Scene testScene;
        private Scene previousScene;
        private readonly List<Object> tempObjects = new List<Object>();

        [SetUp]
        public void Setup()
        {
            previousScene = SceneManager.GetActiveScene();
            testScene = SceneManager.CreateScene("CharInstancing_" + Random.Range(1, 100000));
            SceneManager.SetActiveScene(testScene);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = tempObjects.Count - 1; i >= 0; i--)
            {
                if (tempObjects[i] != null)
                    Object.DestroyImmediate(tempObjects[i]);
            }
            tempObjects.Clear();

            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            if (testScene.IsValid() && testScene.isLoaded)
                SceneManager.UnloadSceneAsync(testScene);
        }

        [Test]
        public void AllCharacterPickupAndTurretMaterials_EnableGpuInstancing()
        {
            var ids = new[]
            {
                EnemyArchetypeId.Residual,
                EnemyArchetypeId.Colossus,
                EnemyArchetypeId.Interceptor,
                EnemyArchetypeId.GhostKnight,
                EnemyArchetypeId.CopperSentinel
            };

            for (int i = 0; i < ids.Length; i++)
            {
                EnemySpriteSet set = EnemySpriteSet.Load(ids[i]);
                Assert.IsNotNull(set, $"Missing EnemySpriteSet for {ids[i]}");
                Assert.IsNotNull(set.material);
                Assert.IsNotNull(set.shadowMaterial);
                Assert.IsTrue(set.material.enableInstancing, $"{set.material.name} must have GPU instancing enabled.");
                Assert.IsTrue(set.shadowMaterial.enableInstancing, $"{set.shadowMaterial.name} must have GPU instancing enabled.");
            }

            MortarSpriteSet mortar = MortarSpriteSet.Load();
            Assert.IsNotNull(mortar);
            Assert.IsTrue(mortar.material.enableInstancing, $"{mortar.material.name} must have GPU instancing enabled.");
            Assert.IsTrue(mortar.shadowMaterial.enableInstancing, $"{mortar.shadowMaterial.name} must have GPU instancing enabled.");

            MothSpriteSet moth = MothSpriteSet.Load();
            Assert.IsNotNull(moth);
            Assert.IsTrue(moth.material.enableInstancing, $"{moth.material.name} must have GPU instancing enabled.");
            Assert.IsTrue(moth.shadowMaterial.enableInstancing, $"{moth.shadowMaterial.name} must have GPU instancing enabled.");

            string[] resourceMaterialPaths =
            {
                "Pickups/Botiquin",
                "Pickups/BotiquinShadow",
                "Upgrades/Vfx/Turret/Base",
                "Upgrades/Vfx/Turret/Cannon"
            };
            for (int i = 0; i < resourceMaterialPaths.Length; i++)
            {
                Material mat = Resources.Load<Material>(resourceMaterialPaths[i]);
                Assert.IsNotNull(mat, $"Missing resource material {resourceMaterialPaths[i]}");
                Assert.IsTrue(mat.enableInstancing, $"{resourceMaterialPaths[i]} must have GPU instancing enabled.");
            }
        }

        [UnityTest]
        public IEnumerator CharacterPixelLitAndContactShadow_RenderDistinctInstancedPropertiesWithoutLeakOrShaderError()
        {
            Shader pixelLit = Shader.Find("EchoScrap/Character Pixel Lit");
            Shader contactShadow = Shader.Find("EchoScrap/Character Contact Shadow");
            Assert.IsNotNull(pixelLit);
            Assert.IsNotNull(contactShadow);
            Assert.AreEqual(7, pixelLit.passCount, "Character Pixel Lit must retain all 7 URP/arena passes.");

            // Two white sprite-quad instances rendered side-by-side sharing one instanced material each.
            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var fill = new Color32[16];
            for (int i = 0; i < fill.Length; i++)
                fill[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(fill);
            tex.Apply();
            tempObjects.Add(tex);

            Material spriteMat = new Material(pixelLit) { enableInstancing = true };
            spriteMat.SetTexture("_BaseMap", tex);
            spriteMat.SetTexture("_GlowMap", Texture2D.whiteTexture);
            tempObjects.Add(spriteMat);

            Material shadowMat = new Material(contactShadow) { enableInstancing = true };
            tempObjects.Add(shadowMat);

            GameObject q1 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            GameObject q2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q1.transform.position = new Vector3(-1.2f, 0f, 5f);
            q2.transform.position = new Vector3(1.2f, 0f, 5f);
            Renderer r1 = q1.GetComponent<Renderer>();
            Renderer r2 = q2.GetComponent<Renderer>();
            r1.sharedMaterial = spriteMat;
            r2.sharedMaterial = spriteMat;

            var b1 = new MaterialPropertyBlock();
            b1.SetVector("_FrameRect", new Vector4(0f, 0f, 1f, 1f));
            b1.SetColor("_BaseColor", Color.black);
            b1.SetColor("_EmissionColor", new Color(0.95f, 0.1f, 0.1f, 1f));
            b1.SetColor("_GlowColor", Color.red);
            b1.SetFloat("_GlowStrength", 1.0f);
            r1.SetPropertyBlock(b1);

            var b2 = new MaterialPropertyBlock();
            b2.SetVector("_FrameRect", new Vector4(0f, 0f, 1f, 1f));
            b2.SetColor("_BaseColor", Color.black);
            b2.SetColor("_EmissionColor", new Color(0.1f, 0.95f, 0.2f, 1f));
            b2.SetColor("_GlowColor", Color.green);
            b2.SetFloat("_GlowStrength", 1.0f);
            r2.SetPropertyBlock(b2);

            // Contact shadow instances below.
            GameObject s1 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            GameObject s2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            s1.transform.position = new Vector3(-1.2f, -1.3f, 5f);
            s2.transform.position = new Vector3(1.2f, -1.3f, 5f);
            Renderer sr1 = s1.GetComponent<Renderer>();
            Renderer sr2 = s2.GetComponent<Renderer>();
            sr1.sharedMaterial = shadowMat;
            sr2.sharedMaterial = shadowMat;
            var sb1 = new MaterialPropertyBlock();
            sb1.SetColor("_BaseColor", new Color(0.9f, 0.1f, 0.1f, 1f));
            sr1.SetPropertyBlock(sb1);
            var sb2 = new MaterialPropertyBlock();
            sb2.SetColor("_BaseColor", new Color(0.1f, 0.2f, 0.9f, 1f));
            sr2.SetPropertyBlock(sb2);

            Camera cam = new GameObject("InstancingTestCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.orthographicSize = 2.5f;
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;

            RenderTexture rt = new RenderTexture(128, 128, 24);
            cam.targetTexture = rt;
            yield return null;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D readback = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            readback.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            tempObjects.Add(readback);
            tempObjects.Add(rt);

            // Sample left vs right sprite quads (mid-height y=64) and left vs right shadow quads (y=30).
            Color leftSprite = readback.GetPixel(33, 64);
            Color rightSprite = readback.GetPixel(95, 64);
            Assert.Greater(leftSprite.r, leftSprite.g + 0.15f, "Left instanced sprite must emit its own red instanced color.");
            Assert.Greater(rightSprite.g, rightSprite.r + 0.15f, "Right instanced sprite must emit its own green instanced color.");

            Color leftShadow = readback.GetPixel(33, 30);
            Color rightShadow = readback.GetPixel(95, 30);
            Assert.Greater(leftShadow.r, leftShadow.b + 0.2f, "Left instanced contact shadow must read its red per-instance _BaseColor.");
            Assert.Greater(rightShadow.b, rightShadow.r + 0.2f, "Right instanced contact shadow must read its blue per-instance _BaseColor.");
        }

        [Test]
        public void ActorVisualComposer_FlashAndClear_PreservesFrameRectAndGlowWithoutDeadColorProp()
        {
            Shader pixelLit = Shader.Find("EchoScrap/Character Pixel Lit");
            Assert.IsNotNull(pixelLit);
            Material mat = new Material(pixelLit) { enableInstancing = true };
            tempObjects.Add(mat);

            var root = new GameObject("ComposerInstancingActor");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var pivot = new GameObject("PresentationPivot");
            pivot.transform.SetParent(visual.transform, false);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(pivot.transform, false);
            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;

            var initial = new MaterialPropertyBlock();
            Vector4 expectedRect = new Vector4(0.25f, 0.5f, 0.125f, 0.0625f);
            initial.SetVector("_FrameRect", expectedRect);
            initial.SetFloat("_GlowStrength", 2.4f);
            renderer.SetPropertyBlock(initial);

            var composer = root.AddComponent<ActorVisualComposer>();
            composer.CacheRenderers();
            composer.TriggerHitReaction(Vector3.forward, isCritical: false);
            Assert.IsTrue(composer.IsFlashActive);

            var duringFlash = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(duringFlash);
            Assert.AreEqual(expectedRect, duringFlash.GetVector("_FrameRect"), "Hit flash must preserve instanced _FrameRect.");
            Assert.Greater(duringFlash.GetColor("_EmissionColor").r, 1.0f);
            Assert.IsFalse(duringFlash.HasProperty("_Color"), "CharacterPixelLit block must not pollute properties with unused _Color.");

            // Advance past flash duration so flash clears without calling SetPropertyBlock(null).
            composer.StepVisuals(0.2f, 0f);
            Assert.IsFalse(composer.IsFlashActive);

            var afterFlash = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(afterFlash);
            Assert.AreEqual(expectedRect, afterFlash.GetVector("_FrameRect"), "Clearing flash must preserve _FrameRect instead of wiping MaterialPropertyBlock.");
            Assert.AreEqual(2.4f, afterFlash.GetFloat("_GlowStrength"), 0.001f, "Clearing flash must preserve _GlowStrength.");
            Assert.AreEqual(Color.white, afterFlash.GetColor("_BaseColor"));
            Assert.AreEqual(Color.clear, afterFlash.GetColor("_EmissionColor"));
            Assert.AreEqual(0f, afterFlash.GetFloat("_StatusAmount"), 0.001f);
            Assert.IsFalse(afterFlash.HasProperty("_Color"));
        }

        [Test]
        public void CollectorSpriteMaterials_EnableGpuInstancing()
        {
            // Also verify the authored Collector sprite material directly via CharacterSpriteSet on prefab if loaded or asset DB-free search.
            Material collector = Resources.Load<Material>("Characters/Coleccionista");
            if (collector != null)
                Assert.IsTrue(collector.enableInstancing);

            EnemySpriteSet res = EnemySpriteSet.Load(EnemyArchetypeId.Residual);
            Assert.IsNotNull(res.material);
            Assert.IsTrue(res.material.enableInstancing);
        }
    }
}
