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

        public DamageNumberSnapshot(int targetId, int sourceId, int burstId, float damage,
            Vector3 worldPosition, bool critical, bool canAggregate)
        {
            TargetId = targetId; SourceId = sourceId; BurstId = burstId; Damage = damage;
            WorldPosition = worldPosition; IsCritical = critical; CanAggregate = canAggregate;
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
            public RectTransform Rect;
            public bool Active;
            public bool Critical;
            public bool CanAggregate;
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
        }

        private readonly Entry[] entries = new Entry[MaxActiveNumbers];
        private readonly char[] numberBuffer = new char[48];
        private readonly Dictionary<double, string> normalTexts = new Dictionary<double, string>(TextCacheCapacity);
        private readonly Dictionary<double, string> criticalTexts = new Dictionary<double, string>(TextCacheCapacity);
        private Canvas canvas;
        private RectTransform canvasRect;
        private CombatSession subscribedSession;
        private bool initialized;
        private bool ownsSettings;
        private float presentationTime;

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
            canvas.sortingOrder = 40;
            canvasRect = (RectTransform)root.transform;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            Font font = settings.Font != null ? settings.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font.RequestCharactersInTexture("0123456789.! ", settings.NormalFontSize, FontStyle.Bold);
            font.RequestCharactersInTexture("0123456789.! ", settings.CriticalFontSize, FontStyle.Bold);
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
                label.text = "! 0123456789012345678901234567890123456789.99";
                var outline = labelObject.GetComponent<Outline>();
                outline.effectColor = new Color(0.06f, 0.025f, 0.08f, 0.95f);
                outline.effectDistance = new Vector2(1f, -1f);
                label.canvasRenderer.SetAlpha(0f);
                entries[i] = new Entry { Label = label, Rect = rect };
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
            foreach (Entry entry in entries)
                if (entry.Active && entry.TargetId == death.ActorId) entry.CanAggregate = false;
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
            if (!InsideCamera(anchor)) { DroppedNumberCount++; return false; }
            ExpireEntries();
            bool aggregate = !damage.IsCritical && damage.BurstId > 0 &&
                (damage.SourceType == DamageSourceType.FlashlightPulse || damage.SourceType == DamageSourceType.SolarPulse);
            if (aggregate)
            {
                foreach (Entry existing in entries)
                {
                    if (!existing.Active || !existing.CanAggregate || existing.Critical || existing.TargetId != damage.TargetId ||
                        existing.SourceId != damage.SourceId || existing.BurstId != damage.BurstId || existing.SourceType != damage.SourceType) continue;
                    existing.Damage = (float)Math.Min(float.MaxValue, (double)existing.Damage + damage.HealthLost);
                    existing.UpdatedAt = presentationTime;
                    existing.WorldPosition = anchor;
                    existing.CanAggregate = !damage.CausedDeath && damage.RemainingHealth > 0f;
                    existing.Label.text = FormattedNumber(existing.Damage, false);
                    existing.VerticalOffset = ChooseVerticalLane(existing);
                    AggregatedHitCount++;
                    Present(existing);
                    return true;
                }
            }

            Entry entry = GetAvailableEntry(damage.IsCritical, anchor);
            if (entry == null) { DroppedNumberCount++; return false; }
            if (entry.Active) RecycledNumberCount++;
            else ActiveNumberCount++;
            entry.Active = true; entry.Critical = damage.IsCritical;
            entry.TargetId = damage.TargetId; entry.SourceId = damage.SourceId; entry.BurstId = damage.BurstId;
            entry.SourceType = damage.SourceType; entry.Damage = damage.HealthLost; entry.WorldPosition = anchor;
            entry.CanAggregate = aggregate && !damage.CausedDeath && damage.RemainingHealth > 0f;
            entry.StartedAt = entry.UpdatedAt = presentationTime;
            int lane = (damage.TargetId ^ damage.BurstId ^ damage.PulseIndex) & 3;
            entry.HorizontalOffset = lane == 0 ? -20f : lane == 1 ? 20f : lane == 2 ? -10f : 30f;
            entry.Label.fontSize = entry.Critical ? settings.CriticalFontSize : settings.NormalFontSize;
            entry.Label.color = entry.Critical ? settings.CriticalColor : settings.NormalColor;
            entry.Label.text = FormattedNumber(entry.Damage, entry.Critical);
            entry.VerticalOffset = ChooseVerticalLane(entry);
            TotalShownCount++;
            Present(entry);
            return true;
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
                if (entry.Critical)
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
            foreach (Entry entry in entries) if (entry.Active) Present(entry);
        }
        private void ExpireEntries()
        {
            foreach (Entry entry in entries)
                if (entry.Active && presentationTime - entry.UpdatedAt >= settings.Lifetime)
                { Hide(entry); ActiveNumberCount--; }
        }
        private void Present(Entry entry)
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(entry.WorldPosition);
            bool visible = InsideCamera(entry.WorldPosition);
            entry.Label.canvasRenderer.cull = !visible;
            if (!visible) { entry.Label.canvasRenderer.SetAlpha(0f); return; }
            float age = Mathf.Max(0f, presentationTime - entry.StartedAt);
            float fadeProgress = Mathf.Clamp01((presentationTime - entry.UpdatedAt) / settings.Lifetime);
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, fadeProgress));
            entry.Label.canvasRenderer.SetAlpha(alpha);
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCamera, out Vector2 position))
            {
                float intensity = session != null ? session.Effects.DecorativeIntensity : 1f;
                position += new Vector2(entry.HorizontalOffset, entry.VerticalOffset + settings.RisePixels * Mathf.Clamp01(age / settings.Lifetime) * intensity);
                entry.Rect.anchoredPosition = position;
                float scale = age < settings.PopInDuration
                    ? Mathf.Lerp(settings.InitialScale, settings.PeakScale, age / settings.PopInDuration)
                    : Mathf.Lerp(settings.PeakScale, 1f, Mathf.Clamp01((age - settings.PopInDuration) / settings.PopReturnDuration));
                entry.Rect.localScale = Vector3.one * Mathf.Lerp(1f, scale, intensity);
            }
        }
        private float ChooseVerticalLane(Entry entry)
        {
            float scale = Mathf.Max(0.001f, canvas.scaleFactor);
            Vector3 screen = worldCamera.WorldToScreenPoint(entry.WorldPosition);
            Vector2 anchor = new Vector2(screen.x / scale + entry.HorizontalOffset, screen.y / scale);
            float width = entry.Label.text.Length * entry.Label.fontSize * 0.62f;
            for (int lane = 0; lane < 4; lane++)
            {
                float offset = lane * (settings.CriticalFontSize + 8f);
                bool overlaps = false;
                foreach (Entry other in entries)
                {
                    if (!other.Active || other == entry) continue;
                    Vector3 projected = worldCamera.WorldToScreenPoint(other.WorldPosition);
                    float otherX = projected.x / scale + other.HorizontalOffset;
                    float otherY = projected.y / scale + other.VerticalOffset;
                    float otherWidth = other.Label.text.Length * other.Label.fontSize * 0.62f;
                    if (Mathf.Abs(anchor.x - otherX) < (width + otherWidth) * 0.5f + 6f
                        && Mathf.Abs(anchor.y + offset - otherY) < settings.CriticalFontSize + 5f)
                    { overlaps = true; break; }
                }
                if (!overlaps) return offset;
            }
            return 3f * (settings.CriticalFontSize + 8f);
        }
        private bool InsideCamera(Vector3 position)
        {
            Vector3 viewport = worldCamera.WorldToViewportPoint(position);
            return viewport.z > worldCamera.nearClipPlane && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
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
            if (critical) { numberBuffer[length++] = '!'; numberBuffer[length++] = ' '; }
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
            text = new string(numberBuffer, 0, length);
            if (cache.Count < TextCacheCapacity) cache.Add(cents, text);
            return text;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Hide(Entry entry)
        {
            entry.Active = false; entry.CanAggregate = false;
            entry.Label.canvasRenderer.SetAlpha(0f); entry.Label.canvasRenderer.cull = true;
            entry.Rect.localScale = Vector3.one;
        }
        public void Clear()
        {
            if (!initialized) return;
            foreach (Entry entry in entries) Hide(entry);
            ActiveNumberCount = 0;
        }
        private void ResetForSession()
        {
            Clear(); presentationTime = 0f;
            TotalShownCount = AggregatedHitCount = RecycledNumberCount = DroppedNumberCount = 0;
        }
        private Entry GetActiveEntry(int index)
        {
            foreach (Entry entry in entries)
                if (entry != null && entry.Active && index-- == 0) return entry;
            return null;
        }
        public Text GetActiveLabel(int index) => index >= 0 ? GetActiveEntry(index)?.Label : null;
        public DamageNumberSnapshot GetActiveNumber(int index)
        {
            Entry entry = index >= 0 ? GetActiveEntry(index) : null;
            return entry != null ? new DamageNumberSnapshot(entry.TargetId, entry.SourceId, entry.BurstId,
                entry.Damage, entry.WorldPosition, entry.Critical, entry.CanAggregate) : default;
        }
    }
}
