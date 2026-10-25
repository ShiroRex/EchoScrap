using System.Collections.Generic;
using System.IO;
using EchoScrap.Combat;
using EchoScrap.Core;
using UnityEditor;
using UnityEngine;

namespace EchoScrap.Editor
{
    public static class CombatBalanceAuthoring
    {
        public const string EnemyFolder = "Assets/_Project/Settings/Enemies/";
        public const string WavesPath = "Assets/_Project/Settings/CombatWaves_Default.asset";
        [MenuItem("EchoScrap/Balance/Preparar ajustes editables")]
        public static void Prepare()
        {
            Directory.CreateDirectory(EnemyFolder); AssetDatabase.Refresh();
            var waves = AssetDatabase.LoadAssetAtPath<CombatWaveSettings>(WavesPath);
            var entries = new List<EnemyArchetypeSettings>(waves.ArchetypeOverrides ?? System.Array.Empty<EnemyArchetypeSettings>());
            for (int i = 1; i <= 5; i++)
            {
                var id = (EnemyArchetypeId)i;
                if (entries.Exists(asset => asset != null && asset.Data.ArchetypeId == id)) continue;
                string path = EnemyFolder + id + ".asset";
                var setting = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSettings>(path);
                if (setting == null)
                {
                    setting = ScriptableObject.CreateInstance<EnemyArchetypeSettings>();
                    setting.Data = EnemyArchetypeData.CreateDefault(id); AssetDatabase.CreateAsset(setting, path);
                }
                entries.Add(setting);
            }
            waves.ArchetypeOverrides = entries.ToArray(); EditorUtility.SetDirty(waves);
            var player = AssetDatabase.LoadAssetAtPath<PlayerCombatSettings>("Assets/_Project/Settings/PlayerCombat_Default.asset");
            EditorUtility.SetDirty(player); AssetDatabase.SaveAssets();
            UpgradeCardAuthoring.Prepare();
            Debug.Log("[EchoScrap] Combat balance ready: five editable enemies, bounded mortar pressure and medkit supply.");
        }
    }

    public sealed class CombatBalanceWindow : EditorWindow
    {
        private int tab, enemyIndex;
        private UnityEditor.Editor inspector;
        private Object selected;
        private Vector2 scroll;
        private readonly string[] enemies = { "Residual", "Interceptor", "Coloso", "Mortero", "Polilla" };
        [MenuItem("EchoScrap/Balance/Editar enemigos y botiquines")]
        public static void Open() => GetWindow<CombatBalanceWindow>("Balance EchoScrap");
        private void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, new[] { "Enemigos", "Botiquines", "Oleadas" });
            Object target;
            if (tab == 0)
            {
                enemyIndex = EditorGUILayout.Popup("Enemigo", enemyIndex, enemies);
                target = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSettings>(CombatBalanceAuthoring.EnemyFolder + (EnemyArchetypeId)(enemyIndex + 1) + ".asset");
            }
            else target = AssetDatabase.LoadMainAssetAtPath(tab == 1 ? "Assets/_Project/Settings/PlayerCombat_Default.asset" : CombatBalanceAuthoring.WavesPath);
            EditorGUILayout.HelpBox("Los ajustes se guardan en assets. Los enemigos nuevos usan estos valores; reinicia la partida para comparar el balance completo.", MessageType.Info);
            if (target == null)
            {
                if (GUILayout.Button("Preparar ajustes")) CombatBalanceAuthoring.Prepare();
                return;
            }
            if (selected != target)
            {
                if (inspector != null) DestroyImmediate(inspector);
                selected = target; inspector = UnityEditor.Editor.CreateEditor(target);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            inspector.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Ver asset en el proyecto")) { Selection.activeObject = target; EditorGUIUtility.PingObject(target); }
        }
        private void OnDisable() { if (inspector != null) DestroyImmediate(inspector); }
    }
}
