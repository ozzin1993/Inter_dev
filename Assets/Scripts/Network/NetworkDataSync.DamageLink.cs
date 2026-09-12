using System;
using System.Collections.Generic;
using Unity.Netcode;
namespace StrategyCore {
 public partial class NetworkDataSync {
  public void DamageLinkSend(int id,int skillId,ushort casterId,ushort[] members,float duration){if(NetworkManager.Singleton==null||!NetworkManager.Singleton.IsServer||!NetworkManager.Singleton.IsListening)return;DamageLinkClientRpc(id,skillId,casterId,members,duration);}
  [Rpc(SendTo.NotServer)]void DamageLinkClientRpc(int id,int skillId,ushort casterId,ushort[] ids,float duration){
   if(NetworkConnectionHandler.instance&&NetworkConnectionHandler.instance.connectionStage==2)return;
   Unit caster=null;if(casterId!=0)TryResolveUnit(casterId,"DamageLink",out caster);var members=new List<Unit>();foreach(var netId in ids)if(TryResolveUnit(netId,"DamageLink",out Unit u))members.Add(u);SkillPresentationEvents.RaiseDamageLink(id,skillId,caster,members.ToArray(),duration);
  }
 }
}
