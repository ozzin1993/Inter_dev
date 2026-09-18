using System.Collections.Generic;
using UnityEditor;

namespace StrategyCore
{
    public static partial class InterflowValidator
    {
        // Дополнение проверки: тип обязателен и у тика состояния, и у взрыва при смерти.
        static void ValidateAdditionalDamageTypes(List<InterflowIssue> issues)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Effector"))
            {
                var effector = AssetDatabase.LoadAssetAtPath<Effector>(AssetDatabase.GUIDToAssetPath(guid));
                if (effector != null && effector.damageAmount != 0 && effector.damageType == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Состояние «{effector.name}»: задан периодический урон, но отсутствует тип урона.",
                        "Effector.EffectorUpdate; DamagePacket", effector));
            }
            foreach (string guid in AssetDatabase.FindAssets("t:CompositePassive"))
            {
                var passive = AssetDatabase.LoadAssetAtPath<CompositePassive>(AssetDatabase.GUIDToAssetPath(guid));
                if (passive?.onDeath != null && passive.onDeath.enabled && AnyPositive(passive.onDeath.enemyDamage)
                    && passive.onDeath.enemyDamageType == null)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"Пассивка «{passive.name}»: задан урон при смерти, но отсутствует тип урона.",
                        "CompositePassive.ReactionsRuntime.DeathBurst", passive));
            }
        }
    }
}
