using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EchoScrap.Combat;
using EchoScrap.Core;
using EchoScrap.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EchoScrap.Tests
{
    public sealed class TerrainCollisionTests
    {
        private CombatGameBootstrap game;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private readonly List<InputDevice> disabledDevices=new List<InputDevice>();
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            foreach(var device in new List<InputDevice>(InputSystem.devices))
                if(device.enabled) { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            keyboard=InputSystem.AddDevice<Keyboard>();
            Time.timeScale=1;
            yield return SceneManager.LoadSceneAsync("CombatArena"); yield return null;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick=false; game.Encounter.AutoTick=false; game.ResetRun(false); game.Weapon.enabled=false;
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus",true);
            game.Session.ResumeFromFocusLoss();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.timeScale=1;
            if(keyboard!=null) InputSystem.RemoveDevice(keyboard);
            foreach(var device in disabledDevices) if(device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear();
            InputSystem.settings.backgroundBehavior=background; InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            if(game!=null)
            {
                var scene=game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("TerrainCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }
        [Test] public void ObstaclesUseCompoundWorldAlignedBoxes_AndTerrainMatchesItsExpandedHeightProfile()
        {
            var world=GameObject.Find("Mundo3D_EchoScrap");
            Assert.AreEqual(0,world.GetComponentsInChildren<MeshCollider>(true).Length,"Decorations use stable boxes rather than triangle collisions.");
            var colliders=world.GetComponentsInChildren<Collider>(true);
            Assert.Greater(colliders.Length,100);
            foreach(var collider in colliders)
            {
                Assert.IsInstanceOf<BoxCollider>(collider,collider.name);
                Assert.IsFalse(collider.isTrigger,collider.name);
                Assert.Less(Quaternion.Angle(collider.transform.rotation,Quaternion.identity),0.01f,collider.name);
                Transform parent=collider.transform;
                while(parent!=null && parent.name!="CollisionBoxes") parent=parent.parent;
                Assert.IsNotNull(parent,"Each obstacle box belongs to its authored CollisionBoxes compound: "+collider.name);
            }
            Assert.AreSame(game.ArenaGround,ArenaSurface.Active.GroundCollider);
            Assert.AreEqual(200f,ArenaSurface.Active.WorldSize,0.001f);
            Assert.AreEqual(5f/3f,ArenaSurface.Active.HorizontalScale,0.001f);
            Assert.AreEqual(200f,game.ArenaGround.bounds.size.x,0.001f);
            Assert.AreEqual(200f,game.ArenaGround.bounds.size.z,0.001f);
            for(int x=-95;x<=95;x+=10) for(int z=-95;z<=95;z+=10)
            {
                Assert.IsTrue(game.ArenaGround.Raycast(new Ray(new Vector3(x,25,z),Vector3.down),out RaycastHit hit,30));
                Assert.AreEqual(ArenaSurface.Profile(x,z),hit.point.y,0.015f);
            }
            Assert.Greater(ArenaSurface.Height(ArenaSurface.ReferenceToWorld(new Vector3(-24,0,3))),5f);
        }
        [Test] public void OnlyTheTiniestLowRocksLoseCollision_AndAllFourBordersAreSolidAndInvisible()
        {
            var world=GameObject.Find("Mundo3D_EchoScrap");
            int tiny=0,solid=0;
            foreach(var anchor in world.GetComponentsInChildren<Transform>(true))
            {
                if(!anchor.name.StartsWith("roca_baja_")) continue;
                var renderers=anchor.GetComponentsInChildren<Renderer>();
                Assert.Greater(renderers.Length,0,anchor.name);
                Bounds bounds=renderers[0].bounds;
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                bool isTiny=Mathf.Max(bounds.size.x,bounds.size.z)<=1.65f && bounds.size.y<=1f;
                var colliders=anchor.GetComponentsInChildren<Collider>(true);
                if(isTiny) { tiny++; Assert.AreEqual(0,colliders.Length,anchor.name); }
                else { solid++; Assert.Greater(colliders.Length,0,anchor.name); }
            }
            Assert.Greater(tiny,0,"There must be small decorative stones to walk through.");
            Assert.Greater(solid,0,"Larger low rocks must keep collision.");
            foreach(string edge in new[]{"North","South","East","West"})
            {
                var boundary=GameObject.Find("Arena_120m/ArenaBounds/Border_"+edge);
                Assert.IsNotNull(boundary,edge);
                var collider=boundary.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider,edge); Assert.IsFalse(collider.isTrigger,edge); Assert.IsTrue(collider.enabled,edge);
                foreach(var renderer in boundary.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(renderer.enabled,edge);
                bool alongZ=edge=="North" || edge=="South";
                float coordinate=alongZ?collider.bounds.center.z:collider.bounds.center.x;
                Assert.AreEqual(edge=="North" || edge=="East"?101f:-101f,coordinate,0.001f,edge);
                Assert.AreEqual(2f,alongZ?collider.bounds.size.z:collider.bounds.size.x,0.001f,edge);
                Assert.GreaterOrEqual(alongZ?collider.bounds.size.x:collider.bounds.size.z,200f,edge);
                Assert.AreEqual(15f,collider.bounds.center.y,0.001f,edge);
                Assert.AreEqual(50f,collider.bounds.size.y,0.001f,edge);
            }
        }
        [UnityTest] public IEnumerator PlayerClimbsAndDescendsActualHill_EnemyFollowsSurface()
        {
            var body=game.Player.GetComponent<Rigidbody>();
            body.interpolation=RigidbodyInterpolation.None;
            Assert.IsTrue(TryFindClearHillLane(out Vector3 start,out Vector3 summit),"The expanded hill must retain a clear walking route between structures.");
            body.position=ArenaSurface.OnGround(start,1f); body.linearVelocity=Vector3.zero;
            game.Player.transform.position=body.position; Physics.SyncTransforms();
            int steps=Mathf.CeilToInt(((Mathf.Abs(start.x-summit.x)+1f)/game.Motor.Settings.MoveSpeed+0.15f)/Time.fixedDeltaTime);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A)); yield return null;
            for(int i=0;i<steps;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Less(body.position.x,Mathf.Lerp(start.x,summit.x,0.8f),"Real input must move up the larger hill.");
            Assert.Greater(body.position.y,4,"Player must rise with terrain: " + body.position + " ground=" + ArenaSurface.Height(body.position) + " active=" + ArenaSurface.Active);
            Assert.AreEqual(ArenaSurface.SupportHeight(body.position,0.45f)+1,body.position.y,0.25f);
            var enemy=game.Enemies.Spawn(EnemyArchetypeId.Residual,summit);
            Assert.Greater(enemy.transform.position.y,4f);
            yield return Capture("Relieve",ArenaSurface.ReferenceToWorld(new Vector3(-4,18,-19)),ArenaSurface.ReferenceToWorld(new Vector3(-23,3,3)));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D)); yield return null;
            for(int i=0;i<steps;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Less(body.position.y,2,"Downhill must not leave the player floating.");
        }
        [UnityTest] public IEnumerator RealMovementAndDashCannotCrossAnyArenaBorder()
        {
            GameObject.Find("Mundo3D_EchoScrap").SetActive(false);
            var body=game.Player.GetComponent<Rigidbody>();
            body.interpolation=RigidbodyInterpolation.None;
            var keys=new[]{Key.W,Key.S,Key.D,Key.A};
            var directions=new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left};
            for(int edge=0;edge<keys.Length;edge++) foreach(bool dash in new[]{false,true})
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
                game.Motor.ResetDashForSession();
                body.position=ArenaSurface.OnGround(directions[edge]*(dash?98.5f:97.5f),1f);
                game.Player.transform.position=body.position; body.linearVelocity=Vector3.zero;
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard,dash?new KeyboardState(keys[edge],Key.Space):new KeyboardState(keys[edge])); yield return null;
                for(int i=0;i<(dash?12:40);i++) yield return new WaitForFixedUpdate();
                float coordinate=Vector3.Dot(body.position,directions[edge]);
                Assert.That(coordinate,Is.InRange(99.25f,99.65f),$"{keys[edge]} dash={dash}: character must meet the inner face and stay inside.");
                if(dash) Assert.IsTrue(game.Motor.WasBlockedByWallThisDash,"The border must also block the player's dash layer.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            }
        }
        [UnityTest] public IEnumerator RealFbxTreeOverheadPassageSurvivesBoxFittingAndPlayerWalksThroughIt()
        {
            Transform world=GameObject.Find("Mundo3D_EchoScrap").transform;
            CapsuleCollider capsule=game.Player.GetComponent<CapsuleCollider>();
            var contacts=new Collider[128];
            Vector3 start=Vector3.zero,end=Vector3.zero;
            Bounds treeBounds=new Bounds();
            string treeName=null;
            // Select a passage from the visible FBX before inspecting the fitted collision.
            // A failed box comparison must fail this test, never select a different route.
            foreach(Transform anchor in world.GetComponentsInChildren<Transform>()
                .Where(t=>t.name.StartsWith("arbol_seco_")).OrderBy(t=>t.name))
            {
                var renderers=anchor.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0) continue;
                Bounds bounds=renderers[0].bounds;
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var boxes=anchor.GetComponentsInChildren<BoxCollider>();
                Assert.Greater(boxes.Length,0,anchor.name+" needs its fitted solid trunk/branches.");
                var enabled=boxes.Select(c=>c.enabled).ToArray();
                var actualMesh=new List<MeshCollider>();
                bool found=false;
                try
                {
                    foreach(var box in boxes) box.enabled=false;
                    foreach(var filter in anchor.GetComponentsInChildren<MeshFilter>())
                    {
                        if(filter.sharedMesh==null) continue;
                        var collider=filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh=filter.sharedMesh;
                        collider.convex=false;
                        actualMesh.Add(collider);
                    }
                    Physics.SyncTransforms();
                    // Leave one diagonal voxel's clearance in the FBX reference in addition
                    // to the 0.3 m safety margin required after conservative voxel fitting.
                    float voxel=Mathf.Max(0.3f,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z))/64f);
                    found=FindVisibleOverheadPassage(bounds,actualMesh,world,capsule,0.3f+Mathf.Sqrt(3f)*voxel,contacts,out start,out end);
                }
                finally
                {
                    foreach(var collider in actualMesh) Object.DestroyImmediate(collider);
                    for(int i=0;i<boxes.Length;i++) boxes[i].enabled=enabled[i];
                    Physics.SyncTransforms();
                }
                if(!found) continue;
                treeName=anchor.name; treeBounds=bounds;
                break;
            }
            Assert.IsNotNull(treeName,"At least one actual FBX tree must expose a walkable passage beneath a visible branch, inside its overall bounds.");
            Assert.IsTrue(IsWorldPathClear(start,end,world,capsule,0.3f,contacts),
                treeName+" fitted boxes must preserve the same FBX passage with 0.3 m clearance; a single enclosing AABB would close it.");
            Debug.Log($"[Real FBX passage] {treeName} start={start} end={end} bounds={treeBounds}");

            var body=game.Player.GetComponent<Rigidbody>();
            body.interpolation=RigidbodyInterpolation.None;
            body.position=ArenaSurface.OnGround(start,1f); game.Player.transform.position=body.position;
            body.linearVelocity=Vector3.zero; Physics.SyncTransforms();
            Vector3 direction=(end-start).normalized;
            Key key=Mathf.Abs(direction.x)>0.5f?Key.D:Key.W;
            float distance=Vector3.Distance(start,end);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(key)); yield return null;
            int steps=Mathf.CeilToInt((distance/game.Motor.Settings.MoveSpeed+0.2f)/Time.fixedDeltaTime);
            for(int i=0;i<steps;i++)
            {
                yield return new WaitForFixedUpdate();
                if(Vector3.Dot(body.position-start,direction)>=distance-0.15f) break;
            }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.GreaterOrEqual(Vector3.Dot(body.position-start,direction),distance-0.3f,
                treeName+" must be crossed by the real motor after all fitted boxes are restored.");
            Assert.AreEqual(ArenaSurface.SupportHeight(body.position,0.45f)+1f,body.position.y,0.25f,
                "Passing under a branch must not lift the player onto the model.");
            Vector3 midpoint=(start+end)*0.5f; midpoint.y=ArenaSurface.Height(midpoint)+1f;
            yield return Capture("PasoBajoArbol",midpoint+new Vector3(9f,10f,-13f),midpoint+Vector3.up);
        }
        [UnityTest] public IEnumerator SpawnAreaAllowsMovementInAllDirections()
        {
            var body=game.Player.GetComponent<Rigidbody>();
            foreach(var key in new[]{Key.W,Key.A,Key.S,Key.D})
            {
                body.position=Vector3.up; body.linearVelocity=Vector3.zero;
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(key)); yield return null;
                for(int i=0;i<25;i++) yield return new WaitForFixedUpdate();
                Assert.Greater(Vector3.Distance(Vector3.up,body.position),2f,"Blocked at spawn: "+key);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            }
        }
        [Test] public void FlashlightCannotTargetOrDamageThroughSolidGeometry()
        {
            var enemy=game.Enemies.Spawn(EnemyArchetypeId.Residual,new Vector3(0,1,5));
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position=new Vector3(0,2,2.5f); wall.transform.localScale=new Vector3(4,4,0.4f);
            Physics.SyncTransforms();
            Assert.IsNull(game.Weapon.FindNearestEnemyTarget(game.Weapon.OriginPosition,12,out _));
            float health=enemy.CurrentHealth; game.Weapon.StepWeapon(0.8f);
            Assert.AreEqual(health,enemy.CurrentHealth);
            Object.DestroyImmediate(wall); Physics.SyncTransforms();
            Assert.AreSame(enemy,game.Weapon.FindNearestEnemyTarget(game.Weapon.OriginPosition,12,out _));
            game.Weapon.StepWeapon(0.8f); Assert.Less(enemy.CurrentHealth,health);
        }
        [TestCase(8f,true)] [TestCase(0.5f,false)]
        public void ProjectilesStopAtWalls_AndCanClearLowCover(float wallHeight,bool shouldStop)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position=new Vector3(0,wallHeight/2,4); wall.transform.localScale=new Vector3(6,wallHeight,0.3f);
            game.Player.GetComponent<Rigidbody>().position=new Vector3(0,1,8);
            game.Player.transform.position=new Vector3(0,1,8); Physics.SyncTransforms();
            var spec=CorrosivePuddlePool.ProjectileSpec.CreateMortarDefault(99,new Vector3(0,2,0),new Vector3(0,0,8));
            Assert.IsTrue(game.Hazards.TryLaunchParabolicProjectile(in spec));
            game.Hazards.StepPool(0.7f);
            Assert.AreEqual(0,game.Hazards.ActiveProjectileCount);
            var puddle=game.Hazards.GetPuddleBySequenceId(1);
            if(shouldStop) { Assert.Less(puddle.CenterXZ.z,4); Assert.AreEqual(game.Player.MaxHealth,game.Player.CurrentHealth); }
            else Assert.Greater(puddle.CenterXZ.z,7.5f);
        }
        [UnityTest] public IEnumerator LavaDamagesBothTeamsAndPausesWithTheGame()
        {
            var zones=Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None);
            Assert.AreEqual(3,zones.Length);
            foreach(var zone in zones) zone.AutoTick=false;
            var lava=zones[0];
            Assert.IsTrue(lava.GetComponentInChildren<Renderer>().sharedMaterial.IsKeywordEnabled("_EMISSION"));
            Vector3 center=ArenaSurface.OnGround(lava.transform.position,1f);
            var body=game.Player.GetComponent<Rigidbody>(); body.position=center; game.Player.transform.position=center;
            var enemy=game.Enemies.Spawn(EnemyArchetypeId.Residual,center+Vector3.right*0.7f);
            float playerHp=game.Player.CurrentHealth, enemyHp=enemy.CurrentHealth;
            lava.StepHazard(0.01f);
            Assert.AreEqual(playerHp-5,game.Player.CurrentHealth,0.001f);
            Assert.AreEqual(enemyHp-5,enemy.CurrentHealth,0.001f);
            lava.StepHazard(0.2f);
            Assert.AreEqual(playerHp-5,game.Player.CurrentHealth,0.001f,"Not damage every frame.");
            game.Player.StepTimers(1.1f); lava.StepHazard(1.1f);
            Assert.AreEqual(playerHp-10,game.Player.CurrentHealth,0.001f);
            Assert.AreEqual(enemyHp-10,enemy.CurrentHealth,0.001f);
            game.Session.SetManualPause(true); float phase=lava.AnimationTime;
            lava.StepHazard(10f);
            Assert.AreEqual(phase,lava.AnimationTime);
            game.Session.SetManualPause(false);
            body.position=Vector3.up; game.Player.transform.position=Vector3.up;
            game.Player.StepTimers(2); lava.StepHazard(2);
            Assert.AreEqual(playerHp-10,game.Player.CurrentHealth,0.001f,"Safe outside lava.");
            yield return Capture("Lava",lava.transform.position+new Vector3(7,10,-9),lava.transform.position);
            game.ResetRun(false);
            Assert.AreEqual(0,lava.AnimationTime);
        }
        private static bool TryFindClearHillLane(out Vector3 start,out Vector3 summit)
        {
            Transform world=GameObject.Find("Mundo3D_EchoScrap").transform;
            var contacts=new Collider[64];
            foreach(float z in new[]{3f,0f,6f,-3f,9f,-6f,12f,-9f})
            {
                start=ArenaSurface.ReferenceToWorld(new Vector3(-9f,0f,z));
                summit=ArenaSurface.ReferenceToWorld(new Vector3(-25f,0f,z));
                if(ArenaSurface.Height(start)>0.8f || ArenaSurface.Height(summit)<3.5f) continue;
                bool clear=true;
                for(int i=0;i<=64 && clear;i++)
                {
                    Vector3 point=Vector3.Lerp(start,summit,i/64f);
                    point.y=ArenaSurface.SupportHeight(point,0.45f)+1f;
                    int count=Physics.OverlapCapsuleNonAlloc(point-Vector3.up*0.55f,point+Vector3.up*0.55f,0.46f,contacts,~0,QueryTriggerInteraction.Ignore);
                    for(int c=0;c<count;c++) if(contacts[c].transform.IsChildOf(world)) { clear=false; break; }
                }
                if(clear) return true;
            }
            start=Vector3.zero; summit=Vector3.zero;
            return false;
        }
        private static bool FindVisibleOverheadPassage(Bounds bounds,List<MeshCollider> visibleMeshes,Transform world,
            CapsuleCollider capsule,float margin,Collider[] contacts,out Vector3 start,out Vector3 end)
        {
            float radius=capsule.radius+margin;
            for(int axis=0;axis<2;axis++)
            {
                float acrossExtent=axis==0?bounds.extents.z:bounds.extents.x;
                // Stay well inside both horizontal edges, not just outside the trunk AABB.
                float maxOffset=acrossExtent-radius-0.3f;
                if(maxOffset<0f) continue;
                for(int offsetIndex=0;offsetIndex<=Mathf.CeilToInt(maxOffset/0.3f)*2;offsetIndex++)
                {
                    float offset=((offsetIndex+1)/2)*0.3f*(offsetIndex%2==0?1f:-1f);
                    if(Mathf.Abs(offset)>maxOffset) continue;
                    start=axis==0?new Vector3(bounds.min.x-radius-0.5f,0f,bounds.center.z+offset)
                        :new Vector3(bounds.center.x+offset,0f,bounds.min.z-radius-0.5f);
                    end=axis==0?new Vector3(bounds.max.x+radius+0.5f,0f,start.z)
                        :new Vector3(start.x,0f,bounds.max.z+radius+0.5f);
                    if(!IsWorldPathClear(start,end,world,capsule,margin,contacts)) continue;
                    int steps=Mathf.CeilToInt(Vector3.Distance(start,end)/0.2f);
                    for(int i=1;i<steps;i++)
                    {
                        Vector3 point=Vector3.Lerp(start,end,i/(float)steps);
                        if(point.x<bounds.min.x+radius || point.x>bounds.max.x-radius
                            || point.z<bounds.min.z+radius || point.z>bounds.max.z-radius) continue;
                        float head=ArenaSurface.SupportHeight(point,0.45f)+1f+capsule.center.y+capsule.height*0.5f+margin;
                        Ray ray=new Ray(new Vector3(point.x,bounds.max.y+1f,point.z),Vector3.down);
                        foreach(var mesh in visibleMeshes)
                            if(mesh.Raycast(ray,out RaycastHit hit,bounds.size.y+2f) && hit.point.y>head+0.1f)
                                return true;
                    }
                }
            }
            start=Vector3.zero; end=Vector3.zero; return false;
        }
        private static bool IsWorldPathClear(Vector3 start,Vector3 end,Transform world,CapsuleCollider capsule,
            float margin,Collider[] contacts)
        {
            int steps=Mathf.CeilToInt(Vector3.Distance(start,end)/0.15f);
            float halfSegment=capsule.height*0.5f-capsule.radius;
            for(int i=0;i<=steps;i++)
            {
                Vector3 point=Vector3.Lerp(start,end,i/(float)steps);
                point.y=ArenaSurface.SupportHeight(point,0.45f)+1f+capsule.center.y;
                int count=Physics.OverlapCapsuleNonAlloc(point-Vector3.up*halfSegment,point+Vector3.up*halfSegment,
                    capsule.radius+margin,contacts,~0,QueryTriggerInteraction.Ignore);
                if(count==contacts.Length) return false;
                for(int c=0;c<count;c++) if(contacts[c].transform.IsChildOf(world)) return false;
            }
            return true;
        }
        private static IEnumerator Capture(string name,Vector3 position,Vector3 target)
        {
            var camera=new GameObject("TerrainAuditCamera").AddComponent<Camera>(); camera.transform.position=position; camera.transform.LookAt(target);
            var rt=new RenderTexture(1200,800,24); camera.targetTexture=rt; yield return null; yield return null;
            var old=RenderTexture.active; RenderTexture.active=rt;
            var texture=new Texture2D(1200,800,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,1200,800),0,0); texture.Apply();
            Directory.CreateDirectory("Logs/WorldArtIntegration"); File.WriteAllBytes("Logs/WorldArtIntegration/"+name+".png",texture.EncodeToPNG());
            RenderTexture.active=old; Object.Destroy(texture); Object.Destroy(rt); Object.Destroy(camera.gameObject);
        }
    }
}
