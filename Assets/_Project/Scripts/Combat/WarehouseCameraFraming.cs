using EchoScrap.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace EchoScrap.Combat
{
    /// <summary>Stops camera follow at the vault walls without changing its angle or lens.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseCameraFraming : CinemachineExtension
    {
        private CombatGameBootstrap game;
        private WarehouseTravel travel;
        private CinemachineFollow follow;
        private Collider floor;
        private CollectorSpriteView collector;
        private Renderer visibleFloor;
        private MeshFilter visualFloor;
        private Mesh originalFloorMesh, presentationFloorMesh, fadeMesh;
        private Material fadeMaterial;
        private GameObject fadeObject;
        public bool HasRoomBounds => floor != null || visibleFloor != null;
        public Bounds RoomBounds => visibleFloor != null ? visibleFloor.bounds : floor != null ? floor.bounds : default;
        public Bounds WalkableBounds => floor != null ? floor.bounds : RoomBounds;
        public bool IsConfining => isActiveAndEnabled && HasRoomBounds && game != null && game.Session.IsInWarehouse;
        public Renderer BoundaryFadeRenderer => fadeObject != null ? fadeObject.GetComponent<Renderer>() : null;

        public void Initialize(CombatGameBootstrap owner, WarehouseTravel connection)
        {
            game = owner; travel = connection;
            follow = game.CameraRig.GetComponent<CinemachineFollow>();
            collector = game.Player.GetComponent<CollectorSpriteView>();
            var surfaces = travel.GetComponentInChildren<VaultFloorSurface>();
            if (surfaces != null && surfaces.Surfaces != null)
                foreach (var candidate in surfaces.Surfaces)
                    if (candidate != null && (floor == null || candidate.bounds.size.x * candidate.bounds.size.z > floor.bounds.size.x * floor.bounds.size.z))
                        floor = candidate;
            if (floor == null)
                foreach (var candidate in travel.GetComponentsInChildren<Collider>())
                    if (!candidate.isTrigger && candidate.name == "SueloInterior") { floor = candidate; break; }
            foreach (var candidate in travel.GetComponentsInChildren<MeshRenderer>())
                if (candidate.name == "SueloContinuo") { visibleFloor = candidate; break; }
            PrepareFloorMargin();
        }

        private void PrepareFloorMargin()
        {
            if (floor == null || visibleFloor == null || travel.InteriorFloorMargin <= 0f) return;
            Bounds current = visibleFloor.bounds, walkable = floor.bounds;
            float margin = Mathf.Max(travel.InteriorFloorMargin, travel.InteriorBoundaryFadeWidth + 1f);
            float x = Mathf.Max(1f, (walkable.size.x + 2f * margin) / current.size.x);
            float z = Mathf.Max(1f, (walkable.size.z + 2f * margin) / current.size.z);
            visualFloor = visibleFloor.GetComponent<MeshFilter>();
            originalFloorMesh = visualFloor.sharedMesh;
            presentationFloorMesh = Instantiate(originalFloorMesh);
            presentationFloorMesh.name = "Vault floor presentation margin";
            var vertices = presentationFloorMesh.vertices; var uv = presentationFloorMesh.uv;
            Transform t = visualFloor.transform;
            Vector3 right = t.TransformVector(Vector3.right), forward = t.TransformVector(Vector3.forward);
            float uScale = new Vector3(right.x * x, right.y, right.z * z).magnitude / right.magnitude;
            float vScale = new Vector3(forward.x * x, forward.y, forward.z * z).magnitude / forward.magnitude;
            Vector2 uvMin = uv[0], uvMax = uv[0];
            foreach (Vector2 point in uv) { uvMin = Vector2.Min(uvMin, point); uvMax = Vector2.Max(uvMax, point); }
            Vector2 uvCenter = (uvMin + uvMax) * .5f;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = t.TransformPoint(vertices[i]);
                p.x = current.center.x + (p.x - current.center.x) * x;
                p.z = current.center.z + (p.z - current.center.z) * z;
                vertices[i] = t.InverseTransformPoint(p);
                uv[i] = uvCenter + Vector2.Scale(uv[i] - uvCenter, new Vector2(uScale, vScale));
            }
            presentationFloorMesh.vertices = vertices; presentationFloorMesh.uv = uv;
            presentationFloorMesh.RecalculateBounds(); presentationFloorMesh.RecalculateNormals(); presentationFloorMesh.RecalculateTangents();
            visualFloor.sharedMesh = presentationFloorMesh;
            PrepareBoundaryFade(walkable, visibleFloor.bounds);
        }

        private void PrepareBoundaryFade(Bounds inner, Bounds outer)
        {
            var shader = Resources.Load<Shader>("Lighting/WarehouseBoundary");
            if (shader == null) return;
            fadeObject = new GameObject("Vault boundary backdrop");
            fadeObject.transform.SetParent(visualFloor.transform, false);
            fadeObject.layer = visibleFloor.gameObject.layer;
            var vertices = new Vector3[16]; var triangles = new int[24];
            AddQuad(0, outer.min.x, inner.min.x, outer.min.z, outer.max.z);
            AddQuad(1, inner.max.x, outer.max.x, outer.min.z, outer.max.z);
            AddQuad(2, inner.min.x, inner.max.x, outer.min.z, inner.min.z);
            AddQuad(3, inner.min.x, inner.max.x, inner.max.z, outer.max.z);
            fadeMesh = new Mesh { name = "Vault boundary apron", vertices = vertices, triangles = triangles };
            fadeMesh.RecalculateBounds();
            fadeObject.AddComponent<MeshFilter>().sharedMesh = fadeMesh;
            var renderer = fadeObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            fadeMaterial = new Material(shader) { name = "Vault boundary backdrop" };
            fadeMaterial.SetVector("_RoomBounds", new Vector4(inner.min.x, inner.min.z, inner.max.x, inner.max.z));
            fadeMaterial.SetFloat("_FadeWidth", travel.InteriorBoundaryFadeWidth);
            fadeMaterial.SetColor("_BaseColor", game.Lighting != null ? game.Lighting.Profile.WarehouseBackground : game.GameplayCamera.backgroundColor);
            renderer.sharedMaterial = fadeMaterial;

            void AddQuad(int index, float minX, float maxX, float minZ, float maxZ)
            {
                int v = index * 4, t = index * 6; float y = outer.max.y + .003f;
                vertices[v] = visualFloor.transform.InverseTransformPoint(new Vector3(minX, y, minZ));
                vertices[v + 1] = visualFloor.transform.InverseTransformPoint(new Vector3(minX, y, maxZ));
                vertices[v + 2] = visualFloor.transform.InverseTransformPoint(new Vector3(maxX, y, maxZ));
                vertices[v + 3] = visualFloor.transform.InverseTransformPoint(new Vector3(maxX, y, minZ));
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
        }

        protected override void OnDestroy()
        {
            if (visualFloor != null && visualFloor.sharedMesh == presentationFloorMesh) visualFloor.sharedMesh = originalFloorMesh;
            if (presentationFloorMesh != null) Destroy(presentationFloorMesh);
            if (fadeObject != null) Destroy(fadeObject);
            if (fadeMesh != null) Destroy(fadeMesh);
            if (fadeMaterial != null) Destroy(fadeMaterial);
            base.OnDestroy();
        }

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize || !IsConfining) return;
            Quaternion rotation = state.GetCorrectedOrientation();
            Vector3 original = state.GetFinalPosition();
            Vector3 focus = original + rotation * Vector3.forward * follow.FollowOffset.magnitude;
            Bounds room = WalkableBounds;
            float padding = travel.InteriorCameraWallPadding;
            float x = Mathf.Min(padding, room.extents.x * .5f), z = Mathf.Min(padding, room.extents.z * .5f);
            Vector3 position = original + new Vector3(
                Mathf.Clamp(focus.x, room.min.x + x, room.max.x - x) - focus.x, 0,
                Mathf.Clamp(focus.z, room.min.z + z, room.max.z - z) - focus.z);

            // A narrow viewport can need less side padding to keep the collector visible.
            // Correct translation only; the authored perspective, pitch and zoom stay intact.
            if (collector != null)
            {
                Quaternion inverse = Quaternion.Inverse(rotation);
                float tangent = Mathf.Tan(state.Lens.FieldOfView * .5f * Mathf.Deg2Rad);
                float lower = float.NegativeInfinity, upper = float.PositiveInfinity;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 view = inverse * (collector.GetFramingCorner(corner) - position);
                    float halfWidth = Mathf.Max(.01f, view.z) * tangent * game.GameplayCamera.aspect * .96f;
                    lower = Mathf.Max(lower, view.x - halfWidth); upper = Mathf.Min(upper, view.x + halfWidth);
                }
                if (lower <= upper) position += rotation * Vector3.right * Mathf.Clamp(0, lower, upper);
            }
            state.PositionCorrection += position - original;
        }
    }
}
