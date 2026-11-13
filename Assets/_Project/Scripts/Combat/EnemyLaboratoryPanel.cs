using UnityEngine;
using UnityEngine.InputSystem;

namespace EchoScrap.Combat
{
    /// <summary>Local experiment controls; never appears in the survival arena.</summary>
    [RequireComponent(typeof(EnemyLaboratory))]
    public sealed class EnemyLaboratoryPanel : MonoBehaviour
    {
        private static readonly EnemyArchetypeId[] Types = { EnemyArchetypeId.Residual, EnemyArchetypeId.Interceptor,
            EnemyArchetypeId.Colossus, EnemyArchetypeId.Mortar, EnemyArchetypeId.ExplosiveMoth,
            EnemyArchetypeId.GhostKnight, EnemyArchetypeId.CopperSentinel, EnemyArchetypeId.GolgoraBoss };
        private static readonly string[] Names = { "Residual", "Interceptor", "Coloso", "Mortero", "Polilla", "Caballero élite", "Centinela élite", "Gólgora" };
        private static readonly string[] Speeds = { "0.25×", "0.5×", "1×", "2×" };
        private static readonly float[] Rates = { 0.25f, 0.5f, 1f, 2f };
        private EnemyLaboratory lab;
        private Vector2 scroll;
        private GUIStyle title, label, small, button, toggle, panel;
        private Texture2D background;
        private int typeIndex;
        private static bool visible = true;
        private static float Scale => Mathf.Clamp(Screen.height / 900f, 0.8f, 1.25f);
        public static bool IsOverPanel(Vector2 screenPoint) => screenPoint.x < (visible ? 354f : 90f) * Scale &&
            (visible || Screen.height - screenPoint.y < 52f * Scale);

        private void Awake() { lab = GetComponent<EnemyLaboratory>(); visible = true; }
        private void Update()
        {
            if (Keyboard.current == null || !Application.isFocused) return;
            if (Keyboard.current.tabKey.wasPressedThisFrame) visible = !visible;
            if (Keyboard.current.rKey.wasPressedThisFrame) lab.ResetLab();
        }
        private void PrepareStyles()
        {
            if (title != null) return;
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.045f, 0.05f, 0.075f, 0.96f));
            background.Apply();
            title = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(1f, 0.83f, 0.4f);
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            label.normal.textColor = new Color(0.92f, 0.94f, 0.96f);
            small = new GUIStyle(label) { fontSize = 13 };
            small.normal.textColor = new Color(0.66f, 0.74f, 0.8f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 30 };
            toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 15, wordWrap = true };
            toggle.normal.textColor = label.normal.textColor;
            toggle.onNormal.textColor = new Color(0.45f, 0.96f, 0.8f);
            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 12, 12) };
            panel.normal.background = background;
        }
        private bool Button(string text) => GUILayout.Button(text, button);
        private void Heading(string text) { GUILayout.Space(9); GUILayout.Label(text, label); }

        private void OnGUI()
        {
            if (lab == null || !lab.IsInitialized) return;
            PrepareStyles();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * Scale);
            if (!visible)
            {
                if (GUI.Button(new Rect(10, 10, 78, 34), "LAB [Tab]", button)) visible = true;
                GUI.matrix = previous;
                return;
            }
            GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height / Scale - 20), panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("LABORATORIO", title);
            if (GUILayout.Button("×", button, GUILayout.Width(32))) visible = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Arena plana · " + lab.ActiveCount + " / " + EnemyLaboratory.EnemyLimit + " enemigos", small);
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            Heading("GENERAR ENEMIGOS");
            typeIndex = GUILayout.SelectionGrid(typeIndex, Names, 2, button);
            lab.SpawnType = Types[typeIndex];
            GUILayout.Label("Cantidad: " + lab.SpawnCount + "    Distancia: " + lab.SpawnDistance.ToString("0") + " m", small);
            lab.SpawnCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(lab.SpawnCount, 1, 20));
            lab.SpawnDistance = GUILayout.HorizontalSlider(lab.SpawnDistance, 2, 20);
            if (Button("Generar en el aro turquesa")) lab.SpawnGroup();
            lab.SpawnWithMouse = GUILayout.Toggle(lab.SpawnWithMouse, "Generar con clic izquierdo en el suelo", toggle);
            if (Button("Volver a generar frente al jugador")) lab.FollowPlayerSpawnPoint();
            Heading("CONDICIONES DE PRUEBA");
            bool protectedPlayer = GUILayout.Toggle(lab.PlayerProtected, "Jugador sin daño", toggle);
            if (protectedPlayer != lab.PlayerProtected) lab.SetPlayerProtected(protectedPlayer);
            bool immortal = GUILayout.Toggle(lab.EnemiesImmortal, "Enemigos inmortales al daño", toggle);
            if (immortal != lab.EnemiesImmortal) lab.SetEnemiesImmortal(immortal);
            bool frozen = GUILayout.Toggle(lab.EnemiesFrozen, "Congelar IA de los enemigos", toggle);
            if (frozen != lab.EnemiesFrozen) lab.SetEnemiesFrozen(frozen);
            bool firing = GUILayout.Toggle(lab.AutoFire, "Ataque automático del jugador", toggle);
            if (firing != lab.AutoFire) lab.SetAutoFire(firing);
            GUILayout.Label("Inmortales: conservan 1 de vida. La Polilla todavía puede armarse y explotar.", small);
            Heading("TIEMPO");
            int speed = 0;
            for (int i = 0; i < Rates.Length; i++)
                if (Mathf.Abs(Rates[i] - lab.Session.Clock.SimulationRate) < 0.01f) speed = i;
            int nextSpeed = GUILayout.Toolbar(speed, Speeds, button);
            if (nextSpeed != speed) lab.SetSpeed(Rates[nextSpeed]);
            GUILayout.BeginHorizontal();
            if (Button(lab.Session.IsPaused ? "Reanudar" : "Pausar")) lab.TogglePause();
            if (Button("Paso +0.1 s")) lab.StepOnce();
            GUILayout.EndHorizontal();
            Heading("ENEMIGO INSPECCIONADO");
            if (lab.Selected != null)
            {
                EnemyActor actor = lab.Selected;
                GUILayout.Label(actor.Archetype.DisplayName + " · " + actor.CurrentHealth.ToString("0.0") + " / " +
                    actor.MaxHealth.ToString("0") + " vida", label);
                GUILayout.Label("Distancia " + Vector3.Distance(actor.WorldCenter, lab.Player.WorldCenter).ToString("0.0") +
                    " m · velocidad " + actor.Velocity.magnitude.ToString("0.0") + " m/s", small);
                if (actor.Charge != null)
                    GUILayout.Label("Carga: " + actor.Charge.State + " · espera " + actor.Charge.CooldownRemaining.ToString("0.0") +
                        " s · choques " + actor.Charge.WallStops, small);
                if (actor.ArchetypeId == EnemyArchetypeId.Mortar)
                    GUILayout.Label("Recarga " + actor.AttackCooldownTimer.ToString("0.0") + " s · aviso " +
                        actor.TelegraphTimer.ToString("0.00") + " s · disparos " + actor.MortarShotsFiredCount, small);
                if (actor.ArchetypeId == EnemyArchetypeId.ExplosiveMoth)
                {
                    GUILayout.Label(actor.IsFuseActive ? "Mecha: " + actor.FuseTimer.ToString("0.00") + " s" : "Mecha sin armar", small);
                    if (Button("Armar explosión de la Polilla")) lab.ArmSelectedMoth();
                }
                if (actor is GolgoraActor boss)
                    GUILayout.Label("Estado " + boss.State + " · pisotones " + boss.StompsExecuted +
                        " · salvas " + boss.BarragesExecuted, small);
                if (actor.Elite != null)
                {
                    GUILayout.Label("Élite · " + actor.Elite.State + (actor.Elite.IsVulnerable ? " · Vulnerable" : ""), small);
                    GUILayout.Label("Daño recibido: físico " + (actor.Elite.DamageMultiplier(EchoScrap.Core.DamageType.Physical)*100f).ToString("0") +
                        "% · sobrenatural " + (actor.Elite.DamageMultiplier(EchoScrap.Core.DamageType.Supernatural)*100f).ToString("0") + "%", small);
                    GUILayout.BeginHorizontal();
                    if (Button("Físico 20")) lab.DamageSelected(20f, EchoScrap.Core.DamageType.Physical);
                    if (Button("Sobrenatural 20")) lab.DamageSelected(20f, EchoScrap.Core.DamageType.Supernatural);
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                if (Button("Daño 5")) lab.DamageSelected(5f);
                if (Button("Daño letal")) lab.DamageSelected(actor.MaxHealth * 100f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (Button("Empujar")) lab.PushSelected();
                if (Button("Reiniciar")) lab.RestartSelected();
                if (Button("Retirar")) lab.RemoveSelected();
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label("Clic derecho sobre un enemigo para ver su estado y probar daño, empuje o reinicio.", small);
            Heading("ESCENARIO");
            GUILayout.BeginHorizontal();
            if (Button("Añadir caja")) lab.AddObstacle();
            if (Button("Quitar cajas")) lab.ClearObstacles();
            GUILayout.EndHorizontal();
            if (Button("Limpiar enemigos y efectos")) lab.ClearEnemies();
            if (Button("Reiniciar laboratorio [R]")) lab.ResetLab();
            GUILayout.Label(lab.Notice, small);
            GUILayout.Space(8);
            GUILayout.Label("WASD: mover · Espacio: dash\nEsc: pausa · Tab: ocultar panel\nVida del jugador: " +
                lab.Player.CurrentHealth.ToString("0") + " / " + lab.Player.MaxHealth.ToString("0"), small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void OnDestroy() { if (background != null) Destroy(background); }
    }
}
