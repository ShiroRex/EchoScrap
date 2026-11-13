using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    // UI.Text cannot switch font within a string. Compose cached runs while retaining its source text.
    [DisallowMultipleComponent, RequireComponent(typeof(Text))]
    public sealed class ReadablePowerNumbers : BaseMeshEffect
    {
        private sealed class Run { public string Value; public Font Font; public Color Color; public float Width; public int Line; }
        private readonly List<Text> labels = new List<Text>();
        private readonly List<Run> runs = new List<Run>();
        private readonly List<float> widths = new List<float>();
        private Text source;
        private Font digits;
        private string previousText;
        private Vector2 previousSize;
        private Color previousColor;
        private int previousFontSize;
        public Font NumericFont => digits;
        public static void Attach(Text text)
        {
            if(text.GetComponent<ReadablePowerNumbers>()==null)text.gameObject.AddComponent<ReadablePowerNumbers>();
        }
        protected override void OnEnable() { base.OnEnable();source=GetComponent<Text>();digits=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").regular;previousText=null; }
        public override void ModifyMesh(VertexHelper vertices) { if(IsActive())vertices.Clear(); }
        private void LateUpdate()
        {
            if(source==null)return;
            var size=source.rectTransform.rect.size;
            if(previousText==source.text && previousSize==size && previousColor==source.color && previousFontSize==source.fontSize)return;
            previousText=source.text;previousSize=size;previousColor=source.color;previousFontSize=source.fontSize;Rebuild();
        }
        public void Rebuild()
        {
            if(source==null)source=GetComponent<Text>();if(digits==null)digits=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").regular;
            runs.Clear();widths.Clear();widths.Add(0);
            string value=source.text;Color color=source.color;int line=0;float limit=source.rectTransform.rect.width;
            for(int i=0;i<value.Length;)
            {
                if(source.supportRichText && value[i]=='<')
                {
                    int end=value.IndexOf('>',i);
                    if(end>=0){string tag=value.Substring(i,end-i+1);if(tag.StartsWith("<color=")){if(ColorUtility.TryParseHtmlString(tag.Substring(7,tag.Length-8),out var parsed))color=parsed*source.color;}else if(tag=="</color>")color=source.color;i=end+1;continue;}
                }
                if(value[i]=='\n'){line++;widths.Add(0);i++;continue;}
                bool number=char.IsDigit(value[i]),space=char.IsWhiteSpace(value[i]);int start=i++;
                while(i<value.Length && value[i]!='<' && value[i]!='\n' && char.IsDigit(value[i])==number && char.IsWhiteSpace(value[i])==space)i++;
                string token=value.Substring(start,i-start);Font font=number?digits:source.font;
                font.RequestCharactersInTexture(token,source.fontSize,source.fontStyle);float width=0;
                foreach(char c in token)if(font.GetCharacterInfo(c,out var info,source.fontSize,source.fontStyle))width+=info.advance;
                if(source.horizontalOverflow==HorizontalWrapMode.Wrap && !space && widths[line]>0 && widths[line]+width>limit){line++;widths.Add(0);}
                if(space && widths[line]==0)continue;
                runs.Add(new Run{Value=token,Font=font,Color=color,Width=width,Line=line});widths[line]+=width;
            }
            float height=source.fontSize*source.lineSpacing*1.25f;
            var rect=source.rectTransform.rect;int alignment=(int)source.alignment;
            float top=alignment/3==0?rect.yMax:alignment/3==1?height*widths.Count*.5f:rect.yMin+height*widths.Count;
            int current=-1;float x=0;int used=0;
            foreach(var run in runs)
            {
                if(run.Line!=current){current=run.Line;x=alignment%3==0?rect.xMin:alignment%3==1?-widths[current]*.5f:rect.xMax-widths[current];}
                if(string.IsNullOrWhiteSpace(run.Value)){x+=run.Width;continue;}
                if(used==labels.Count)
                {
                    var item=new GameObject("TramoPoder",typeof(RectTransform),typeof(Text),typeof(Shadow)).GetComponent<Text>();item.transform.SetParent(transform,false);item.raycastTarget=false;item.alignment=TextAnchor.MiddleLeft;item.supportRichText=false;item.horizontalOverflow=HorizontalWrapMode.Overflow;item.verticalOverflow=VerticalWrapMode.Overflow;
                    var shadow=item.GetComponent<Shadow>();shadow.effectColor=new Color(.025f,.018f,.04f,.95f);shadow.effectDistance=new Vector2(1,-2);labels.Add(item);
                }
                var label=labels[used++];label.gameObject.SetActive(true);label.text=run.Value;label.font=run.Font;label.fontSize=source.fontSize;label.fontStyle=source.fontStyle;label.color=run.Color;
                label.rectTransform.anchorMin=label.rectTransform.anchorMax=Vector2.one*.5f;label.rectTransform.pivot=new Vector2(0,.5f);
                label.rectTransform.anchoredPosition=new Vector2(x,top-(run.Line+.5f)*height);label.rectTransform.sizeDelta=new Vector2(run.Width+2,height);x+=run.Width;
            }
            for(int i=used;i<labels.Count;i++)labels[i].gameObject.SetActive(false);
            source.SetVerticesDirty();
        }
        protected override void OnDisable(){foreach(var label in labels)if(label!=null)label.gameObject.SetActive(false);base.OnDisable();previousText=null;}
    }
}
