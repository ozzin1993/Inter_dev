using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // ============ INTERFLOW EDITOR — ГРУППЫ УМЕНИЙ (единый источник, правило 5) ==
    // Одна классификация на три места: группы в списке вкладки «Умения и эффекторы» (С1),
    // фильтр вкладки «Производство» (С2) и шаги создания умения (К1).
    //
    // Основа — штатное вычисляемое свойство Ability.type (правило 2), НИЧЕГО не сериализуется.
    // Исключения нужны потому, что UpgradeBuilding и Transformation числятся Active,
    // а по смыслу это стройка (сверено прогоном по 51 типу и 94 ассетам 2026-08-03).
    public static class InterflowAbilityGroups
    {
        /// <summary>Группа умения глазами геймдизайнера.</summary>
        public enum Group { Combat, Passive, Production }

        /// <summary>
        /// Типы, чей вычисляемый AbilityType не совпадает со смыслом. Ключ — имя КЛАССА:
        /// оно не зависит от языка подписи и не ломается при переименовании ассета.
        /// </summary>
        static readonly HashSet<string> ProductionByClassName = new HashSet<string>
        {
            "UpgradeBuilding",   // Active, но это апгрейд здания
            "Transformation"     // Active, но это превращение постройки/юнита
        };

        // Вычисляемый тип у Ability — свойство, а не поле: чтобы узнать его у КЛАССА (без ассета),
        // приходится создать временный экземпляр. Кэш держит это на один прогон домена.
        static readonly Dictionary<Type, AbilityType> typeCache = new Dictionary<Type, AbilityType>();

        // ======================== КЛАССИФИКАЦИЯ ========================

        /// <summary>Группа существующего ассета умения.</summary>
        public static Group Of(Ability ability)
        {
            if (ability == null) return Group.Combat;
            if (ProductionByClassName.Contains(ability.GetType().Name)) return Group.Production;

            AbilityType t;
            try { t = ability.type; } catch { t = AbilityType.Active; }
            return FromAbilityType(t);
        }

        /// <summary>Группа КЛАССА умения — для списка типов при создании (ассета ещё нет).</summary>
        public static Group OfType(Type abilityClass)
        {
            if (abilityClass == null) return Group.Combat;
            if (ProductionByClassName.Contains(abilityClass.Name)) return Group.Production;

            return FromAbilityType(ComputedTypeOf(abilityClass));
        }

        static Group FromAbilityType(AbilityType t)
        {
            switch (t)
            {
                case AbilityType.Passive:      return Group.Passive;
                case AbilityType.Process:      return Group.Production;
                case AbilityType.Construction: return Group.Production;
                case AbilityType.Container:    return Group.Production;
                default:                       return Group.Combat;
            }
        }

        /// <summary>
        /// Какой AbilityType даст этот класс. Создаёт временный экземпляр (штатный CreateInstance),
        /// результат кэшируется на прогон домена. При отказе — Active, чтобы вкладка не падала.
        /// </summary>
        public static AbilityType ComputedTypeOf(Type abilityClass)
        {
            if (abilityClass == null) return AbilityType.Active;
            if (typeCache.TryGetValue(abilityClass, out var cached)) return cached;

            AbilityType result = AbilityType.Active;
            try
            {
                var probe = ScriptableObject.CreateInstance(abilityClass) as Ability;
                if (probe != null)
                {
                    result = probe.type;
                    UnityEngine.Object.DestroyImmediate(probe);
                }
            }
            catch { /* тип не переживает CreateInstance — остаётся Active, вкладка продолжает работать */ }

            typeCache[abilityClass] = result;
            return result;
        }

        // ======================== ПОДПИСИ ========================

        /// <summary>Заголовок группы для списков и шагов создания.</summary>
        public static string Title(Group g)
        {
            switch (g)
            {
                case Group.Passive:    return "Пассивки";
                case Group.Production: return "Производство и постройки";
                default:               return "Боевые умения";
            }
        }

        /// <summary>Короткое пояснение группы — одна строка под заголовком.</summary>
        public static string Hint(Group g)
        {
            switch (g)
            {
                case Group.Passive:    return "работают сами, без нажатия и без каста";
                case Group.Production: return "постройки, обучение юнитов, исследования, категории панели";
                default:               return "то, что кастуют: по нажатию, по цели, по точке, по области";
            }
        }

        /// <summary>Русское название вычисляемого типа — для карточки «что получится».</summary>
        public static string AbilityTypeRu(AbilityType t)
        {
            switch (t)
            {
                case AbilityType.Container:    return "категория — папка для других умений в панели";
                case AbilityType.Process:      return "процесс — идёт по времени (обучение, исследование)";
                case AbilityType.Area:         return "по области вокруг указанной точки";
                case AbilityType.Location:     return "по точке на земле";
                case AbilityType.Unit:         return "по одной выбранной цели";
                case AbilityType.Active:       return "по нажатию кнопки, без выбора цели игроком";
                case AbilityType.Toggle:       return "переключатель: включается и выключается";
                case AbilityType.Aura:         return "аура — срабатывает каждый тик, пока активна";
                case AbilityType.Passive:      return "пассивное — работает само, без нажатия";
                case AbilityType.Construction: return "постройка — ставится на землю";
                default:                       return "тип не определён";
            }
        }
    }
}
