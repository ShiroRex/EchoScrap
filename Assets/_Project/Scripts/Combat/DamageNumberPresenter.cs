using System;
using System.Collections.Generic;
using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    public readonly struct DamageNumberSnapshot
    {
        public readonly int TargetId;
        public readonly int SourceId;
        public readonly int BurstId;
        public readonly float Damage;
        public readonly Vector3 WorldPosition;
        public readonly bool IsCritical;
        public readonly bool CanAggregate;
        public readonly DamageType DamageType;
        public readonly bool WasExecuted;
        public readonly bool WasResisted;

        public DamageNumberSnapshot(int targetId, int sourceId, int burstId, float damage,
            Vector3 worldPosition, bool critical, bool canAggregate, DamageType damageType = global::EchoScrap.Core.DamageType.Physical, bool wasExecuted = false, bool wasResisted = false)
        {
            TargetId = targetId; SourceId = sourceId; BurstId = burstId; Damage = damage;
            WorldPosition = worldPosition; IsCritical = critical; CanAggregate = canAggregate;
            DamageType = damageType; WasExecuted = wasExecuted;
            WasResisted = wasResisted;
        }
    }

    /// <summary>
    /// Session-local, fixed-budget damage labels. Values and anchors come from resolved damage,
    /// never from pooled actors. Projection happens after Cinemachine; no gameplay timer is modified.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class DamageNumberPresenter : MonoBehaviour
    {
        public const int MaxActiveNumbers = 96;
        private const int TextCacheCapacity = 512;
        [SerializeField] private CombatSession session;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private DamageNumberSettings settings;

        private sealed class Entry
        {
            public Text Label;
            public Text ResistanceLabel;
            public RectTransform Rect;
            public bool Active;
            public bool Critical;
            public bool CanAggregate;
            public bool Executed;
            public bool Resisted;
            public DamageType DamageType;
            public int TargetId;
            public int SourceId;
            public int BurstId;
            public DamageSourceType SourceType;
            public float Damage;
            public Vector3 WorldPosition;
            public float StartedAt;
            public float UpdatedAt;
            public float HorizontalOffset;
            public float VerticalOffset;
            public Vector2 FlightOrigin;
            public Vector2 LaunchVelocity;
            public float FlightStartedAt;
            public float LaunchHeight;
            public float InitialTravel;
            public float Gravity;
            public float RotationDegrees;
            public float RotationOrigin;
            public Vector3 CachedViewportPos;
            public Vector3 CachedWorldForViewport;
            public int CachedViewportFrame = -1;
            public float LastLabelAlpha = -1f;
            public float LastResistAlpha = -1f;
            public float LastUniformScale = -1f;
            public int LastLabelCull = -1;
            public int LastResistCull = -1;
        }

        private readonly Entry[] entries = new Entry[MaxActiveNumbers];
        private readonly List<Entry> activeEntries = new List<Entry>(MaxActiveNumbers);
        private readonly char[] numberBuffer = new char[48];
        private readonly Dictionary<double, string> normalTexts = new Dictionary<double, string>(TextCacheCapacity);
        private readonly Dictionary<double, string> criticalTexts = new Dictionary<double, string>(TextCacheCapacity);
        private Canvas canvas;
        private RectTransform canvasRect;
        private CombatSession subscribedSession;
        private bool initialized;
        private bool ownsSettings;
        private float presentationTime;
        private float lastExpiredAt = -1f;
        private uint motionSequence;
        private Vector3 cachedCameraPos;
        private Quaternion cachedCameraRot;
        private float cachedCameraFov = -1f;
        private int cachedCameraFrame = -1;
        private int cameraCacheEpoch;

        public int ActiveNumberCount { get; private set; }
        public int PoolCapacity => initialized ? entries.Length : 0;
        public int TotalShownCount { get; private set; }
        public int AggregatedHitCount { get; private set; }
        public int RecycledNumberCount { get; private set; }
        public int DroppedNumberCount { get; private set; }
        public Camera WorldCamera => worldCamera;
        public Canvas NumberCanvas => canvas;
        public DamageNumberSettings Settings { get { EnsureInitialized(); return settings; } }

        private void Awake() => EnsureInitialized();
        private void OnEnable()
        {
            EnsureInitialized();
            if (session == null) session = FindAnyObjectByType<CombatSession>();
            if (worldCamera == null) worldCamera = Camera.main;
            Subscribe();
            if (canvas != null) canvas.gameObject.SetActive(true);
        }
        private void OnDisable()
        {
            Unsubscribe();
            Clear();
            if (canvas != null) canvas.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            Unsubscribe();
            if (ownsSettings && settings != null) Destroy(settings);
        }

        public void BindReferences(CombatSession combatSession, Camera camera)
        {
            EnsureInitialized();
            if (session == combatSession && worldCamera == camera) { Subscribe(); return; }
            Unsubscribe(); Clear();
            session = combatSession; worldCamera = camera;
            Subscribe();
        }

        public void EnsureInitialized()
        {
            if (initialized && canvas != null) return;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<DamageNumberSettings>();
                ownsSettings = true;
            }
            var root = new GameObject("Damage Number Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // Below the HUD and its pause/result overlay (20).
            canvasRect = (RectTransform)root.transform;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            Font font = settings.Font != null ? settings.Font : Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.semibold ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font.RequestCharactersInTexture("0123456789.! EJECUTADO Resistido", settings.NormalFontSize, FontStyle.Bold);
            font.RequestCharactersInTexture("0123456789.! EJECUTADO", settings.CriticalFontSize, FontStyle.Bold);
            WarmKnownValues();
            for (int i = 0; i < entries.Length; i++)
            {
                var labelObject = new GameObject("Damage Number " + i, typeof(RectTransform), typeof(Text), typeof(Outline));
                var rect = (RectTransform)labelObject.transform;
                rect.SetParent(root.transform, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(220f, 64f);
                Text label = labelObject.GetComponent<Text>();
                label.font = font; label.fontSize = settings.CriticalFontSize;
                label.fontStyle = FontStyle.Bold; label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Overflow;
                label.supportRichText = false; label.raycastTarget = false;
                label.text = "0123456789012345678901234567890123456789.99! EJECUTADO";
                var outline = labelObject.GetComponent<Outline>();
                outline.effectColor = new Color(0.06f, 0.025f, 0.08f, 0.95f);
                outline.effectDistance = new Vector2(1f, -1f);
                label.canvasRenderer.SetAlpha(0f);
                var resistanceObject = new GameObject("Resistido", typeof(RectTransform), typeof(Text), typeof(Outline));
                var resistanceRect = (RectTransform)resistanceObject.transform;
                resistanceRect.SetParent(rect, false);
                resistanceRect.sizeDelta = new Vector2(220f, 28f);
                resistanceRect.anchoredPosition = new Vector2(0f, -25f);
                Text resistance = resistanceObject.GetComponent<Text>();
                resistance.font = font; resistance.fontSize = 18; resistance.fontStyle = FontStyle.Bold;
                resistance.alignment = TextAnchor.MiddleCenter; resistance.text = "Resistido";
                resistance.color = new Color(.75f,.85f,1f); resistance.raycastTarget = false;
                resistanceObject.GetComponent<Outline>().effectColor = new Color(.04f,.04f,.07f,.95f);
                entries[i] = new Entry { Label = label, ResistanceLabel = resistance, Rect = rect };
            }
            Canvas.ForceUpdateCanvases();
            foreach (Entry entry in entries) Hide(entry);
            initialized = true;
        }

        private void Subscribe()
        {
            if (!isActiveAndEnabled || subscribedSession == session) return;
            Unsubscribe(); subscribedSession = session;
            if (subscribedSession == null) return;
            subscribedSession.Events.OnDamageResolved += HandleDamage;
            subscribedSession.Events.OnActorDied += HandleDeath;
            subscribedSession.Events.OnSessionStateChanged += HandleSessionState;
            subscribedSession.SessionReset += ResetForSession;
        }
        private void Unsubscribe()
        {
            if (subscribedSession != null)
            {
                subscribedSession.Events.OnDamageResolved -= HandleDamage;
                subscribedSession.Events.OnActorDied -= HandleDeath;
                subscribedSession.Events.OnSessionStateChanged -= HandleSessionState;
                subscribedSession.SessionReset -= ResetForSession;
            }
            subscribedSession = null;
        }
        private void HandleDamage(DamageResult damage) => TryShowDamage(in damage);
        private void HandleDeath(ActorDeathEvent death)
        {
            for (int i = 0; i < activeEntries.Count; i++)
            {
                Entry entry = activeEntries[i];
                if (entry.TargetId == death.ActorId) entry.CanAggregate = false;
            }
        }
        private void HandleSessionState(SessionStateEvent state)
        {
            if (state.State == SessionState.Victory || state.State == SessionState.Defeat || state.State == SessionState.BossIntro)
                Clear();
        }

        public bool TryShowDamage(in DamageResult damage)
        {
            if (!isActiveAndEnabled || !damage.AppliedRealDamage || damage.IsPlayerTarget ||
                !Finite(damage.HealthLost) || !Finite(damage.WorldPosition.x) || !Finite(damage.WorldPosition.y) ||
                !Finite(damage.WorldPosition.z) || worldCamera == null) return false;
            EnsureInitialized();
            Vector3 anchor = damage.WorldPosition + Vector3.up * settings.WorldHeightOffset;
            Vector3 anchorViewport = ProjectViewport(anchor);
            if (!IsViewportVisible(anchorViewport)) { DroppedNumberCount++; return false; }
            ExpireEntries();
            bool aggregate = !damage.IsCritical && !damage.WasExecuted && damage.BurstId > 0 &&
                DamageTypes.IsFlashlight(damage.SourceType);
            if (aggregate)
            {
                for (int i = 0; i < activeEntries.Count; i++)
                {
                    Entry existing = activeEntries[i];
                    if (!existing.CanAggregate || existing.Critical || existing.TargetId != damage.TargetId ||
                        existing.SourceId != damage.SourceId || existing.BurstId != damage.BurstId || existing.SourceType != damage.SourceType || existing.DamageType != damage.DamageType || existing.Resisted != damage.WasResisted) continue;
                    Vector2 currentFlight = FlightOffset(existing);
                    float intensity = session != null ? session.Effects.DecorativeIntensity : 1f;
                    if (intensity > .0001f &&
                        TryViewportToCanvasLocal(GetEntryViewport(existing), out Vector2 oldAnchor) &&
                        TryViewportToCanvasLocal(anchorViewport, out Vector2 newAnchor))
                        currentFlight += (oldAnchor-newAnchor)/intensity;
                    existing.Damage = (float)Math.Min(float.MaxValue, (double)existing.Damage + damage.HealthLost);
                    existing.UpdatedAt = presentationTime;
                    existing.WorldPosition = anchor;
                    existing.CachedWorldForViewport = anchor;
                    existing.CachedViewportPos = anchorViewport;
                    existing.CachedViewportFrame = cameraCacheEpoch;
                    existing.CanAggregate = !damage.CausedDeath && damage.RemainingHealth > 0f;
                    existing.Label.text = FormattedNumber(existing.Damage, false);
                    StartFlight(existing, currentFlight, settings.AggregateBounceMultiplier);
                    AggregatedHitCount++;
                    Present(existing);
                    return true;
                }
            }

            Entry entry = GetAvailableEntry(damage.IsCritical || damage.WasExecuted, anchor);
            if (entry == null) { DroppedNumberCount++; return false; }
            if (entry.Active) RecycledNumberCount++;
            else { ActiveNumberCount++; activeEntries.Add(entry); }
            entry.Active = true; entry.Critical = damage.IsCritical; entry.Executed = damage.WasExecuted; entry.DamageType = damage.DamageType;
            entry.Resisted = damage.WasResisted;
            entry.TargetId = damage.TargetId; entry.SourceId = damage.SourceId; entry.BurstId = damage.BurstId;
            entry.SourceType = damage.SourceType; entry.Damage = damage.HealthLost; entry.WorldPosition = anchor;
            entry.CachedWorldForViewport = anchor; entry.CachedViewportPos = anchorViewport; entry.CachedViewportFrame = cameraCacheEpoch;
            entry.CanAggregate = aggregate && !damage.CausedDeath && damage.RemainingHealth > 0f;
            entry.StartedAt = entry.UpdatedAt = presentationTime;
            int lane = (damage.TargetId ^ damage.BurstId ^ damage.PulseIndex) & 3;
            entry.HorizontalOffset = lane == 0 ? -20f : lane == 1 ? 20f : lane == 2 ? -10f : 30f;
            entry.Label.fontSize = entry.Critical || entry.Executed ? settings.CriticalFontSize : settings.NormalFontSize;
            entry.Label.color = entry.Executed ? settings.ExecutionColor : settings.DamageColor(entry.DamageType, entry.Critical);
            entry.Label.text = entry.Executed ? "EJECUTADO" : FormattedNumber(entry.Damage, entry.Critical);
            InitializeFlight(entry, in damage);
            entry.VerticalOffset = ChooseVerticalLane(entry, anchorViewport);
            TotalShownCount++;
            Present(entry);
            return true;
        }

        // A local hash varies cosmetics without sampling combat or presentation RNG streams.
        private static float MotionSample(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; value ^= value >> 16; }
            return (value & 0xffffff) / 16777216f;
        }
        private void InitializeFlight(Entry entry, in DamageResult damage)
        {
            uint seed = unchecked(++motionSequence * 0x9e3779b9u ^ (uint)damage.TargetId * 0x85ebca6bu ^ (uint)damage.SourceId ^ (uint)damage.BurstId * 65711u ^ (uint)damage.PulseIndex << 16);
            float sign = MotionSample(seed) < .5f ? -1f : 1f;
            Vector2 travel = settings.HorizontalTravelPixels;
            float punch = entry.Critical ? settings.CriticalLaunchMultiplier : 1f;
            entry.InitialTravel = sign * Mathf.Lerp(travel.x, travel.y, MotionSample(seed ^ 0xa511e9b3u)) * punch;
            entry.LaunchHeight = settings.RisePixels * Mathf.Lerp(1f-settings.HeightVariation, 1f+settings.HeightVariation, MotionSample(seed ^ 0x63d83595u)) * punch;
            entry.RotationDegrees = Mathf.Lerp(-settings.MaximumTiltDegrees, settings.MaximumTiltDegrees, MotionSample(seed ^ 0xb5297a4du));
            StartFlight(entry, Vector2.zero, 1f);
        }
        private void StartFlight(Entry entry, Vector2 origin, float kick)
        {
            float duration = settings.Lifetime;
            float height = entry.LaunchHeight * kick;
            entry.FlightOrigin = origin;
            entry.RotationOrigin = Mathf.DeltaAngle(0f, entry.Rect.localEulerAngles.z);
            entry.FlightStartedAt = presentationTime;
            entry.LaunchVelocity = new Vector2(entry.InitialTravel * Mathf.Lerp(.6f, 1f, kick) / duration, 4f*height/duration);
            entry.Gravity = (8f*height + 2f*settings.LandingDropPixels) / (duration*duration);
        }
        private Vector2 FlightOffset(Entry entry) => FlightOffset(entry, presentationTime);
        private Vector2 FlightOffset(Entry entry, float atTime)
        {
            if (entry.Executed) return Vector2.up * (settings.RisePixels*.5f*Mathf.Clamp01((atTime-entry.StartedAt)/settings.ExecutionLifetime));
            float t = Mathf.Max(0f, atTime-entry.FlightStartedAt);
            return entry.FlightOrigin + entry.LaunchVelocity*t - Vector2.up*(.5f*entry.Gravity*t*t);
        }

        private Entry GetAvailableEntry(bool critical, Vector3 anchor)
        {
            Entry farthestNormal = null;
            Entry oldestCritical = null;
            float farthestDistance = -1f;
            Vector3 cameraPosition = worldCamera.transform.position;
            foreach (Entry entry in entries)
            {
                if (!entry.Active) return entry;
                if (entry.Critical || entry.Executed)
                {
                    if (oldestCritical == null || entry.StartedAt < oldestCritical.StartedAt) oldestCritical = entry;
                    continue;
                }
                float distance = (entry.WorldPosition - cameraPosition).sqrMagnitude;
                if (distance > farthestDistance || (Mathf.Approximately(distance, farthestDistance) &&
                    (farthestNormal == null || entry.StartedAt < farthestNormal.StartedAt)))
                { farthestDistance = distance; farthestNormal = entry; }
            }
            if (critical) return farthestNormal ?? oldestCritical;
            return farthestNormal != null && (anchor - cameraPosition).sqrMagnitude <= farthestDistance
                ? farthestNormal : null;
        }

        private void LateUpdate() => StepPresentation(Time.deltaTime);
        public void StepPresentation(float deltaTime)
        {
            if (!isActiveAndEnabled) return;
            EnsureInitialized();
            if (Finite(deltaTime) && deltaTime > 0f && (session == null || !session.IsPaused)) presentationTime += deltaTime;
            ExpireEntries();
            RefreshProjection();
        }
        public void RefreshProjection()
        {
            if (!initialized || worldCamera == null) return;
            EnsureCameraCache();
            for (int i = 0; i < activeEntries.Count; i++) Present(activeEntries[i]);
        }
        private void ExpireEntries()
        {
            if (activeEntries.Count == 0 || lastExpiredAt == presentationTime) return;
            lastExpiredAt = presentationTime;
            for (int i = activeEntries.Count - 1; i >= 0; i--)
            {
                Entry entry = activeEntries[i];
                if (presentationTime - entry.UpdatedAt >= (entry.Executed ? settings.ExecutionLifetime : settings.Lifetime))
                {
                    Hide(entry);
                    activeEntries.RemoveAt(i);
                    ActiveNumberCount--;
                }
            }
        }
        private void EnsureCameraCache()
        {
            int frame = Time.frameCount;
            Transform camTransform = worldCamera.transform;
            Vector3 pos = camTransform.position;
            Quaternion rot = camTransform.rotation;
            float fov = worldCamera.fieldOfView;
            if (cachedCameraFrame != frame || pos != cachedCameraPos || rot != cachedCameraRot || fov != cachedCameraFov)
            {
                cachedCameraFrame = frame;
                cachedCameraPos = pos;
                cachedCameraRot = rot;
                cachedCameraFov = fov;
                cameraCacheEpoch++;
            }
        }
        private Vector3 ProjectViewport(Vector3 worldPos)
        {
            EnsureCameraCache();
            return worldCamera.WorldToViewportPoint(worldPos);
        }
        private Vector3 GetEntryViewport(Entry entry)
        {
            EnsureCameraCache();
            if (entry.CachedViewportFrame == cameraCacheEpoch && entry.CachedWorldForViewport == entry.WorldPosition)
                return entry.CachedViewportPos;
            entry.CachedViewportPos = worldCamera.WorldToViewportPoint(entry.WorldPosition);
            entry.CachedWorldForViewport = entry.WorldPosition;
            entry.CachedViewportFrame = cameraCacheEpoch;
            return entry.CachedViewportPos;
        }
        private bool IsViewportVisible(Vector3 viewport) =>
            viewport.z > worldCamera.nearClipPlane && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        private Vector2 ViewportToScreenPoint(Vector3 viewport)
        {
            Rect pixelRect = worldCamera.pixelRect;
            return new Vector2(pixelRect.x + viewport.x * pixelRect.width, pixelRect.y + viewport.y * pixelRect.height);
        }
        private bool TryViewportToCanvasLocal(Vector3 viewport, out Vector2 position)
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Vector2 screen = ViewportToScreenPoint(viewport);
                float scale = Mathf.Max(0.0001f, canvas.scaleFactor);
                Rect rect = canvasRect.rect;
                position = new Vector2(screen.x / scale + rect.xMin, screen.y / scale + rect.yMin);
                return true;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, ViewportToScreenPoint(viewport), canvas.worldCamera, out position);
        }
        private void Present(Entry entry)
        {
            Vector3 viewport = GetEntryViewport(entry);
            bool visible = IsViewportVisible(viewport);
            int cullLabel = visible ? 0 : 1;
            int cullResist = (!visible || !entry.Resisted || entry.Executed) ? 1 : 0;
            if (entry.LastLabelCull != cullLabel) { entry.LastLabelCull = cullLabel; entry.Label.canvasRenderer.cull = !visible; }
            if (entry.LastResistCull != cullResist) { entry.LastResistCull = cullResist; entry.ResistanceLabel.canvasRenderer.cull = cullResist != 0; }
            if (!visible)
            {
                if (entry.LastLabelAlpha != 0f) { entry.LastLabelAlpha = 0f; entry.Label.canvasRenderer.SetAlpha(0f); }
                if (entry.LastResistAlpha != 0f) { entry.LastResistAlpha = 0f; entry.ResistanceLabel.canvasRenderer.SetAlpha(0f); }
                return;
            }
            float age = Mathf.Max(0f, presentationTime - entry.StartedAt);
            float fadeProgress = Mathf.Clamp01((presentationTime - entry.UpdatedAt) / (entry.Executed ? settings.ExecutionLifetime : settings.Lifetime));
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, fadeProgress));
            if (entry.LastLabelAlpha != alpha) { entry.LastLabelAlpha = alpha; entry.Label.canvasRenderer.SetAlpha(alpha); }
            float resistAlpha = entry.Resisted && !entry.Executed ? alpha : 0f;
            if (entry.LastResistAlpha != resistAlpha) { entry.LastResistAlpha = resistAlpha; entry.ResistanceLabel.canvasRenderer.SetAlpha(resistAlpha); }
            if (TryViewportToCanvasLocal(viewport, out Vector2 position))
            {
                float intensity = session != null ? session.Effects.DecorativeIntensity : 1f;
                position += new Vector2(entry.HorizontalOffset, entry.VerticalOffset) + FlightOffset(entry)*intensity;
                float flightProgress = Mathf.Clamp01((presentationTime-entry.FlightStartedAt)/settings.Lifetime);
                entry.Rect.localRotation = Quaternion.Euler(0f,0f,entry.Executed?0f:(Mathf.Lerp(entry.RotationOrigin,0f,flightProgress)+entry.RotationDegrees*Mathf.Sin(flightProgress*Mathf.PI))*intensity);
                entry.Rect.anchoredPosition = position;
                float scale = age < settings.PopInDuration
                    ? Mathf.Lerp(settings.InitialScale, settings.PeakScale, age / settings.PopInDuration)
                    : Mathf.Lerp(settings.PeakScale, 1f, Mathf.Clamp01((age - settings.PopInDuration) / settings.PopReturnDuration));
                float appliedScale = Mathf.Lerp(1f, scale, intensity);
                if (entry.LastUniformScale != appliedScale)
                {
                    entry.LastUniformScale = appliedScale;
                    entry.Rect.localScale = Vector3.one * appliedScale;
                }
            }
        }
        private float ChooseVerticalLane(Entry entry, Vector3 entryViewport)
        {
            float scale = Mathf.Max(0.001f, canvas.scaleFactor);
            Vector2 screen = ViewportToScreenPoint(entryViewport);
            Vector2 anchor = new Vector2(screen.x / scale + entry.HorizontalOffset, screen.y / scale);
            float width = entry.Label.text.Length * entry.Label.fontSize * 0.62f;
            float intensity = session != null ? session.Effects.DecorativeIntensity : 1f;
            float maxFlightX = settings.HorizontalTravelPixels.y * settings.CriticalLaunchMultiplier * intensity + 140f;
            for (int lane = 0; lane < 4; lane++)
            {
                float offset = lane * (settings.CriticalFontSize + 8f + (entry.Resisted ? 28f : 0f));
                bool overlaps = false;
                for (int i = 0; i < activeEntries.Count; i++)
                {
                    Entry other = activeEntries[i];
                    if (other == entry) continue;
                    Vector2 projected = ViewportToScreenPoint(GetEntryViewport(other));
                    float otherWidth = other.Label.text.Length * other.Label.fontSize * 0.62f;
                    float baseOtherX = projected.x / scale + other.HorizontalOffset;
                    float combinedHalfWidth = (width + otherWidth) * .5f + 6f;
                    if (Mathf.Abs(anchor.x - baseOtherX) > combinedHalfWidth + maxFlightX) continue;
                    // Reserve a clear lane through the upcoming arc, not only at its starting point.
                    for (int sample=0;sample<3;sample++)
                    {
                        float atTime=presentationTime+sample*.18f;
                        if (atTime-other.UpdatedAt >= (other.Executed?settings.ExecutionLifetime:settings.Lifetime)) continue;
                        Vector2 flight=FlightOffset(other,atTime)*intensity;
                        Vector2 ownFlight=FlightOffset(entry,atTime)*intensity;
                        float otherX=baseOtherX+flight.x;
                        float otherY=projected.y/scale+other.VerticalOffset+flight.y;
                        if (Mathf.Abs(anchor.x+ownFlight.x-otherX)<combinedHalfWidth &&
                            Mathf.Abs(anchor.y+offset+ownFlight.y-otherY)<settings.CriticalFontSize+5f+((entry.Resisted || other.Resisted)?28f:0f))
                        {overlaps=true;break;}
                    }
                    if (overlaps) break;
                }
                if (!overlaps) return offset;
            }
            return 3f * (settings.CriticalFontSize + 8f + (entry.Resisted ? 28f : 0f));
        }

        private void WarmKnownValues()
        {
            FormattedNumber(0f, false);
            for (int integer = 1; integer <= 100; integer++)
            { FormattedNumber(integer, false); FormattedNumber(integer, true); }
            // Eight-card flashlight pool: base/overload, normal/Solar, normal/critical, one to four pulses.
            for (int overload = 0; overload < 4; overload++)
            for (int solar = 0; solar < 2; solar++)
            {
                float factor = overload == 1 ? 1.5f : overload == 2 ? 1.8f : overload == 3 ? 2.2f : 1f;
                float damage = 3.75f * factor * (solar == 1 ? 2.5f : 1f);
                for (int pulses = 1; pulses <= 4; pulses++) FormattedNumber(damage * pulses, false);
                FormattedNumber(damage * 1.75f, true);
            }
        }
        private string FormattedNumber(float damage, bool critical)
        {
            double cents = Math.Floor((double)damage * 100d + 0.5d);
            var cache = critical ? criticalTexts : normalTexts;
            if (cache.TryGetValue(cents, out string text)) return text;
            int length = 0;
            double integer = Math.Floor(cents / 100d);
            int fraction = cents < 9007199254740992d ? (int)(cents - integer * 100d) : 0;
            double divisor = 1d;
            while (integer / divisor >= 10d) divisor *= 10d;
            do
            {
                int digit = (int)Math.Min(9d, Math.Max(0d, Math.Floor(integer / divisor)));
                numberBuffer[length++] = (char)('0' + digit);
                integer = Math.Max(0d, integer - digit * divisor); divisor /= 10d;
            } while (divisor >= 1d);
            if (fraction > 0)
            {
                numberBuffer[length++] = '.';
                numberBuffer[length++] = (char)('0' + fraction / 10);
                if (fraction % 10 != 0) numberBuffer[length++] = (char)('0' + fraction % 10);
            }
            if (critical) numberBuffer[length++] = '!';
            text = new string(numberBuffer, 0, length);
            if (cache.Count < TextCacheCapacity) cache.Add(cents, text);
            return text;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Hide(Entry entry)
        {
            entry.Active = false; entry.CanAggregate = false; entry.Executed = false;
            entry.Resisted = false; entry.CachedViewportFrame = -1;
            entry.LastResistAlpha = 0f; entry.LastResistCull = 1;
            entry.LastLabelAlpha = 0f; entry.LastLabelCull = 1;
            entry.LastUniformScale = 1f;
            entry.ResistanceLabel.canvasRenderer.SetAlpha(0f); entry.ResistanceLabel.canvasRenderer.cull = true;
            entry.Label.canvasRenderer.SetAlpha(0f); entry.Label.canvasRenderer.cull = true;
            entry.Rect.localScale = Vector3.one;
            entry.Rect.localRotation = Quaternion.identity;
            entry.FlightOrigin = entry.LaunchVelocity = Vector2.zero;
            entry.Gravity = entry.RotationDegrees = entry.RotationOrigin = 0f;
        }
        public void Clear()
        {
            if (!initialized) return;
            foreach (Entry entry in entries) Hide(entry);
            activeEntries.Clear();
            ActiveNumberCount = 0;
            lastExpiredAt = -1f;
        }
        private void ResetForSession()
        {
            Clear(); presentationTime = 0f; lastExpiredAt = -1f; motionSequence = 0;
            cachedCameraFrame = -1; cameraCacheEpoch++;
            TotalShownCount = AggregatedHitCount = RecycledNumberCount = DroppedNumberCount = 0;
        }
        private Entry GetActiveEntry(int index)
        {
            foreach (Entry entry in entries)
                if (entry != null && entry.Active && index-- == 0) return entry;
            return null;
        }
        public Text GetActiveLabel(int index) => index >= 0 ? GetActiveEntry(index)?.Label : null;
        public Text GetActiveResistanceLabel(int index) => index >= 0 ? GetActiveEntry(index)?.ResistanceLabel : null;
        public DamageNumberSnapshot GetActiveNumber(int index)
        {
            Entry entry = index >= 0 ? GetActiveEntry(index) : null;
            return entry != null ? new DamageNumberSnapshot(entry.TargetId, entry.SourceId, entry.BurstId,
                entry.Damage, entry.WorldPosition, entry.Critical, entry.CanAggregate, entry.DamageType, entry.Executed, entry.Resisted) : default;
        }
    }
}
