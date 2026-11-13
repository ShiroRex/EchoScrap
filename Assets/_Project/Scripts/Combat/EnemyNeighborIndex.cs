using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Reusable planar broad phase. Results retain registration order and exact radius checks.</summary>
    public sealed class EnemyNeighborIndex
    {
        private const float CellSize=4f;
        private readonly Dictionary<Vector2Int,List<int>> cells=new Dictionary<Vector2Int,List<int>>();
        private readonly Stack<List<int>> spare=new Stack<List<int>>();
        private readonly List<Vector3> positions=new List<Vector3>();
        public int LastCandidatesExamined {get;private set;}
        private static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.z/CellSize));
        public Vector3 PositionAt(int index) => positions[index];
        public bool IsDense(Vector3 position)=>cells.TryGetValue(Cell(position),out var bucket)&&bucket.Count>=32;
        public void Clear(){foreach(var bucket in cells.Values){bucket.Clear();spare.Push(bucket);}cells.Clear();positions.Clear();}
        public void Add(Vector3 position)
        {
            var key=Cell(position);if(!cells.TryGetValue(key,out var bucket)){bucket=spare.Count>0?spare.Pop():new List<int>(16);cells.Add(key,bucket);}
            bucket.Add(positions.Count);positions.Add(position);
        }
        public void Query(Vector3 position,float radius,List<int> results)
        {
            results.Clear();LastCandidatesExamined=0;var min=Cell(position-new Vector3(radius,0,radius));var max=Cell(position+new Vector3(radius,0,radius));float squared=radius*radius;
            for(int z=min.y;z<=max.y;z++)for(int x=min.x;x<=max.x;x++){
                if(!cells.TryGetValue(new Vector2Int(x,z),out var bucket))continue;
                for(int i=0;i<bucket.Count;i++){int index=bucket[i];LastCandidatesExamined++;Vector3 delta=position-positions[index];delta.y=0;if(delta.sqrMagnitude<squared)results.Add(index);}
            }
            results.Sort();
        }
    }
}
