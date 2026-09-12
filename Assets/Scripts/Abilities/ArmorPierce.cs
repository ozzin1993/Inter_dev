using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Пробитие брони: атаки носителя игнорируют долю физической защиты цели.
    /// Потребители: Тир 1 [А] «Пробитие брони» (доля 0.2), Тир 4 [Б] «Бронебойный наконечник» (доля 1).
    ///
    /// Реализация: регистрация в <see cref="InterflowCombat"/>; сама броня уменьшается внутри
    /// <c>Unit.GetDamage</c> (маркер <c>[Interflow fix 2026-07-24 combat-hub]</c>) — то есть урон считается
    /// один раз и правильно, без «доборов» вторым вызовом.
    ///
    /// Открывается технологией: положить в <c>abilities[]</c> префаба и заполнить <c>Required Tech</c>
    /// технологией узла специализации. До покупки узла способность залочена, эффекта нет.
    /// </summary>
    public class ArmorPierce : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        [Header("Пробитие брони")]
        [Tooltip("Какая доля брони цели игнорируется, по уровням способности. 0.2 = игнорируется 20% защиты, 1 = броня цели не учитывается совсем.")]
        [Range(0f, 1f)]
        public float[] pierceFraction = new float[1] { 0.2f };

        // Что уже применено на юните. Нужен, потому что ассет зовёт Unlock повторно:
        // способность без Required Tech разблокируется заново на КАЖДОЕ событие OnTechUnlock
        // (Unit.Ability.cs: ветка «требований нет»), и без этого доля пробития копилась бы до 100%.
        readonly System.Collections.Generic.Dictionary<Unit, float> applied =
            new System.Collections.Generic.Dictionary<Unit, float>();

        public override void Init()
        {
            base.Init();
            applied.Clear();
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || applied.ContainsKey(unit)) return;

            float fraction = FractionAt(level);
            applied[unit] = fraction;
            InterflowCombat.ArmorPierceAdd(unit, fraction);
            AbilityFacts.Granted(this, unit, level);   // [2026-09-10] показ срабатывания — см. AbilityFacts

            InterflowDebug.Event("ПРОБИТИЕ БРОНИ включено у " + InterflowDebug.Name(unit) +
                                 ": игнорирует " + (fraction * 100f).ToString("0.#") + "% брони цели");

            if (InterflowDebug.FullOn)
                InterflowDebug.Full("ПРОБИТИЕ БРОНИ («" + name + "»): выдано | носитель=" + InterflowDebug.Name(unit) +
                                    " | доля умения=" + fraction.ToString("0.##") +
                                    " | уровень=" + level +
                                    " | у носителя стало=" + InterflowCombat.ArmorPierceOf(unit).ToString("0.##"));
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null) return;
            if (!applied.TryGetValue(unit, out float fraction)) return;

            InterflowCombat.ArmorPierceRemove(unit, fraction);
            AbilityFacts.Revoked(this, unit, level);   // [2026-09-10] показ срабатывания — см. AbilityFacts
            applied.Remove(unit);

            InterflowDebug.Event("ПРОБИТИЕ БРОНИ снято у " + InterflowDebug.Name(unit));

            if (InterflowDebug.FullOn)
                InterflowDebug.Full("ПРОБИТИЕ БРОНИ («" + name + "»): снято | носитель=" + InterflowDebug.Name(unit) +
                                    " | доля умения=" + fraction.ToString("0.##") +
                                    " | у носителя осталось=" + InterflowCombat.ArmorPierceOf(unit).ToString("0.##"));
        }

        /// <summary>Доля пробития для уровня — общая выборка по уровням (Б8): массив короче — последний заполненный.</summary>
        float FractionAt(int level) => Mathf.Clamp01(InterflowAbility.LevelValue(pierceFraction, level));
    }
}
