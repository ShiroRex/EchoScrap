using System;
using System.Collections.Generic;
using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Único compositor visual por actor.
    /// Separa raíz física, orientación (Visual) y deformación/presentación (PresentationPivot).
    /// Aplica flash mediante MaterialPropertyBlock sobre todos los renderers sin instanciar materiales,
    /// y gestiona hit-stop local (retención de pose) mientras la raíz física continúa su simulación.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorVisualComposer : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Jerarquía Visual")]
        [SerializeField] private Transform orientationRoot;
        [SerializeField] private Transform presentationPivot;
        [SerializeField] private Transform flashlightPivot;

        [Header("Locomoción y Arranque/Frenado")]
        [SerializeField, Range(0f, 15f)] private float maxLocomotionTiltDegrees = 6f;
        [SerializeField, Range(0f, 0.25f)] private float startStopCompression = 0.06f;
        [SerializeField, Min(0.05f)] private float dustMinInterval = 0.12f;

        [Header("Reacción de Impacto")]
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.96f, 0.88f, 1f);
        [SerializeField] private Color critFlashColor = new Color(1f, 0.85f, 0.35f, 1f);
        [SerializeField] private Color playerHurtFlashColor = new Color(1f, 0.28f, 0.22f, 1f);
        [SerializeField, Min(0.01f)] private float normalFlashDuration = 0.06f;
        [SerializeField, Min(0f)] private float normalHitStopDuration = 0.035f;
        [SerializeField, Min(0f)] private float criticalHitStopDuration = 0.05f;
        [SerializeField, Min(0f)] private float maxVisualRecoilDistance = 0.08f;
        [SerializeField, Min(0.01f)] private float hitRecoveryDuration = 0.12f;
        [SerializeField] private bool isHeavyMassActor;
        [SerializeField, Range(0.01f, 0.08f)] private float heavyMassDeformation = 0.03f;

        private Renderer[] cachedRenderers = Array.Empty<Renderer>();
        private Color[] originalBaseColors = Array.Empty<Color>();
        private readonly List<Renderer> rendererScratch = new List<Renderer>(8);
        private MaterialPropertyBlock flashBlock;
        private MaterialPropertyBlock restoreBlock;

        private Vector3 targetFacing = Vector3.forward;
        private Vector3 previousVelocity;
        private float currentTiltPitch;
        private float currentTiltRoll;
        private float startStopSquashTimer;
        private float lastDustEmitTime = -10f;
        private bool wasMoving;

        // Estado de Dash visual
        private bool isDashStretching;
        private float dashEndRecoveryTimer;
        private float dashEndRecoveryDuration = 0.08f;
        private float wallBlockTimer;

        // Estado de Impacto, Recoil y Hit-Stop local
        private float flashRemaining;
        private Color activeFlashColor;
        private bool flashApplied;
        private float hitStopRemaining;
        private float hitRecoveryTimer;
        private float activeHitRecoveryDuration = 0.12f;
        private Vector3 activeRecoilDirection;
        private float activeRecoilMagnitude;
        private float activeHitDeformation;

        // Empuje (Knockback)
        private Vector3 knockbackVelocityVisual;
        private bool isKnockbackImmune;

        // Retención de pose congelada durante hit-stop local
        private Vector3 retainedLocalPosition = Vector3.zero;
        private Quaternion retainedLocalRotation = Quaternion.identity;
        private Vector3 retainedLocalScale = Vector3.one;
        private bool hasRetainedPose;

        public Transform OrientationRoot => orientationRoot;
        public Transform PresentationPivot => presentationPivot;
        public Transform FlashlightPivot => flashlightPivot;
        public bool IsFlashActive => flashApplied && flashRemaining > 0f;
        public bool IsHitStopActive => hitStopRemaining > 0f;
        public float HitStopRemaining => Mathf.Max(0f, hitStopRemaining);
        public Vector3 CurrentPresentationScale => presentationPivot != null ? presentationPivot.localScale : Vector3.one;
        public Vector3 CurrentPresentationLocalPosition => presentationPivot != null ? presentationPivot.localPosition : Vector3.zero;
        public Quaternion CurrentPresentationLocalRotation => presentationPivot != null ? presentationPivot.localRotation : Quaternion.identity;
        public float MaxLocomotionTiltDegrees => maxLocomotionTiltDegrees;
        public float CurrentTiltPitch => currentTiltPitch;
        public int DustEmissionCount { get; private set; }
        public float LastDustEmitTimestamp => lastDustEmitTime;

        public event Action<Vector3, int> OnLocomotionDustRequested;

        private void Awake()
        {
            EnsureHierarchy();
            CacheRenderers();
        }

        public void EnsureHierarchy()
        {
            if (orientationRoot == null)
            {
                Transform foundVisual = transform.Find("Visual");
                if (foundVisual == null)
                {
                    var visualGo = new GameObject("Visual");
                    visualGo.transform.SetParent(transform, false);
                    foundVisual = visualGo.transform;
                }
                orientationRoot = foundVisual;
            }

            if (presentationPivot == null && orientationRoot != null)
            {
                Transform foundPivot = orientationRoot.Find("PresentationPivot");
                if (foundPivot == null)
                {
                    var pivotGo = new GameObject("PresentationPivot");
                    pivotGo.transform.SetParent(orientationRoot, false);
                    foundPivot = pivotGo.transform;

                    for (int i = orientationRoot.childCount - 1; i >= 0; i--)
                    {
                        Transform child = orientationRoot.GetChild(i);
                        if (child == foundPivot || child.name == "FlashlightPivot")
                            continue;
                        child.SetParent(foundPivot, true);
                    }
                }
                presentationPivot = foundPivot;
            }

            if (flashlightPivot == null)
            {
                Transform foundFlashlight = transform.Find("FlashlightPivot");
                if (foundFlashlight == null && orientationRoot != null)
                    foundFlashlight = orientationRoot.Find("FlashlightPivot");
                flashlightPivot = foundFlashlight;
            }

            flashBlock ??= new MaterialPropertyBlock();
            restoreBlock ??= new MaterialPropertyBlock();
        }

        public void CacheRenderers()
        {
            EnsureHierarchy();
            Transform rootForRenderers = presentationPivot != null ? presentationPivot : (orientationRoot != null ? orientationRoot : transform);
            rendererScratch.Clear();
            rootForRenderers.GetComponentsInChildren(true, rendererScratch);
            int count = 0;
            for (int i = 0; i < rendererScratch.Count; i++)
            {
                if (rendererScratch[i] is MeshRenderer || rendererScratch[i] is SkinnedMeshRenderer || rendererScratch[i] is SpriteRenderer)
                    count++;
            }

            if (cachedRenderers.Length != count)
            {
                cachedRenderers = new Renderer[count];
                originalBaseColors = new Color[count];
            }
            int index = 0;
            for (int i = 0; i < rendererScratch.Count; i++)
            {
                Renderer renderer = rendererScratch[i];
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is SpriteRenderer)
                {
                    cachedRenderers[index] = renderer;
                    Material shared = renderer.sharedMaterial;
                    if (shared != null && shared.HasProperty(BaseColorId))
                        originalBaseColors[index] = shared.GetColor(BaseColorId);
                    else if (shared != null && shared.HasProperty(ColorId))
                        originalBaseColors[index] = shared.GetColor(ColorId);
                    else
                        originalBaseColors[index] = Color.white;
                    index++;
                }
            }
        }

        public void ConfigureActorMass(bool heavyMass, bool knockbackImmune, float heavyDeform = 0.03f)
        {
            isHeavyMassActor = heavyMass;
            isKnockbackImmune = knockbackImmune;
            heavyMassDeformation = Mathf.Clamp(heavyDeform, 0.02f, 0.04f);
        }

        public void SetLocomotionTiltDegrees(float degrees)
        {
            maxLocomotionTiltDegrees = Mathf.Clamp(degrees, 0f, 20f);
        }

        public void ReportLocomotion(Vector3 facingDirection, Vector3 velocity, float maxSpeed, float gameplayTime, float decorativeIntensity = 1f)
        {
            if (facingDirection.sqrMagnitude > 0.0001f)
            {
                facingDirection.y = 0f;
                if (facingDirection.sqrMagnitude > 0.0001f)
                    targetFacing = facingDirection.normalized;
            }

            Vector3 planarVel = new Vector3(velocity.x, 0f, velocity.z);
            float speed = planarVel.magnitude;
            bool movingNow = speed > 0.35f;

            if (movingNow != wasMoving)
            {
                startStopSquashTimer = 0.09f;
                if (decorativeIntensity > 0.01f && gameplayTime - lastDustEmitTime >= dustMinInterval)
                {
                    lastDustEmitTime = gameplayTime;
                    DustEmissionCount++;
                    int particleCount = speed > maxSpeed * 0.5f ? 5 : 3;
                    OnLocomotionDustRequested?.Invoke(transform.position, particleCount);
                }
                wasMoving = movingNow;
            }

            float normalizedSpeed = maxSpeed > 0.01f ? Mathf.Clamp01(speed / maxSpeed) : 0f;
            Vector3 localVel = orientationRoot != null
                ? orientationRoot.InverseTransformDirection(planarVel)
                : planarVel;
            float forwardRatio = maxSpeed > 0.01f ? Mathf.Clamp(localVel.z / maxSpeed, -1f, 1f) : 0f;
            float sideRatio = maxSpeed > 0.01f ? Mathf.Clamp(localVel.x / maxSpeed, -1f, 1f) : 0f;

            currentTiltPitch = forwardRatio * maxLocomotionTiltDegrees * Mathf.Max(0.4f, normalizedSpeed);
            currentTiltRoll = -sideRatio * (maxLocomotionTiltDegrees * 0.65f);
            previousVelocity = planarVel;
        }

        public void ReportKnockbackVelocity(Vector3 knockbackVelocity)
        {
            if (isKnockbackImmune)
            {
                knockbackVelocityVisual = Vector3.zero;
                return;
            }
            knockbackVelocityVisual = new Vector3(knockbackVelocity.x, 0f, knockbackVelocity.z);
        }

        public void SetDashState(bool dashing, bool endedNow = false, bool blockedByWallNow = false)
        {
            EnsureHierarchy();
            isDashStretching = dashing;
            if (endedNow)
                dashEndRecoveryTimer = dashEndRecoveryDuration;
            if (blockedByWallNow)
                wallBlockTimer = 0.08f;

            if (hitStopRemaining <= 0f)
            {
                ComposePoseImmediate(out Vector3 localPos, out Quaternion localRot, out Vector3 localScale);
                ApplyPoseToTransform(localPos, localRot, localScale);
            }
        }

        public void TriggerHitReaction(Vector3 hitDirection, bool isCritical, bool isPlayerHurt = false, bool reducedEffects = false)
        {
            EnsureHierarchy();
            if (cachedRenderers.Length == 0)
                CacheRenderers();

            activeFlashColor = isPlayerHurt ? playerHurtFlashColor : (isCritical ? critFlashColor : hitFlashColor);
            flashRemaining = normalFlashDuration;
            ApplyFlashBlock(true);

            Vector3 planarDir = new Vector3(hitDirection.x, 0f, hitDirection.z);
            if (planarDir.sqrMagnitude < 0.0001f)
                planarDir = -targetFacing;
            else
                planarDir.Normalize();

            activeRecoilDirection = orientationRoot != null
                ? orientationRoot.InverseTransformDirection(planarDir)
                : planarDir;

            if (isHeavyMassActor)
            {
                activeRecoilMagnitude = maxVisualRecoilDistance * 0.25f;
                activeHitDeformation = heavyMassDeformation;
                activeHitRecoveryDuration = hitRecoveryDuration * 1.35f;
            }
            else
            {
                activeRecoilMagnitude = reducedEffects ? maxVisualRecoilDistance * 0.4f : maxVisualRecoilDistance;
                activeHitDeformation = isCritical ? 0.14f : 0.10f;
                activeHitRecoveryDuration = hitRecoveryDuration;
            }

            hitRecoveryTimer = activeHitRecoveryDuration;

            // Capturar la pose deformada inicial y retenerla durante el hit-stop local
            ComposePoseImmediate(out retainedLocalPosition, out retainedLocalRotation, out retainedLocalScale);
            ApplyPoseToTransform(retainedLocalPosition, retainedLocalRotation, retainedLocalScale);
            hasRetainedPose = true;
            hitStopRemaining = reducedEffects ? 0f : (isCritical ? criticalHitStopDuration : normalHitStopDuration);
        }

        public void StepVisuals(float deltaTime, float turnSpeedDegrees = 720f, bool reducedEffects = false)
        {
            EnsureHierarchy();
            if (deltaTime <= 0f)
                return;

            // 1. Actualizar flash de material (corre siempre con el reloj de sesión)
            if (flashApplied)
            {
                flashRemaining -= deltaTime;
                if (flashRemaining <= 0f)
                {
                    flashRemaining = 0f;
                    ApplyFlashBlock(false);
                }
            }

            // 2. La orientación base acompaña a la dirección objetivo salvo durante hit-stop
            if (hitStopRemaining > 0f)
            {
                hitStopRemaining = Mathf.Max(0f, hitStopRemaining - deltaTime);
                if (hasRetainedPose && presentationPivot != null)
                {
                    ApplyPoseToTransform(retainedLocalPosition, retainedLocalRotation, retainedLocalScale);
                }
                return;
            }

            hasRetainedPose = false;

            if (orientationRoot != null && targetFacing.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(targetFacing, Vector3.up);
                orientationRoot.rotation = Quaternion.RotateTowards(
                    orientationRoot.rotation,
                    desiredRotation,
                    turnSpeedDegrees * deltaTime);
            }

            if (startStopSquashTimer > 0f)
                startStopSquashTimer = Mathf.Max(0f, startStopSquashTimer - deltaTime);
            if (dashEndRecoveryTimer > 0f)
                dashEndRecoveryTimer = Mathf.Max(0f, dashEndRecoveryTimer - deltaTime);
            if (wallBlockTimer > 0f)
                wallBlockTimer = Mathf.Max(0f, wallBlockTimer - deltaTime);
            if (hitRecoveryTimer > 0f)
                hitRecoveryTimer = Mathf.Max(0f, hitRecoveryTimer - deltaTime);

            ComposePoseImmediate(out Vector3 localPos, out Quaternion localRot, out Vector3 localScale, reducedEffects);
            ApplyPoseToTransform(localPos, localRot, localScale);
        }

        private void ComposePoseImmediate(
            out Vector3 localPosition,
            out Quaternion localRotation,
            out Vector3 localScale,
            bool reducedEffects = false)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            localScale = Vector3.one;

            if (reducedEffects)
                return;

            // Recoil local de impacto (la raíz física no se altera)
            if (hitRecoveryTimer > 0f && activeHitRecoveryDuration > 0.0001f)
            {
                float t = Mathf.Clamp01(hitRecoveryTimer / activeHitRecoveryDuration);
                float smooth = t * t;
                localPosition += activeRecoilDirection * (activeRecoilMagnitude * smooth);
            }

            // Inclinación por locomoción + empuje recibido
            float pitch = currentTiltPitch;
            float roll = currentTiltRoll;
            if (!isKnockbackImmune && knockbackVelocityVisual.sqrMagnitude > 0.05f && orientationRoot != null)
            {
                Vector3 localKb = orientationRoot.InverseTransformDirection(knockbackVelocityVisual);
                float kbStrength = Mathf.Clamp01(knockbackVelocityVisual.magnitude / 10f);
                pitch += Mathf.Clamp(localKb.z, -1f, 1f) * 10f * kbStrength;
                roll -= Mathf.Clamp(localKb.x, -1f, 1f) * 10f * kbStrength;
            }
            localRotation = Quaternion.Euler(pitch, 0f, roll);

            // Escala (Prioridad: Muro -> Dash -> Fin de Dash -> Impacto -> Arranque/Frenado)
            if (wallBlockTimer > 0f)
            {
                float w = Mathf.Clamp01(wallBlockTimer / 0.08f);
                localScale = Vector3.Lerp(Vector3.one, new Vector3(1.18f, 1.12f, 0.78f), w);
            }
            else if (isDashStretching)
            {
                localScale = new Vector3(0.85f, 0.85f, 1.30f);
            }
            else if (dashEndRecoveryTimer > 0f)
            {
                float d = Mathf.Clamp01(dashEndRecoveryTimer / Mathf.Max(0.001f, dashEndRecoveryDuration));
                localScale = Vector3.Lerp(Vector3.one, new Vector3(1.12f, 0.90f, 0.88f), d);
            }
            else if (hitRecoveryTimer > 0f)
            {
                float h = Mathf.Clamp01(hitRecoveryTimer / Mathf.Max(0.001f, activeHitRecoveryDuration));
                float deform = activeHitDeformation * h;
                localScale = new Vector3(1f + deform, 1f - deform * 0.8f, 1f + deform);
            }
            else if (startStopSquashTimer > 0f)
            {
                float s = Mathf.Sin((startStopSquashTimer / 0.09f) * Mathf.PI) * startStopCompression;
                localScale = new Vector3(1f + s * 0.5f, 1f - s, 1f + s * 0.5f);
            }
        }

        private void ApplyPoseToTransform(Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            if (presentationPivot == null)
                return;

            presentationPivot.localPosition = localPos;
            presentationPivot.localRotation = localRot;
            presentationPivot.localScale = localScale;
        }

        private void ApplyFlashBlock(bool enableFlash)
        {
            flashApplied = enableFlash;
            if (cachedRenderers == null || cachedRenderers.Length == 0)
                return;

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                Renderer r = cachedRenderers[i];
                if (r == null)
                    continue;

                if (enableFlash)
                {
                    r.GetPropertyBlock(flashBlock);
                    flashBlock.SetColor(BaseColorId, activeFlashColor);
                    flashBlock.SetColor(ColorId, activeFlashColor);
                    flashBlock.SetColor(EmissionColorId, activeFlashColor * 1.6f);
                    r.SetPropertyBlock(flashBlock);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        public void ResetVisualState(bool resetOrientation = false)
        {
            EnsureHierarchy();
            flashRemaining = 0f;
            hitStopRemaining = 0f;
            hitRecoveryTimer = 0f;
            startStopSquashTimer = 0f;
            dashEndRecoveryTimer = 0f;
            wallBlockTimer = 0f;
            isDashStretching = false;
            hasRetainedPose = false;
            knockbackVelocityVisual = Vector3.zero;
            currentTiltPitch = 0f;
            currentTiltRoll = 0f;
            wasMoving = false;
            previousVelocity = Vector3.zero;
            if (resetOrientation)
            {
                targetFacing = transform.forward;
                if (orientationRoot != null)
                    orientationRoot.localRotation = Quaternion.identity;
            }
            ApplyFlashBlock(false);
            if (presentationPivot != null)
            {
                presentationPivot.localPosition = Vector3.zero;
                presentationPivot.localRotation = Quaternion.identity;
                presentationPivot.localScale = Vector3.one;
            }
        }

        private void OnDisable()
        {
            ResetVisualState();
        }
    }
}
