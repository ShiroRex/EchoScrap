using System.Collections;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class GeometryOptimizationTests
    {
        private static int CountTriangles(Mesh mesh)
        {
            int indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
                indices += (int)mesh.GetIndexCount(s);
            return indices / 3;
        }

        private CombatGameBootstrap game;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("CombatArena", LoadSceneMode.Single);
            yield return null;
            game = Object.FindAnyObjectByType<CombatGameBootstrap>();
            if (game != null)
            {
                game.Director.AutoTick = false;
                game.Encounter.AutoTick = false;
                game.ResetRun(false);
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1f;
            if (game != null)
            {
                Scene scene = game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("GeometryOptCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }

        [Test]
        public void TreeWallStraightInstances_PromoteLod1AndLod2_Without1MTrisLod0()
        {
            int count = 0;
            LODGroup[] groups = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include);
            foreach (LODGroup group in groups)
            {
                bool isTreeWallStraight = false;
                foreach (LOD l in group.GetLODs())
                {
                    foreach (Renderer r in l.renderers)
                    {
                        if (r != null && r.name.StartsWith("TreeWallStraight_LOD", System.StringComparison.Ordinal))
                        {
                            isTreeWallStraight = true;
                            break;
                        }
                    }
                    if (isTreeWallStraight) break;
                }
                if (!isTreeWallStraight)
                    continue;
                count++;
                LOD[] lods = group.GetLODs();
                Assert.AreEqual(2, lods.Length, group.name);
                Assert.AreEqual(0.12f, lods[0].screenRelativeTransitionHeight, 0.002f, group.name);
                Assert.AreEqual(0.005f, lods[1].screenRelativeTransitionHeight, 0.001f, group.name);
                foreach (LOD lod in lods)
                {
                    foreach (Renderer r in lod.renderers)
                    {
                        Assert.IsNotNull(r, group.name);
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        Assert.IsNotNull(mf);
                        Assert.IsNotNull(mf.sharedMesh);
                        Assert.LessOrEqual(CountTriangles(mf.sharedMesh), 45000, r.name);
                        Assert.IsFalse(r.name.Contains("LOD0"), r.name);
                    }
                }
            }
            Assert.Greater(count, 10, "Expected TreeWallStraight perimeter segments in CombatArena.");
        }

        [Test]
        public void ArenaPropsAndLava_RetainMeshesCollidersAndUvsAfterDecimation()
        {
            MeshCollider[] colliders = Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include);
            Assert.Greater(colliders.Length, 100);
            foreach (MeshCollider mc in colliders)
            {
                Assert.IsNotNull(mc.sharedMesh, $"Missing MeshCollider.sharedMesh on {mc.name}");
                Assert.Greater(mc.sharedMesh.vertexCount, 0, $"Empty collider mesh on {mc.name}");
            }

            GameObject world = GameObject.Find("Mundo3D_EchoScrap");
            Assert.IsNotNull(world);
            int checkedProps = 0;
            int splitColliderProps = 0;
            foreach (MeshFilter mf in world.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.Contains("Floor") || mf.name == "PortalVortex" || mf.name == "Emblema_triangular")
                    continue;
                Assert.IsNotNull(mf.sharedMesh, $"Missing MeshFilter.sharedMesh on {mf.name}");
                Assert.Greater(mf.sharedMesh.vertexCount, 0, $"Empty mesh on {mf.name}");
                int tris = CountTriangles(mf.sharedMesh);
                Assert.Greater(tris, 10, mf.name);
                Assert.LessOrEqual(tris, 20000, $"Unexpectedly high tri count on {mf.name}: {tris}");
                Assert.IsTrue(mf.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0), $"Missing UVs on {mf.name}");
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc != null && mc.sharedMesh != null)
                {
                    int colTris = CountTriangles(mc.sharedMesh);
                    Assert.GreaterOrEqual(colTris, tris, $"Collider should keep original detail on {mf.name}");
                    if (mc.sharedMesh != mf.sharedMesh)
                        splitColliderProps++;
                }
                checkedProps++;
            }
            Assert.Greater(checkedProps, 100);
            Assert.Greater(splitColliderProps, 200, "Expected decimated arena props to preserve distinct original collider meshes.");
        }
    }
}
