using Unity.Netcode;
namespace StrategyCore {
 public partial class NetworkDataSync {
  public void CompositeMorphSend(ushort targetId,int abilityId,int level){if(NetworkManager.Singleton==null||!NetworkManager.Singleton.IsServer||!NetworkManager.Singleton.IsListening)return;CompositeMorphClientRpc(targetId,abilityId,level);}
  [Rpc(SendTo.NotServer)] void CompositeMorphClientRpc(ushort targetId,int abilityId,int level){
   if(NetworkConnectionHandler.instance&&NetworkConnectionHandler.instance.connectionStage==2)return;
   if(!GameManager.instance||!TryResolveUnit(targetId,"CompositeMorph",out Unit target)||!target||target.dead)return;
   if(GameManager.instance.gameAbilities.TryGetValue(abilityId,out var ability)&&ability is CompositeSkill skill)skill.ApplyMorphFromNetwork(level,target);
  }
 }
}
