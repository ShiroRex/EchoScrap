using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public sealed class UpgradeCardPerspective : BaseMeshEffect
    {
        private UpgradeCardMotion motion;
        public void Bind(UpgradeCardMotion cardMotion) => motion = cardMotion;

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || motion == null || motion.Tilt == Vector2.zero) return;
            Matrix4x4 toCard = motion.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            Matrix4x4 fromCard = toCard.inverse;
            UIVertex vertex = default;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.position = fromCard.MultiplyPoint3x4(motion.Project(toCard.MultiplyPoint3x4(vertex.position)));
                mesh.SetUIVertex(vertex, i);
            }
        }
    }
}
