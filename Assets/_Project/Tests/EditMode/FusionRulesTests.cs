using EchoScrap.Combat;
using NUnit.Framework;
using UnityEngine;
namespace EchoScrap.Tests
{
    public sealed class FusionRulesTests
    {
        [TestCase(new[]{0,0,0},60,0)]
        [TestCase(new[]{0,1,1},120,0)]
        [TestCase(new[]{2,0,0},180,0)]
        [TestCase(new[]{2,2,0},180,0)]
        [TestCase(new[]{2,2,1},180,1)]
        [TestCase(new[]{2,3,3},180,1)]
        public void RecipesUseQualityAndNeverExceedThreeMinutes(int[] ids,int seconds,int legend)
        {Assert.IsTrue(FusionRecipe.TryCreate(ids,out var recipe));Assert.AreEqual(seconds,recipe.Seconds);Assert.AreEqual(legend,recipe.Legendary);Assert.AreEqual(100,recipe.Common+recipe.Rare+recipe.Epic+recipe.Legendary);}
        [Test] public void EveryRecipeHasExactOddsAndLegendaryRequiresTwoEpicsAndRare()
        {
            for(int a=0;a<4;a++)for(int b=0;b<4;b++)for(int c=0;c<4;c++){
                var ids=new[]{a,b,c};Assert.IsTrue(FusionRecipe.TryCreate(ids,out var r));int[] counts=new int[4];
                for(int roll=0;roll<10000;roll++)counts[(int)FusionCatalog.Rarities[r.Roll(roll,roll)]]++;
                CollectionAssert.AreEqual(new[]{r.Common*100,r.Rare*100,r.Epic*100,r.Legendary*100},counts);
                int epics=0,rares=0;foreach(int id in ids){if(id>=2)epics++;if(id==1)rares++;}
                Assert.AreEqual(epics>=2&&epics+rares==3?100:0,counts[3]);
            }
        }
        [Test] public void LegendaryIsProtectedAndInvalidRecipesAreRejected()
        {foreach(var ids in new[]{new[]{4,2,3},new[]{-1,2,3},new[]{9,2,3},new[]{0,0},new int[0]})Assert.IsFalse(FusionRecipe.TryCreate(ids,out _));Assert.IsFalse(FusionRecipe.TryCreate(null,out _));}
        [Test] public void DuplicateInputsResultsAndOverflowStayInInventory()
        {
            var p=new FusionProgress();Assert.AreEqual(0,p.owned[4]);
            for(int i=0;i<12;i++){Assert.IsTrue(p.Begin(new[]{0,0,0},9999,0));Assert.IsFalse(p.Begin(new[]{2,2,2},0,0));p.Advance(60);Assert.AreEqual(FusionPhase.Opening,p.phase);Assert.IsFalse(p.Resolve(true));p.Advance(1.2f);Assert.IsTrue(p.Resolve(true));Assert.IsFalse(p.Resolve(true));}
            Assert.AreEqual(13,p.owned[0]);Assert.AreEqual(8,p.displays.Length);Assert.AreEqual(0,p.displays[7]);
            p.Begin(new[]{2,3,1},0,0);p.Advance(180);p.Advance(1.2f);p.Resolve(true);
            CollectionAssert.Contains(p.displays,4);Assert.AreEqual(13,p.owned[0]);Assert.AreEqual(1,p.owned[4]);
            foreach(int initial in new[]{0,1,2,3})CollectionAssert.Contains(p.displays,initial);
        }
        [Test] public void ReleaseDiscardsWithoutAddingOwnedOrDisplay()
        {var p=new FusionProgress();p.Begin(new[]{2,3,1},0,0);p.Advance(180);p.Advance(1.2f);Assert.AreEqual(4,p.result);Assert.IsTrue(p.Resolve(false));Assert.AreEqual(0,p.owned[4]);Assert.AreEqual(-1,p.displays[2]);Assert.AreEqual(-1,p.result);}
        [Test] public void SaveRoundTripPreservesPendingResultAndRemainingTime()
        {var p=new FusionProgress();p.Begin(new[]{2,3,1},0,0);p.Advance(75);var q=JsonUtility.FromJson<FusionProgress>(JsonUtility.ToJson(p));q.Validate();Assert.AreEqual(105,q.remaining);Assert.AreEqual(4,q.result);q.Advance(105);q.Advance(1.2f);q.Resolve(true);Assert.AreEqual(1,q.owned[4]);Assert.AreEqual(4,q.displays[2]);}
        [Test] public void NonpositiveOrInvalidTimeCannotFinishFusion()
        {var p=new FusionProgress();p.Begin(new[]{0,0,0},5,0);p.Advance(-100);p.Advance(float.NaN);p.Advance(float.PositiveInfinity);Assert.AreEqual(60,p.remaining);}
    }
}
