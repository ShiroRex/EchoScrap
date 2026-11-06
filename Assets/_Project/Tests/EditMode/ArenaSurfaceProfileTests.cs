using EchoScrap.Core;
using NUnit.Framework;
using UnityEngine;

namespace EchoScrap.Tests
{
    public sealed class ArenaSurfaceProfileTests
    {
        [Test]
        public void OptimizedProfileMatchesAuthoredTerrainAcrossSizesAndTrenchEdges()
        {
            foreach(float size in new[]{20f,120f,199.9f,200f,350f})
            {
                for(int x=-110;x<=110;x+=3)
                    for(int z=-110;z<=110;z+=3)
                        Assert.AreEqual(LegacyProfile(x,z,size),ArenaSurface.Profile(x,z,size),$"Terrain at ({x},{z}) size {size}");
                foreach(float x in new[]{-6.001f,-6f,0f,6f,6.001f})
                    foreach(float z in new[]{59.999f,60f,75.999f,76f,83f,83.001f})
                        Assert.AreEqual(LegacyProfile(x,z,size),ArenaSurface.Profile(x,z,size),$"Trench at ({x},{z}) size {size}");
            }
        }

        // Freeze the authored height function: performance changes must not move scenery,
        // alter collision support, or change the shape of the portal approach.
        private static float LegacyProfile(float x,float z,float size)
        {
            float scale=Mathf.Max(20f,size)/120f;
            if(x>=-6f&&x<=6f&&z>=60f&&z<=83f)
                return z<76f?Mathf.Lerp(-.25f,-8.25f,Mathf.Clamp01((z-60f)/16f)):-8f;
            x/=scale;z/=scale;
            float height=Hill(x,z,-24,3,5.5f,19,22)+Hill(x,z,23,29,4.5f,21,19)+
                Hill(x,z,-12,-39,4,20,18)+Hill(x,z,-37,50,7,24,19)+Hill(x,z,46,-8,3.5f,18,23);
            float center=Mathf.SmoothStep(0,1,Mathf.InverseLerp(11,19,new Vector2(x,z).magnitude));
            float boss=size>=200?Mathf.SmoothStep(0,1,Mathf.InverseLerp(22,26,new Vector2(x,z).magnitude*scale)):1;
            return height*center*boss;
        }
        private static float Hill(float x,float z,float cx,float cz,float height,float rx,float rz)
        {
            float r=(x-cx)*(x-cx)/(rx*rx)+(z-cz)*(z-cz)/(rz*rz);
            return height*Mathf.SmoothStep(0,1,Mathf.Clamp01(1-r));
        }
    }
}
