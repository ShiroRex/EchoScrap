using System.Linq;
using EchoScrap.Combat;
using NUnit.Framework;
using UnityEngine;
namespace EchoScrap.Tests
{
 public sealed class FusionRulesTests
 {
  private static FusionProgress Stock(){var p=new FusionProgress();for(int i=0;i<FusionCatalog.Count;i++)p.owned[i]=20;return p;}
  [Test] public void CapacityCountsEquippedAndDuplicateUnitsOnlyOnce(){var p=new FusionProgress();p.owned[0]=9;p.equipped=new[]{0,0,0};Assert.AreEqual(9,p.OwnedCount);Assert.IsTrue(p.TryCollect(8));Assert.AreEqual(10,p.OwnedCount);Assert.IsFalse(p.TryCollect(0));Assert.IsFalse(p.TryCollect(4));Assert.AreEqual(9,p.owned[0]);p.owned[0]--;Assert.IsTrue(p.TryCollect(4));Assert.AreEqual(10,p.OwnedCount);}
  [Test] public void FullInventoryKeepsFusionResultUntilSpaceIsFreed(){var p=new FusionProgress();p.owned[0]=4;Assert.IsTrue(p.Begin(new[]{0,0,0,0},9999,0));for(int i=0;i<10;i++)Assert.IsTrue(p.TryCollect(0));p.Advance(30);p.OpenReady();p.Advance(2);int result=p.result;Assert.IsFalse(p.Resolve(true));Assert.AreEqual(result,p.result);Assert.AreEqual(FusionPhase.AwaitingDecision,p.phase);Assert.AreEqual(10,p.OwnedCount);p.owned[0]--;Assert.IsTrue(p.Resolve(true));Assert.AreEqual(10,p.OwnedCount);Assert.AreEqual(1,p.owned[result]);Assert.IsFalse(p.Resolve(true));}
  [Test] public void OverflowLegacySaveIsPreservedButCannotCollectMore(){var p=Stock();int count=p.OwnedCount;p.Validate();Assert.AreEqual(count,p.OwnedCount);Assert.IsFalse(p.TryCollect(4));Assert.AreEqual(count,p.OwnedCount);Assert.IsFalse(p.TryCollect(-1));Assert.IsFalse(p.TryCollect(FusionCatalog.Count));}
  [Test] public void CatalogHasFifteenCharactersAndSeparateSources(){Assert.AreEqual(15,FusionCatalog.Count);Assert.AreEqual(15,FusionCatalog.Names.Distinct().Count());CollectionAssert.AreEquivalent(Enumerable.Range(0,15),FusionCatalog.MapIds.Concat(FusionCatalog.FusionIds));Assert.IsFalse(FusionCatalog.MapIds.Intersect(FusionCatalog.FusionIds).Any());Assert.AreEqual(CollectibleRarity.Mythic,FusionCatalog.Rarities[4]);}
  [Test] public void ExistingThirteenCharacterSaveKeepsInventoryEquipmentAndFusion(){var p=new FusionProgress{owned=Enumerable.Repeat(2,13).ToArray(),equipped=new[]{9,6,4},phase=FusionPhase.Fusing,result=12,remaining=75,duration=180};p.Validate();Assert.AreEqual(15,p.owned.Length);Assert.AreEqual(26,p.owned.Sum());Assert.AreEqual(0,p.owned[13]);Assert.AreEqual(0,p.owned[14]);CollectionAssert.AreEqual(new[]{9,6,4},p.equipped);Assert.AreEqual(FusionPhase.Fusing,p.phase);Assert.AreEqual(12,p.result);Assert.AreEqual(30,p.remaining);Assert.AreEqual(30,p.duration);Assert.IsTrue(p.hasEquippedCompanion);}
  [Test] public void SoldierAndJoelShareTheirMapRarityAndStayOutOfFusionOutputs(){Assert.AreEqual(CollectibleRarity.Special,FusionCatalog.Rarities[13]);Assert.AreEqual(CollectibleRarity.Rare,FusionCatalog.Rarities[14]);Assert.AreEqual(5,Enumerable.Range(0,100).Count(roll=>FusionCatalog.RollMap(roll)==13));Assert.AreEqual(10,Enumerable.Range(0,100).Count(roll=>FusionCatalog.RollMap(roll)==14));}
  [Test] public void MapRaritiesHaveExactRequestedOdds(){var counts=new int[6];for(int roll=0;roll<100;roll++){int id=FusionCatalog.RollMap(roll);Assert.IsTrue(FusionCatalog.CanSpawn(id));counts[(int)FusionCatalog.Rarities[id]]++;}CollectionAssert.AreEqual(new[]{50,30,10,7,3,0},counts);}
  [TestCase(new[]{0,0,0,0},0,30)]
  [TestCase(new[]{6,11,6,11},1,30)]
  [TestCase(new[]{7,0,0,0},1,30)]
  [TestCase(new[]{7,12,0,0},5,30)]
  [TestCase(new[]{7,12,7,0},15,30)]
  [TestCase(new[]{7,12,7,12},30,30)]
  public void MythicOddsMatchLegendaryCount(int[] input,int mythic,int seconds){Assert.IsTrue(FusionRecipe.TryCreate(input,out var r));Assert.AreEqual(mythic,r.ResultPercent(4));Assert.AreEqual(seconds,r.Seconds);}
  [Test] public void AllRecipesSumToOneHundredAndOnlyProduceFusionSpecies(){
   int[] ids=Enumerable.Range(0,15).Where(id=>id!=4).ToArray();
   foreach(int a in ids)foreach(int b in ids)foreach(int c in ids)foreach(int d in ids){var input=new[]{a,b,c,d};Assert.IsTrue(FusionRecipe.TryCreate(input,out var r));Assert.AreEqual(10000,Enumerable.Range(0,15).Sum(r.ResultWeight));Assert.AreEqual(30,r.Seconds);foreach(int id in FusionCatalog.MapIds)Assert.AreEqual(0,r.ResultWeight(id));}
  }
  [TestCase(0,8,9)] [TestCase(1,9,10)] [TestCase(3,10,11)] public void HomogeneousLowerTiersOnlyOfferCurrentAndNextTier(int input,int same,int next){FusionRecipe.TryCreate(new[]{input,input,input,input},out var r);Assert.AreEqual(60,r.ResultPercent(same));Assert.AreEqual(40,r.ResultPercent(next));Assert.AreEqual(2,FusionCatalog.FusionIds.Count(id=>r.ResultWeight(id)>0));}
  [Test] public void EveryRollMatchesDisplayedProbabilities(){foreach(var input in new[]{new[]{0,0,0,0},new[]{1,2,3,6},new[]{7,12,0,0},new[]{6,11,6,11},new[]{7,7,7,12}}){FusionRecipe.TryCreate(input,out var r);var count=new int[15];for(int roll=0;roll<10000;roll++)count[r.Roll(roll,0)]++;for(int id=0;id<15;id++)Assert.AreEqual(r.ResultWeight(id),count[id]);}}
  [Test] public void ProductionStartsEmptyAndPrototypeInventoryIsDiscarded(){var p=new FusionProgress();p.Validate();Assert.AreEqual(0,p.owned.Sum());Assert.IsTrue(p.equipped.All(id=>id==-1));p.version=3;p.owned=Enumerable.Repeat(999,5).ToArray();p.Validate();Assert.AreEqual(15,p.owned.Length);Assert.AreEqual(0,p.owned.Sum());}
  [Test] public void FusionConsumesExactlyFourAvailableCopiesAndProtectsEquipped(){var p=new FusionProgress();p.owned[0]=4;p.equipped[0]=0;Assert.IsFalse(p.Begin(new[]{0,0,0,0},0,0));Assert.AreEqual(4,p.owned[0]);p.equipped[0]=-1;Assert.IsTrue(p.Begin(new[]{0,0,0,0},0,0));Assert.AreEqual(0,p.owned[0]);Assert.IsFalse(p.Begin(new[]{0,0,0,0},0,0));p.Advance(30);Assert.IsTrue(p.OpenReady());p.Advance(2);Assert.IsTrue(p.Resolve(true));Assert.AreEqual(1,p.owned[8]);Assert.IsFalse(p.Resolve(true));}
  [Test] public void MythicIsProtectedAndInvalidRecipesAreRejected(){var p=Stock();foreach(var input in new[]{new[]{4,0,0,0},new[]{-1,0,0,0},new[]{15,0,0,0},new[]{0,0},new int[0]})Assert.IsFalse(p.Begin(input,0,0));Assert.IsFalse(FusionRecipe.TryCreate(null,out _));}
  [Test] public void MixedPreviewIsOrderIndependent(){Assert.IsTrue(FusionRecipe.TryPreview(new[]{7,12,0},out var a));Assert.IsTrue(FusionRecipe.TryPreview(new[]{0,12,7},out var b));Assert.AreEqual(a.Odds,b.Odds);Assert.AreEqual(5,a.Mythic);}
  [Test] public void EpicRecipeUsesDocumentedLabels(){Assert.IsTrue(FusionRecipe.TryPreview(new[]{6,11,6},out var r));Assert.AreEqual(5940,r.ResultWeight(11));Assert.AreEqual(60,r.DisplayPercent(11));Assert.AreEqual(40,r.DisplayPercent(12));Assert.AreEqual(1,r.DisplayPercent(4));}
  [Test] public void SaveReloadDoesNotRefundConsumedItemsOrAutoOpen(){var p=Stock();p.Begin(new[]{7,12,7,12},0,0);p.Advance(10);var copy=JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(p));copy.Validate();Assert.AreEqual(20,copy.remaining);Assert.AreEqual(18,copy.owned[7]);copy.Advance(20);copy.Advance(1000);Assert.AreEqual(FusionPhase.Ready,copy.phase);Assert.IsFalse(copy.Resolve(true));Assert.IsTrue(copy.OpenReady());Assert.IsFalse(copy.OpenReady());copy.Advance(2);Assert.IsTrue(copy.Resolve(false));Assert.AreEqual(20,copy.owned[4]);}
  [Test] public void InterruptedOpeningResumesAtCollection()
  {
   var progress = new FusionProgress { phase = FusionPhase.Opening, opening = .4f, result = 8 };
   var saved = JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(progress));
   saved.RestoreAfterLoading();
   Assert.AreEqual(FusionPhase.AwaitingDecision, saved.phase);
   Assert.IsTrue(saved.Resolve(true));
   Assert.AreEqual(1, saved.owned[8]);
  }
  [Test] public void NonpositiveOrInvalidTimeCannotFinishFusion(){var p=Stock();p.Begin(new[]{0,0,0,0},9999,0);p.Advance(-1);p.Advance(float.NaN);p.Advance(float.PositiveInfinity);Assert.AreEqual(30,p.remaining);}
  [Test] public void DiscoveredStatePersistsEvenAfterFusionConsumesUnits(){var p=new FusionProgress();p.Validate();Assert.AreEqual(0,p.DiscoveredCount);for(int i=0;i<FusionCatalog.Count;i++)Assert.IsFalse(p.IsDiscovered(i));for(int i=0;i<4;i++)Assert.IsTrue(p.TryCollect(0));Assert.IsTrue(p.IsDiscovered(0));Assert.AreEqual(1,p.DiscoveredCount);Assert.IsTrue(p.Begin(new[]{0,0,0,0},0,0));Assert.AreEqual(0,p.owned[0]);Assert.IsTrue(p.IsDiscovered(0));p.Advance(30);Assert.IsTrue(p.OpenReady());p.Advance(2);Assert.IsTrue(p.Resolve(true));Assert.IsTrue(p.IsDiscovered(0));Assert.IsTrue(p.IsDiscovered(8));Assert.AreEqual(2,p.DiscoveredCount);}
 }
}
