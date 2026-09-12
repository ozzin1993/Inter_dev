#if UNITY_EDITOR
using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEditor;using UnityEngine;using UnityEngine.Playables;using UnityEngine.Animations;
[InitializeOnLoad] public static class SkillAnimationContactSheet
{
 const string W=@"C:\Users\aleks\Documents\Codex\2026-09-11\v\work\skills-28\";
 static SkillAnimationContactSheet(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(W+"preview-paladin.txt"))return;try{File.Delete(W+"preview-paladin.txt");}catch(IOException){return;}try{for(int i=1;i<=8;i++)Render(i);File.WriteAllText(W+"preview-paladin-finished.txt","OK");}catch(Exception e){File.WriteAllText(W+"preview-paladin-finished.txt",e.ToString());Debug.LogException(e);}}
 static void Render(int index)
 {
  string path="Assets/DoubleL/FBX_Animations/Two Hand Base/Skills/InPlace/2Hand_Base_Skill_"+index+"_InPlace.fbx";
  var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  var preview=new PreviewRenderUtility();var graphs=new List<PlayableGraph>();
  try {
   preview.camera.orthographic=true;preview.camera.orthographicSize=3.5f;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;
   preview.camera.transform.position=new Vector3(10,7,-17);preview.camera.transform.LookAt(new Vector3(10,1.5f,0));preview.camera.backgroundColor=new Color(.12f,.14f,.17f);preview.camera.clearFlags=CameraClearFlags.Color;
   preview.lights[0].intensity=1.3f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);preview.lights[1].intensity=.8f;
   for(int j=0;j<5;j++) {
    var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UnitPrefabs/Units/Humans/SteelPaladin.prefab"));preview.AddSingleGO(root);root.transform.position=new Vector3(j*5,0,0);
    foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))ps.gameObject.SetActive(false);
    var animator=root.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
    var graph=PlayableGraph.Create("Skill pose preview");graphs.Add(graph);graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,clip);var output=AnimationPlayableOutput.Create(graph,"Pose",animator);output.SetSourcePlayable(playable);graph.Play();playable.SetTime(clip.length*(.1f+j*.18f));graph.Evaluate(0);
   }
   preview.BeginStaticPreview(new Rect(0,0,1600,450));preview.Render(true);var texture=preview.EndStaticPreview();try{File.WriteAllBytes(W+"paladin-skill-option-"+index+".png",texture.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(texture);}
  }finally{foreach(var graph in graphs)if(graph.IsValid())graph.Destroy();preview.Cleanup();}
 }
}
#endif
