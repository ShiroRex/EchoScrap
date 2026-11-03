using System.Collections.Generic;
using System.Diagnostics;
using EchoScrap.Combat;
using NUnit.Framework;
using UnityEngine;

namespace EchoScrap.Tests
{
    public sealed class EnemyNeighborIndexTests
    {
        [Test] public void QueriesMatchFullScanIncludingNegativeCoordinatesBoundariesAndRebuilds()
        {
            var index=new EnemyNeighborIndex();var points=new List<Vector3>();var random=new System.Random(917);
            for(int i=0;i<1000;i++){var p=new Vector3((float)random.NextDouble()*200-100,i%5,(float)random.NextDouble()*200-100);points.Add(p);index.Add(p);}
            var actual=new List<int>();var expected=new List<int>();
            foreach(float radius in new[]{.1f,1.5f,4f,12f})foreach(var p in points){expected.Clear();for(int i=0;i<points.Count;i++){var d=points[i]-p;d.y=0;if(d.sqrMagnitude<radius*radius)expected.Add(i);}index.Query(p,radius,actual);CollectionAssert.AreEqual(expected,actual);}
            index.Clear();index.Add(Vector3.zero);index.Add(Vector3.right*2);index.Query(Vector3.zero,2,actual);CollectionAssert.AreEqual(new[]{0},actual);
            index.Query(Vector3.right*2,.1f,actual);CollectionAssert.AreEqual(new[]{1},actual);
        }
        [Test] public void ThousandEnemyBroadPhaseReducesChecksWithoutDroppingNeighbors()
        {
            var index=new EnemyNeighborIndex();var points=new Vector3[1000];var found=new List<int>(64);
            for(int i=0;i<points.Length;i++){points[i]=new Vector3(i%40*4,0,i/40*4);index.Add(points[i]);}
            int checkedCount=0,neighbors=0;var timer=Stopwatch.StartNew();
            foreach(var p in points){index.Query(p,3,found);checkedCount+=index.LastCandidatesExamined;neighbors+=found.Count;}
            timer.Stop();Assert.AreEqual(1000,neighbors);Assert.Less(checkedCount,10000);
            UnityEngine.Debug.Log($"NEIGHBOR_BENCHMARK 1000 enemies: {checkedCount} candidates vs 1000000 full-scan checks; {timer.Elapsed.TotalMilliseconds:0.###} ms queries.");
            index.Clear();for(int i=0;i<1000;i++)index.Add(Vector3.zero);Assert.IsTrue(index.IsDense(Vector3.zero),"Dense crowds keep the original early-exit path rather than sorting the entire crowd.");
        }
    }
}
