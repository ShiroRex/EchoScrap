using EchoScrap.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace EchoScrap.Combat
{
    /// <summary>Temporary capsule telegraphs. The controller owns every phase and damage rule.</summary>
    public sealed class EliteEnemyPresentation : MonoBehaviour
    {
        private EnemyActor actor;
        private EliteEnemyController elite;
        private LineRenderer warning;
        private GameObject status;
        private Camera camera;
        private const int RaySegments = 32;
        public bool VulnerableVisible => status != null && status.activeInHierarchy;
        public bool WarningVisible => warning != null && warning.enabled;
        public Text VulnerableLabel { get; private set; }

        public void Bind(EnemyActor owner, EliteEnemyController controller)
        {
            actor = owner; elite = controller;
            camera = Camera.main;
            if (warning != null) return;
            var ground = new GameObject("Elite attack warning"); ground.transform.SetParent(transform, false);
            warning = ground.AddComponent<LineRenderer>();
            warning.sharedMaterial = Prototype.VfxRuntimeUtils.FlatMaterial;
            warning.useWorldSpace = true; warning.widthMultiplier = .065f;
            warning.shadowCastingMode = ShadowCastingMode.Off; warning.receiveShadows = false;
            warning.positionCount = elite.IsCopper ? 3 * (RaySegments * 2 + 1) : 35;
            status = new GameObject("Vulnerable status", typeof(RectTransform), typeof(Canvas));
            status.transform.SetParent(transform, false);
            status.transform.localScale = Vector3.one * .012f;
            var canvas = status.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 12;
            ((RectTransform)status.transform).sizeDelta = new Vector2(240f, 40f);
            var labelObject = new GameObject("Vulnerable", typeof(RectTransform), typeof(Text), typeof(Outline));
            labelObject.transform.SetParent(status.transform, false);
            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            VulnerableLabel = labelObject.GetComponent<Text>();
            VulnerableLabel.font = Resources.Load<UpgradeCardSkin>("UI/UpgradeCardSkin")?.semibold ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            VulnerableLabel.text = "Vulnerable"; VulnerableLabel.fontSize = 28;
            VulnerableLabel.fontStyle = FontStyle.Bold; VulnerableLabel.alignment = TextAnchor.MiddleCenter;
            VulnerableLabel.color = new Color(.55f, 1f, .55f); VulnerableLabel.raycastTarget = false;
            labelObject.GetComponent<Outline>().effectColor = Color.black;
            Refresh();
        }

        public void Refresh()
        {
            if (warning == null) return;
            bool alive = actor.IsAlive && actor.gameObject.activeInHierarchy;
            status.SetActive(alive && elite.IsVulnerable);
            bool sweep = !elite.IsCopper && elite.State == EliteEnemyState.Recovery && elite.PhaseTime < .15f;
            warning.enabled = alive && (elite.State == EliteEnemyState.Windup || sweep);
            if (!warning.enabled) return;
            Color color = sweep ? new Color(.4f, .95f, 1f) : new Color(1f, .55f, .12f);
            warning.startColor = warning.endColor = color;
            Vector3 origin = elite.WarningOrigin;
            origin.y = ArenaSurface.SupportHeight(origin, actor.HitRadius) + .06f;
            float half = elite.Settings.ArcDegrees * .5f;
            if (elite.IsCopper)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 direction = Quaternion.Euler(0, (i - 1) * half, 0) * elite.LockedDirection;
                    // Trace each spoke out and back; a single long return segment cuts through hills.
                    for (int j = 0; j <= RaySegments * 2; j++)
                    {
                        int sample = j <= RaySegments ? j : RaySegments * 2 - j;
                        Vector3 point = origin + direction * (elite.Settings.Range * sample / RaySegments);
                        warning.SetPosition(i * (RaySegments * 2 + 1) + j, ArenaSurface.OnGround(point, .08f));
                    }
                }
            }
            else
            {
                warning.SetPosition(0, origin);
                for (int i = 0; i <= 32; i++)
                    warning.SetPosition(i + 1, ArenaSurface.OnGround(origin + Quaternion.Euler(0, Mathf.Lerp(-half, half, i / 32f), 0) * elite.LockedDirection * elite.Settings.Range, .08f));
                warning.SetPosition(34, origin);
            }
        }

        private void LateUpdate()
        {
            if (actor == null) return;
            Refresh();
            if (!VulnerableVisible) return;
            status.transform.position = new Vector3(actor.WorldCenter.x, actor.BodyCollider.bounds.max.y + .45f, actor.WorldCenter.z);
            if (camera != null) status.transform.rotation = camera.transform.rotation;
        }

        private void OnDisable()
        {
            if (warning != null) warning.enabled = false;
            if (status != null) status.SetActive(false);
        }
    }
}
