using System;
using System.Linq;
using UnityEngine;
namespace EchoScrap.Combat
{
    public enum CollectibleRarity { Common, Rare, Special, Epic, Legendary, Mythic }
    public enum FusionPhase { Idle, Fusing, Opening, AwaitingDecision, Ready }
    public static class FusionCatalog
    {
        public const int Count=15, Yanikua=4;
        public static readonly string[] Names={"Lobito","Mago","Demonio","Shireen","Yanikua","Basket","Hada","Lisa","Reaper","Chico Emo","Conde Alucard","Murciélago Cristal","Sirena","Soldier","Joel"};
        public static readonly string[] ResourceKeys={"Lobito","Mago","Demonio","Shireen","Yanikua","Basket","Hada","Lisa","Reaper","Chico_Emo","Conde_Alucard","MurcielagoCristal","Sirena","Soldier","Joel"};
        public static readonly CollectibleRarity[] Rarities={CollectibleRarity.Common,CollectibleRarity.Rare,CollectibleRarity.Rare,CollectibleRarity.Special,CollectibleRarity.Mythic,CollectibleRarity.Common,CollectibleRarity.Epic,CollectibleRarity.Legendary,CollectibleRarity.Common,CollectibleRarity.Rare,CollectibleRarity.Special,CollectibleRarity.Epic,CollectibleRarity.Legendary,CollectibleRarity.Special,CollectibleRarity.Rare};
        public static readonly string[] RarityNames={"COMÚN","RARO","ESPECIAL","ÉPICO","LEGENDARIO","MÍTICO"};
        public static readonly int[] MapIds={5,0,1,2,14,3,13,6,7};
        public static readonly int[] FusionIds={8,9,10,11,12,4};
        private static readonly int[] MapWeights={25,25,10,10,10,5,5,7,3};
        private static readonly Color[] Colors={new Color(.3f,.85f,.35f),new Color(.2f,.55f,1),new Color(1,.45f,.08f),new Color(.7f,.25f,1),new Color(1,.95f,.1f),new Color(1,.67f,.08f)};
        public static bool Valid(int id)=>id>=0&&id<Count;
        public static Color ColorOf(int id)=>Valid(id)?Colors[(int)Rarities[id]]:Color.gray;
        public static bool CanSpawn(int id)=>Array.IndexOf(MapIds,id)>=0;
        public static int RollMap(int roll){if(roll<0||roll>=100)throw new ArgumentOutOfRangeException(nameof(roll));for(int i=0;i<MapIds.Length;i++){if(roll<MapWeights[i])return MapIds[i];roll-=MapWeights[i];}return 7;}
    }
    public readonly struct FusionRecipe
    {
        public const int FusionSeconds = 30;
        public readonly int Seconds;
        private readonly int[] weights;
        public int Common=>ResultWeight(8)/100;
        public int Rare=>ResultWeight(9)/100;
        public int Special=>ResultWeight(10)/100;
        public int Epic=>ResultWeight(11)/100;
        public int Legendary=>ResultWeight(12)/100;
        public int Mythic=>ResultWeight(4)/100;
        private FusionRecipe(int[] values)
        {
            Seconds = FusionSeconds;
            weights = values;
        }
        public string Odds { get { var recipe=this;return string.Join(" · ",FusionCatalog.FusionIds.Where(id=>recipe.ResultWeight(id)>0).Select(id=>FusionCatalog.Names[id]+" "+recipe.ResultPercent(id).ToString("0.##")+"%")); } }
        public static bool TryCreate(int[] ingredients,out FusionRecipe recipe){recipe=default;return ingredients!=null&&ingredients.Length==4&&TryPreview(ingredients,out recipe);}
        public static bool IsIngredient(int id) => FusionCatalog.Valid(id) && id != FusionCatalog.Yanikua;
        public static bool TryPreview(int[] ingredients,out FusionRecipe recipe)
        {
            recipe=default;if(ingredients==null||ingredients.Length==0||ingredients.Length>4)return false;
            int legends=0,total=0,bestQuality=0;
            foreach(int id in ingredients){if(!IsIngredient(id))return false;int quality=(int)FusionCatalog.Rarities[id];total+=quality;bestQuality=Mathf.Max(bestQuality,quality);if(quality==4)legends++;}
            int mythic=legends==4?30:legends==3?15:legends==2?5:bestQuality>=3?1:0;
            float qualityMean=total/(float)ingredients.Length;
            // Each quality band offers its own tier and the next; mixed inputs interpolate those bands.
            int[,] table={{60,40,0,0,0},{0,60,40,0,0},{0,0,60,40,0},{0,0,0,60,40},{0,0,0,0,100}};
            int low=Mathf.FloorToInt(qualityMean),high=Mathf.Min(4,low+1),remaining=10000-mythic*100;
            var w=new int[FusionCatalog.Count];float[] fractions=new float[5];int assigned=0;
            for(int i=0;i<5;i++){float exact=Mathf.Lerp(table[low,i],table[high,i],qualityMean-low)*remaining/100;int value=Mathf.FloorToInt(exact);w[FusionCatalog.FusionIds[i]]=value;fractions[i]=exact-value;assigned+=value;}
            while(assigned<remaining){int best=0;for(int i=1;i<5;i++)if(fractions[i]>fractions[best])best=i;w[FusionCatalog.FusionIds[best]]++;fractions[best]=-1;assigned++;}
            w[4]=mythic*100;recipe=new FusionRecipe(w);return true;
        }
        public int ResultWeight(int id)=>weights!=null&&FusionCatalog.Valid(id)?weights[id]:0;
        public float ResultPercent(int id)=>ResultWeight(id)/100f;
        public int DisplayPercent(int id)
        {
            int weight = ResultWeight(id);
            // The documented epic recipe displays Crystal Bat as 60% while drawing at 59.4%.
            return id == 11 && weight == 5940 ? 60 : Mathf.RoundToInt(weight / 100f);
        }
        public int Roll(int roll,int variant)
        {
            if(roll<0||roll>=10000)throw new ArgumentOutOfRangeException(nameof(roll));
            // The first interval remains the explicitly advertised mythic chance.
            if(roll<ResultWeight(4))return 4;roll-=ResultWeight(4);
            foreach(int id in FusionCatalog.FusionIds){if(id==4)continue;if(roll<ResultWeight(id))return id;roll-=ResultWeight(id);}return 12;
        }
    }
    [Serializable]
    public sealed class FusionProgress
    {
        public const int Capacity=10;
        // Equipped copies already belong to owned; count each unit once, including duplicates.
        public int OwnedCount { get { long total=0;if(owned!=null)foreach(int count in owned)total+=Math.Max(0,count);return (int)Math.Min(int.MaxValue,total); } }
        public int DiscoveredCount { get { int total=0;for(int i=0;i<FusionCatalog.Count;i++)if(IsDiscovered(i))total++;return total; } }
        public bool HasSpace=>OwnedCount<Capacity;
        public bool IsDiscovered(int id)=>FusionCatalog.Valid(id)&&((discovered!=null&&id<discovered.Length&&discovered[id])||(owned!=null&&id<owned.Length&&owned[id]>0)||(equipped!=null&&Array.IndexOf(equipped,id)>=0));
        public bool TryCollect(int id)
        {
            if(!FusionCatalog.Valid(id)||owned==null||id>=owned.Length||!HasSpace)return false;
            owned[id]++;
            if(discovered==null||discovered.Length!=FusionCatalog.Count)Array.Resize(ref discovered,FusionCatalog.Count);
            discovered[id]=true;
            return true;
        }
        public int version=4;
        public bool hasEquippedCompanion;
        public int[] equipped={-1,-1,-1};
        public FusionPhase phase;
        public float remaining,duration,opening;
        public int result=-1;
        public int[] owned=new int[FusionCatalog.Count];
        public bool[] discovered=new bool[FusionCatalog.Count];
        public int[] displays={-1,-1,-1,-1,-1,-1,-1,-1,-1,-1};
        public int Available(int id)=>FusionCatalog.Valid(id)?Mathf.Max(0,owned[id]-equipped.Count(value=>value==id)):0;
        public bool CanBegin(int[] ingredients, int[] equipmentSlots = null)
        {
            if (phase != FusionPhase.Idle || !FusionRecipe.TryCreate(ingredients, out _) ||
                equipmentSlots != null && equipmentSlots.Length != ingredients.Length) return false;
            var usedFree = new int[FusionCatalog.Count];
            int usedSlots = 0;
            for (int i = 0; i < ingredients.Length; i++)
            {
                int id = ingredients[i];
                int slot = equipmentSlots == null ? -1 : equipmentSlots[i];
                if (slot == -1)
                {
                    if (++usedFree[id] > Available(id)) return false;
                    continue;
                }
                if (slot < 0 || slot >= equipped.Length || equipped[slot] != id || (usedSlots & (1 << slot)) != 0)
                    return false;
                usedSlots |= 1 << slot;
            }
            return true;
        }
        public bool Begin(int[] ingredients,int roll,int variant,int[] equipmentSlots=null)
        {
            if(!CanBegin(ingredients,equipmentSlots)||!FusionRecipe.TryCreate(ingredients,out var recipe))return false;
            int outcome=recipe.Roll(roll,variant);
            if (equipmentSlots != null)
                foreach (int slot in equipmentSlots)
                    if (slot >= 0) equipped[slot] = -1;
            foreach(int id in ingredients)owned[id]--;
            result=outcome;duration=remaining=recipe.Seconds;opening=0;phase=FusionPhase.Fusing;return true;
        }
        public bool Advance(float seconds)
        {
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return false;var before=phase;
            if(phase==FusionPhase.Fusing){remaining=Mathf.Max(0,remaining-seconds);if(remaining<=0){phase=FusionPhase.Ready;opening=0;}}
            else if(phase==FusionPhase.Opening){opening=Mathf.Min(1,opening+seconds/1.2f);if(opening>=1)phase=FusionPhase.AwaitingDecision;}
            return before!=phase;
        }
        public bool OpenReady(){if(phase!=FusionPhase.Ready)return false;opening=0;phase=FusionPhase.Opening;return true;}
        public bool Resolve(bool collect)
        {
            if(phase!=FusionPhase.AwaitingDecision||!FusionCatalog.Valid(result))return false;
            if(collect&&!TryCollect(result))return false;
            if(discovered==null||discovered.Length!=FusionCatalog.Count)Array.Resize(ref discovered,FusionCatalog.Count);
            discovered[result]=true;
            result=-1;remaining=duration=opening=0;phase=FusionPhase.Idle;return true;
        }
        public void RestoreAfterLoading()
        {
            Validate();
            if (phase == FusionPhase.Opening)
            {
                phase = FusionPhase.AwaitingDecision;
                opening = 1f;
            }
        }
        public void Validate()
        {
            // Prototype collections are deliberately discarded once; production saves keep all counts.
            if(version<4||owned==null){owned=new int[FusionCatalog.Count];discovered=new bool[FusionCatalog.Count];equipped=new[]{-1,-1,-1};phase=FusionPhase.Idle;result=-1;hasEquippedCompanion=false;}
            if(owned.Length!=FusionCatalog.Count)Array.Resize(ref owned,FusionCatalog.Count);
            if(discovered==null||discovered.Length!=FusionCatalog.Count)Array.Resize(ref discovered,FusionCatalog.Count);
            for(int i=0;i<owned.Length;i++){owned[i]=Mathf.Max(0,owned[i]);if(owned[i]>0)discovered[i]=true;}
            if(displays==null||displays.Length!=10)displays=Enumerable.Repeat(-1,10).ToArray();
            for(int i=0;i<displays.Length;i++)if(!FusionCatalog.Valid(displays[i])||owned[displays[i]]==0)displays[i]=-1;
            if(phase<FusionPhase.Idle||phase>FusionPhase.Ready||(phase!=FusionPhase.Idle&&!FusionCatalog.Valid(result))){phase=FusionPhase.Idle;result=-1;}
            if(equipped==null||equipped.Length!=3)equipped=new[]{-1,-1,-1};var used=new int[FusionCatalog.Count];
            for(int i=0;i<3;i++){int id=equipped[i];if(!FusionCatalog.Valid(id)||++used[id]>owned[id])equipped[i]=-1;else discovered[id]=true;}
            hasEquippedCompanion |= Array.Exists(equipped, FusionCatalog.Valid);
            version = 4;
            duration = phase == FusionPhase.Idle ? 0f : FusionRecipe.FusionSeconds;
            if (float.IsNaN(remaining) || float.IsInfinity(remaining)) remaining = duration;
            remaining = phase == FusionPhase.Fusing ? Mathf.Clamp(remaining, 0f, duration) : 0f;
            opening = float.IsNaN(opening) ? 0f : Mathf.Clamp01(opening);
        }
    }
}
