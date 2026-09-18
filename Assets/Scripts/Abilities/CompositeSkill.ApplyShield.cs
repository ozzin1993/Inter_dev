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
            bool hasRetaliation = NeedsRetaliation(shield);

            // Снижение входящего урона привязано к ЖИЗНИ ЩИТА, а не к своему таймеру: ставим вместе
            // со щитом, снимаем в onEnded (пробит, истёк или носитель погиб). Тем и отличается
            // от такого же поля в блоке бафа.
            // Длительность обязательна: штатный IncomingDamageModifier.Apply при нулевой молча ничего
            // не ставит (IncomingDamageModifier.cs:43). У бессрочного щита снижения не будет — ровно так же
            // вёл себя класс ShieldAlly, поведение не меняем.
            float incomingMultiplier = shield.incomingDamageMultiplier;
            bool hasIncomingRule = NeedsIncomingRule(shield, duration);

            // [Interflow 2026-09-17, решение Artsiom 21] Визуал на время щита — СОСТОЯНИЕ, и снимать его
            // нечем, кроме onEnded. Поэтому при заданном визуале коротких путей ниже не бывает: оба ранних
            // выхода зовут AbsorbShield.Apply БЕЗ onEnded, и визуал остался бы висеть навсегда.
            bool hasVisual = shield.visualEffector != null;
            bool needsOnEnded = ShieldNeedsOnEnded(shield, duration);

            if (!hasReaction && !needsOnEnded)
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

            if (!needsOnEnded)
            {
                AbsorbShield.Apply(target, amount, duration, onDepleted);

                if (InterflowDebug.FullOn) LogShieldApplied(target, amount, duration, hasReaction, hasRetaliation, hasIncomingRule);
                return;
            }

            // Ответ на удар и снижение урона живут ровно столько, сколько щит: снимаем их в onEnded
            // (любая причина снятия). ПОРЯДОК КАК В ShieldAlly: сначала щит, потом подписка — перекаст
            // поверх живого щита дёргает прежний onEnded, и тот снёс бы только что поставленную подписку.
            InterflowCombat.DamagedHandler retaliation = null;
            EffectorHolder visualHolder = null;   // держатель визуала щита: снимаем ровно своё наложение
            Action<Unit> onEnded = carrier =>
            {
                if (carrier == null) return;

                if (retaliation != null) InterflowCombat.DamagedListenerRemove(carrier, retaliation);
                if (hasIncomingRule) IncomingDamageModifier.RemoveRule(carrier, incomingMultiplier);

                // Держатель нулевой — наложения не было вовсе (здание, труп, отказ приёмника):
                // EffectorAdd в этих случаях отдаёт null (Effectors/Effector.cs).
                if (visualHolder != null) { Effector.EffectorRemove(carrier, visualHolder); visualHolder = null; }
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

            // Визуал — тоже ПОСЛЕ щита: перекаст поверх живого щита дёргает ПРЕЖНИЙ onEnded уже после
            // подмены реакций (Units/AbsorbShield.cs), и поставленное раньше наложение он бы и снял.
            if (hasVisual) visualHolder = Effector.EffectorAdd(target, shield.visualEffector, null, castingPlayer);

            if (InterflowDebug.FullOn) LogShieldApplied(target, amount, duration, hasReaction, hasRetaliation, hasIncomingRule);
        }

        /// <summary>
        /// Нужна ли щиту перегрузка <c>AbsorbShield.Apply</c> С <c>onEnded</c>. ЕДИНСТВЕННЫЙ источник
        /// этого решения: оба ранних выхода ApplyShield спрашивают его же, второй копии правила нет.
        ///
        /// onEnded нужен тому, кто вешает на ВРЕМЯ ЩИТА что-то, что обязан снять: подписку ответа,
        /// правило снижения урона, состояние-визуал. Без onEnded снимать их нечем, и они пережили бы щит.
        ///
        /// Публичный ради теста режима редактора — живой ApplyShield в EditMode неподъёмен
        /// (нужны цели, приёмник и менеджеры сцены), а правило проверять надо.
        /// </summary>
        /// <param name="duration">Длительность щита на этом уровне: у бессрочного правило снижения урона
        /// не ставится вовсе (штатный IncomingDamageModifier при нулевой длительности молчит).</param>
        public static bool ShieldNeedsOnEnded(SkillShieldBlock shield, float duration)
        {
            if (shield == null) return false;

            return NeedsRetaliation(shield) || NeedsIncomingRule(shield, duration) || shield.visualEffector != null;
        }

        /// <summary>Ставится ли вместе со щитом подписка «ответ на удар по носителю».</summary>
        static bool NeedsRetaliation(SkillShieldBlock shield)
        {
            return shield.retaliationRadius > 0f
                   && (shield.retaliationStunSeconds > 0f
                       || (shield.retaliationEffectors != null && shield.retaliationEffectors.Length > 0));
        }

        /// <summary>Ставится ли вместе со щитом правило снижения входящего урона.</summary>
        static bool NeedsIncomingRule(SkillShieldBlock shield, float duration)
        {
            return !Mathf.Approximately(shield.incomingDamageMultiplier, 1f)
                   && shield.incomingDamageMultiplier > 0f
                   && duration > 0f;
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
