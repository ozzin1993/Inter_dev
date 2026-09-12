using System.Collections.Generic;
using UnityEngine;
namespace StrategyCore {
 public partial class SkillPresenter {
  readonly Dictionary<int,DamageLinkView> damageLinkViews=new Dictionary<int,DamageLinkView>();
  void HandleDamageLink(int id,int skillId,Unit caster,Unit[] members,float duration){if(damageLinkViews.TryGetValue(id,out var old)&&old)Destroy(old.gameObject);damageLinkViews.Remove(id);if(duration<=0||members.Length<2)return;var skill=ResolveSkill(skillId);if(!skill||skill.damageLink==null||!skill.damageLink.chainMaterial)return;var go=new GameObject("SkillDamageLinkVisual");var v=go.AddComponent<DamageLinkView>();v.Init(caster,members,skill.damageLink,duration);damageLinkViews[id]=v;}
 }
 public sealed class DamageLinkView:MonoBehaviour {
  Unit caster;Unit[] members;float end;readonly List<LineRenderer> lines=new List<LineRenderer>();
  public void Init(Unit caster,Unit[] members,SkillDamageLinkBlock b,float duration){this.caster=caster;this.members=members;end=Time.time+duration;for(int i=0;i<members.Length;i++)for(int j=i+1;j<members.Length;j++){var line=new GameObject("FelChain").AddComponent<LineRenderer>();line.transform.SetParent(transform,false);line.sharedMaterial=b.chainMaterial;line.startColor=line.endColor=b.chainColor;line.startWidth=line.endWidth=b.chainWidth;line.positionCount=17;line.useWorldSpace=true;line.textureMode=LineTextureMode.Tile;line.numCapVertices=2;lines.Add(line);}LateUpdate();}
  void LateUpdate(){if(!caster||caster.dead||Time.time>=end){Destroy(gameObject);return;}int n=0,live=0;foreach(var u in members)if(u&&!u.dead)live++;if(live<2){Destroy(gameObject);return;}
   for(int i=0;i<members.Length;i++)for(int j=i+1;j<members.Length;j++){var line=lines[n++];var a=members[i];var b=members[j];line.enabled=a&&b&&!a.dead&&!b.dead&&a.FoWVisible&&b.FoWVisible;if(!line.enabled)continue;var p=a.transform.position+Vector3.up*.8f;var q=b.transform.position+Vector3.up*.8f;var side=Vector3.Cross(q-p,Vector3.up).normalized;for(int k=0;k<17;k++){float t=k/16f;line.SetPosition(k,Vector3.Lerp(p,q,t)+side*Mathf.Sin(t*Mathf.PI*8+Time.time*8)*.055f* Mathf.Sin(t*Mathf.PI)+Vector3.down*Mathf.Sin(t*Mathf.PI)*.12f);}}
  }
 }
}
