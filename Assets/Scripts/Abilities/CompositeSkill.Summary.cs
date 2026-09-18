using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.Summary.cs — человекочитаемая сводка для редактора. Вырезано 1:1 из CompositeSkill.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // ================================================================== СВОДКА ==

        /// <summary>
        /// Человекочитаемая строка «что делает этот скилл» — её показывает вкладка «Умения»
        /// над полями, чтобы геймдизайнер понимал скилл, не раскрывая все блоки.
        /// </summary>
        public string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("Цель: ").Append(TargetModeText());
            if (PicksTargetByStrategy) sb.Append(" (").Append(StrategyText()).Append(')');
            if (PicksTargetByStrategy && avoidTargetState != SkillTargetAvoidState.None)
            {
                sb.Append(" • мимо целей: ").Append(AvoidStateText());
                if (avoidNoFreeTarget == SkillNoFreeTargetFallback.Skip) sb.Append(" (иначе не применять)");
            }
            if (maxTargets > 0) sb.Append(", не больше ").Append(maxTargets);
            if (buttonCast) sb.Append(" • по кнопке");

            if (PicksTargetByStrategy && targetFrontAngle > 0f && targetFrontAngle < 360f)
                sb.Append(" • впереди ").Append(targetFrontAngle.ToString("0")).Append('°');

            if (targetPrefabs != null && targetPrefabs.Length > 0)
                sb.Append(" • префабов: ").Append(targetPrefabs.Length);

            if (targetMode == SkillTargetMode.SmartPoint && independentAreaTargets)
                sb.Append(" • область: свой селектор");

            if (delivery == SkillDelivery.Projectile) sb.Append(" • снарядом");
            if (delivery == SkillDelivery.Projectile && projectileDirectAttack) sb.Append(" • снаряд — атака");
            if (presentation.socket != SkillSocketType.None) sb.Append(" из точки «").Append(SocketText()).Append('»');

            if (damage != null && damage.enabled && damage.entries != null && damage.entries.Length > 0)
            {
                sb.Append(" • урон");
                for (int i = 0; i < damage.entries.Length; i++)
                {
                    SkillDamageEntry e = damage.entries[i];
                    if (e == null) continue;
                    sb.Append(i == 0 ? " " : ", ").Append(LevelValue(e.amount, 0).ToString("0.#"));
                    if (e.damageType != null) sb.Append(' ').Append(e.damageType.name);
                }
            }

            if (status != null && status.enabled)
            {
                float stun = LevelValue(status.stunSeconds, 0);
                float disarm = LevelValue(status.disarmSeconds, 0);
                float mute = LevelValue(status.muteSeconds, 0);
                if (stun > 0f) sb.Append(" • оглушение ").Append(stun.ToString("0.#")).Append(" с");
                if (disarm > 0f) sb.Append(" • обезоруживание ").Append(disarm.ToString("0.#")).Append(" с");
                if (mute > 0f) sb.Append(" • немота ").Append(mute.ToString("0.#")).Append(" с");
            }

            if (heal != null && heal.enabled)
            {
                float flat = LevelValue(heal.flat, 0);
                float pct = LevelValue(heal.percentOfMaxHp, 0);
                if (flat > 0f) sb.Append(" • лечение ").Append(flat.ToString("0.#"));
                if (pct > 0f) sb.Append(" • лечение ").Append(pct.ToString("0.#")).Append("% макс. ХП");
            }

            if (effectors != null && effectors.enabled && effectors.records != null)
            {
                int liveRecords = 0;
                for (int i = 0; i < effectors.records.Length; i++)
                    if (effectors.records[i] != null && effectors.records[i].effector != null) liveRecords++;

                if (liveRecords > 0) sb.Append(" • состояний: ").Append(liveRecords);
            }

            if (buff != null && buff.enabled)
                sb.Append(" • баф ").Append(LevelValue(buff.duration, 0).ToString("0.#")).Append(" с");

            if (shield != null && shield.enabled) sb.Append(" • щит");
            if (blind != null && blind.enabled) sb.Append(" • ослепление");
            if (selfCost != null && selfCost.enabled) sb.Append(" • стоит здоровья кастеру");
            if (summon != null && summon.enabled) sb.Append(" • призыв");
            if (groundZone != null && groundZone.enabled) sb.Append(" • зона на земле");
            if (delegateService != null && delegateService.enabled && delegateService.service != SkillServerService.None)
                sb.Append(" • сервис: ").Append(ServiceText());
            if (gaugeBlock != null && gaugeBlock.enabled)
                sb.Append(" • шкала: ").Append(gaugeBlock.requireFull ? "полная" : "есть").Append(gaugeBlock.spendAll ? ", в ноль" : "");
            if (castConditions != null && castConditions.enabled)
            {
                string conditions = CastConditionsText();
                if (conditions.Length > 0) sb.Append(" • условия: ").Append(conditions);
            }
            if (projectileImpact != null && projectileImpact.enabled)
                sb.Append(" • при прилёте: ")
                  .Append(projectileImpact.skill != null ? projectileImpact.skill.name : "умение не задано");

            if (statusEffector != null) sb.Append(" • значок: ").Append(statusEffector.name);

            // [Interflow 2026-09-17] Наборы визуала СОБЫТИЙ (не каста): по строке на каждый заполненный.
            // Набор самого каста в сводку не идёт — про него уже говорит строка «из точки «…»».
            string eventSets = EventPresentationText();
            if (eventSets.Length > 0) sb.Append(" • визуал события: ").Append(eventSets);

            return sb.ToString();
        }

        /// <summary>
        /// Блоки умения, у которых заполнен НАБОР визуала события, через запятую. Пусто — ни одного.
        /// Читается по тому же признаку, по которому сервер решает слать факт (EventPresentation.Any).
        /// </summary>
        string EventPresentationText()
        {
            StringBuilder sb = new StringBuilder();

            if (heal != null && heal.presentation != null && heal.presentation.Any) sb.Append("лечение");
            // У блока 14 ДВА набора (решение Artsiom 33 от 17.09.2026): «каст» на кастере и «цель»
            // на каждой задетой цели. В сводке блок назван один раз — заполнен любой из двух.
            if (secondary != null
                && ((secondary.castPresentation != null && secondary.castPresentation.Any)
                 || (secondary.presentation != null && secondary.presentation.Any)))
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("вторичные цели");
            }
            if (projectileImpact != null && projectileImpact.presentation != null && projectileImpact.presentation.Any)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append("прилёт снаряда");
            }

            return sb.ToString();
        }

        /// <summary>Условия каста блока 21 через косую черту: «облик/цель в области/ХП &lt; 30%».</summary>
        string CastConditionsText()
        {
            StringBuilder sb = new StringBuilder();

            if (castConditions.requireOriginalForm) sb.Append("исходный облик");

            if (castConditions.requiredShape != null)
            {
                if (sb.Length > 0) sb.Append('/');
                sb.Append("облик: ").Append(castConditions.requiredShape.name);
            }

            if (castConditions.requireAreaTarget)
            {
                if (sb.Length > 0) sb.Append('/');
                sb.Append("цель в области");
            }

            if (castConditions.casterHpBelow > 0f)
            {
                if (sb.Length > 0) sb.Append('/');
                sb.Append("ХП < ").Append((castConditions.casterHpBelow * 100f).ToString("0")).Append('%');
            }

            return sb.ToString();
        }

        string TargetModeText()
        {
            switch (targetMode)
            {
                case SkillTargetMode.Self:           return "на себя";
                case SkillTargetMode.WholeTeam:      return "вся команда";
                case SkillTargetMode.AreaAroundSelf: return "область вокруг кастера";
                case SkillTargetMode.Cone:           return $"конус {coneAngle:0}°";
                case SkillTargetMode.SmartUnit:      return "юнит";
                case SkillTargetMode.SmartPoint:     return "точка";
            }
            return targetMode.ToString();
        }

        string StrategyText()
        {
            switch (targetStrategy)
            {
                case SkillTargetStrategy.Nearest:               return "ближайший";
                case SkillTargetStrategy.NearestBelowThreshold: return "ближайший ниже порога";
                case SkillTargetStrategy.HighestDps: return "наибольший урон в секунду";
                case SkillTargetStrategy.MostWounded:           return "самый раненый";
                case SkillTargetStrategy.WoundedBelowThreshold: return "раненый ниже порога";
                case SkillTargetStrategy.Strongest:             return "наибольший запас ХП";
                case SkillTargetStrategy.RandomOne:             return "случайный";
                case SkillTargetStrategy.Cluster:               return "скопление";
                case SkillTargetStrategy.CurrentAttackTarget:   return "текущая цель атаки";
            }
            return targetStrategy.ToString();
        }

        string AvoidStateText()
        {
            switch (avoidTargetState)
            {
                case SkillTargetAvoidState.Shielded:      return "под щитом";
                case SkillTargetAvoidState.Stunned:       return "оглушённых";
                case SkillTargetAvoidState.Disarmed:      return "обезоруженных";
                case SkillTargetAvoidState.Muted:         return "в немоте";
                case SkillTargetAvoidState.Blinded:       return "ослеплённых";
                case SkillTargetAvoidState.NamedEffector: return avoidTargetEffector != null
                                                                 ? "с состоянием «" + avoidTargetEffector.name + "»"
                                                                 : "с состоянием (ассет не задан)";
            }
            return avoidTargetState.ToString();
        }

        string SocketText()
        {
            switch (presentation.socket)
            {
                case SkillSocketType.RightHand: return "правая рука";
                case SkillSocketType.LeftHand:  return "левая рука";
                case SkillSocketType.Weapon:    return "оружие";
                case SkillSocketType.Chest:     return "грудь";
                case SkillSocketType.Center:    return "центр";
                case SkillSocketType.Head:      return "голова";
                case SkillSocketType.Ground:    return "под ногами";
            }
            return "центр объекта";
        }

        string ServiceText()
        {
            switch (delegateService.service)
            {
                case SkillServerService.MeteorStorm:         return "метеоритный дождь";
                case SkillServerService.ResurrectFromGraves: return "подъём павших";
            }
            return "нет";
        }
    }
}
