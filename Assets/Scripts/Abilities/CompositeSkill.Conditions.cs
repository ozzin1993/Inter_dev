using UnityEngine;

namespace StrategyCore
{
    [System.Serializable]
    public class SkillCastConditions
    {
        [Range(0,1), Tooltip("Каст разрешён только ниже этой доли здоровья носителя. 0 — ограничения нет.")]
        public float casterHpBelow;
        [Tooltip("Для области вокруг кастера: применять только при наличии подходящей живой цели в радиусе.")]
        public bool requireAreaTarget;
        public bool requireOriginalForm;
        [Tooltip("Разрешённый облик или исходный префаб юнита. Пусто — любой.")] public Unit requiredShape;
    }

    public partial class CompositeSkill
    {
        public bool IsFormAllowed(Unit caster) {
            if(!caster)return false;if(castConditions==null)return true;
            if(castConditions.requireOriginalForm&&caster.polymorphed)return false;
            if(castConditions.requiredShape){var shape=caster.polymorphed?caster.polymorphShape:caster;if(!shape||shape.unitTypeID!=castConditions.requiredShape.unitTypeID)return false;}
            return true;
        }
        bool MeetsCastConditions(Unit caster, int level)
        {
            if(castConditions==null)return true;
            if(caster==null||caster.dead)return false;
            var travel=caster.GetComponent<SkillTravel>();if(travel&&travel.Running)return false;
            if(!IsFormAllowed(caster))return false;
            if(castConditions.casterHpBelow>0&&!SkillTargeting.IsBelowHealthThreshold(caster,castConditions.casterHpBelow))return false;
            if(!castConditions.requireAreaTarget)return true;
            if(caster==null||caster.dead)return false;
            var candidates=Utils.GetUnitsInRadius(new Vector2(caster.transform.position.x,caster.transform.position.z),LevelValue(radius,level),caster.owner,unitSelector,-1,includeSelf?null:caster);
            if(candidates!=null)foreach(var target in candidates)if(IsEligibleTarget(target,caster.owner,caster))return true;
            return false;
        }
    }
}
