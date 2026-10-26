using System.Collections;
using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace EchoScrap.Tests
{
    public sealed class UpgradeVisualIntegrationTests
    {
        private CombatGameBootstrap game;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale=1;
            yield return SceneManager.LoadSceneAsync("CombatArena",LoadSceneMode.Single);
            yield return null;
            game=Object.FindAnyObjectByType<CombatGameBootstrap>();
            game.Director.AutoTick=false;game.Encounter.AutoTick=false;game.ResetRun(false);
            game.Upgrades.AutoTick=false;game.Weapon.enabled=false;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale=1;
            var scene=game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("UpgradeVisualCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);CombatTargetRegistry.Clear();
        }
        [Test]
        public void VfxSpritesAndShaderTemplatesAreIncludedAsRuntimeResources()
        {
            foreach(var name in new[]{"Saw","Mine","Turret","Spark","Frost","Smoke","Shockwave","Shield","Wind","Explosion","Armor"})
            {
                var texture=Resources.Load<Texture2D>("Upgrades/Vfx/"+name);
                Assert.That(texture,Is.Not.Null,name);
                Assert.That(texture.mipmapCount,Is.EqualTo(1),name);
                if(name=="Saw"||name=="Mine"||name=="Turret"||name=="Armor")Assert.That(texture.filterMode,Is.EqualTo(FilterMode.Point),name);
            }
            foreach(var name in new[]{"SpriteMaterial","ShieldMaterial"})
            {
                var material=Resources.Load<Material>("Upgrades/Vfx/"+name);
                Assert.That(material,Is.Not.Null);Assert.That(material.shader.isSupported,Is.True);
                Assert.That(material.shader.name,Does.StartWith("EchoScrap/Upgrade"));
            }
        }

        [UnityTest]
        public IEnumerator SawNormalReliefRespondsToLightWithoutEmissionOrSpecularGlare()
        {
            var material=Resources.Load<Material>("Upgrades/Vfx/SawMatte");
            var normal=Resources.Load<Texture2D>("Upgrades/Vfx/Saw_Normal");
            Assert.That(material,Is.Not.Null);Assert.That(normal,Is.Not.Null);
            Assert.That(material.shader.name,Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(material.GetTexture("_BumpMap"),Is.SameAs(normal));
            Assert.That(material.IsKeywordEnabled("_NORMALMAP"),Is.True);
            Assert.That(material.GetFloat("_SpecularHighlights"),Is.Zero);
            Assert.That(material.GetFloat("_EnvironmentReflections"),Is.Zero);
            Assert.That(material.GetFloat("_Smoothness"),Is.LessThanOrEqualTo(.05f));
            Assert.That(material.GetColor("_EmissionColor").maxColorComponent,Is.Zero);
            Assert.That(normal.filterMode,Is.EqualTo(FilterMode.Point));
            Assert.That(normal.mipmapCount,Is.EqualTo(1));
#if UNITY_EDITOR
            var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath("Assets/_Project/Resources/Upgrades/Vfx/Saw_Normal.png");
            Assert.That(importer.textureType,Is.EqualTo(UnityEditor.TextureImporterType.NormalMap));
            Assert.That(importer.sRGBTexture,Is.False);Assert.That(importer.flipGreenChannel,Is.False);
#endif
            game.Stats.SetUpgradeTier("SCRAP_03",1);game.Upgrades.Step(.001f);
            MeshRenderer saw=null;
            foreach(var renderer in game.Upgrades.GetComponentsInChildren<MeshRenderer>())
                if(renderer.sharedMaterial==material){saw=renderer;break;}
            Assert.That(saw,Is.Not.Null,"The actual orbital saw must use the matte normal material.");
            var mesh=saw.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.tangents.Length,Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.tangents[0].x,Is.GreaterThan(.99f));
            Assert.That(mesh.tangents[0].w,Is.EqualTo(-1f));
            var camera=new GameObject("Saw normal review camera").AddComponent<Camera>();
            camera.CopyFrom(game.GameplayCamera);camera.fieldOfView=38f;
            var focus=saw.bounds.center;
            camera.transform.position=focus+new Vector3(0,3.8f,-2f);camera.transform.LookAt(focus);
            var light=new GameObject("Saw normal test light").AddComponent<Light>();
            light.type=LightType.Point;light.range=6f;light.intensity=4f;light.shadows=LightShadows.None;
            light.transform.position=focus+new Vector3(-1.8f,2.2f,0);
            game.Hud.CanvasRoot.SetActive(false);
            string folder=Path.Combine(Application.dataPath,"..","Logs","SawNormalMap");Directory.CreateDirectory(folder);
            var original=saw.sharedMaterial;var block=new MaterialPropertyBlock();saw.GetPropertyBlock(block);
            var legacy=new Material(Resources.Load<Material>("Upgrades/Vfx/SpriteMaterial"));
            legacy.SetTexture("_MainTex",Resources.Load<Texture2D>("Upgrades/Vfx/Saw"));
            legacy.SetFloat("_Glow",1f);
            var flat=new Material(material);flat.DisableKeyword("_NORMALMAP");
            try
            {
                saw.sharedMaterial=legacy;
                var oldBlock=new MaterialPropertyBlock();oldBlock.SetColor("_Color",Color.white);saw.SetPropertyBlock(oldBlock);
                yield return GalleryCapture(camera,folder,"Saw_Before",false);
                saw.sharedMaterial=original;saw.SetPropertyBlock(block);
                yield return GalleryCapture(camera,folder,"Saw_Normal_Left",false);
                saw.sharedMaterial=flat;
                yield return GalleryCapture(camera,folder,"Saw_Flat_Left",false);
                saw.sharedMaterial=original;light.transform.position=focus+new Vector3(1.8f,2.2f,0);
                yield return GalleryCapture(camera,folder,"Saw_Normal_Right",false);
                var lit=ReadReviewPixels(Path.Combine(folder,"Saw_Normal_Left.png"));
                var noNormal=ReadReviewPixels(Path.Combine(folder,"Saw_Flat_Left.png"));
                var right=ReadReviewPixels(Path.Combine(folder,"Saw_Normal_Right.png"));
                int relief=0,lightResponse=0;
                for(int y=200;y<700;y++)for(int x=400;x<1000;x++)
                {
                    int i=y*1400+x;
                    int delta=Mathf.Abs(lit[i].r-noNormal[i].r)+Mathf.Abs(lit[i].g-noNormal[i].g)+Mathf.Abs(lit[i].b-noNormal[i].b);
                    if(delta>8)relief++;
                    if(Mathf.Abs(lit[i].r-right[i].r)+Mathf.Abs(lit[i].g-right[i].g)+Mathf.Abs(lit[i].b-right[i].b)>12)lightResponse++;
                }
                Assert.That(relief,Is.GreaterThan(100),"The normal must change rendered bevels, not just be assigned in the Inspector.");
                Assert.That(lightResponse,Is.GreaterThan(100));
                Debug.Log($"[Saw normal] {relief} relief pixels; {lightResponse} moving-light pixels.");
            }
            finally{saw.sharedMaterial=original;saw.SetPropertyBlock(block);Object.Destroy(legacy);Object.Destroy(flat);Object.Destroy(camera.gameObject);Object.Destroy(light.gameObject);}
        }
        private static Color32[] ReadReviewPixels(string path)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGB24,false);texture.LoadImage(File.ReadAllBytes(path));
            var pixels=texture.GetPixels32();Object.Destroy(texture);return pixels;
        }

        [UnityTest]
        public IEnumerator PlayableArenaShowsUpgradeEffectsAndRerollWithoutReplacingExistingArt()
        {
            Assert.That(game.Session.Upgrades,Is.SameAs(game.Upgrades));
            var p=game.Player.WorldCenter;
            foreach(var card in UpgradeCatalog.Cards)game.Stats.SetUpgradeTier(card.Id,card.MaxTier);
            game.Stats.SetUpgradeTier("CRIT_01",0);game.Stats.SetUpgradeTier("CRIT_05",0); // deterministic visual staging
            game.Weapon.ResetWeaponCycle();game.Player.IgnoreIncomingDamage=true;
            var actors=new List<EnemyActor>();
            for(int i=0;i<10;i++)
            {
                float a=i*Mathf.PI*2/10;var data=EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);data.MaxHealth=100000;data.BaseMoveSpeed=0;data.ContactDamage=0;
                var enemy=game.Enemies.Spawn(data,p+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*4.5f);
                enemy.ApplyFreeze(3);actors.Add(enemy);
            }
            game.Upgrades.Step(10f); // deploy turret, mines, blizzard and saws
            var dash=new DashStateEvent(true,false,false,true,false,p+Vector3.right*2,Vector3.forward,.5f,.5f);game.Session.Events.PublishDash(in dash);
            dash=new DashStateEvent(false,true,false,false,false,p,Vector3.forward,.5f,.5f);game.Session.Events.PublishDash(in dash);
            game.Upgrades.Step(.1f);
            var hit=new DamageRequest(game.Player.TargetId,DamageSourceType.FlashlightPulse,p,Vector3.forward,10,false,burstId:game.Weapon.CurrentBurstId,pulseIndex:4);
            actors[0].ReceiveDamage(in hit);
            game.Upgrades.Step(.03f);
            Assert.That(game.Upgrades.ActiveDeviceCount,Is.GreaterThan(0));Assert.That(game.Upgrades.ActiveVisualCount,Is.GreaterThan(10));
            game.Session.SetManualPause(true);game.Hud.CanvasRoot.SetActive(false);
            yield return Capture("Gameplay",false);
            game.Session.SetManualPause(false);game.Hud.CanvasRoot.SetActive(true);
            game.Progression.AddExperience(100);game.DraftPresentation.StepPresentation(.8f);
            var texts=game.DraftPresentation.CanvasRoot.GetComponentsInChildren<Text>();
            Assert.That(System.Array.Exists(texts,t=>t.text.Contains("REINTENTAR")),Is.True);
            yield return Capture("Draft",true);
            Assert.That(game.Draft.TryReroll(),Is.True);Assert.That(game.Draft.RerollsRemaining,Is.EqualTo(2));
            game.ResetRun(false);Assert.That(game.Upgrades.ActiveVisualCount,Is.Zero);
        }

        [UnityTest]
        public IEnumerator CaptureEachUpgradeEffectForVisualReview()
        {
            var camera = new GameObject("Upgrade gallery camera").AddComponent<Camera>();
            camera.CopyFrom(game.GameplayCamera);
            camera.fieldOfView = 48f;
            var urp = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = game.GameplayCamera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing;
            var source = game.GameplayCamera;
            source.enabled = false;
            var p = game.Player.WorldCenter;
            camera.transform.position = p + new Vector3(0f, 13f, -12f);
            camera.transform.LookAt(p + Vector3.forward * 2f);
            game.DamageNumbers.BindReferences(game.Session, camera);
            var flash = game.Player.GetComponent<FlashlightPresentation>();
            var dashView = game.Player.GetComponent<PlayerDashPresentation>();
            string folder = Path.Combine(Application.dataPath, "..", "Logs", "UpgradeEffectGalleryV2");
            Directory.CreateDirectory(folder);
            var manifest = new List<string> { "file\tid\tname\tnote" };
            int captures = 0;
            game.ResetRun(false);
            game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = game.Pickups.AutoTick = false;
            game.Weapon.enabled = false; game.Hud.CanvasRoot.SetActive(false);
            GalleryEnemy(p + Vector3.forward * 4f);
            GalleryEnemy(p + new Vector3(-3f, 0f, 6f)); GalleryEnemy(p + new Vector3(3f, 0f, 6f));
            Physics.SyncTransforms(); game.Weapon.UpdateAim(.1f); game.Weapon.StepWeapon(.205f); flash.StepPresentation(.015f);
            yield return GalleryCapture(camera, folder, "BASELINE", false);
            manifest.Add("BASELINE.png\tBASELINE\tLuz sin mejoras\tReferencia del cono de luz original."); captures++;
            foreach (var card in UpgradeCatalog.Cards)
            {
                game.ResetRun(false);
                game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = game.Pickups.AutoTick = false;
                game.Weapon.enabled = false;
                game.Hud.CanvasRoot.SetActive(false);
                game.Player.IgnoreIncomingDamage = false;
                game.Stats.SetUpgradeTier(card.Id, 1);
                game.Weapon.ResetWeaponCycle();
                var main = GalleryEnemy(p + Vector3.forward * 4f);
                GalleryEnemy(p + new Vector3(-3f, 0f, 6f));
                GalleryEnemy(p + new Vector3(3f, 0f, 6f));
                Physics.SyncTransforms();
                string note = "Nivel 1; efecto actual del juego.";
                bool hud = false;
                switch (card.Id)
                {
                    case "FLASH_01":
                    case "FLASH_02":
                    case "FLASH_04":
                    case "FLASH_05":
                        game.Weapon.UpdateAim(.1f);
                        game.Weapon.StepWeapon(.205f);
                        flash.StepPresentation(.015f);
                        note = card.Id == "FLASH_01" ? "Cono de luz más largo y ancho."
                            : card.Id == "FLASH_02" ? "Misma luz; cambia la cadencia. Un fotograma no muestra la velocidad."
                            : card.Id == "FLASH_04" ? "Cono más estrecho; impacto y empuje existentes."
                            : "Luz solar continua con los pulsos existentes.";
                        break;
                    case "FLASH_03":
                        GalleryHit(main, 10, 4);
                        game.Upgrades.Step(.055f);
                        Assert.That(game.Upgrades.ActiveProjectileCount, Is.EqualTo(2));
                        note = "Dos chispas turquesa salen del enemigo alcanzado y buscan a otros.";
                        break;
                    case "DASH_01":
                        typeof(EchoScrap.Player.PlayerMotor).GetMethod("StartDashInFixedStep", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(game.Motor, new object[] { Vector3.right });
                        game.Motor.transform.position += Vector3.right;
                        dashView.StepPresentation(.046f);
                        game.Motor.transform.position += Vector3.right;
                        dashView.StepPresentation(.046f);
                        game.Motor.GetComponent<Rigidbody>().position = game.Motor.transform.position;
                        note = "Reutiliza las siluetas del dash; cambia velocidad y recarga.";
                        break;
                    case "DASH_02":
                        var stunned = GalleryEnemy(p + Vector3.forward * 2.5f);
                        GalleryDash(p, false);
                        game.Upgrades.Step(.01f); game.Upgrades.Step(.12f);
                        Assert.That(stunned.StunRemaining, Is.GreaterThan(0));
                        note = "Onda ámbar al llegar; aro ámbar de aturdimiento en los alcanzados.";
                        break;
                    case "DASH_03":
                        GalleryDash(p + Vector3.left * 2.5f, true);
                        game.Upgrades.Step(.02f);
                        yield return GalleryCapture(camera, folder, "DASH_03_senuelo", false);
                        manifest.Add("DASH_03_senuelo.png\tDASH_03\tHolo-señuelo: copia\tCopia turquesa que atrae enemigos."); captures++;
                        game.Upgrades.Step(2.01f); game.Upgrades.Step(.12f);
                        note = "Al desaparecer, el señuelo causa una implosión turquesa.";
                        break;
                    case "DASH_04":
                        GalleryDash(p, true);
                        game.Upgrades.Step(.01f); game.Upgrades.Step(.13f);
                        yield return GalleryCapture(camera, folder, "DASH_04_salida", false);
                        manifest.Add("DASH_04_salida.png\tDASH_04\tCataclismo: salida\tExplosión ámbar en el punto de salida."); captures++;
                        game.Upgrades.ResetForSession();
                        GalleryDash(p, false);
                        game.Upgrades.Step(.08f);
                        note = "Cuchilla turquesa perforante que sale al terminar el dash.";
                        break;
                    case "BIO_01":
                        game.Player.ForceSetHealthForTesting(60);
                        game.Player.Heal(15);
                        game.Upgrades.Step(.11f);
                        hud = true;
                        note = "Aumenta vida en HUD y reutiliza el aro verde de curación al elegir.";
                        break;
                    case "BIO_02":
                        game.Player.ForceSetHealthForTesting(40);
                        for (int i = 0; i < 100 && game.Player.CurrentHealth <= 40; i++) GalleryHit(main, 1, 4);
                        Assert.That(game.Player.CurrentHealth, Is.GreaterThan(40));
                        game.Upgrades.Step(.1f);
                        note = "Aro verde compartido de curación cuando se activa el sifón.";
                        break;
                    case "BIO_03":
                        game.Player.ForceSetHealthForTesting(50);
                        game.Upgrades.Step(2.5f); game.Upgrades.Step(.1f);
                        yield return GalleryCapture(camera, folder, "BIO_03_curacion", false);
                        manifest.Add("BIO_03_curacion.png\tBIO_03\tNanobots: curación\tAro verde compartido al regenerar vida."); captures++;
                        game.Upgrades.ResetForSession();
                        game.Player.ForceSetHealthForTesting(21);
                        game.Player.TakeContactOrHazardDamage(5, Vector3.forward);
                        game.Upgrades.Step(.11f);
                        Assert.That(game.Player.UpgradeProtectionRemaining, Is.GreaterThan(0));
                        note = "Escudo de energía envolvente con celdas luminosas y partículas.";
                        break;
                    case "BIO_04":
                        game.Player.ForceSetHealthForTesting(game.Stats.MaxHealth);
                        game.Player.Heal(15);
                        game.Upgrades.Step(.01f); game.Upgrades.Step(.13f);
                        note = "Nova: onda ámbar radial al acumular curación sobrante.";
                        break;
                    case "GREED_01":
                        for (int i = 0; i < 8; i++) game.Pickups.SpawnExperience(p + new Vector3((i - 4) * .9f, 0, 3.7f), 1);
                        game.Pickups.Step(.12f);
                        note = "Reutiliza la atracción de gemas; no tiene un aura de imán propia.";
                        break;
                    case "GREED_02":
                        game.Pickups.SpawnExperience(p + Vector3.forward * 2, 10); game.Pickups.CollectAll(); hud = true;
                        note = "Solo cambia la experiencia recibida; no tiene un VFX propio.";
                        break;
                    case "GREED_03":
                        game.Player.ForceSetHealthForTesting(50);
                        game.Pickups.SpawnMedkit(p + Vector3.right * 2); game.Pickups.CollectAll();
                        game.Upgrades.Step(.1f); hud = true;
                        note = "Botiquines y aro verde de curación compartidos; cambia la cantidad curada.";
                        break;
                    case "GREED_04":
                        game.Progression.AddExperience(100); game.DraftPresentation.StepPresentation(.8f);
                        note = "Aparece el botón REINTENTAR en las cartas; sin VFX de combate.";
                        break;
                    case "GREED_05":
                        for(int gem=0;gem<48;gem++)
                        {
                            float a=gem*Mathf.PI*2/48f, r=12f+(gem%4)*5f;
                            game.Pickups.SpawnExperience(p+new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r),1);
                        }
                        game.Pickups.SpawnMedkit(p + Vector3.left * 8);
                        game.Upgrades.UpgradeAcquired(card.Id); game.Upgrades.Step(.14f); game.Pickups.Step(.25f);
                        Assert.That(game.Pickups.GlobalFlightCount, Is.GreaterThan(0));
                        note = "Remolino de recogida y XP real viajando hacia el jugador.";
                        break;
                    case "CRIT_01":
                    case "CRIT_02":
                        GalleryHit(main, 10, 1, true); flash.StepPresentation(.02f);
                        note = "Reutiliza el destello y número crítico; no tiene un VFX exclusivo.";
                        break;
                    case "CRIT_03":
                        GalleryHit(main, 945);
                        Assert.That(main.IsAlive, Is.False);
                        note = "Muerte normal al cruzar el umbral; no hay una marca de ejecución propia.";
                        break;
                    case "CRIT_04":
                        var pulse = new PulseResolvedEvent(1, 4, false, 1, 0, 1, p, Vector3.forward, 7, 60);
                        game.Session.Events.PublishPulse(in pulse);
                        game.Upgrades.Step(.12f);
                        note = "Aro ámbar al preparar los críticos de la siguiente ráfaga.";
                        break;
                    case "CRIT_05":
                        GalleryHit(main, 700);
                        game.DamageNumbers.Clear();
                        GalleryHit(main, 10, 1, true);
                        game.Upgrades.Step(.01f); game.Upgrades.Step(.12f);
                        note = "Crítico reforzado y onda ámbar; aro ámbar de aturdimiento.";
                        break;
                    case "DEMO_01":
                        game.Stats.SetUpgradeTier("DASH_04", 1);
                        GalleryDash(p, true); game.Upgrades.Step(.01f); game.Upgrades.Step(.13f);
                        note = "Misma explosión de Cataclismo, con un radio mayor; sin textura propia.";
                        break;
                    case "DEMO_02":
                        for (int i = 0; i < 64 && game.Upgrades.ExplosionsResolved == 0; i++)
                        { var victim = GalleryEnemy(p + Vector3.forward * 3); GalleryHit(victim, 2000); game.Upgrades.Step(.001f); }
                        Assert.That(game.Upgrades.ExplosionsResolved, Is.GreaterThan(0));
                        game.Upgrades.Step(.12f);
                        note = "Onda ámbar que sale del enemigo muerto al activar la cadena.";
                        break;
                    case "DEMO_03":
                        game.Upgrades.Step(4f);
                        game.Motor.transform.position+=Vector3.left*2f;game.Motor.GetComponent<Rigidbody>().position=game.Motor.transform.position;
                        Assert.That(game.Upgrades.ActiveDeviceCount, Is.EqualTo(1));
                        yield return GalleryCapture(camera, folder, "DEMO_03_mina", false);
                        manifest.Add("DEMO_03_mina.png\tDEMO_03\tMinas: colocación\tPieza ámbar dentada en el suelo."); captures++;
                        GalleryEnemy(p + Vector3.forward * .7f);
                        game.Upgrades.Step(.01f); game.Upgrades.Step(.12f);
                        note = "Explosión ámbar de la mina al acercarse un enemigo.";
                        break;
                    case "DEMO_04":
                        game.Stats.SetUpgradeTier("DASH_04", 1);
                        GalleryDash(p, true); game.Upgrades.Step(.01f);
                        game.Upgrades.Step(.41f); game.Upgrades.Step(.1f);
                        note = "Eco turquesa que se contrae después de la explosión inicial.";
                        break;
                    case "SCRAP_01":
                        hud = true;
                        note = "Placas de acero y porcentaje de reducción junto a la barra de vida.";
                        break;
                    case "SCRAP_02":
                        for (int i = 0; i < 15; i++) game.Pickups.SpawnExperience(p + Vector3.right * 6, 1);
                        game.Pickups.CollectAll(); game.Upgrades.Step(.1f);
                        Assert.That(game.Upgrades.ActiveProjectileCount, Is.GreaterThan(0));
                        note = "Proyectil de chatarra guiado, con estela ámbar y luz de impacto.";
                        break;
                    case "SCRAP_03":
                        game.Upgrades.Step(.3f);
                        note = "Sprites de sierra metálica con arcos de viento y chispas al impactar.";
                        break;
                    case "SCRAP_04":
                        game.Upgrades.Step(10f); game.Motor.transform.position+=Vector3.left*2f;game.Motor.GetComponent<Rigidbody>().position=game.Motor.transform.position;game.Upgrades.Step(.09f);
                        note = "Torreta ámbar con cañón orientable y proyectil ámbar.";
                        break;
                    case "CRYO_01":
                        GalleryHit(main, 1); game.Upgrades.Step(.02f);
                        Assert.That(main.SlowRemaining, Is.GreaterThan(0));
                        note = "Tinte celeste persistente y partículas de frío bajo el enemigo ralentizado.";
                        break;
                    case "CRYO_02":
                        game.Stats.SetUpgradeTier("CRYO_01", 1); game.Weapon.ResetWeaponCycle();
                        for (int i = 0; i < 4; i++) GalleryHit(main, 1);
                        game.Upgrades.Step(.02f);
                        Assert.That(main.IsFrozen, Is.True);
                        note = "Escarcha, motas de hielo y niebla fría alrededor del enemigo congelado.";
                        break;
                    case "CRYO_03":
                        game.Upgrades.Step(6f); game.Upgrades.Step(.12f);
                        Assert.That(main.IsFrozen, Is.True);
                        note = "Onda azul radial y cristal de congelación en los alcanzados.";
                        break;
                    case "CRYO_04":
                        game.Stats.SetUpgradeTier("CRYO_01", 1); game.Stats.SetUpgradeTier("CRYO_02", 1); game.Weapon.ResetWeaponCycle();
                        for (int i = 0; i < 4; i++) GalleryHit(main, 1);
                        GalleryHit(main, 2000); game.Upgrades.Step(.1f);
                        Assert.That(game.Upgrades.ProjectilesFired, Is.EqualTo(6));
                        note = "Seis dardos azules salen del enemigo congelado al morir.";
                        break;
                }
                if (card.Id == "CRYO_01" || card.Id == "CRYO_02")
                    for(int step=0;step<12;step++)game.Upgrades.Step(.016f);
                if (card.Id == "SCRAP_03")
                    for(int step=0;step<12;step++)game.Upgrades.Step(.016f);
                main.StepVisuals(.15f);
                game.Player.GetComponent<ActorVisualComposer>().StepVisuals(.15f);
                flash.StepPresentation(0);
                game.DamageNumbers.StepPresentation(.08f);
                if (hud) game.Hud.CanvasRoot.SetActive(true);
                yield return GalleryCapture(camera, folder, card.Id, hud, card.Id == "GREED_04");
                manifest.Add(card.Id + ".png\t" + card.Id + "\t" + card.Name + "\t" + note); captures++;
                if (card.Id=="CRYO_02" || card.Id=="BIO_03" || card.Id=="SCRAP_03" || card.Id=="GREED_05" || card.Id=="FLASH_03" || card.Id=="DEMO_03")
                {
                    for(int frame=0;frame<12;frame++)
                    {
                        game.Upgrades.Step(.05f); game.Pickups.Step(.05f); game.Player.StepTimers(.05f);
                        main.StepVisuals(.05f); game.Player.GetComponent<ActorVisualComposer>().StepVisuals(.05f);
                        yield return GalleryCapture(camera,folder,card.Id+"_motion_"+frame.ToString("00"),false);
                    }
                }
                Debug.Log("[Upgrade gallery] " + card.Id + ": " + note);
            }
            game.ResetRun(false);
            game.Director.AutoTick = game.Encounter.AutoTick = game.Upgrades.AutoTick = game.Pickups.AutoTick = false;
            game.Weapon.enabled=false;game.Hud.CanvasRoot.SetActive(false);game.Stats.SetUpgradeTier("CRIT_03",1);
            for(int channel=0;channel<4;channel++)
            {
                var target=GalleryEnemy(p+new Vector3((channel-1.5f)*3f,0,4));
                bool critical=channel%2==1;
                var request=new DamageRequest(game.Player.TargetId,DamageSourceType.UpgradeProjectile,p,Vector3.forward,20,critical,critical?1f:0f,1.75f,damageType:channel<2?DamageType.Physical:DamageType.Supernatural);
                target.ReceiveDamage(in request);target.StepVisuals(.2f);
            }
            var executed=GalleryEnemy(p+Vector3.forward*7.5f);GalleryHit(executed,945);
            Assert.That(game.DamageNumbers.ActiveNumberCount,Is.EqualTo(5));
            yield return GalleryCapture(camera,folder,"DamageChannels",false);
            File.WriteAllLines(Path.Combine(folder, "Manifest.tsv"), manifest);
            Assert.That(captures, Is.EqualTo(40));
            Object.Destroy(camera.gameObject); source.enabled = true;
        }
        private EnemyActor GalleryEnemy(Vector3 position)
        {
            var data = EnemyArchetypeData.CreateDefault(EnemyArchetypeId.Residual);
            data.MaxHealth = 1000; data.BaseMoveSpeed = 0; data.ContactDamage = 0; data.SpeedVarianceFraction = 0;
            var enemy = game.Enemies.Spawn(data, position);
            enemy.enabled = false;
            CombatTargetRegistry.Register(enemy); // Freeze locomotion while keeping real attack/status targeting.
            return enemy;
        }
        private void GalleryHit(EnemyActor enemy, float damage, int index = 1, bool critical = false)
        {
            var hit = new DamageRequest(game.Player.TargetId, DamageSourceType.FlashlightPulse, game.Player.WorldCenter,
                enemy.WorldCenter-game.Player.WorldCenter, damage, critical, critical ? 1f : 0f, game.Stats.CritMultiplier,
                burstId:game.Weapon.CurrentBurstId, pulseIndex:index);
            enemy.ReceiveDamage(in hit);
        }
        private void GalleryDash(Vector3 p, bool start)
        {
            var dash = new DashStateEvent(start,!start,false,start,false,p,Vector3.forward,.5f,.5f);
            game.Session.Events.PublishDash(in dash);
        }
        private IEnumerator GalleryCapture(Camera camera, string folder, string name, bool hud, bool draft = false)
        {
            game.Session.SetManualPause(true);
            bool hudEnabled = game.Hud.enabled;
            game.Hud.RefreshNow();
            game.Hud.enabled = false;
            game.Hud.CanvasRoot.transform.Find("PauseAndResultOverlay").gameObject.SetActive(false);
            var target = new RenderTexture(1400, 900, 24);
            var prior = camera.targetTexture; var active = RenderTexture.active;
            var canvases = new List<Canvas> {game.DamageNumbers.NumberCanvas};
            if (hud) canvases.AddRange(game.Hud.CanvasRoot.GetComponentsInChildren<Canvas>());
            if (draft) canvases.Add(game.DraftPresentation.CanvasRoot.GetComponent<Canvas>());
            var modes = new List<RenderMode>();
            foreach (var c in canvases) {modes.Add(c.renderMode); c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
            try
            {
                camera.targetTexture=target;
                Canvas.ForceUpdateCanvases(); yield return null; yield return null; yield return null;
                game.DamageNumbers.RefreshProjection(); Canvas.ForceUpdateCanvases(); yield return null;
                RenderTexture.active=target;
                var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG()); Object.Destroy(image);
            }
            finally
            {
                for(int i=0;i<canvases.Count;i++)canvases[i].renderMode=modes[i];
                camera.targetTexture=prior;RenderTexture.active=active;target.Release();Object.Destroy(target);
                game.Hud.enabled = hudEnabled;
                game.Session.SetManualPause(false);
            }
        }
        private IEnumerator Capture(string name,bool ui)
        {
            var camera=game.GameplayCamera;var target=new RenderTexture(1600,900,24);
            var prior=camera.targetTexture;var active=RenderTexture.active;
            var data=camera.GetComponent<UniversalAdditionalCameraData>();bool post=data.renderPostProcessing;var aa=data.antialiasing;
            Canvas canvas=ui?game.DraftPresentation.CanvasRoot.GetComponent<Canvas>():null;
            if(canvas!=null){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            Texture2D image=null;
            try
            {
                camera.targetTexture=target;
                if(ui){data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;}
                Canvas.ForceUpdateCanvases();yield return null;yield return null;yield return null;
                RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string folder=Path.Combine(Application.dataPath,"..","Logs","CompleteUpgradesVisuals");Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=prior;RenderTexture.active=active;data.renderPostProcessing=post;data.antialiasing=aa;
                if(canvas!=null)canvas.renderMode=RenderMode.ScreenSpaceOverlay;
                target.Release();Object.Destroy(target);if(image!=null)Object.Destroy(image);
            }
        }
    }
}
