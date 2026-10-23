using EchoScrap.Combat;
using UnityEditor;
using UnityEngine;

namespace EchoScrap.Editor
{
    [CustomEditor(typeof(FlashlightPresentation))]
    public sealed class FlashlightPresentationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Daño, alcance y cadencia: PlayerCombat_Default. Color, intensidad, sombras e indicador: Refactor Flashlight View. Este componente conecta la vista con el arma y sus impactos.", MessageType.Info);
            foreach (string field in new[] { "weapon", "session", "flashlightPivot", "spotLight", "prototypeView", "worldEffectsRoot" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            var view = serializedObject.FindProperty("prototypeView").objectReferenceValue;
            using (new EditorGUI.DisabledScope(view == null))
                if (GUILayout.Button("Seleccionar aspecto de la linterna")) Selection.activeObject = view;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
