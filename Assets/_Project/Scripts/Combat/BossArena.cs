using EchoScrap.Core;
using EchoScrap.Player;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>The reusable central foundry: safe transfer, solid perimeter and announced reinforcements.</summary>
    [DisallowMultipleComponent]
    public sealed class BossArena : MonoBehaviour
    {
        [SerializeField] private Vector3 center;
        [SerializeField, Min(12f)] private float radius = 22f;
        [SerializeField] private Vector3 playerEntryOffset = new Vector3(0f, 0f, -12f);
        [SerializeField, Min(0.1f)] private float transferFadeDuration = 0.3f;
        [SerializeField, Min(0.5f)] private float reinforcementWarning = 0.9f;
        [SerializeField, Range(1, 12)] private int minionLimit = 8;

        private struct Reinforcement
        {
            public bool Active;
            public float Age;
            public Vector3 Position;
            public EnemyArchetypeData Archetype;
        }

        private readonly Reinforcement[] pending = new Reinforcement[8];
        private readonly Transform[] spawnMarkers = new Transform[8];
        private readonly Collider[] overlap = new Collider[32];
        private readonly BoxCollider[] barriers = new BoxCollider[48];
        private readonly Renderer[] rimRenderers = new Renderer[48];
        private readonly Renderer[] plateRenderers = new Renderer[48];
        private readonly Renderer[] fieldRenderers = new Renderer[48];
        private CombatGameBootstrap game;
        private GolgoraActor boss;
        private Transform cameraAnchor;
        private CinemachineFollow follow;
        private Transform originalFollow;
        private Vector3 originalOffset;
        private Quaternion originalRotation;
        private float originalFov;
        private Material steel, dormant, hot, warning, field;
        private Mesh annulus;
        private Image fade;
        private float transferAge;
        private bool transferred, initialized, cameraOwned;
        private int nextVent;
        private Renderer[] bodyRenderers;

        public Vector3 Center => center;
        public float Radius => radius;
        public int MinionLimit => minionLimit;
        public bool IsClosed { get; private set; }
        public bool IsTransferring { get; private set; }
        public int PendingMinions { get; private set; }
        public int SpawnedMinions { get; private set; }
        public bool BarriersEnabled => barriers[0] != null && barriers[0].enabled;
        public float TransferAlpha => fade != null ? fade.color.a : 0f;
        public event System.Action<int> MinionsSpawned;

        public void BindReferences(CombatGameBootstrap owner)
        {
            game = owner;
            if (initialized) return;
            initialized = true;
            follow = game.CameraRig.GetComponent<CinemachineFollow>();
            cameraAnchor = new GameObject("GolgoraCameraFocus").transform;
            cameraAnchor.SetParent(transform, false);
            BuildFoundry();
            BuildFade();
        }

        public void BeginEncounter(GolgoraActor actor)
        {
            if (!initialized) return;
            RestoreCamera();
            boss = actor;
            var rig = actor.GetComponentInChildren<Animator>();
            bodyRenderers = rig != null ? rig.GetComponentsInChildren<Renderer>() : null;
            originalFollow = game.CameraRig.Follow;
            originalOffset = follow != null ? follow.FollowOffset : Vector3.zero;
            originalRotation = game.CameraRig.transform.rotation;
            originalFov = game.CameraRig.Lens.FieldOfView;
            cameraOwned = true;
            IsClosed = IsTransferring = true;
            transferred = false;
            transferAge = 0f;
            SpawnedMinions = nextVent = 0;
            SetPerimeter(true);
            CancelReinforcements();
            SetFade(0f);
            game.Motor.CancelDashAndRestoreState();
            game.Motor.Stop();
            SetTransferInput(false);
            game.Player.IsEncounterTransferProtected = true;
        }

        public void Step(float deltaTime)
        {
            if (!initialized || !IsClosed || deltaTime <= 0f || game.Session.IsPaused
                || !game.Session.IsGameplayFlowActive()) return;
            if (IsTransferring)
            {
                SetTransferInput(false);
                game.Motor.Stop();
                transferAge += deltaTime;
                if (!transferred && transferAge >= transferFadeDuration)
                {
                    transferred = true;
                    MovePlayerIntoFoundry();
                }
                SetFade(transferAge <= transferFadeDuration
                    ? transferAge / transferFadeDuration
                    : 1f - (transferAge - transferFadeDuration) / transferFadeDuration);
                if (transferAge >= transferFadeDuration * 2f)
                {
                    IsTransferring = false;
                    SetFade(0f);
                    game.Player.IsEncounterTransferProtected = false;
                    SetTransferInput(true);
                }
            }
            for (int i = 0; i < pending.Length; i++)
            {
                if (!pending[i].Active) continue;
                pending[i].Age += deltaTime;
                float progress = Mathf.Clamp01(pending[i].Age / reinforcementWarning);
                spawnMarkers[i].localScale = Vector3.one * Mathf.Lerp(1.1f, 2.2f, progress);
                if (progress < 1f) continue;
                var slot = pending[i];
                pending[i].Active = false;
                PendingMinions--;
                spawnMarkers[i].gameObject.SetActive(false);
                if (PositionClear(slot.Position, SpawnRadius(slot.Archetype)))
                {
                    var actor = game.Enemies.Spawn(slot.Archetype, slot.Position);
                    if (actor != null)
                    {
                        SpawnedMinions++;
                        MinionsSpawned?.Invoke(1);
                    }
                }
            }
        }

        private void MovePlayerIntoFoundry()
        {
            Vector3 before = game.Player.transform.position;
            Vector3 position = ArenaSurface.OnGround(center + playerEntryOffset, 1f);
            var body = game.Player.GetComponent<Rigidbody>();
            game.Motor.CancelDashAndRestoreState();
            game.Motor.Stop();
            game.Player.transform.SetPositionAndRotation(position, Quaternion.identity);
            body.position = position;
            body.rotation = Quaternion.identity;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            game.CameraRig.OnTargetObjectWarped(game.Player.transform, position - before);
            cameraAnchor.position = position;
            game.CameraRig.Follow = cameraAnchor;
            game.CameraRig.PreviousStateIsValid = false;
            Physics.SyncTransforms();
        }

        private void SetTransferInput(bool enabled)
        {
            if (game == null || game.Motor == null || game.Player == null || game.Session == null) return;
            var input = game.Motor.GetComponent<PlayerInputReader>();
            if (input != null) input.SetInputEnabled(enabled && !game.Session.IsPaused && game.Session.State==SessionState.Running && game.Player.IsAlive);
        }

        private void LateUpdate()
        {
            if (!cameraOwned || !IsClosed || !transferred || boss == null || game.Session.IsPaused) return;
            Vector3 target = Vector3.Lerp(game.Player.WorldCenter, boss.WorldCenter, 0.4f);
            target.y = game.Player.WorldCenter.y + 0.6f;
            float blend = 1f - Mathf.Exp(-Time.deltaTime * 5f);
            cameraAnchor.position = Vector3.Lerp(cameraAnchor.position, target, blend);
            Vector3 right = originalRotation * Vector3.right;
            Vector3 up = originalRotation * Vector3.up;
            Vector3 forward = originalRotation * Vector3.forward;
            float required = originalOffset.magnitude;
            float aspect = game.GameplayCamera != null ? game.GameplayCamera.aspect : 16f / 9f;
            Vector3 playerFeet = game.Player.WorldCenter - Vector3.up;
            Vector3 playerHead = game.Player.WorldCenter + Vector3.up;
            required = Mathf.Max(required, RequiredCameraDistance(playerFeet, cameraAnchor.position, right, up, forward, originalFov, aspect));
            required = Mathf.Max(required, RequiredCameraDistance(playerHead, cameraAnchor.position, right, up, forward, originalFov, aspect));
            Bounds bounds = new Bounds(boss.WorldCenter + Vector3.up * 2f, new Vector3(boss.HitRadius * 2f, 6f, boss.HitRadius * 2f));
            if (bodyRenderers != null && !boss.IsAirborne)
                for (int i = 0; i < bodyRenderers.Length; i++)
                    if (bodyRenderers[i] != null && bodyRenderers[i].enabled && bodyRenderers[i].gameObject.activeInHierarchy &&
                        (bodyRenderers[i] is MeshRenderer || bodyRenderers[i] is SkinnedMeshRenderer))
                        bounds.Encapsulate(bodyRenderers[i].bounds);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                required = Mathf.Max(required, RequiredCameraDistance(point, cameraAnchor.position, right, up, forward, originalFov, aspect));
            }
            float zoom = required / Mathf.Max(1f, originalOffset.magnitude);
            if (follow != null)
            {
                // Expand immediately when necessary to keep the player visible; return gently after closing distance.
                float current = follow.FollowOffset.magnitude / Mathf.Max(1f, originalOffset.magnitude);
                float fitted = zoom > current ? zoom : Mathf.Lerp(current, zoom, blend);
                follow.FollowOffset = originalOffset * fitted;
            }
            game.CameraRig.transform.rotation = originalRotation;
        }

        public static float RequiredCameraDistance(Vector3 point, Vector3 focus, Vector3 right, Vector3 up,
            Vector3 forward, float verticalFov, float aspect)
        {
            Vector3 relative = point - focus;
            float depth = Vector3.Dot(relative, forward);
            float tangent = Mathf.Tan(Mathf.Clamp(verticalFov, 20f, 100f) * Mathf.Deg2Rad * 0.5f);
            float vertical = Mathf.Abs(Vector3.Dot(relative, up)) / (tangent * 0.70f) - depth;
            float horizontal = Mathf.Abs(Vector3.Dot(relative, right)) / (tangent * Mathf.Max(0.5f, aspect) * 0.86f) - depth;
            return Mathf.Max(1f, Mathf.Max(vertical, horizontal));
        }

        public int QueueMinionWave(CombatWaveSettings settings, int orderOffset, int populationCap)
        {
            if (!IsClosed || IsTransferring || boss == null || !boss.IsCombatActive || boss.IsCercoSequence) return 0;
            var order = settings.BossMinionOrder;
            if (order == null || order.Length == 0) return 0;
            int alive = 0;
            var actors = EnemyActor.ActiveEnemies;
            for (int i = 0; i < actors.Count; i++)
                if (actors[i] != null && actors[i].IsAlive && actors[i] != boss && actors[i].Session == game.Session) alive++;
            int available = Mathf.Min(minionLimit, populationCap) - alive - PendingMinions;
            int queued = 0;
            for (int member = 0; member < Mathf.Min(4, available); member++)
            {
                int slot = -1;
                for (int i = 0; i < pending.Length; i++) if (!pending[i].Active) { slot = i; break; }
                if (slot < 0) break;
                var data = settings.GetArchetype(order[(member + orderOffset) % order.Length]);
                bool found = false;
                Vector3 position = Vector3.zero;
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    float angle = (nextVent++ % 4) * 90f + (attempt / 4) * 12f;
                    position = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (radius - 3.5f);
                    position = ArenaSurface.OnGround(position, 1f);
                    if (!PositionClear(position, SpawnRadius(data))) continue;
                    found = true;
                    break;
                }
                if (!found) continue;
                pending[slot] = new Reinforcement { Active = true, Position = position, Archetype = data };
                PendingMinions++;
                queued++;
                spawnMarkers[slot].position = ArenaSurface.OnGround(position, 0.07f);
                spawnMarkers[slot].localScale = Vector3.one * 1.1f;
                spawnMarkers[slot].gameObject.SetActive(true);
            }
            return queued;
        }

        private bool PositionClear(Vector3 position, float actorRadius)
        {
            float clearance = Mathf.Max(0.6f, actorRadius) + 0.5f;
            Vector3 delta = position - game.Player.WorldCenter;
            delta.y = 0f;
            if (delta.sqrMagnitude < (clearance + 2.5f) * (clearance + 2.5f)) return false;
            var actors = EnemyActor.ActiveEnemies;
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (actor == null || !actor.IsAlive || actor.Session != game.Session) continue;
                delta = position - actor.WorldCenter;
                delta.y = 0f;
                float separation = clearance + actor.HitRadius;
                if (delta.sqrMagnitude < separation * separation) return false;
            }
            for (int i = 0; i < pending.Length; i++)
                if (pending[i].Active && (pending[i].Position - position).sqrMagnitude < clearance * clearance * 4f) return false;
            int count = Physics.OverlapCapsuleNonAlloc(position, position + Vector3.up * 1.5f, clearance,
                overlap, 1 << PhysicsLayers.DefaultLayer, QueryTriggerInteraction.Ignore);
            if (count == overlap.Length) return false;
            for (int i = 0; i < count; i++)
                if (overlap[i] != null && overlap[i] != game.ArenaGround && overlap[i] != ArenaSurface.Active?.GroundCollider) return false;
            return true;
        }

        private static float SpawnRadius(EnemyArchetypeData data) => data.ColliderRadius * Mathf.Max(data.VisualScale.x, data.VisualScale.z);

        public void CancelReinforcements()
        {
            PendingMinions = 0;
            for (int i = 0; i < pending.Length; i++)
            {
                pending[i].Active = false;
                if (spawnMarkers[i] != null) spawnMarkers[i].gameObject.SetActive(false);
            }
        }

        public void EndEncounter()
        {
            IsClosed = IsTransferring = false;
            CancelReinforcements();
            SetPerimeter(false);
            SetFade(0f);
            if (game != null && game.Session != null && game.Session.IsGameplayFlowActive()) SetTransferInput(true);
            if (game != null && game.Player != null) game.Player.IsEncounterTransferProtected = false;
            RestoreCamera();
            boss = null;
            bodyRenderers = null;
        }

        private void RestoreCamera()
        {
            if (!cameraOwned || game == null || game.CameraRig == null) return;
            game.CameraRig.Follow = originalFollow;
            if (follow != null) follow.FollowOffset = originalOffset;
            game.CameraRig.transform.rotation = originalRotation;
            var lens = game.CameraRig.Lens;
            lens.FieldOfView = originalFov;
            game.CameraRig.Lens = lens;
            game.CameraRig.PreviousStateIsValid = false;
            cameraOwned = false;
        }

        private void SetPerimeter(bool closed)
        {
            for (int i = 0; i < barriers.Length; i++)
            {
                if (barriers[i] != null) barriers[i].enabled = closed;
                if (plateRenderers[i] != null) plateRenderers[i].enabled = closed;
                if (rimRenderers[i] != null)
                {
                    rimRenderers[i].enabled = closed;
                    rimRenderers[i].sharedMaterial = closed ? hot : dormant;
                }
                if (fieldRenderers[i] != null) fieldRenderers[i].enabled = closed;
            }
        }

        private void BuildFoundry()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            steel = Material(shader, "FoundrySteel", new Color(0.09f, 0.085f, 0.095f));
            dormant = Material(shader, "FoundryDormant", new Color(0.20f, 0.16f, 0.13f));
            hot = Material(shader, "FoundryMagma", new Color(1.1f, 0.27f, 0.04f));
            warning = Material(shader, "FoundryReinforcement", new Color(1f, 0.62f, 0.12f));
            field = Material(shader, "FoundryContainment", new Color(1f, 0.32f, 0.06f, 0.12f));
            field.SetFloat("_Surface", 1f);
            field.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            field.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            field.SetFloat("_ZWrite", 0f);
            field.SetFloat("_Cull", (float)CullMode.Off);
            field.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            field.SetOverrideTag("RenderType", "Transparent");
            field.renderQueue = (int)RenderQueue.Transparent;
            annulus = BuildRing();
            for (int i = 0; i < barriers.Length; i++)
            {
                float angle = i * 360f / barriers.Length;
                Vector3 outward = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 position = ArenaSurface.OnGround(center + outward * (radius + 0.4f));
                var segment = new GameObject("FoundryBoundary_" + i);
                segment.transform.SetParent(transform, false);
                segment.transform.position = position;
                segment.transform.rotation = Quaternion.Euler(0, angle, 0);
                var box = segment.AddComponent<BoxCollider>();
                box.center = Vector3.up * 3f;
                box.size = new Vector3((radius + 0.4f) * Mathf.PI * 2f / barriers.Length + 0.1f, 6f, 0.9f);
                box.enabled = false;
                barriers[i] = box;
                plateRenderers[i] = Primitive("BuriedPlate", segment.transform, new Vector3(0, 0.025f, 0), new Vector3(2.9f, 0.05f, 1.4f), steel);
                plateRenderers[i].enabled = false;
                rimRenderers[i] = Primitive("HeatedSeam", segment.transform, new Vector3(0, 0.08f, -0.15f), new Vector3(2.8f, 0.08f, 0.24f), dormant);
                rimRenderers[i].enabled = false;
                // A visible raised shutter represents the same solid volume as the collider.
                fieldRenderers[i] = Primitive("ContainmentField", segment.transform, Vector3.up * 1.2f, new Vector3(2.85f, 2.4f, 0.12f), field);
                fieldRenderers[i].enabled = false;
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 position = center + Quaternion.Euler(0, i * 90f, 0) * Vector3.forward * (radius - 3.5f);
                var vent = new GameObject("ReinforcementVent_" + i).transform;
                vent.SetParent(transform, false);
                vent.position = ArenaSurface.OnGround(position);
                Primitive("VentPlate", vent, new Vector3(0, 0.03f, 0), new Vector3(2.4f, 0.06f, 2.4f), steel);
                for (int slat = -2; slat <= 2; slat++)
                    Primitive("VentSlat", vent, new Vector3(slat * 0.36f, 0.075f, 0), new Vector3(0.1f, 0.06f, 1.9f), dormant);
            }
            for (int i = 0; i < spawnMarkers.Length; i++)
            {
                var marker = new GameObject("ReinforcementWarning_" + i);
                marker.transform.SetParent(transform, false);
                marker.AddComponent<MeshFilter>().sharedMesh = annulus;
                var renderer = marker.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = warning;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                spawnMarkers[i] = marker.transform;
                marker.SetActive(false);
            }
        }

        private static Renderer Primitive(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        private static Material Material(Shader shader, string name, Color color)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            return material;
        }

        private static Mesh BuildRing()
        {
            const int count = 48;
            var vertices = new Vector3[(count + 1) * 2];
            var triangles = new int[count * 6];
            for (int i = 0; i <= count; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 360f / count, 0) * Vector3.forward;
                vertices[i * 2] = direction;
                vertices[i * 2 + 1] = direction * 0.88f;
                if (i == count) continue;
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "FoundrySpawnRing", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildFade()
        {
            var overlay = new GameObject("FoundryTransfer", typeof(Canvas), typeof(CanvasScaler));
            overlay.transform.SetParent(transform, false);
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var curtain = new GameObject("Curtain", typeof(RectTransform), typeof(Image));
            curtain.transform.SetParent(overlay.transform, false);
            fade = curtain.GetComponent<Image>();
            fade.raycastTarget = false;
            var rect = curtain.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            SetFade(0f);
        }

        private void SetFade(float alpha)
        {
            if (fade != null) fade.color = new Color(0.015f, 0.01f, 0.018f, Mathf.Clamp01(alpha));
        }

        private void OnDisable() { if (initialized) EndEncounter(); }
        private void OnDestroy()
        {
            RestoreCamera();
            Destroy(steel); Destroy(dormant); Destroy(hot); Destroy(warning); Destroy(field); Destroy(annulus);
        }
    }
}
