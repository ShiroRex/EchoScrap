using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Presentación de daño recibido y curación del jugador:
    /// - Pérdida inmediata de HP y barra de daño reciente con retardo suave.
    /// - Marca direccional orientada hacia la amenaza en XZ.
    /// - Borde suave de pantalla (respetando modo de efectos reducidos).
    /// - Confirmación de curación únicamente cuando se gana HP real (nunca a salud completa).
    /// - Sin feedback al recibir golpes inmunes o evadidos.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHurtPresentation : MonoBehaviour
    {
        [SerializeField] private CombatSession session;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("UI Opcional")]
        [SerializeField] private Image immediateHealthFill;
        [SerializeField] private Image recentDamageFill;
        [SerializeField] private Image screenBorderOverlay;
        [SerializeField] private RectTransform directionalMarker;
        [SerializeField] private Image directionalMarkerImage;
        [SerializeField] private Image healPulseOverlay;

        [Header("Tiempos de Respuesta")]
        [SerializeField, Min(0.02f)] private float recentDamageHoldDuration = 0.16f;
        [SerializeField, Min(0.1f)] private float recentDamageDecaySpeed = 0.85f;
        [SerializeField, Min(0.05f)] private float borderFadeDuration = 0.30f;
        [SerializeField, Range(0f, 1f)] private float borderMaxAlpha = 0.28f;
        [SerializeField, Range(0f, 1f)] private float reducedBorderMaxAlpha = 0.12f;
        [SerializeField, Min(0.05f)] private float directionalDuration = 0.35f;
        [SerializeField, Min(0.05f)] private float healPulseDuration = 0.25f;

        private float immediateRatio = 1f;
        private float recentDamageRatio = 1f;
        private float recentDamageHoldTimer;
        private float borderTimer;
        private float directionalTimer;
        private float directionalAngleDegrees;
        private float healPulseTimer;
        private CombatSession subscribedSession;
        private PlayerHealth subscribedHealth;

        public float ImmediateHealthRatio => immediateRatio;
        public float RecentDamageRatio => recentDamageRatio;
        public float ScreenBorderAlpha { get; private set; }
        public float DirectionalMarkerAlpha { get; private set; }
        public float DirectionalAngleDegrees => directionalAngleDegrees;
        public float HealPulseAlpha { get; private set; }
        public int HurtFeedbackTriggerCount { get; private set; }
        public int HealFeedbackTriggerCount { get; private set; }
        public Vector3 LastThreatDirection { get; private set; }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            SyncFromCurrentHealth();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearTransientFeedback();
        }

        public void Bind(CombatSession combatSession, PlayerHealth health)
        {
            Unsubscribe();
            session = combatSession;
            playerHealth = health;
            Subscribe();
            SyncFromCurrentHealth();
        }

        public void AssignUiReferences(
            Image healthFillImg,
            Image recentFillImg,
            Image borderImg,
            RectTransform dirRect,
            Image dirImg,
            Image healImg = null)
        {
            immediateHealthFill = healthFillImg;
            recentDamageFill = recentFillImg;
            screenBorderOverlay = borderImg;
            directionalMarker = dirRect;
            directionalMarkerImage = dirImg;
            healPulseOverlay = healImg;
            ApplyToUiElements();
        }

        private void ResolveReferences()
        {
            if (session == null)
                session = FindAnyObjectByType<CombatSession>();
            if (playerHealth == null)
                playerHealth = session != null && session.PlayerHealth != null
                    ? session.PlayerHealth
                    : FindAnyObjectByType<PlayerHealth>();
        }

        private void Subscribe()
        {
            Unsubscribe();
            subscribedSession = session;
            subscribedHealth = playerHealth;

            if (subscribedSession != null)
            {
                subscribedSession.Events.OnDamageResolved += HandleDamageResolved;
                subscribedSession.Events.OnPlayerHealed += HandlePlayerHealed;
                subscribedSession.SessionReset += ClearTransientFeedback;
            }
            else if (subscribedHealth != null)
            {
                subscribedHealth.OnDamageProcessed += HandleDamageResolved;
                subscribedHealth.OnHealed += HandlePlayerHealed;
            }

            if (subscribedHealth != null)
            {
                subscribedHealth.OnHealthChanged += HandleHealthChanged;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedSession != null)
            {
                subscribedSession.Events.OnDamageResolved -= HandleDamageResolved;
                subscribedSession.Events.OnPlayerHealed -= HandlePlayerHealed;
                subscribedSession.SessionReset -= ClearTransientFeedback;
            }
            if (subscribedHealth != null)
            {
                subscribedHealth.OnDamageProcessed -= HandleDamageResolved;
                subscribedHealth.OnHealed -= HandlePlayerHealed;
                subscribedHealth.OnHealthChanged -= HandleHealthChanged;
            }
            subscribedSession = null;
            subscribedHealth = null;
        }

        private void SyncFromCurrentHealth()
        {
            if (playerHealth == null)
                return;

            float max = Mathf.Max(1f, playerHealth.MaxHealth);
            immediateRatio = Mathf.Clamp01(playerHealth.CurrentHealth / max);
            recentDamageRatio = immediateRatio;
            ApplyToUiElements();
        }

        private void HandleHealthChanged(float current, float max)
        {
            float nextRatio = Mathf.Clamp01(current / Mathf.Max(1f, max));
            immediateRatio = nextRatio;
            if (nextRatio > recentDamageRatio)
                recentDamageRatio = nextRatio;
            ApplyToUiElements();
        }

        private void HandleDamageResolved(DamageResult result)
        {
            if (!result.IsPlayerTarget)
                return;

            // Una evasión o un impacto ignorado/inmune jamás reproducen feedback de daño recibido
            if (!result.AppliedRealDamage)
                return;

            HurtFeedbackTriggerCount++;
            float max = Mathf.Max(1f, result.MaxHealth);
            float nextRatio = Mathf.Clamp01(result.RemainingHealth / max);
            recentDamageRatio = Mathf.Max(recentDamageRatio, immediateRatio);
            immediateRatio = nextRatio;
            recentDamageHoldTimer = recentDamageHoldDuration;

            borderTimer = borderFadeDuration;
            directionalTimer = directionalDuration;

            Vector3 toThreat = result.OriginPosition - result.WorldPosition;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude < 0.0001f)
                toThreat = -result.HitDirection;
            if (toThreat.sqrMagnitude < 0.0001f)
                toThreat = Vector3.forward;
            else
                toThreat.Normalize();

            LastThreatDirection = toThreat;
            directionalAngleDegrees = Mathf.Atan2(toThreat.x, toThreat.z) * Mathf.Rad2Deg;

            UpdateAlphas();
            ApplyToUiElements();
        }

        private void HandlePlayerHealed(HealResult healResult)
        {
            // Sin confirmación de curación si ya estaba lleno o no ganó HP real
            if (healResult.WasAlreadyFull || healResult.ActualHealed <= 0.0001f)
                return;

            HealFeedbackTriggerCount++;
            healPulseTimer = healPulseDuration;
            float max = Mathf.Max(1f, healResult.MaxHealth);
            immediateRatio = Mathf.Clamp01(healResult.CurrentHealth / max);
            recentDamageRatio = Mathf.Max(recentDamageRatio, immediateRatio);
            UpdateAlphas();
            ApplyToUiElements();
        }

        private void Update()
        {
            if (session != null && session.IsPaused)
                return;
            if (Time.timeScale <= 0f)
                return;

            StepPresentation(Time.deltaTime);
        }

        public void StepPresentation(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            if (recentDamageHoldTimer > 0f)
            {
                recentDamageHoldTimer = Mathf.Max(0f, recentDamageHoldTimer - deltaTime);
            }
            else if (recentDamageRatio > immediateRatio)
            {
                recentDamageRatio = Mathf.MoveTowards(recentDamageRatio, immediateRatio, recentDamageDecaySpeed * deltaTime);
            }

            if (borderTimer > 0f)
                borderTimer = Mathf.Max(0f, borderTimer - deltaTime);
            if (directionalTimer > 0f)
                directionalTimer = Mathf.Max(0f, directionalTimer - deltaTime);
            if (healPulseTimer > 0f)
                healPulseTimer = Mathf.Max(0f, healPulseTimer - deltaTime);

            UpdateAlphas();
            ApplyToUiElements();
        }

        private void UpdateAlphas()
        {
            bool reduced = session != null && session.Effects.ReducedEffectsMode;
            float maxBorder = reduced ? reducedBorderMaxAlpha : borderMaxAlpha;
            ScreenBorderAlpha = borderTimer > 0f
                ? maxBorder * Mathf.Clamp01(borderTimer / Mathf.Max(0.01f, borderFadeDuration))
                : 0f;

            DirectionalMarkerAlpha = directionalTimer > 0f
                ? Mathf.Clamp01(directionalTimer / Mathf.Max(0.01f, directionalDuration))
                : 0f;

            HealPulseAlpha = healPulseTimer > 0f
                ? 0.45f * Mathf.Clamp01(healPulseTimer / Mathf.Max(0.01f, healPulseDuration))
                : 0f;
        }

        private void ApplyToUiElements()
        {
            if (immediateHealthFill != null)
                immediateHealthFill.fillAmount = immediateRatio;

            if (recentDamageFill != null)
                recentDamageFill.fillAmount = recentDamageRatio;

            if (screenBorderOverlay != null)
            {
                Color c = new Color(0.86f, 0.12f, 0.10f, ScreenBorderAlpha);
                screenBorderOverlay.color = c;
            }

            if (directionalMarker != null)
            {
                directionalMarker.localRotation = Quaternion.Euler(0f, 0f, -directionalAngleDegrees);
            }

            if (directionalMarkerImage != null)
            {
                Color c = new Color(1f, 0.35f, 0.18f, DirectionalMarkerAlpha);
                directionalMarkerImage.color = c;
            }

            if (healPulseOverlay != null)
            {
                Color c = new Color(0.32f, 0.88f, 0.46f, HealPulseAlpha);
                healPulseOverlay.color = c;
            }
        }

        public void ClearTransientFeedback()
        {
            recentDamageHoldTimer = 0f;
            borderTimer = 0f;
            directionalTimer = 0f;
            healPulseTimer = 0f;
            ScreenBorderAlpha = 0f;
            DirectionalMarkerAlpha = 0f;
            HealPulseAlpha = 0f;
            if (playerHealth != null)
            {
                immediateRatio = Mathf.Clamp01(playerHealth.CurrentHealth / Mathf.Max(1f, playerHealth.MaxHealth));
                recentDamageRatio = immediateRatio;
            }
            ApplyToUiElements();
        }
    }
}
