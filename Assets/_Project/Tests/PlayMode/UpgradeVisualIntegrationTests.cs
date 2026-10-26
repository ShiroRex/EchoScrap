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
