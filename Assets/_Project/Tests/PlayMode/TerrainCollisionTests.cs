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
        private void Loaded(Scene scene,LoadSceneMode mode){var station=Object.FindAnyObjectByType<FusionStation>();if(station!=null)station.PersistProgress=false;}
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
            SceneManager.sceneLoaded+=Loaded;yield return SceneManager.LoadSceneAsync("CombatArena"); yield return null;SceneManager.sceneLoaded-=Loaded;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick=false; game.Encounter.AutoTick=false; game.ResetRun(false); game.Weapon.enabled=false;
            game.Motor.GetComponent<PlayerInputReader>().SendMessage("OnApplicationFocus",true);
            game.Session.ResumeFromFocusLoss();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            SceneManager.sceneLoaded-=Loaded;
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
        [Test] public void ObstaclesUseVisibleMeshCollisions_AndTerrainMatchesExpandedProfile()
        {
            var world=GameObject.Find("Mundo3D_EchoScrap");
            // The open portal frame and emblem retain their authored mesh surfaces.
            var colliders=world.GetComponentsInChildren<Collider>(true).Where(c=>
                !c.transform.IsChildOf(world.transform.Find("Portal")) &&
                (world.transform.Find("Emblema_triangular")==null || !c.transform.IsChildOf(world.transform.Find("Emblema_triangular")))).ToArray();
            Assert.Greater(colliders.Length,100);
            foreach(var collider in colliders){
                Assert.IsInstanceOf<MeshCollider>(collider,collider.name);Assert.IsFalse(collider.isTrigger);
                Assert.AreSame(collider.GetComponent<MeshFilter>().sharedMesh,((MeshCollider)collider).sharedMesh);
                Assert.IsFalse(((MeshCollider)collider).convex,"Do not fill concave gaps with a convex hull.");
            }
            Assert.AreSame(game.ArenaGround,ArenaSurface.Active.GroundCollider);
            Assert.AreEqual(200f,ArenaSurface.Active.WorldSize,0.001f);
            Assert.AreEqual(5f/3f,ArenaSurface.Active.HorizontalScale,0.001f);
            Assert.AreEqual(200f,game.ArenaGround.bounds.size.x,0.001f);
            Assert.AreEqual(200f,game.ArenaGround.bounds.size.z,0.001f);
            for(int x=-95;x<=95;x+=10) for(int z=-95;z<=95;z+=10)
            {
                Assert.IsTrue(game.ArenaGround.Raycast(new Ray(new Vector3(x,25,z),Vector3.down),out RaycastHit hit,40));
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
#if UNITY_EDITOR
        [Test] public void DebrisWestOfPortalRestsOnTheTerrain()
        {
            var scenery = GameObject.Find("Mundo3D_EchoScrap/Acumulaciones_dibujadas").transform;
            foreach (string name in new[] { "escombros_concreto_-2_44", "tablas_-3_42", "bolsa_basura_-1_46" })
            {
                var debris = scenery.Find(name);
                Assert.IsNotNull(debris, name);
                var filter = debris.GetComponentInChildren<MeshFilter>();
                using var data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                using var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                data[0].GetVertices(vertices);
                float lowestGap = float.PositiveInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = filter.transform.TransformPoint(vertex);
                    var ray = new Ray(point + Vector3.up * 10f, Vector3.down);
                    Assert.IsTrue(game.ArenaGround.Raycast(ray, out RaycastHit hit, 20f), name);
                    lowestGap = Mathf.Min(lowestGap, point.y - hit.point.y);
                }
                Assert.That(lowestGap, Is.InRange(-.25f, .05f), name);
            }
        }
#endif

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
        [UnityTest] public IEnumerator RealFbxTreeOverheadPassageAllowsPlayerMovement()
        {
            Transform world=GameObject.Find("Mundo3D_EchoScrap").transform;
            CapsuleCollider capsule=game.Player.GetComponent<CapsuleCollider>();
            var contacts=new Collider[128];
            Vector3 start=Vector3.zero,end=Vector3.zero;
            Bounds treeBounds=new Bounds();
            string treeName=null;
            // This fixture compares one imported tree with its fitted boxes.
            // Neighbouring scenery must not prevent discovery of that tree's
            // own passage. Dispose restores every original enabled state,
            // including when an assertion fails or the iterator is stopped.
            using var isolatedTree=new TreeCollisionIsolation(world);
            // Select a passage from the visible FBX before inspecting the fitted collision.
            // A failed box comparison must fail this test, never select a different route.
            foreach(Transform anchor in world.GetComponentsInChildren<Transform>()
                .Where(t=>t.name.StartsWith("arbol_seco_")).OrderBy(t=>t.name))
            {
                isolatedTree.Select(anchor);
                var renderers=anchor.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0) continue;
                Bounds bounds=renderers[0].bounds;
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var actualMesh=anchor.GetComponentsInChildren<MeshCollider>().ToList();Assert.Greater(actualMesh.Count,0);
                bool originalBackfaceQueries=Physics.queriesHitBackfaces;bool found;
                try{Physics.queriesHitBackfaces=true;found=FindVisibleOverheadPassage(bounds,actualMesh,world,capsule,.3f,contacts,out start,out end);}
                finally{Physics.queriesHitBackfaces=originalBackfaceQueries;}
                if(!found) continue;
                treeName=anchor.name; treeBounds=bounds;
                break;
            }
            Assert.IsNotNull(treeName,"At least one actual FBX tree must expose a walkable passage beneath a visible branch, inside its overall bounds.");
            Debug.Log($"[Selected FBX corridor] {treeName} start={start} end={end} bounds={treeBounds}");
            Assert.IsTrue(IsWorldPathClear(start,end,world,capsule,0.3f,contacts),
                treeName+" fitted boxes must preserve the same FBX passage with 0.3 m clearance; a single enclosing AABB would close it.");
            Debug.Log($"[Real FBX passage] {treeName} start={start} end={end} bounds={treeBounds}");

            var body=game.Player.GetComponent<Rigidbody>();
            body.interpolation=RigidbodyInterpolation.None;
            body.position=ArenaSurface.OnGround(start,1f); game.Player.transform.position=body.position;
            body.linearVelocity=Vector3.zero; Physics.SyncTransforms();
            Vector3 direction=(end-start).normalized;
            var keys=new List<Key>(2);
            if(direction.x>0.1f) keys.Add(Key.D);
            else if(direction.x<-.1f) keys.Add(Key.A);
            if(direction.z>0.1f) keys.Add(Key.W);
            else if(direction.z<-.1f) keys.Add(Key.S);
            float distance=Vector3.Distance(start,end);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray())); yield return null;
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
            var zones=Object.FindObjectsByType<LavaHazard>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Assert.AreEqual(3,zones.Length);
            foreach(var zone in zones) zone.AutoTick=false;
            zones[0].transform.parent.gameObject.SetActive(true);
            yield return null;
            var lava=zones[0];
            // StylizedLava writes its molten colour directly; URP Lit's
            // _EMISSION variant keyword is not part of this custom shader.
            var material=lava.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.AreEqual("EchoScrap/StylizedLava",material.shader.name);
            Assert.IsTrue(material.shader.isSupported);
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
        [UnityTest] public IEnumerator EnergyArtworkTracksActualDashAndRecharge()
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.Space));
            yield return null; yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Assert.IsTrue(game.Motor.IsDashing);
            game.Hud.RefreshNow();
            var fill=game.Hud.CanvasRoot.transform.Find("Dash/ArtworkTrack/DashFill").GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(0f,fill.fillAmount,0.001f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return new WaitForSeconds(1.5f);
            game.Hud.RefreshNow(); Assert.AreEqual(1f,fill.fillAmount,0.001f);
        }
        [Test] public void ImportedVaultWallsAreSymmetric_AndStairsCoverTheCorridor()
        {
            var passage=GameObject.Find("WarehouseConnection/PasilloDescendente").transform;
            var west=passage.Find("MuroOeste").GetComponent<BoxCollider>().bounds;
            var east=passage.Find("MuroEste").GetComponent<BoxCollider>().bounds;
            Assert.AreEqual(0f,west.center.x+east.center.x,0.01f);
            Assert.Less(Vector3.Distance(west.size,east.size),0.001f);
            var stairs=passage.Find("EscaleraImportada").GetComponentInChildren<MeshCollider>().bounds;
            Assert.LessOrEqual(stairs.min.x,Mathf.Min(west.max.x,east.max.x));
            Assert.GreaterOrEqual(stairs.max.x,Mathf.Max(west.min.x,east.min.x));
            foreach(float x in new[]{-3f,0f,3f})
                for(float z=63;z<=75;z+=1)
                    Assert.IsTrue(WalkableStairs.Active.TryHeight(new Vector3(x,0,z),out _),"Missing tread at "+x+","+z);
            Assert.IsNotNull(passage.Find("PiezasImportadas/Fondo"));
        }
        [Test] public void NorthAccessMovesBothPortalAndTerrain_WhileRestoringTheOldGround()
        {
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            var entry=Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None).First(g=>!g.FromWarehouse);
            var fusion=Object.FindAnyObjectByType<FusionStation>();
            Assert.AreEqual(0f,entry.transform.position.x,.01f);
            Assert.That(entry.transform.position.z,Is.InRange(77f,79f));
            Assert.Less(travel.CombatArrival.position.z,entry.transform.position.z-2f);
            Assert.Greater(fusion.transform.position.x,150);
            Assert.IsNotNull(fusion.GetComponentInParent<VaultFloorSurface>());
            Assert.Less(ArenaSurface.Profile(0,78,200),-7.9f);
            Assert.Greater(ArenaSurface.Profile(70,-58,200),-1f,"The old southern excavation must be filled.");
            foreach(var p in new[]{new Vector3(70,0,-58),new Vector3(0,0,78)})
            {
                Assert.IsTrue(game.ArenaGround.Raycast(new Ray(p+Vector3.up*20,Vector3.down),out var hit,40));
                Assert.AreEqual(ArenaSurface.Profile(p.x,p.z,200),hit.point.y,.15f,"Saved mesh and locomotion must agree.");
            }
        }
        [Test] public void CentralCalderaHasFlatGroundAndNoSolidSceneryWithin24Metres()
        {
            for(int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16f;
                var point=new Vector3(Mathf.Cos(angle)*22f,0,Mathf.Sin(angle)*22f);
                Assert.AreEqual(0f,ArenaSurface.Profile(point.x,point.z,200),.001f);
            }
            var world=GameObject.Find("Mundo3D_EchoScrap");
            foreach(var collider in world.GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled || collider.isTrigger)continue;
                var b=collider.bounds;
                var nearest=new Vector2(Mathf.Clamp(0,b.min.x,b.max.x),Mathf.Clamp(0,b.min.z,b.max.z));
                Assert.GreaterOrEqual(nearest.magnitude,24f,collider.transform.parent.name+" blocks the boss clearing.");
            }
            foreach(var lava in Object.FindObjectsByType<LavaHazard>(FindObjectsSortMode.None))
                Assert.IsFalse(lava.Contains(Vector3.zero,22f),"Existing lava must stay outside the caldera.");
        }
        [Test] public void PortalRailsBlockEntryFromAboveAndFromBothOutsideEdges()
        {
            var passage = GameObject.Find("WarehouseConnection/PasilloDescendente").transform;
            foreach (var wall in new[] { "MuroOeste", "MuroEste" })
            {
                var collider = passage.Find(wall).GetComponent<BoxCollider>();
                float side = Mathf.Sign(collider.bounds.center.x);
                foreach (float z in new[] { 59f, 63f, 70f, 78f })
                {
                    var from = new Vector3(side * 10f, 2f, z);
                    Assert.IsTrue(collider.Raycast(new Ray(from, Vector3.left * side), out _, 6f), wall + " exterior " + z);
                    from.x = 0f;
                    Assert.IsTrue(collider.Raycast(new Ray(from, Vector3.right * side), out var hit, 5f), wall + " interior " + z);
                    Assert.That(Mathf.Abs(hit.point.x), Is.InRange(4.5f, 4.7f));
                }
            }
        }
        [Test] public void RightPortalRailStopsShotsAtTheSameInnerEdgeAsTheLeft()
        {
            foreach(float z in new[]{63f,66f,68f,70f,71f})
            {
                Assert.IsTrue(WalkableStairs.Active.TryHeight(new Vector3(0,0,z),out float tread));
                var origin=new Vector3(0,tread+1,z);
                Assert.IsTrue(Physics.Raycast(origin,Vector3.left,out var left,5.35f,1,QueryTriggerInteraction.Ignore),"Left rail at "+z);
                Assert.IsTrue(Physics.Raycast(origin,Vector3.right,out var right,5.35f,1,QueryTriggerInteraction.Ignore),"Right rail at "+z);
                Assert.AreEqual(Mathf.Abs(left.point.x),right.point.x,.5f,"Both rails must protect the visible inner edge.");
            }
        }
        [UnityTest] public IEnumerator RightPortalRailBlocksWalkingAndDashWithoutBlockingRetreat()
        {
            foreach(var gate in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None))gate.GetComponent<Collider>().enabled=false;
            var body=game.Player.GetComponent<Rigidbody>();
            foreach(float z in new[]{63f,68f,73f})foreach(bool dash in new[]{false,true})
            {
                var start=ArenaSurface.OnGround(new Vector3(3.7f,0,z),1);
                body.position=start;game.Player.transform.position=start;body.linearVelocity=Vector3.zero;
                game.Motor.ResetDashForSession();Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));yield return null;
                if(dash){InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D,Key.Space));yield return null;}
                for(int tick=0;tick<25;tick++)yield return new WaitForFixedUpdate();
                Assert.Less(body.position.x,4.4f,"Crossed the right visible rail: z="+z+" dash="+dash+" position="+body.position);
                var contact=body.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));yield return null;
                for(int tick=0;tick<20;tick++)yield return new WaitForFixedUpdate();
                Assert.Less(body.position.x,contact.x-.8f,"Cannot retreat from the rail.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            }
        }
        [UnityTest] public IEnumerator DashIntoPortalRailCanImmediatelyMoveAway()
        {
            foreach(var gate in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None))gate.GetComponent<Collider>().enabled=false;
            var body=game.Player.GetComponent<Rigidbody>();
            foreach(float side in new[]{-1f,1f})foreach(float z in new[]{61f,65f,72f,76f}){
                var start=ArenaSurface.OnGround(new Vector3(side*4.2f,0,z),1);body.position=start;game.Player.transform.position=start;body.linearVelocity=Vector3.zero;game.Motor.ResetDashForSession();Physics.SyncTransforms();
                Key into=side<0?Key.A:Key.D,away=side<0?Key.D:Key.A;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(into));yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState(into,Key.Space));yield return null;
                for(int tick=0;tick<12;tick++)yield return new WaitForFixedUpdate();
                float contact=body.position.x;InputSystem.QueueStateEvent(keyboard,new KeyboardState(away));yield return null;
                for(int tick=0;tick<35;tick++)yield return new WaitForFixedUpdate();
                Assert.Greater((body.position.x-contact)*-side,1f,"Cannot escape portal rail: start="+start+" end="+body.position);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            }
        }
        [UnityTest] public IEnumerator RepeatedDashesCanDescendAndClimbPortalWithoutGettingStuck()
        {
            foreach(var gate in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None))gate.GetComponent<Collider>().enabled=false;
            var body=game.Player.GetComponent<Rigidbody>();
            foreach(float lane in new[]{-3f,0f,3f}){
                var start=ArenaSurface.OnGround(new Vector3(lane,0,59),1);body.position=start;game.Player.transform.position=start;body.linearVelocity=Vector3.zero;Physics.SyncTransforms();
                foreach(var key in new[]{Key.W,Key.S}){
                    for(int dash=0;dash<7;dash++){
                        game.Motor.ResetDashForSession();InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key,Key.Space));yield return null;
                        for(int tick=0;tick<10;tick++)yield return new WaitForFixedUpdate();
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                        Assert.IsFalse(game.Motor.IsDashing);Assert.AreEqual(ArenaSurface.SupportHeight(body.position,.45f)+1,body.position.y,.2f,"Dash must stay grounded lane="+lane+" pos="+body.position);
                    }
                    if(key==Key.W)Assert.Greater(body.position.z,76,"Descent stuck: "+body.position);else Assert.Less(body.position.z,61,"Ascent stuck: "+body.position);
                }
            }
        }
        [UnityTest] public IEnumerator PortalStairsAllowDescentAndAscent()
        {
            // Este recorrido prueba escalones; el viaje por trigger tiene su propia prueba.
            foreach(var gateway in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None)) gateway.GetComponent<Collider>().enabled=false;
            var body=game.Player.GetComponent<Rigidbody>();
            foreach(float lane in new[]{-3f,0f,3f})
            {
            body.position=new Vector3(lane,1,59); body.linearVelocity=Vector3.zero;
            yield return new WaitForSeconds(.5f);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W)); yield return null;
            for(int i=0;i<300 && body.position.z<77;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            if(body.position.z<=76.5f)
                foreach(var contact in Physics.OverlapSphere(body.position,2))
                    Debug.Log("STAIR_BLOCK lane="+lane+" body="+body.position+" height="+ArenaSurface.Height(body.position)+" collider="+contact.name+" bounds="+contact.bounds);
            Assert.Greater(body.position.z,76.5f,"The stairs must be reachable without invisible blockers, lane="+lane+" body="+body.position);
            Assert.Less(body.position.y,-6.5f);
            yield return Capture("PasilloPortal",new Vector3(18,18,47),new Vector3(0,-3,71));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S)); yield return null;
            for(int i=0;i<300 && body.position.z>59;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Less(body.position.z,60f,"Can climb back out of the passage.");
            Assert.AreEqual(ArenaSurface.SupportHeight(body.position,0.5f)+1f,body.position.y,0.15f);
            }
        }
        [UnityTest] public IEnumerator NorthVaultApproachKeepsCombatCameraOrientation()
        {
            var director=Object.FindAnyObjectByType<WarehouseCameraDirector>();
            var follow=game.CameraRig.GetComponent<Unity.Cinemachine.CinemachineFollow>();
            Vector3 original=follow.FollowOffset;
            var body=game.Player.GetComponent<Rigidbody>();
            Quaternion originalRotation=game.CameraRig.transform.rotation;
            body.position=ArenaSurface.OnGround(new Vector3(0,0,65),1); body.linearVelocity=Vector3.zero;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(0f,director.CaveBlend);
            Assert.Less(Quaternion.Angle(game.CameraRig.transform.rotation,originalRotation),.1f);
            Assert.Less(game.GameplayCamera.transform.position.z,body.position.z);
            yield return Capture("CamaraCueva",game.GameplayCamera.transform.position,game.GameplayCamera.transform.position+game.GameplayCamera.transform.forward*10);
            body.position=Vector3.up; body.linearVelocity=Vector3.zero;
            yield return new WaitForSeconds(1.5f);
            Assert.Less(director.CaveBlend,.01f);Assert.Less(Vector3.Distance(original,follow.FollowOffset),.1f);
        }
        [UnityTest] public IEnumerator NorthVaultControlsFollowScreenAndDashUsesW()
        {
            foreach(var gateway in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None)) gateway.GetComponent<Collider>().enabled=false;
            var body=game.Player.GetComponent<Rigidbody>();
            body.position=ArenaSurface.OnGround(new Vector3(0,0,59),1);body.linearVelocity=Vector3.zero;
            yield return new WaitForSeconds(.5f);
            var director=Object.FindAnyObjectByType<WarehouseCameraDirector>();
            var collector=game.Player.GetComponent<CollectorSpriteView>();
            Assert.AreEqual(0f,director.CaveBlend,"The north-facing access requires no camera orbit.");
            var follow=game.CameraRig.GetComponent<Unity.Cinemachine.CinemachineFollow>();
            Assert.Greater(follow.FollowOffset.magnitude,11.9f,"The exterior view keeps its ordinary distance.");
            foreach(var key in new[]{Key.W,Key.S,Key.A,Key.D}) {
                body.position=ArenaSurface.OnGround(new Vector3(0,0,67),1);body.linearVelocity=Vector3.zero;
                yield return new WaitForFixedUpdate();
                Vector3 start=body.position;
                var camera=game.GameplayCamera.transform;
                Vector3 expected=Vector3.ProjectOnPlane(key==Key.W?camera.forward:key==Key.S?-camera.forward:key==Key.A?-camera.right:camera.right,Vector3.up).normalized;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;
                for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();
                game.Weapon.UpdateAim(1f);
                Assert.Greater(Vector3.Dot(game.Weapon.CurrentAimDirection,expected),.99f,key+" must aim in the same world direction as movement after the orbit.");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                Assert.Greater(Vector3.Dot(body.position-start,expected),.25f,key+" must follow screen direction.");
                CharacterFacing facing=key==Key.W?CharacterFacing.Up:key==Key.S?CharacterFacing.Down:key==Key.A?CharacterFacing.Left:CharacterFacing.Right;
                Assert.AreEqual(facing,collector.Facing,key+" must use the matching screen-facing walk animation.");
                Assert.Less(Quaternion.Angle(collector.CharacterRenderer.transform.rotation,Quaternion.identity),.01f,"The sprite must keep its fixed world plane.");
            }
            body.position=ArenaSurface.OnGround(new Vector3(0,0,63),1);body.linearVelocity=Vector3.zero;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.Space));yield return null;yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            Assert.IsTrue(game.Motor.IsDashing);Assert.Greater(game.Motor.DashDirection.z,.9f,"W + dash heads north toward the portal.");
        }
        [Test] public void GarageHasTenAccessibleDisplayAreas()
        {
            var room=GameObject.Find("BovedaModular");Assert.IsNotNull(room);
            var slots = Object.FindAnyObjectByType<FusionStation>().DisplaySlots;
            Assert.AreEqual(10, slots.Length);
            var aisle = new Bounds(new Vector3(180f, 3.5f, 1f), new Vector3(32f, 7f, 38f));
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                var capsule = room.transform.Find($"Capsula_{i + 1}").GetComponent<Collider>().bounds;
                Assert.AreEqual(capsule.center.x, slot.position.x, .01f, slot.name);
                Assert.AreEqual(capsule.center.z, slot.position.z, .01f, slot.name);
                if (i >= 3) Assert.IsFalse(capsule.Intersects(aisle), slot.name + " blocks the central aisle");

                var p = ArenaSurface.OnGround(slot.position + Vector3.back * 4f, 1f);
                Assert.IsFalse(Physics.CheckCapsule(p - Vector3.up * .5f, p + Vector3.up * .5f,
                    .45f, 1, QueryTriggerInteraction.Ignore), slot.name + " approach is blocked");
            }
            var capsuleModel = room.transform.Find("Capsula_1").GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.AreEqual(10, room.GetComponentsInChildren<MeshFilter>().Count(filter => filter.sharedMesh == capsuleModel),
                "Only the ten usable entity capsules should appear in the vault.");
            Assert.IsFalse(room.transform.Find("BaseCircular").GetComponent<Collider>().bounds.Intersects(aisle));

            var floor = room.transform.Find("ApoyoPlantaBaja").GetComponent<Collider>().bounds;
            Assert.GreaterOrEqual(floor.size.x, 47.9f);
            Assert.GreaterOrEqual(floor.size.z, 63.9f);
        }
        [Test] public void ArenaHousesRejectEnemySpawnsInsideTheirFootprints()
        {
            var director = game.Director;
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var rejects = director.GetType().GetMethod("OverlapsBlockedInterior", flags);
            Assert.IsNotNull(rejects);
            var buildings = GameObject.Find("Mundo3D_EchoScrap").GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("casa_") || t.name.StartsWith("ruina_almacen_")).ToArray();
            Assert.AreEqual(7, buildings.Length);
            foreach (var building in buildings)
            {
                var collider = building.GetComponentInChildren<MeshCollider>();
                Assert.IsNotNull(collider, building.name);
                var center = collider.bounds.center;
                center.y = ArenaSurface.Height(center) + 1f;
                Assert.IsTrue((bool)rejects.Invoke(director, new object[] { center, .5f }), building.name);
            }
            Assert.IsFalse((bool)rejects.Invoke(director, new object[] { Vector3.up, .5f }),
                "The open arena must remain available.");
        }

        [UnityTest] public IEnumerator PortalsPreserveCombatAndAllowReturnWithoutBouncing()
        {
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsNotNull(travel);
            var gates=Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None);
            Assert.AreEqual(2,gates.Length);
            WarehouseGateway entry=null,exit=null;
            foreach(var gate in gates) { if(gate.FromWarehouse) exit=gate; else entry=gate; }
            var body=game.Player.GetComponent<Rigidbody>();
            var enemy=game.Enemies.Spawn(EnemyArchetypeId.Residual,new Vector3(0,1,8));
            game.Player.TakeContactOrHazardDamage(5,Vector3.zero);
            float health=game.Player.CurrentHealth,enemyHealth=enemy.CurrentHealth;
            game.Session.Clock.SetGameplayTimeForTesting(60f);
            body.position=ArenaSurface.OnGround(entry.transform.position,1); Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return null;
            Assert.IsTrue(travel.IsInWarehouse,"Walking into the actual trigger must transport the player.");
            Assert.Greater(body.position.x,170f);
            var interior=GameObject.Find("BovedaModular"); Assert.IsNotNull(interior);
            int slots=Object.FindAnyObjectByType<FusionStation>().DisplaySlots.Length;
            Assert.AreEqual(10,slots);
            var confiner=game.CameraRig.GetComponent<Unity.Cinemachine.CinemachineConfiner3D>();
            Assert.AreSame(travel.InteriorCameraBounds,confiner.BoundingVolume);
            Assert.IsTrue(game.CameraRig.GetComponent<WarehouseCameraFraming>().IsConfining,"The entire interior view must stay in the room.");
            float clock=game.Session.GameplayTime;
            Vector3 enemyPosition=enemy.transform.position;
            Assert.IsFalse(travel.TryTravel(true,game.Player),"Prevent an immediate portal bounce.");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W)); yield return null;
            for(int i=0;i<20;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Greater(body.position.z,travel.WarehouseArrival.position.z+1f,"Movement remains available inside the warehouse.");
            Assert.AreEqual(clock,game.Session.GameplayTime);
            Assert.Less(Vector3.Distance(enemyPosition,enemy.transform.position),0.05f);
            Assert.AreEqual(health,game.Player.CurrentHealth);
            Assert.AreEqual(enemyHealth,enemy.CurrentHealth);
            yield return Capture("Almacen",new Vector3(180,7,-29),new Vector3(180,4,29));
            yield return Capture("CamaraGarage",game.GameplayCamera.transform.position,game.GameplayCamera.transform.position+game.GameplayCamera.transform.forward*10);
            yield return new WaitForSeconds(1.05f);
            body.position=ArenaSurface.OnGround(exit.transform.position,1); Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return null;
            if(travel.IsInWarehouse) foreach(var c in Physics.OverlapSphere(body.position,2)) Debug.Log("RETURN_BLOCK body="+body.position+" gate="+exit.transform.position+" collider="+c.name);
            Assert.IsFalse(travel.IsInWarehouse);
            Assert.Less(Vector3.Distance(body.position,travel.CombatArrival.position),0.3f);
            yield return new WaitForSeconds(1.2f);
            Assert.IsFalse(travel.IsInWarehouse,"Return spawn must remain outside the entrance trigger.");
            Assert.AreEqual(health,game.Player.CurrentHealth);
            Assert.IsTrue(enemy.IsAlive);
            yield return new WaitForSeconds(0.1f);
            Assert.Greater(game.Session.GameplayTime,clock);
        }
        [UnityTest] public IEnumerator Arena200BordersBlockExitAndKeepPerimeterWalkable()
        {
            Assert.AreEqual(new Vector2(100,100),game.WaveSettings.ArenaHalfExtents);
            Assert.AreEqual(200f,game.ArenaGround.bounds.size.x,0.01f);
            Assert.AreEqual(200f,game.ArenaGround.bounds.size.z,0.01f);
            var body=game.Player.GetComponent<Rigidbody>();
            var starts=new[]{new Vector3(96,1,0),new Vector3(-96,1,0),new Vector3(0,1,96),new Vector3(0,1,-96)};
            var keys=new[]{Key.D,Key.A,Key.W,Key.S};
            for(int side=0;side<4;side++)
            {
                body.position=starts[side]; body.linearVelocity=Vector3.zero; Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys[side])); yield return null;
                for(int i=0;i<55;i++) yield return new WaitForFixedUpdate();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
                float edge=side<2?Mathf.Abs(body.position.x):Mathf.Abs(body.position.z);
                Assert.That(edge,Is.InRange(98f,100f),"Border "+side+" is reachable but cannot be crossed.");
            }
            body.position=new Vector3(96,1,-20); body.linearVelocity=Vector3.zero;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W)); yield return null;
            for(int i=0;i<100;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Greater(body.position.z,-8,"Perimeter path must not have invisible model blockers.");
        }
        [UnityTest] public IEnumerator PortalIsUprightOpenAndHasGalaxyParticles()
        {
            var frame=GameObject.Find("Mundo3D_EchoScrap/Portal/portal_43_-40");
            var renderer=frame.GetComponentInChildren<Renderer>(); var bounds=renderer.bounds;
            Assert.Greater(bounds.size.y,bounds.size.z*1.5f,"Portal must be vertical, not lying down.");
            Transform aperture=null;
            foreach(var child in frame.GetComponentsInChildren<Transform>()) if(child.name=="ApertureCenter") aperture=child;
            Assert.IsNotNull(aperture);
            var ray=new Ray(new Vector3(aperture.position.x,aperture.position.y,bounds.max.z+2),Vector3.back);
            foreach(var collider in frame.GetComponentsInChildren<MeshCollider>())
                Assert.IsFalse(collider.Raycast(ray,out _,bounds.size.z+4),"The original solid disc must be removed.");
            Assert.AreEqual(2,Object.FindObjectsByType<GalaxyPortalVfx>(FindObjectsSortMode.None).Length);
            foreach(var vfx in Object.FindObjectsByType<GalaxyPortalVfx>(FindObjectsSortMode.None))
            {
                Assert.IsNotNull(vfx.GetComponentInChildren<ParticleSystem>());
                Assert.IsNotNull(vfx.SparkMaterial);
            }
            yield return Capture("PortalGalaxia",new Vector3(0,-3,73),new Vector3(0,-4,80.5f));
        }
        [UnityTest] public IEnumerator ResetFromWarehouseReturnsToOriginalSpawn()
        {
            var travel=Object.FindAnyObjectByType<WarehouseTravel>();
            Assert.IsTrue(travel.TryTravel(false,game.Player));
            game.ResetRun(false); yield return null;
            
            Assert.IsFalse(travel.IsInWarehouse);
            Assert.Less(Vector3.Distance(game.Player.transform.position,game.SpawnPosition),0.1f);
            Assert.IsTrue(game.Session.IsGameplayFlowActive());
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
            // A passage under one branch need not cross the whole bounding
            // box of a tree whose other branches reach down to the floor.
            // Seed corridors from actual overhead FBX surfaces, then verify
            // at least 3.2 m of movement under and beyond that surface.
            float halfLength=Mathf.Max(1.6f,radius*2f);
            int overheadSamples=0,blockedCorridors=0;
            for(float x=bounds.min.x+radius;x<=bounds.max.x-radius;x+=0.15f)
            for(float z=bounds.min.z+radius;z<=bounds.max.z-radius;z+=0.15f)
            {
                var midpoint=new Vector3(x,0f,z);
                float head=ArenaSurface.SupportHeight(midpoint,0.45f)+1f+capsule.center.y+capsule.height*0.5f+margin;
                var ray=new Ray(new Vector3(x,bounds.max.y+1f,z),Vector3.down);
                bool overhead=false;
                foreach(var mesh in visibleMeshes)
                    if(mesh.Raycast(ray,out RaycastHit hit,bounds.size.y+2f) && hit.point.y>head+0.1f)
                    { overhead=true; break; }
                if(!overhead)continue;
                overheadSamples++;
                for(int directionIndex=0;directionIndex<8;directionIndex++)
                {
                    float angle=directionIndex*Mathf.PI*.25f;
                    var axis=new Vector3(Mathf.Cos(angle),0f,Mathf.Sin(angle));
                    start=midpoint-axis*halfLength;
                    end=midpoint+axis*halfLength;
                    if(start.x<bounds.min.x+radius || start.x>bounds.max.x-radius
                        || start.z<bounds.min.z+radius || start.z>bounds.max.z-radius
                        || end.x<bounds.min.x+radius || end.x>bounds.max.x-radius
                        || end.z<bounds.min.z+radius || end.z>bounds.max.z-radius)continue;
                    if(IsWorldPathClear(start,end,world,capsule,margin,contacts))return true;
                    blockedCorridors++;
                }
            }
            Debug.Log($"[FBX tree reference] meshes={visibleMeshes.Count} bounds={bounds} overheadSamples={overheadSamples} blockedCorridors={blockedCorridors} capsule={capsule.height}/{capsule.radius} margin={margin}");
            start=Vector3.zero; end=Vector3.zero; return false;
        }
        private sealed class TreeCollisionIsolation : System.IDisposable
        {
            private readonly Collider[] colliders;
            private readonly bool[] enabled;

            public TreeCollisionIsolation(Transform world)
            {
                colliders=world.GetComponentsInChildren<Collider>(true);
                enabled=colliders.Select(c=>c.enabled).ToArray();
            }

            public void Select(Transform tree)
            {
                for(int i=0;i<colliders.Length;i++)
                    if(colliders[i]!=null)
                        colliders[i].enabled=enabled[i] && colliders[i].transform.IsChildOf(tree);
                Physics.SyncTransforms();
            }

            public void Dispose()
            {
                for(int i=0;i<colliders.Length;i++)
                    if(colliders[i]!=null) colliders[i].enabled=enabled[i];
                Physics.SyncTransforms();
            }
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
