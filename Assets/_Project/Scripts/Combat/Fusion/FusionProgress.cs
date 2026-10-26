using System;
using UnityEngine;
namespace EchoScrap.Combat
{
    public enum CollectibleRarity { Common, Rare, Epic, Legendary }
    public enum FusionPhase { Idle, Fusing, Opening, AwaitingDecision }
    public static class FusionCatalog
    {
        public static readonly string[] Names={"Lobito","Mago","Demonio","Shireen","Yanikua"};
        public static readonly CollectibleRarity[] Rarities={CollectibleRarity.Common,CollectibleRarity.Rare,CollectibleRarity.Epic,CollectibleRarity.Epic,CollectibleRarity.Legendary};
        public static readonly string[] RarityNames={"COMÚN","RARO","ÉPICO","LEGENDARIO"};
        public static bool Valid(int id)=>id>=0 && id<Names.Length;
        public static Color ColorOf(int id) => !Valid(id)?Color.gray:Rarities[id]==CollectibleRarity.Common?new Color(.65f,.85f,.65f):Rarities[id]==CollectibleRarity.Rare?new Color(.3f,.7f,1):Rarities[id]==CollectibleRarity.Epic?new Color(.8f,.4f,1):new Color(1,.75f,.2f);
    }
    public readonly struct FusionRecipe
    {
        public readonly int Seconds, Common, Rare, Epic, Legendary;
        public FusionRecipe(int seconds,int common,int rare,int epic,int legendary)
        {Seconds=seconds;Common=common;Rare=rare;Epic=epic;Legendary=legendary;}
        public string Odds=>$"Común {Common}%  ·  Raro {Rare}%  ·  Épico {Epic}%  ·  Yanikua {Legendary}%";
        public static bool TryCreate(int[] ingredients,out FusionRecipe recipe)
        {
            recipe=default;if(ingredients==null || ingredients.Length!=3)return false;
            int epics=0,rares=0;
            foreach(int id in ingredients) {
                if(!FusionCatalog.Valid(id)||id==4)return false; // Legendary collectibles are protected.
                if(FusionCatalog.Rarities[id]==CollectibleRarity.Epic)epics++;
                else if(FusionCatalog.Rarities[id]==CollectibleRarity.Rare)rares++;
            }
            recipe=epics>=2 && epics+rares==3 ? new FusionRecipe(180,0,0,99,1)
                :epics>0?new FusionRecipe(180,0,20,80,0)
                :rares>0?new FusionRecipe(120,20,60,20,0):new FusionRecipe(60,70,25,5,0);
            return true;
        }
        public int Roll(int roll,int variant)
        {
            if(roll<0||roll>=10000)throw new ArgumentOutOfRangeException(nameof(roll));
            if(roll<Legendary*100)return 4;
            roll-=Legendary*100;
            if(roll<Epic*100)return 2+Math.Abs(variant%2);
            roll-=Epic*100;
            return roll<Rare*100?1:0;
        }
    }
    [Serializable]
    public sealed class FusionProgress
    {
        public int version=1;
        public FusionPhase phase;
        public float remaining, duration, opening;
        public int result=-1;
        public int[] owned={1,1,1,1,0};
        public int[] displays={0,1,-1,-1,2,3,-1,-1};
        public bool Begin(int[] ingredients,int roll,int variant)
        {
            if(phase!=FusionPhase.Idle || !FusionRecipe.TryCreate(ingredients,out var recipe))return false;
            result=recipe.Roll(roll,variant);duration=remaining=recipe.Seconds;opening=0;phase=FusionPhase.Fusing;return true;
        }
        public bool Advance(float seconds)
        {
            if(seconds<=0 || float.IsNaN(seconds)||float.IsInfinity(seconds))return false;
            var before=phase;
            if(phase==FusionPhase.Fusing) {remaining=Mathf.Max(0,remaining-seconds);if(remaining<=0){phase=FusionPhase.Opening;opening=0;}}
            else if(phase==FusionPhase.Opening) {opening=Mathf.Min(1,opening+seconds/1.2f);if(opening>=1)phase=FusionPhase.AwaitingDecision;}
            return before!=phase;
        }
        public bool Resolve(bool collect)
        {
            if(phase!=FusionPhase.AwaitingDecision || !FusionCatalog.Valid(result))return false;
            if(collect) {
                owned[result]++;
                int slot=Array.IndexOf(displays,-1);
                // Keep a newly discovered species visible even when duplicates fill all bays.
                if(slot<0 && Array.IndexOf(displays,result)<0)
                    for(int i=0;i<displays.Length;i++) {
                        int copies=0;foreach(int displayed in displays)if(displayed==displays[i])copies++;
                        if(copies>1){slot=i;break;}
                    }
                if(slot>=0)displays[slot]=result;
            }
            result=-1;remaining=duration=opening=0;phase=FusionPhase.Idle;return true;
        }
        public void Validate()
        {
            if(owned==null||owned.Length!=5)owned=new[]{1,1,1,1,0};
            if(displays==null||displays.Length!=8)displays=new[]{0,1,-1,-1,2,3,-1,-1};
            for(int i=0;i<owned.Length;i++)owned[i]=Mathf.Max(i<4?1:0,owned[i]);
            for(int i=0;i<displays.Length;i++)if(!FusionCatalog.Valid(displays[i]) || owned[displays[i]]==0)displays[i]=-1;
            if(phase<FusionPhase.Idle || phase>FusionPhase.AwaitingDecision || (phase!=FusionPhase.Idle&&!FusionCatalog.Valid(result))) {phase=FusionPhase.Idle;result=-1;}
            remaining=float.IsNaN(remaining)||float.IsInfinity(remaining)?180:Mathf.Clamp(remaining,0,180);
            duration=Mathf.Clamp(duration,60,180);opening=Mathf.Clamp01(opening);
        }
    }
}
