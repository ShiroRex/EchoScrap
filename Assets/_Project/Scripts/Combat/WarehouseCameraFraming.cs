using EchoScrap.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>Confines the complete vault view and keeps the character card inside the frame.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseCameraFraming : CinemachineExtension
    {
        private CombatGameBootstrap game;
        private WarehouseTravel travel;
        private Collider floor, player;
        private CollectorSpriteView collector;
        private Renderer fallbackFloor;
        private MeshFilter visualFloor;
        private Mesh originalFloorMesh, presentationFloorMesh;
        public bool HasRoomBounds => floor != null || fallbackFloor != null;
        public Bounds RoomBounds => fallbackFloor != null ? fallbackFloor.bounds : floor != null ? floor.bounds : default;
        public Bounds WalkableBounds => floor != null ? floor.bounds : RoomBounds;
        public bool IsConfining => isActiveAndEnabled && HasRoomBounds && game != null && game.Session.IsInWarehouse;

        public void Initialize(CombatGameBootstrap owner, WarehouseTravel connection)
        {
            game = owner; travel = connection;
            player = game.Player.GetComponent<Collider>();
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
                if (candidate.name == "SueloContinuo") { fallbackFloor = candidate; break; }
            PrepareFloorMargin();
        }

        private void PrepareFloorMargin()
        {
            if (floor == null || fallbackFloor == null || travel.InteriorFloorMargin <= 0f) return;
            Bounds current = fallbackFloor.bounds, walkable = floor.bounds;
            float x = Mathf.Max(1f, (walkable.size.x + 2f * travel.InteriorFloorMargin) / current.size.x);
            float z = Mathf.Max(1f, (walkable.size.z + 2f * travel.InteriorFloorMargin) / current.size.z);
            if (x < 1.001f && z < 1.001f) return;
            visualFloor = fallbackFloor.GetComponent<MeshFilter>();
            originalFloorMesh = visualFloor.sharedMesh;
            presentationFloorMesh = Instantiate(originalFloorMesh);
            presentationFloorMesh.name = "Vault floor presentation margin";
            var vertices = presentationFloorMesh.vertices; var uv = presentationFloorMesh.uv;
            Transform t = visualFloor.transform;
            Vector3 right = t.TransformVector(Vector3.right), forward = t.TransformVector(Vector3.forward);
            float uScale = new Vector3(right.x * x, right.y, right.z * z).magnitude / right.magnitude;
            float vScale = new Vector3(forward.x * x, forward.y, forward.z * z).magnitude / forward.magnitude;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = t.TransformPoint(vertices[i]);
                p.x = current.center.x + (p.x - current.center.x) * x;
                p.z = current.center.z + (p.z - current.center.z) * z;
                vertices[i] = t.InverseTransformPoint(p);
                uv[i] = Vector2.Scale(uv[i], new Vector2(uScale, vScale));
            }
            presentationFloorMesh.vertices = vertices; presentationFloorMesh.uv = uv;
            presentationFloorMesh.RecalculateBounds(); presentationFloorMesh.RecalculateNormals(); presentationFloorMesh.RecalculateTangents();
            visualFloor.sharedMesh = presentationFloorMesh;
        }

        protected override void OnDestroy()
        {
            if (visualFloor != null && visualFloor.sharedMesh == presentationFloorMesh) visualFloor.sharedMesh = originalFloorMesh;
            if (presentationFloorMesh != null) Destroy(presentationFloorMesh);
            base.OnDestroy();
        }

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize || !IsConfining) return;
            Quaternion baseRotation = state.GetCorrectedOrientation(), rotation = baseRotation;
            Vector3 original = state.GetFinalPosition();
            Vector3 focus = original + rotation * Vector3.forward * travel.InteriorCameraDistance;
            Bounds room = RoomBounds;
            float size = travel.InteriorOrthographicSize;
            float aspect = Mathf.Max(.1f, game.GameplayCamera.aspect);
            Vector3 position = original;
            bool fitted = false;
            float extraPitch = Mathf.Max(0, travel.InteriorMaximumCameraPitch - travel.InteriorCameraPitch);
            Vector3 center = player != null ? player.bounds.center : focus;
            float edge = Mathf.Min(Mathf.Min(center.x - room.min.x, room.max.x - center.x),
                Mathf.Min(center.z - room.min.z, room.max.z - center.z));
            float edgeBlend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(travel.InteriorCameraDistance * .75f, 1.5f, edge));
            for (int elevation = 0; elevation < 5 && !fitted; elevation++)
            {
                // Elevated displays can put the player's head above the far wall's
                // ground projection. A steeper view keeps both the head and room in frame.
                float pitchBlend = Mathf.Lerp(edgeBlend, 1f, elevation / 4f);
                rotation = Quaternion.AngleAxis(extraPitch * pitchBlend, baseRotation * Vector3.right) * baseRotation;
                position = focus - rotation * Vector3.forward * travel.InteriorCameraDistance;
                bool confined = ConfineFrame(room, rotation, size, aspect, ref position);
                bool insideWalls = position.x >= room.min.x && position.x <= room.max.x && position.z >= room.min.z && position.z <= room.max.z;
                fitted = confined && insideWalls && (player == null || ContainsPlayer(player.bounds, position, rotation, size, aspect));
            }
            state.PositionCorrection += position - original;
            state.RawOrientation = rotation; state.OrientationCorrection = Quaternion.identity;
            state.Lens.Dutch = 0;
            state.Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            state.Lens.OrthographicSize = size;
        }

        public static bool ConfineFrame(Bounds room, Quaternion rotation, float size, float aspect, ref Vector3 position)
        {
            Vector3 ray = rotation * Vector3.forward;
            if (ray.y >= -.0001f) return false;
            Vector3 min = new Vector3(float.MaxValue, 0, float.MaxValue), max = -min;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 origin = position + rotation * new Vector3((corner % 2 == 0 ? -1 : 1) * size * aspect,
                    (corner < 2 ? -1 : 1) * size, 0);
                Vector3 hit = origin + ray * ((room.max.y - origin.y) / ray.y);
                min = Vector3.Min(min, hit); max = Vector3.Max(max, hit);
            }
            if (max.x - min.x > room.size.x || max.z - min.z > room.size.z) return false;
            position.x += Mathf.Clamp(0, room.min.x - min.x, room.max.x - max.x);
            position.z += Mathf.Clamp(0, room.min.z - min.z, room.max.z - max.z);
            return true;
        }

        private bool ContainsPlayer(Bounds bounds, Vector3 position, Quaternion rotation, float size, float aspect)
        {
            Quaternion inverse = Quaternion.Inverse(rotation);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 view = inverse * (point - position);
                if (view.z <= .1f || Mathf.Abs(view.x) > size * aspect * .98f || Mathf.Abs(view.y) > size * .98f) return false;
            }
            if (collector != null)
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 view = inverse * (collector.GetFramingCorner(corner) - position);
                    if (view.z <= .1f || Mathf.Abs(view.x) > size * aspect * .99f || Mathf.Abs(view.y) > size * .99f) return false;
                }
            return true;
        }
    }
}
