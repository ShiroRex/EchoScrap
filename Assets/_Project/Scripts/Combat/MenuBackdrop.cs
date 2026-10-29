using EchoScrap.Core;
using UnityEngine;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>One procedural UI surface; its decorative clock is independent of the paused game.</summary>
    [DisallowMultipleComponent]
    public sealed class MenuBackdrop : MonoBehaviour
    {
        private static readonly int AnimationTimeId = Shader.PropertyToID("_AnimationTime");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int PortalId = Shader.PropertyToID("_Portal");
        private RectTransform rect;
        private RectTransform menuPanel;
        private Material material, foregroundMaterial, characterMaterial;
        private MenuPortalCharacters characters;
        private EffectPreferences preferences;
        private bool subscribed;
        private float motion = 1f;
        public float PresentationTime { get; private set; }

        internal static MenuBackdrop Create(Transform parent, EffectPreferences effects)
        {
            var root = new GameObject("AnimatedMenuBackdrop", typeof(RectTransform), typeof(Canvas), typeof(RawImage));
            root.transform.SetParent(parent, false);
            var backdrop = root.AddComponent<MenuBackdrop>();
            backdrop.rect = (RectTransform)root.transform;
            backdrop.rect.anchorMin = Vector2.zero; backdrop.rect.anchorMax = Vector2.one;
            backdrop.rect.offsetMin = backdrop.rect.offsetMax = Vector2.zero;
            // Below CombatHud (20), so configuration can reuse the existing pause panel.
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 10;
            var image = root.GetComponent<RawImage>();
            image.texture = Texture2D.whiteTexture; image.raycastTarget = false;
            var template = Resources.Load<Material>("UI/MenuBackdrop");
            if (template != null && template.shader.isSupported)
            {
                backdrop.material = new Material(template) { name = "MenuBackdrop (Runtime)" };
                image.material = backdrop.material;
                backdrop.characterMaterial = new Material(template) { name = "MenuPortalCast (Runtime)" };
                backdrop.characterMaterial.SetFloat("_Foreground", 2);
                backdrop.characters = MenuPortalCharacters.Create(root.transform, backdrop.characterMaterial);
                var rim = new GameObject("PortalForeground", typeof(RectTransform), typeof(RawImage));
                rim.transform.SetParent(root.transform, false);
                var rimRect = (RectTransform)rim.transform;
                rimRect.anchorMin = Vector2.zero; rimRect.anchorMax = Vector2.one;
                rimRect.offsetMin = rimRect.offsetMax = Vector2.zero;
                backdrop.foregroundMaterial = new Material(template) { name = "MenuPortalRim (Runtime)" };
                backdrop.foregroundMaterial.SetFloat("_Foreground", 1);
                var rimImage = rim.GetComponent<RawImage>();
                rimImage.texture = Texture2D.whiteTexture; rimImage.raycastTarget = false;
                rimImage.material = backdrop.foregroundMaterial;
            }
            else
            {
                image.color = new Color(.025f, .04f, .07f, 1);
                Debug.LogError("MenuBackdrop requires its supported UI/MenuBackdrop material.");
            }
            backdrop.preferences = effects;
            backdrop.Subscribe();
            backdrop.UpdateSurface();
            return backdrop;
        }

        internal void PlaceMenuPanel(RectTransform panel)
        {
            menuPanel = panel;
            UpdateSurface();
        }

        private void OnEnable() => Subscribe();
        private void Subscribe()
        {
            if (subscribed || preferences == null) return;
            preferences.OnPreferencesChanged += SyncMotion;
            subscribed = true;
            SyncMotion();
        }
        private void SyncMotion() => motion = preferences.DecorativeIntensity;
        private void OnDisable()
        {
            if (!subscribed) return;
            preferences.OnPreferencesChanged -= SyncMotion;
            subscribed = false;
        }
        private void Update()
        {
            PresentationTime += Time.unscaledDeltaTime * motion;
            UpdateSurface();
        }
        private void UpdateSurface()
        {
            if (material == null) return;
            Vector2 size = rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            const float margin = 72f;
            float panelWidth = menuPanel != null ? menuPanel.sizeDelta.x : 904f;
            float leftWidth = Mathf.Max(260f, size.x - panelWidth - margin * 3f);
            float radius = Mathf.Min(leftWidth * .43f, size.y * .32f);
            Vector2 center = new Vector2(margin + leftWidth * .5f - size.x * .5f, size.y * .02f);
            if (menuPanel != null) menuPanel.anchoredPosition = new Vector2(size.x * .5f - panelWidth * .5f - margin, 0);
            Vector4 portal = new Vector4(center.x / size.x + .5f, center.y / size.y + .5f, radius / size.y, 0);
            UpdateMaterial(material, size.x / size.y, portal);
            UpdateMaterial(foregroundMaterial, size.x / size.y, portal);
            characters.Step(PresentationTime, center, radius);
        }
        private void UpdateMaterial(Material surface, float aspect, Vector4 portal)
        {
            surface.SetFloat(AnimationTimeId, PresentationTime);
            surface.SetFloat(AspectId, aspect);
            surface.SetVector(PortalId, portal);
        }
        private void OnDestroy()
        {
            OnDisable();
            if (material != null) Destroy(material);
            if (foregroundMaterial != null) Destroy(foregroundMaterial);
            if (characterMaterial != null) Destroy(characterMaterial);
        }
    }
}
