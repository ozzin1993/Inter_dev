namespace StrategyCore
{
    // Кирпич B7 — стоимость способности в % ТЕКУЩЕГО ХП. Общий статик-хелпер (паттерн SkvernaExplosion, правило 5):
    // один хелпер на многих потребителей (Обряд Костра — с кастера; Удар кнутом — с цели). Списание — только сервер,
    // вызывать из Use(). Проверку смертельной self-стоимости — из Check(). Ассет StrategyCore не трогаем (правило 1).
    public static class PercentHpCost
    {
        /// <summary>Проверка (для Check): не смертельна ли self-стоимость, если задан блок. true = каст разрешён.</summary>
        public static bool CanPayFromCaster(Unit caster, float pct, bool blockIfLethal)
        {
            if (caster == null) return false;
            if (!blockIfLethal) return true;
            float cost = pct * caster.health;
            return caster.health - cost > 0f; // строго: стоимость не должна добить кастера
        }

        /// <summary>Списать pct текущего ХП с кастера (флэт ChangeHP). Только сервер. Возвращает списанную величину.</summary>
        public static float PayFromCaster(Unit caster, float pct)
        {
            if (NetworkConnectionHandler.isClient) return 0f;   // списание — только сервер (правило 6)
            if (caster == null || caster.dead || pct <= 0f) return 0f;
            float cost = pct * caster.health;
            InterflowAbility.PayHealth(caster, cost);           // клампит/синкает; смертельная стоимость — честная смерть
            return cost;
        }

        /// <summary>Списать pct текущего ХП с цели. asDamage=true — через броню/тип (DealDamage, уважает isInvulnerable);
        /// иначе флэт ChangeHP (инвула не защищает). Только сервер. Возвращает списанную/запрошенную величину.</summary>
        public static float ApplyToTarget(Unit target, int byOwner, Unit byUnit, float pct, bool asDamage, DamageType type,
                                          Ability sourceAbility) // умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения
        {
            if (NetworkConnectionHandler.isClient) return 0f;   // списание — только сервер (правило 6)
            if (target == null || target.dead || pct <= 0f) return 0f;
            float cost = pct * target.health;
            if (asDamage)
            {
                DamagePacket packet = DamagePacket.Create(cost, type, byOwner, byUnit, false, sourceAbility);   // [Interflow fix 2026-09-04 damage-full-packet] пакет одной записи
                target.GetDamage(in packet, out _); // через броню/тип (не directAttack)
            }
            else
                target.ChangeHP(-cost);                         // флэт
            return cost;
        }
    }
}
