using UnityEngine;

namespace StrategyCore
{
    // Переиспользуемый «взрыв Скверны»: разовый урон по площади вокруг точки.
    // Используется «Выбросом Скверны» (враги + свои орки) и детонацией «Обряда Костра» (обычно только враги).
    // Урон — только на сервере (правило 6), штатным Unit.DealDamage. Наш код; ядро StrategyCore не трогаем (правило 1).
    // Единый источник взрыва (правило 5): числа/типы/селекторы задают вызывающие умения из своих .asset.
    public static class SkvernaExplosion
    {
        /// <summary>
        /// Разовый взрыв в точке center радиусом radius. dealer — источник урона (герой-кастер) для штатного
        /// DealDamage; exclude — юнит, которого взрыв не задевает (детонатор). Обе группы опциональны: если урон ≤ 0
        /// либо не заданы тип/селектор — группа пропускается. Направление directAttack=false (это способность, не атака).
        /// </summary>
        public static void Detonate(
            Unit dealer, Vector2 center, float radius,
            float enemyDamage, DamageType enemyDamageType, UnitSelector enemySelector,
            float allyDamage, DamageType allyDamageType, UnitSelector allySelector,
            Unit exclude,
            Ability sourceAbility) // умение-источник для диагностики очереди пакетов (решение Artsiom 05.09.2026); null — без умения
        {
            if (NetworkConnectionHandler.isClient) return;      // урон — только сервер (правило 6)
            if (dealer == null || radius <= 0f) return;
            int owner = dealer.owner;

            // По врагам (чистый «Скверна»: тип с множителями ко всем броням = 1, задаётся в .asset).
            if (enemyDamage > 0f && enemyDamageType != null)
            {
                Unit[] enemies = Utils.GetUnitsInRadius(center, radius, owner, enemySelector, -1, exclude);
                for (int i = 0; i < enemies.Length; i++)
                    if (enemies[i] != null && !enemies[i].dead)
                        dealer.DealDamage(enemies[i], enemyDamage, enemyDamageType, false, Vector3.zero, sourceAbility);
            }

            // По своим оркам вокруг (физический). Детонатор исключён (exclude) — он и так жертвуется.
            if (allyDamage > 0f && allyDamageType != null)
            {
                Unit[] allies = Utils.GetUnitsInRadius(center, radius, owner, allySelector, -1, exclude);
                for (int i = 0; i < allies.Length; i++)
                    if (allies[i] != null && !allies[i].dead)
                        dealer.DealDamage(allies[i], allyDamage, allyDamageType, false, Vector3.zero, sourceAbility);
            }
        }
    }
}
