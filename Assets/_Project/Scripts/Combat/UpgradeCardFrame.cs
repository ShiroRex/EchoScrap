using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    // Nine-slice corners plus the original edge ornaments at their native pixel scale.
    // A normal sliced Image would stretch the central gems across the entire rail.
    public sealed class UpgradeCardFrame : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (sprite == null || type != Type.Sliced) { base.OnPopulateMesh(mesh); return; }
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float scale = (canvas != null ? canvas.referencePixelsPerUnit : 100f) / (sprite.pixelsPerUnit * pixelsPerUnitMultiplier);
            float corner = Mathf.Min(12f * scale, Mathf.Min(rect.width, rect.height) * 0.5f);
            float ornamentWidth = Mathf.Min(40f * scale, rect.width - corner * 2f);
            float ornamentHeight = Mathf.Min(40f * scale, rect.height - corner * 2f);
            float left = rect.xMin, right = rect.xMax, bottom = rect.yMin, top = rect.yMax;
            float ornamentLeft = rect.center.x - ornamentWidth * 0.5f;
            float ornamentBottom = rect.center.y - ornamentHeight * 0.5f;
            Vector4 uv = DataUtility.GetOuterUV(sprite);

            Quad(mesh, uv, new Rect(left, bottom, corner, corner), new Rect(0, 0, 12, 12));
            Quad(mesh, uv, new Rect(right - corner, bottom, corner, corner), new Rect(52, 0, 12, 12));
            Quad(mesh, uv, new Rect(left, top - corner, corner, corner), new Rect(0, 52, 12, 12));
            Quad(mesh, uv, new Rect(right - corner, top - corner, corner, corner), new Rect(52, 52, 12, 12));

            Horizontal(mesh, uv, left + corner, ornamentLeft, bottom, corner, 0);
            Horizontal(mesh, uv, ornamentLeft + ornamentWidth, right - corner, bottom, corner, 0);
            Horizontal(mesh, uv, left + corner, ornamentLeft, top - corner, corner, 52);
            Horizontal(mesh, uv, ornamentLeft + ornamentWidth, right - corner, top - corner, corner, 52);
            Vertical(mesh, uv, bottom + corner, ornamentBottom, left, corner, 0);
            Vertical(mesh, uv, ornamentBottom + ornamentHeight, top - corner, left, corner, 0);
            Vertical(mesh, uv, bottom + corner, ornamentBottom, right - corner, corner, 52);
            Vertical(mesh, uv, ornamentBottom + ornamentHeight, top - corner, right - corner, corner, 52);

            Quad(mesh, uv, new Rect(ornamentLeft, bottom, ornamentWidth, corner), new Rect(12, 0, 40, 12));
            Quad(mesh, uv, new Rect(ornamentLeft, top - corner, ornamentWidth, corner), new Rect(12, 52, 40, 12));
            Quad(mesh, uv, new Rect(left, ornamentBottom, corner, ornamentHeight), new Rect(0, 12, 12, 40));
            float extraWidth = Mathf.Max(0f, sprite.rect.width - 64f);
            Quad(mesh, uv, new Rect(right - corner, ornamentBottom, corner + extraWidth * scale, ornamentHeight), new Rect(52, 12, 12 + extraWidth, 40));
        }

        private void Horizontal(VertexHelper mesh, Vector4 uv, float from, float to, float y, float height, int sourceY)
        {
            Quad(mesh, uv, new Rect(from, y, to - from, height), new Rect(10, sourceY, 1, 12));
        }

        private void Vertical(VertexHelper mesh, Vector4 uv, float from, float to, float x, float width, int sourceX)
        {
            Quad(mesh, uv, new Rect(x, from, width, to - from), new Rect(sourceX, 10, 12, 1));
        }

        private void Quad(VertexHelper mesh, Vector4 uv, Rect destination, Rect source)
        {
            if (destination.width <= 0f || destination.height <= 0f) return;
            // The original epic canvas is 72x64, and the legendary canvas is 64x70.
            // Their main frame occupies the upper-left 64x64; keep the extra pixels.
            float bottomPadding = sprite.rect.height - 64f;
            float u0 = Mathf.Lerp(uv.x, uv.z, source.xMin / sprite.rect.width);
            float u1 = Mathf.Lerp(uv.x, uv.z, source.xMax / sprite.rect.width);
            float v0 = Mathf.Lerp(uv.y, uv.w, (source.yMin + bottomPadding) / sprite.rect.height);
            float v1 = Mathf.Lerp(uv.y, uv.w, (source.yMax + bottomPadding) / sprite.rect.height);
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(destination.xMin, destination.yMin), color, new Vector2(u0, v0));
            mesh.AddVert(new Vector3(destination.xMin, destination.yMax), color, new Vector2(u0, v1));
            mesh.AddVert(new Vector3(destination.xMax, destination.yMax), color, new Vector2(u1, v1));
            mesh.AddVert(new Vector3(destination.xMax, destination.yMin), color, new Vector2(u1, v0));
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
