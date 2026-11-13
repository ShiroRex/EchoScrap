using System.Collections;
using System.Linq;
using EchoScrap.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace EchoScrap.Tests
{
 public sealed class PowerNumberTypographyTests
 {
  [UnityTest] public IEnumerator OnlyDigitsUseReadableFontAndWrappingKeepsAllValues()
  {
   var canvas=new GameObject("TypographyTest",typeof(Canvas));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
   var text=new GameObject("Power",typeof(RectTransform),typeof(Text)).GetComponent<Text>();text.transform.SetParent(canvas.transform,false);
   var pixel=Resources.Load<ExpeditionIntro>("UI/ExpeditionIntro").AnnouncementFont;var normal=Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin").regular;
   text.font=pixel;text.fontSize=20;text.color=Color.white;text.alignment=TextAnchor.UpperLeft;text.rectTransform.sizeDelta=new Vector2(250,200);
   text.text="<color=#FF9900>Soldier</color> +15 VIDA · −15% DAÑO\nShireen CURA 2 / 5 S";
   ReadablePowerNumbers.Attach(text);yield return null;yield return null;
   var labels=text.GetComponentsInChildren<Text>().Where(t=>t!=text&&t.gameObject.activeSelf).ToArray();
   CollectionAssert.AreEqual(new[]{"15","15","2","5"},labels.Where(t=>t.text.All(char.IsDigit)).Select(t=>t.text).ToArray());
   foreach(var label in labels)Assert.AreSame(label.text.All(char.IsDigit)?normal:pixel,label.font,label.text);
   Assert.AreEqual(new Color(1,.6f,0),labels.Single(t=>t.text=="Soldier").color);
   Assert.Greater(labels.Select(t=>t.rectTransform.anchoredPosition.y).Distinct().Count(),1);
   text.text="+10 VIDA";yield return null;yield return null;
   labels=text.GetComponentsInChildren<Text>().Where(t=>t!=text&&t.gameObject.activeSelf).ToArray();Assert.AreEqual("+10VIDA",string.Concat(labels.Select(t=>t.text)));
   Object.Destroy(canvas);yield return null;
  }
 }
}
