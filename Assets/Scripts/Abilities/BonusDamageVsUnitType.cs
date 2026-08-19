using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Бонус урона по определённому ТИПУ объекта (здание, дерево, разрушаемый объект).
    /// Потребители: Тир 4 [А] «Осадный калибр» (урон по зданиям), Тир 4 [Б] «Бронебойный наконечник» (×2 по постройкам).
    ///
    /// Почему не через матрицу DamageType × ArmorType: матрица настраивается на РАСУ целиком и общая для всех
    /// юнитов с этим типом урона. Здесь бонус принадлежит конкретному юниту и включается технологией.
    ///
    /// Реализация: штатный хук атакующего <c>OnAfterDamageDealCallbacks</c> (в нём есть цель) —
    /// добивается недостающая часть урона тем же типом. Урон проходит через броню как обычно,
    /// поэтому в поле указывается множитель ИТОГОВОГО урона (2 = вдвое больше).
    /// </summary>
    public class BonusDamageVsUnitType : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Бонус по типу цели")]
        [Tooltip("Тип цели, по которому действует бонус: Building — здания (замки, башни), StaticDestructible — разрушаемые объекты, Tree — деревья.")]
        public UnitType targetUnitType = UnitType.Building;

        [Tooltip("Во сколько раз больше урона по такой цели, по уровням. 2 = двойной урон, 5 = пятикратный. Значение 1 = бонуса нет.")]
        public float[] damageMultiplier = new float[1] { 2f };

        [Tooltip("Считать только прямые атаки юнита. ВЫКЛ — бонус пойдёт и от способностей/эффекторов носителя.")]
        public bool onlyDirectAttack = true;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
                        InterflowAbility.CallbackAdd(unit.OnAfterDamageDealCallbacks, this, level, BonusApply);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            InterflowAbility.CallbackRemove(unit.OnAfterDamageDealCallbacks, this, level);
        }

        void BonusApply(Unit targetUnit, Vector3 targetPosition, Effector[] effectors, float dmg, bool directAttack,
                        DamageType damageType, Unit byUnit, Projectile byProjectile, int byOwner, int level)
        {
            if (NetworkConnectionHandler.isClient) return; // правило 6 — урон наносит сервер
            if (targetUnit == null || byUnit == null) return;
            if (onlyDirectAttack && !directAttack) return;
            if (targetUnit.unitType != targetUnitType) return;

            float mult = MultiplierAt(level);
            if (mult <= 1f) return;

            // Штатный удар уже прошёл. Добиваем недостающую часть тем же типом урона:
            // итоговый множитель получается ровно mult, так как добавка проходит ту же броню и ту же матрицу типов.
            float extra = dmg * (mult - 1f);
            if (extra <= 0f) return;

            targetUnit.GetDamage(extra, damageType, byOwner, byUnit, false, out float _);

            RequestForceSync();
        }

        float MultiplierAt(int level)
        {
            if (damageMultiplier == null || damageMultiplier.Length == 0) return 1f;
            if (level < 0) level = 0;
            if (level >= damageMultiplier.Length) level = damageMultiplier.Length - 1;

            return damageMultiplier[level];
        }
    }
}
