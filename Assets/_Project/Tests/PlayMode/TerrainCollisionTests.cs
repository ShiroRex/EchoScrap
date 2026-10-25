using System.Collections;
using System.IO;
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
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
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
            InputSystem.settings.backgroundBehavior=background; InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
            if(game!=null)
            {
                var scene=game.gameObject.scene;
                SceneManager.SetActiveScene(SceneManager.CreateScene("TerrainCleanup"));
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            CombatTargetRegistry.Clear();
        }
        [Test] public void CollisionsUseVisibleTriangles_AndTerrainMatchesItsHeightProfile()
        {
            var world=GameObject.Find("Mundo3D_EchoScrap");
            Assert.AreEqual(0,world.GetComponentsInChildren<BoxCollider>().Length,"No oversized proxy boxes.");
            foreach(var filter in world.GetComponentsInChildren<MeshFilter>())
            {
                if(filter.name=="PortalVortex") continue;
                var collider=filter.GetComponent<MeshCollider>();
                Assert.IsNotNull(collider,filter.name);
                Assert.AreSame(filter.sharedMesh,collider.sharedMesh,filter.name);
                Assert.IsFalse(collider.convex,filter.name+" must preserve openings.");
            }
            Assert.AreSame(game.ArenaGround,ArenaSurface.Active.GroundCollider);
            for(int x=-50;x<=50;x+=5) for(int z=-50;z<=50;z+=5)
            {
                Assert.IsTrue(game.ArenaGround.Raycast(new Ray(new Vector3(x,25,z),Vector3.down),out RaycastHit hit,30));
                Assert.AreEqual(ArenaSurface.Profile(x,z),hit.point.y,0.015f);
            }
            Assert.Greater(ArenaSurface.Profile(-24,3),5f);
            foreach(string wall in new[]{"North","South","East","West"})
                Assert.IsTrue(GameObject.Find("Arena_120m/Wall_"+wall).GetComponent<Renderer>().enabled);
        }
        [UnityTest] public IEnumerator PlayerClimbsAndDescendsActualHill_EnemyFollowsSurface()
        {
            var body=game.Player.GetComponent<Rigidbody>();
            body.position=new Vector3(-9,1,3); body.linearVelocity=Vector3.zero;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A)); yield return null;
            for(int i=0;i<130;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Less(body.position.x,-20,"Real input must move up the hill.");
            Assert.Greater(body.position.y,4,"Player must rise with terrain: " + body.position + " ground=" + ArenaSurface.Height(body.position) + " active=" + ArenaSurface.Active);
            Assert.AreEqual(ArenaSurface.SupportHeight(body.position,0.45f)+1,body.position.y,0.25f);
            var enemy=game.Enemies.Spawn(EnemyArchetypeId.Residual,new Vector3(-24,1,3));
            Assert.Greater(enemy.transform.position.y,5f);
            yield return Capture("Relieve",new Vector3(-4,18,-19),new Vector3(-23,3,3));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D)); yield return null;
            for(int i=0;i<130;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Less(body.position.y,2,"Downhill must not leave the player floating.");
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
            Assert.AreEqual(140f,west.center.x+east.center.x,0.01f);
            Assert.Less(Vector3.Distance(west.size,east.size),0.001f);
            var stairs=passage.Find("EscaleraImportada").GetComponentInChildren<MeshCollider>().bounds;
            Assert.LessOrEqual(stairs.min.x,west.max.x);
            Assert.GreaterOrEqual(stairs.max.x,east.min.x);
            foreach(float x in new[]{67f,70f,73f})
                for(float z=-48;z>=-60;z-=1)
                    Assert.IsTrue(WalkableStairs.Active.TryHeight(new Vector3(x,0,z),out _),"Missing tread at "+x+","+z);
            Assert.IsNotNull(passage.Find("PiezasImportadas/Fondo"));
        }
        [UnityTest] public IEnumerator PortalStairsAllowDescentAndAscent()
        {
            // Este recorrido prueba escalones; el viaje por trigger tiene su propia prueba.
            foreach(var gateway in Object.FindObjectsByType<WarehouseGateway>(FindObjectsSortMode.None)) gateway.GetComponent<Collider>().enabled=false;
            var body=game.Player.GetComponent<Rigidbody>();
            foreach(float lane in new[]{67f,70f,73f})
            {
            body.position=new Vector3(lane,1,-44); body.linearVelocity=Vector3.zero;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S)); yield return null;
            for(int i=0;i<300 && body.position.z>-62;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            if(body.position.z>=-61.5f)
                foreach(var contact in Physics.OverlapSphere(body.position,2))
                    Debug.Log("STAIR_BLOCK lane="+lane+" body="+body.position+" height="+ArenaSurface.Height(body.position)+" collider="+contact.name+" bounds="+contact.bounds);
            Assert.Less(body.position.z,-61.5f,"The stairs must be reachable without invisible blockers, lane="+lane+" body="+body.position);
            Assert.Less(body.position.y,-6.5f);
            yield return Capture("PasilloPortal",new Vector3(88,18,-32),new Vector3(70,-3,-56));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W)); yield return null;
            for(int i=0;i<300 && body.position.z<-44;i++) yield return new WaitForFixedUpdate();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
            Assert.Greater(body.position.z,-45f,"Can climb back out of the passage.");
            Assert.AreEqual(1f,body.position.y,0.15f);
            }
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
            yield return Capture("Almacen",new Vector3(199,22,-22),new Vector3(180,0,0));
            yield return new WaitForSeconds(1.05f);
            body.position=ArenaSurface.OnGround(exit.transform.position,1); Physics.SyncTransforms();
            yield return new WaitForFixedUpdate(); yield return null;
            Assert.IsFalse(travel.IsInWarehouse);
            Assert.Less(Vector3.Distance(body.position,travel.CombatArrival.position),0.3f);
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
            yield return Capture("PortalGalaxia",new Vector3(70,-3,-58),new Vector3(70,-4,-65.5f));
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
