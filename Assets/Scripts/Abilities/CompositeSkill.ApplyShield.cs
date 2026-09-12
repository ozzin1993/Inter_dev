using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // CompositeSkill.ApplyShield.cs — щит, ответ бьющим, вспышка по радиусу (блок 7). Вырезано 1:1 из CompositeSkill.Blocks.cs (разрезка на partial-ы, правило 22).
    public partial class CompositeSkill
    {
        // -------------------------------------------------------------------- 7. ЩИТ --
        void ApplyShield(int castingPlayer, int level, Unit target)
        {
            if (shield == null || !shield.enabled) return;

            float amount = LevelValue(shield.flat, level)
                         + LevelValue(shield.percentOfMaxHp, level) * target.maxHealth;
            if (amount <= 0f)
            {
                if (InterflowDebug.FullOn) LogShieldSkipped(target, "объём щита нулевой");
                return;
            }

            float duration = LevelValue(shield.duration, level);

            Effector[] onDepletedEffectors = shield.onDepletedEffectors;
            float blindChance = shield.onDepletedBlindChance;
            float blindDuration = shield.onDepletedBlindDuration;
            float burstRadius = shield.onDepletedRadius;
            bool hasReaction = (onDepletedEffectors != null && onDepletedEffectors.Length > 0)
                               || (blindChance > 0f && blindDuration > 0f);
            bool hasRetaliation = shield.retaliationRadius > 0f
                                  && (shield.retaliationStunSeconds > 0f
                                      || (shield.retaliationEffectors != null && shield.retaliationEffectors.Length > 0));

            // Снижение входящего урона привязано к ЖИЗНИ ЩИТА, а не к своему таймеру: ставим вместе
            // со щитом, снимаем в onEnded (пробит, истёк или носитель погиб). Тем и отличается
            // от такого же поля в блоке бафа.
            // Длительность обязательна: штатный IncomingDamageModifier.Apply при нулевой молча ничего
            // не ставит (IncomingDamageModifier.cs:43). У бессрочного щита снижения не будет — ровно так же
            // вёл себя класс ShieldAlly, поведение не меняем.
            float incomingMultiplier = shield.incomingDamageMultiplier;
            bool hasIncomingRule = !Mathf.Approximately(incomingMultiplier, 1f)
                                   && incomingMultiplier > 0f
                                   && duration > 0f;

            if (!hasReaction && !hasRetaliation && !hasIncomingRule)
            {
                AbsorbShield.Apply(target, amount, duration);

                if (InterflowDebug.FullOn) LogShieldApplied(target, amount, duration, hasReaction, hasRetaliation, hasIncomingRule);
                return;
            }

            // Реакция на ПРОБИТИЕ (объём исчерпан), а не на любое снятие — паттерн ShieldAlly.
            // При заданном радиусе она достаётся не носителю, а всем вокруг него по селектору ответа.
            Action<Unit> onDepleted = carrier =>
            {
                if (carrier == null || carrier.dead) return;

                if (burstRadius <= 0f)
                {
                    if (onDepletedEffectors != null && onDepletedEffectors.Length > 0)
                        Effector.EffectorAdd(castingPlayer, carrier, onDepletedEffectors);
                    if (blindChance > 0f && blindDuration > 0f)
                        carrier.Blind(blindChance, blindDuration, null, castingPlayer);
                    return;
                }

                Unit[] around = UnitsAroundCarrier(carrier, burstRadius);
                if (around == null) return;

                for (int i = 0; i < around.Length; i++)
                {
                    Unit u = around[i];
                    if (u == null || u.dead) continue;

                    if (onDepletedEffectors != null && onDepletedEffectors.Length > 0)
                        Effector.EffectorAdd(castingPlayer, u, onDepletedEffectors);
                    if (blindChance > 0f && blindDuration > 0f)
                        u.Blind(blindChance, blindDuration, null, castingPlayer);
                }
            };

            if (!hasRetaliation && !hasIncomingRule)
            {
                AbsorbShield.Apply(target, amount, duration, onDepleted);

                if (InterflowDebug.FullOn) LogShieldApplied(target, amount, duration, hasReaction, hasRetaliation, hasIncomingRule);
                return;
            }

            // Ответ на удар и снижение урона живут ровно столько, сколько щит: снимаем их в onEnded
            // (любая причина снятия). ПОРЯДОК КАК В ShieldAlly: сначала щит, потом подписка — перекаст
            // поверх живого щита дёргает прежний onEnded, и тот снёс бы только что поставленную подписку.
            InterflowCombat.DamagedHandler retaliation = null;
            Action<Unit> onEnded = carrier =>
            {
                if (carrier == null) return;

                if (retaliation != null) InterflowCombat.DamagedListenerRemove(carrier, retaliation);
                if (hasIncomingRule) IncomingDamageModifier.RemoveRule(carrier, incomingMultiplier);
            };

            AbsorbShield.Apply(target, amount, duration, onDepleted, onEnded);

            if (hasRetaliation)
            {
                retaliation = (victim, attacker, damageType, damageDealt, directAttack) =>
                    RetaliateAround(victim, castingPlayer);
                InterflowCombat.DamagedListenerAdd(target, retaliation);
            }

            // Ставим ПОСЛЕ щита по той же причине, что и подписку: onEnded прежнего щита снял бы новое правило.
            // Таймер здесь страховочный — обычно правило снимает onEnded, когда щит сходит.
            if (hasIncomingRule) IncomingDamageModifier.Apply(target, incomingMultiplier, duration, this);

            if (InterflowDebug.FullOn) LogShieldApplied(target, amount, duration, hasReaction, hasRetaliation, hasIncomingRule);
        }

        /// <summary>Кого задевают вспышка и ответ щита: враги носителя по селектору ответа.</summary>
        Unit[] UnitsAroundCarrier(Unit carrier, float radius)
        {
            if (carrier == null || radius <= 0f) return null;

            Vector2 center = new Vector2(carrier.transform.position.x, carrier.transform.position.z);
            return Utils.GetUnitsInRadius(center, radius, carrier.owner, shield.reactionSelector, -1, carrier);
        }

        /// <summary>Ответ щита на удар по носителю: оглушение и эффекторы тем, кто рядом.</summary>
        void RetaliateAround(Unit carrier, int castingPlayer)
        {
            if (IsClientPeer) return;
            if (carrier == null || carrier.dead) return;

            Unit[] around = UnitsAroundCarrier(carrier, shield.retaliationRadius);
            if (around == null) return;

            for (int i = 0; i < around.Length; i++)
            {
                Unit u = around[i];
                if (u == null || u.dead) continue;
                if (shield.retaliationOnlyMelee && !u.melee) continue;

                if (shield.retaliationStunSeconds > 0f) u.Stun(shield.retaliationStunSeconds, carrier, castingPlayer);
                if (shield.retaliationEffectors != null && shield.retaliationEffectors.Length > 0)
                    Effector.EffectorAdd(castingPlayer, u, shield.retaliationEffectors);
            }
        }
    }
}
