using System;
using System.Linq;
using EchoScrap.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EchoScrap.Editor
{
    public sealed class ArenaLightingWindow : EditorWindow
    {
        [SerializeField] ArenaLightingProfile profile;
        [SerializeField] ArenaLightingRig rig;
        [SerializeField] ArenaLightPlacement placement=new ArenaLightPlacement();
        [SerializeField] bool placing,aiming,previewEnabled,previewFocus;
        [SerializeField] float planeHeight;
        [SerializeField] string search="";
        Vector2 scroll;
        ArenaLightingScenePreview preview;
        static readonly string[] Presets={"Puntual cálida","Spot","Reflector mural","Reflector de pie","God rays","Polvo","Bruma"};

        [MenuItem("EchoScrap/Iluminación y atmósfera")]
        public static void Open()
        {
            var window=GetWindow<ArenaLightingWindow>();window.titleContent=new GUIContent("Luces y atmósfera");window.minSize=new Vector2(390,460);window.Show();
        }
        public static void Select(ArenaLightingItem item)
        {
            Open();var window=GetWindow<ArenaLightingWindow>();var owner=item.GetComponentInParent<ArenaLightingRig>();
            if(window.rig!=owner){window.StopPreview();window.rig=owner;}
            Selection.activeGameObject=item.gameObject;
        }
        void OnEnable()
        {
            if(profile==null)profile=ArenaLightingAuthoring.DefaultProfile;
            if(rig==null)rig=ArenaLightingRig.FindSceneLayout(SceneManager.GetActiveScene());
            SceneView.duringSceneGui+=SceneGUI;Selection.selectionChanged+=Repaint;Undo.undoRedoPerformed+=UndoChanged;
            EditorSceneManager.activeSceneChangedInEditMode+=SceneChanged;EditorApplication.playModeStateChanged+=PlayChanged;
        }
        void OnDisable()
        {
            StopPreview();SceneView.duringSceneGui-=SceneGUI;Selection.selectionChanged-=Repaint;Undo.undoRedoPerformed-=UndoChanged;
            EditorSceneManager.activeSceneChangedInEditMode-=SceneChanged;EditorApplication.playModeStateChanged-=PlayChanged;
        }
        void SceneChanged(Scene before,Scene after){StopPreview();rig=ArenaLightingRig.FindSceneLayout(after);placing=aiming=false;Repaint();}
        void PlayChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.ExitingEditMode){StopPreview();placing=aiming=false;}
            if(state==PlayModeStateChange.EnteredEditMode)rig=ArenaLightingRig.FindSceneLayout(SceneManager.GetActiveScene());
            Repaint();
        }
        void UndoChanged(){if(rig==null)rig=ArenaLightingRig.FindSceneLayout(SceneManager.GetActiveScene());if(rig!=null)rig.RefreshItems();SceneView.RepaintAll();Repaint();}
        void StopPreview(){preview?.Dispose();preview=null;previewEnabled=false;}
        void TogglePreview(bool value)
        {
            StopPreview();
            if(value && rig!=null && profile!=null && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if(!ArenaLightingAuthoring.EnsurePreviewRenderer(profile)){ShowNotification(new GUIContent("No hay renderer de atmósfera compatible."));return;}
                preview=new ArenaLightingScenePreview(profile,rig){FocusEnabled=previewFocus};previewEnabled=true;
            }
        }
        ArenaLightingItem Selected
        {
            get
            {
                var go=Selection.activeGameObject;if(go==null)return null;
                var item=go.GetComponentInParent<ArenaLightingItem>();
                return item!=null&&item.GetComponentInParent<ArenaLightingRig>()==rig?item:null;
            }
        }

        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Label("Iluminación y atmósfera",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Coloca, apunta y ajusta desde la vista de escena.",EditorStyles.wordWrappedLabel);
            bool playing=EditorApplication.isPlayingOrWillChangePlaymode;
            if(playing)EditorGUILayout.HelpBox("Sal de Play para editar y guardar el montaje.",MessageType.Info);
            using(new EditorGUI.DisabledScope(playing))
            {
                EditorGUI.BeginChangeCheck();
                profile=(ArenaLightingProfile)EditorGUILayout.ObjectField("Perfil",profile,typeof(ArenaLightingProfile),false);
                var chosen=(ArenaLightingRig)EditorGUILayout.ObjectField("Montaje de escena",rig,typeof(ArenaLightingRig),true);
                if(EditorGUI.EndChangeCheck()){StopPreview();rig=chosen;}
                if(rig==null)
                {
                    EditorGUILayout.HelpBox("Prepara el montaje una vez. Después se guarda con la escena (Ctrl+S) y el juego lo usa en lugar del predeterminado.",MessageType.Info);
                    using(new EditorGUI.DisabledScope(profile==null))
                    {
                        if(GUILayout.Button("Preparar desde el montaje actual",GUILayout.Height(30)))Prepare(true);
                        if(GUILayout.Button("Crear montaje vacío"))Prepare(false);
                    }
                }
                else if(!rig.SceneAuthored || !rig.gameObject.scene.IsValid())
                    EditorGUILayout.HelpBox("Selecciona un montaje propio de la escena, no el asset del proyecto.",MessageType.Warning);
                else DrawTools();
            }
            EditorGUILayout.EndScrollView();
        }
        void Prepare(bool copy)
        {
            try {rig=ArenaLightingAuthoring.Prepare(SceneManager.GetActiveScene(),profile,copy);TogglePreview(true);}
            catch(Exception exception){Debug.LogException(exception);ShowNotification(new GUIContent(exception.Message));}
        }
        void DrawTools()
        {
            EditorGUILayout.Space(6);
            using(new EditorGUILayout.HorizontalScope())
            {
                bool show=GUILayout.Toggle(previewEnabled,"Previsualizar luz y atmósfera",EditorStyles.miniButton);
                if(show!=previewEnabled)TogglePreview(show);
                bool focus=GUILayout.Toggle(previewFocus,"Enfoque",EditorStyles.miniButton,GUILayout.Width(80));
                if(focus!=previewFocus){previewFocus=focus;if(preview!=null)preview.FocusEnabled=focus;}
            }
            GUILayout.Label("Añadir",EditorStyles.boldLabel);
            int preset=GUILayout.SelectionGrid((int)placement.Kind,Presets,2,EditorStyles.miniButton);
            if(preset!=(int)placement.Kind){placement.Preset((ArenaLightingItemKind)preset);aiming=false;}
            bool light=(int)placement.Kind<=(int)ArenaLightingItemKind.GodRay;
            if(light)
            {
                placement.Color=EditorGUILayout.ColorField("Color",placement.Color);
                placement.Intensity=Mathf.Max(0,EditorGUILayout.FloatField("Intensidad",placement.Intensity));
                placement.Range=Mathf.Max(.1f,EditorGUILayout.FloatField("Alcance",placement.Range));
                if(placement.Kind!=ArenaLightingItemKind.Point)
                {
                    placement.Angle=EditorGUILayout.Slider("Apertura",placement.Angle,5,120);
                    placement.GodRays=EditorGUILayout.Toggle("God rays",placement.GodRays);
                    placement.AddDust=EditorGUILayout.Toggle("Añadir polvo al haz",placement.AddDust);
                }
                placement.Shadows=EditorGUILayout.Toggle("Sombras dinámicas",placement.Shadows);
            }
            if(placement.Kind==ArenaLightingItemKind.Dust || placement.Kind==ArenaLightingItemKind.Mist || (light&&placement.Kind!=ArenaLightingItemKind.Point&&placement.AddDust))
            {
                placement.Size=Positive(EditorGUILayout.Vector3Field("Tamaño del área",placement.Size));
                if(placement.Kind!=ArenaLightingItemKind.Mist)placement.DustRate=EditorGUILayout.Slider("Partículas por segundo",placement.DustRate,0,30);
            }
            placement.Height=EditorGUILayout.FloatField("Altura sobre superficie",placement.Height);
            placement.Offset=EditorGUILayout.FloatField("Separación del apoyo",placement.Offset);
            placement.Yaw=EditorGUILayout.Slider("Giro",placement.Yaw,-180,180);
            if(placement.Kind==ArenaLightingItemKind.Spot||placement.Kind==ArenaLightingItemKind.GodRay)
                placement.Pitch=EditorGUILayout.Slider("Inclinación",placement.Pitch,0,89);
            planeHeight=EditorGUILayout.FloatField(new GUIContent("Plano de respaldo Y","Se usa sólo cuando el cursor no encuentra superficie."),planeHeight);
            using(new EditorGUILayout.HorizontalScope())
            {
                bool paint=GUILayout.Toggle(placing,"Colocar con clic",EditorStyles.miniButton,GUILayout.Height(29));
                if(paint!=placing){placing=paint;aiming=false;SceneView.RepaintAll();}
                if(GUILayout.Button("En la selección",GUILayout.Height(29)))
                {
                    Vector3 position=Selection.activeTransform!=null?Selection.activeTransform.position:
                        SceneView.lastActiveSceneView!=null?SceneView.lastActiveSceneView.pivot:Vector3.zero;
                    Add(position,Vector3.up);
                }
            }
            if(placing||aiming)EditorGUILayout.HelpBox(aiming?"Haz clic en la superficie hacia la que quieres apuntar. Esc cancela.":
                "Haz clic en el escenario para colocar. Puedes seguir colocando; Alt navega y Esc termina.",MessageType.Info);
            var selected=Selected;if(selected!=null)DrawSelected(selected);
            DrawList();
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Los cambios se guardan con la escena. Se muestran hasta 4 haces cercanos; las demás luces siguen iluminando.",MessageType.None);
            if(GUILayout.Button("Guardar escena"))EditorSceneManager.SaveScene(rig.gameObject.scene);
            if(GUILayout.Button("Guardar como montaje compartido…") && EditorUtility.DisplayDialog("Montaje compartido",
                "Reemplazar el montaje predeterminado por éste. Lo usarán las arenas sin un montaje propio de escena.","Guardar compartido","Cancelar"))
            {
                try{ArenaLightingAuthoring.SaveShared(rig,profile);ShowNotification(new GUIContent("Montaje compartido guardado"));}
                catch(Exception exception){Debug.LogException(exception);}
            }
        }
        void Add(Vector3 point,Vector3 normal)
        {
            try {ArenaLightingAuthoring.Place(rig,placement,point,normal,profile);SceneView.RepaintAll();Repaint();}
            catch(Exception exception){Debug.LogException(exception);ShowNotification(new GUIContent(exception.Message));}
        }
        void DrawSelected(ArenaLightingItem item)
        {
            EditorGUILayout.Space(8);GUILayout.Label("Seleccionado · "+item.name,EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();Vector3 position=EditorGUILayout.Vector3Field("Posición",item.transform.position);
            if(EditorGUI.EndChangeCheck()){Undo.RecordObject(item.transform,"Mover iluminación");item.transform.position=position;Changed(item);}
            if(item.Source!=null)
            {
                var source=item.Source;EditorGUI.BeginChangeCheck();
                Color color=EditorGUILayout.ColorField("Color de luz",source.color);
                float intensity=Mathf.Max(0,EditorGUILayout.FloatField("Intensidad de luz",source.intensity));
                float range=Mathf.Max(.1f,EditorGUILayout.FloatField("Alcance de luz",source.range));
                float angle=source.type==LightType.Spot?EditorGUILayout.Slider("Apertura de luz",source.spotAngle,5,120):source.spotAngle;
                bool shadows=EditorGUILayout.Toggle("Sombras de luz",source.shadows!=LightShadows.None);
                if(EditorGUI.EndChangeCheck())
                {Undo.RecordObject(source,"Ajustar luz");source.color=color;source.intensity=intensity;source.range=range;source.spotAngle=angle;source.innerSpotAngle=Mathf.Min(source.innerSpotAngle,angle);source.shadows=shadows?LightShadows.Soft:LightShadows.None;PrefabUtility.RecordPrefabInstancePropertyModifications(source);Changed(item);}
                if(source.type==LightType.Spot)
                {
                    EditorGUI.BeginChangeCheck();bool rays=EditorGUILayout.Toggle("Haz volumétrico",item.GodRays);
                    if(EditorGUI.EndChangeCheck()){Undo.RecordObject(item,"Cambiar god rays");item.GodRays=rays;Changed(item);}
                    bool aim=GUILayout.Toggle(aiming,"Apuntar con clic",EditorStyles.miniButton);
                    if(aim!=aiming){aiming=aim;placing=false;SceneView.RepaintAll();}
                }
            }
            if(item.Dust!=null)
            {
                var ps=item.Dust;var emission=ps.emission;var shape=ps.shape;var main=ps.main;
                EditorGUI.BeginChangeCheck();float rate=EditorGUILayout.Slider("Densidad de polvo",emission.rateOverTime.constantMax,0,30);
                Vector3 size=Positive(EditorGUILayout.Vector3Field("Área de polvo",shape.scale));
                float speed=EditorGUILayout.Slider("Velocidad del polvo",main.startSpeed.constantMax,0,.5f);
                if(EditorGUI.EndChangeCheck()){Undo.RecordObject(ps,"Ajustar polvo");emission.rateOverTime=rate;shape.scale=size;main.startSpeed=speed;ps.Clear();Changed(item);}
            }
            if(item.MistBank!=null)
            {
                EditorGUI.BeginChangeCheck();Vector3 size=Positive(EditorGUILayout.Vector3Field("Área de bruma",item.MistBank.localScale));
                if(EditorGUI.EndChangeCheck()){Undo.RecordObject(item.MistBank,"Ajustar bruma");item.MistBank.localScale=size;Changed(item);}
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Enfocar"))SceneView.lastActiveSceneView?.Frame(new Bounds(item.Source!=null?item.Source.transform.position:item.transform.position,Vector3.one*4),false);
                if(GUILayout.Button("Duplicar"))ArenaLightingAuthoring.Duplicate(item);
                if(GUILayout.Button("Eliminar"))ArenaLightingAuthoring.Remove(item);
            }
        }
        void Changed(ArenaLightingItem item){rig.RefreshItems();EditorUtility.SetDirty(item);EditorSceneManager.MarkSceneDirty(item.gameObject.scene);SceneView.RepaintAll();}
        void DrawList()
        {
            EditorGUILayout.Space(8);GUILayout.Label("Elementos del montaje",EditorStyles.boldLabel);search=EditorGUILayout.TextField("Buscar",search);
            foreach(var item in rig.GetComponentsInChildren<ArenaLightingItem>(true))
            {
                if(!string.IsNullOrEmpty(search)&&item.name.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                using(new EditorGUILayout.HorizontalScope())
                {
                    bool active=EditorGUILayout.Toggle(item.gameObject.activeSelf,GUILayout.Width(18));
                    if(active!=item.gameObject.activeSelf){Undo.RecordObject(item.gameObject,"Activar iluminación");item.gameObject.SetActive(active);Changed(item);}
                    if(GUILayout.Button(item.name,EditorStyles.miniButton))Selection.activeGameObject=item.gameObject;
                }
            }
        }
        void SceneGUI(SceneView view)
        {
            if(rig==null || EditorApplication.isPlayingOrWillChangePlaymode)return;
            var selected=Selected;
            if(selected!=null)
            {
                if(selected.Source!=null){Handles.color=selected.Source.color;Handles.ArrowHandleCap(0,selected.Source.transform.position,selected.Source.transform.rotation,1.5f,EventType.Repaint);}
                if(selected.MistBank!=null){using(new Handles.DrawingScope(new Color(.65f,.45f,1,.7f),selected.MistBank.localToWorldMatrix))Handles.DrawWireCube(Vector3.zero,Vector3.one);}
            }
            if(!placing&&!aiming)return;
            var e=Event.current;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){placing=aiming=false;e.Use();Repaint();return;}
            if(e.alt)return;
            int control=GUIUtility.GetControlID(FocusType.Passive);
            if(e.type==EventType.Layout)HandleUtility.AddDefaultControl(control);
            Vector3 point,normal;
            bool hit=HandleUtility.PlaceObject(e.mousePosition,out point,out normal);
            if(!hit)
            {
                var ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);var plane=new Plane(Vector3.up,new Vector3(0,planeHeight,0));
                if(!plane.Raycast(ray,out float distance))return;point=ray.GetPoint(distance);normal=Vector3.up;
            }
            if(e.type==EventType.Repaint)
            {
                Handles.color=aiming?Color.yellow:placement.Color;
                Handles.DrawWireDisc(point,normal,HandleUtility.GetHandleSize(point)*.13f);
                Handles.Label(point,aiming?"Apuntar aquí":ArenaLightingAuthoring.Name(placement.Kind)+(hit?"":" · plano de respaldo"));
                if(placing)Handles.DrawDottedLine(point,point+Vector3.up*placement.Height,3);
            }
            if(e.type==EventType.MouseMove)view.Repaint();
            if(e.type==EventType.MouseDown&&e.button==0&&GUIUtility.hotControl==0)
            {
                if(aiming){if(selected!=null)ArenaLightingAuthoring.Aim(selected,point);aiming=false;Repaint();}
                else Add(point,normal);
                e.Use();
            }
        }
        static Vector3 Positive(Vector3 value)=>new Vector3(Mathf.Max(.1f,value.x),Mathf.Max(.1f,value.y),Mathf.Max(.1f,value.z));
    }

    [CustomEditor(typeof(ArenaLightingItem))]
    public sealed class ArenaLightingItemEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var item=(ArenaLightingItem)target;
            EditorGUILayout.LabelField(ArenaLightingAuthoring.Name(item.Kind),EditorStyles.boldLabel);
            if(GUILayout.Button("Editar en Luces y atmósfera"))ArenaLightingWindow.Select(item);
            serializedObject.Update();EditorGUILayout.PropertyField(serializedObject.FindProperty("Source"),new GUIContent("Luz"));
            if(item.Source!=null && item.Source.type==LightType.Spot)EditorGUILayout.PropertyField(serializedObject.FindProperty("GodRays"),new GUIContent("God rays"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Dust"),new GUIContent("Polvo"));
            if(serializedObject.ApplyModifiedProperties())item.GetComponentInParent<ArenaLightingRig>()?.RefreshItems();
        }
    }
}
