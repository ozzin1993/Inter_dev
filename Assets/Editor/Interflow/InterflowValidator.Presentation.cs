using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StrategyCore
{
    // ==== INTERFLOW EDITOR — ВАЛИДАТОР: НАБОРЫ ВИЗУАЛА СОБЫТИЙ (правило 22 — партиал по фиче) ====
    // Правила Р24–Р31 проекта «Презентация_Поля_Проект» §4.5. Отдельным файлом, а не добавкой
    // в InterflowValidator.cs: там уже больше 1800 строк, и наращивать его вширь правило 22 запрещает.
    //
    // Точки входа остаются в основном файле: ValidateSkillPresentations зовётся из ValidateSkill
    // (правила набора видны в карточке умения), ValidateEventPresentations — из RunAll и RunAbilityScope.
    public static partial class InterflowValidator
    {
        // ======================== НАБОРЫ ВИЗУАЛА СОБЫТИЙ (Р24–Р31) ========================
        // Правила проекта «Презентация_Поля_Проект» §4.5. Слово «визуал» в текстах обязательно:
        // по нему вкладка «Боевые умения» кладёт сообщение в общую зону карточки (ISSUE_ROUTES).

        // Умолчания набора берутся у самого типа: числа в коде проверки не заводим (правило 3).
        static readonly EventPresentation PresentationDefaults = new EventPresentation();

        /// <summary>Время жизни выставлено руками (отличается от умолчания типа) и при этом положительное.</summary>
        static bool LifetimeSetByHand(float value, float defaultValue)
            => value > 0f && !Mathf.Approximately(value, defaultValue);

        // Р27: зарезервированные схемы стейтов ядра. Список — из тултипа поля стейта анимации
        // (Scripts/Core/Types/EventPresentation.cs) и из ядра Unit.State / Unit.Combat.
        static readonly string[] RESERVED_ANIMATION_PREFIXES = { "attack", "death", "idle" };
        static readonly string[] RESERVED_ANIMATION_STATES =
            { "idleready", "cast", "casting", "hit", "walk", "building", "construction" };

        /// <summary>
        /// Наборы самого умения: каст, лечение (блок 7), ДВА набора вторичных целей (блок 14 — «каст»
        /// и «цель»), прилёт снаряда (блок 22) и визуал щита.
        /// </summary>
        static void ValidateSkillPresentations(List<InterflowIssue> issues, CompositeSkill skill, string n)
        {
            string owner = "Умение «" + n + "»";

            // Набор каста живёт всегда: выключить сам каст блоком нельзя, поэтому «блок включён» = true.
            ValidateEventPresentation(issues, skill.presentation, owner, "визуал каста", true, skill);

            if (skill.heal != null)
                ValidateEventPresentation(issues, skill.heal.presentation, owner, "визуал лечения",
                                          skill.heal.enabled, skill);

            // Слово «вторичн» в тексте места обязательно: по нему вкладка «Боевые умения» кладёт
            // сообщение в карточку блока 14, а не в общую зону (ISSUE_ROUTES).
            // У блока 14 ДВА набора (решение Artsiom 33 от 17.09.2026), и правила Р24–Р27 применяются
            // к каждому отдельно — иначе половина полей блока осталась бы без проверки.
            if (skill.secondary != null)
            {
                ValidateEventPresentation(issues, skill.secondary.castPresentation, owner,
                                          "визуал блока вторичных целей, один раз за каст",
                                          skill.secondary.enabled, skill);

                ValidateEventPresentation(issues, skill.secondary.presentation, owner,
                                          "визуал попадания по вторичной цели", skill.secondary.enabled, skill);
            }

            if (skill.projectileImpact != null)
                ValidateEventPresentation(issues, skill.projectileImpact.presentation, owner,
                                          "визуал прилёта снаряда", skill.projectileImpact.enabled, skill);

            ValidateProjectileImpactWiring(issues, skill, owner);
            ValidateShieldVisual(issues, skill, owner);
        }

        /// <summary>
        /// Р31: у блока 22 заполнен набор, а вложенного умения нет. Подписка на прилёт снаряда ставится
        /// ТОЛЬКО при заданном умении (<c>CompositeSkill.Cast.cs</c>, <c>SpawnProjectile</c>: CallbackAdd
        /// на <c>OnProjectileImpactCallbacks</c> под условием <c>projectileImpact.skill != null</c>),
        /// и без неё события не возникает вовсе — показывать нечего и некому.
        /// Код прилёта не меняется (решение Artsiom 32 от 17.09.2026), поэтому здесь только предупреждение.
        /// </summary>
        static void ValidateProjectileImpactWiring(List<InterflowIssue> issues, CompositeSkill skill, string ownerName)
        {
            SkillProjectileImpactBlock block = skill.projectileImpact;
            if (block == null || !block.enabled) return;
            if (block.skill != null) return;
            if (block.presentation == null || !block.presentation.Any) return;

            issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                $"{ownerName} (визуал прилёта снаряда): визуал прилёта не сыграет — без вложенного умения " +
                "подписка на прилёт не ставится.",
                "Презентация_Поля_Проект §4.5 (Р31); CompositeSkill.Cast.cs SpawnProjectile — CallbackAdd на OnProjectileImpactCallbacks",
                skill));
        }

        /// <summary>
        /// Р24–Р27 для ОДНОГО набора. Применяются ко всем наборам одинаково, включая набор самого умения.
        /// </summary>
        /// <param name="place">Где набор лежит, для текста сообщения (в нём обязательно слово «визуал»).</param>
        /// <param name="blockEnabled">Включён ли блок-хозяин: у выключенного набор не сыграет никогда (Р26).</param>
        static void ValidateEventPresentation(List<InterflowIssue> issues, EventPresentation set,
                                              string ownerName, string place, bool blockEnabled, Object target)
        {
            if (set == null) return;

            // Р24. Время жизни ЗАДАНО РУКАМИ, а самого визуала нет — поле заполнено наполовину.
            // «Задано руками» = отличается от умолчания типа: умолчание стоит в КАЖДОМ пустом наборе
            // (их у контента сотни), и правило по одному лишь «больше нуля» было бы сплошным шумом.
            // Умолчание читается у самого типа, а не числом в коде (правило 3).
            if (set.carrierVFX == null && LifetimeSetByHand(set.carrierVfxLifetime, PresentationDefaults.carrierVfxLifetime))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{ownerName} ({place}): время жизни визуала У НОСИТЕЛЯ задано, а сам визуал не выбран — показывать нечего.",
                    "Презентация_Поля_Проект §4.5 (Р24)", target));

            if (set.pointVFX == null && LifetimeSetByHand(set.pointVfxLifetime, PresentationDefaults.pointVfxLifetime))
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{ownerName} ({place}): время жизни визуала В ТОЧКЕ задано, а сам визуал не выбран — показывать нечего.",
                    "Презентация_Поля_Проект §4.5 (Р24)", target));

            // Р25. Визуал задан, а время жизни ≤ 0 — хелперы показа выходят сразу (InterflowAbility.cs).
            if (set.carrierVFX != null && set.carrierVfxLifetime <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{ownerName} ({place}): визуал У НОСИТЕЛЯ задан, но время его жизни не больше нуля — визуал не создастся вовсе.",
                    "Презентация_Поля_Проект §4.5 (Р25); InterflowAbility.PlaySocketVFX", target));

            if (set.pointVFX != null && set.pointVfxLifetime <= 0f)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                    $"{ownerName} ({place}): визуал В ТОЧКЕ задан, но время его жизни не больше нуля — визуал не создастся вовсе.",
                    "Презентация_Поля_Проект §4.5 (Р25); InterflowAbility.PlayPointVFX", target));

            // Р26. Набор заполнен у ВЫКЛЮЧЕННОГО блока — он не сыграет никогда.
            if (!blockEnabled && set.Any)
                issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                    $"{ownerName} ({place}): визуал задан у ВЫКЛЮЧЕННОГО блока — он не сыграет никогда.",
                    "Презентация_Поля_Проект §4.5 (Р26)", target));

            // Р27. Стейт анимации из зарезервированной схемы ядра или не строчными буквами.
            if (!string.IsNullOrEmpty(set.animationState))
            {
                string st = set.animationState;
                string low = st.ToLowerInvariant();

                if (st != low)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"{ownerName} ({place}): стейт анимации визуала «{st}» записан не строчными буквами — он не совпадёт со стейтом в контроллере.",
                        "Презентация_Поля_Проект §4.5 (Р27)", target));

                bool reserved = System.Array.IndexOf(RESERVED_ANIMATION_STATES, low) >= 0;
                if (!reserved)
                    foreach (string prefix in RESERVED_ANIMATION_PREFIXES)
                    {
                        // Схема ядра — «слово + НОМЕР» (attack0, death1, idle2). Проверяем именно её,
                        // а не любое начало: иначе правило запретило бы, например, «attackspin»,
                        // которого у ядра нет и который со своими стейтами оно не спутает.
                        if (low.Length > prefix.Length && low.StartsWith(prefix)
                            && char.IsDigit(low[prefix.Length])) { reserved = true; break; }
                    }

                if (reserved)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                        $"{ownerName} ({place}): стейт анимации визуала «{st}» — из зарезервированной схемы ядра " +
                        "(attack0…, death0…, idle0…, idleReady, cast, casting, hit, walk, building, construction); ядро спутает его со своим.",
                        "Презентация_Поля_Проект §4.5 (Р27)", target));
            }
        }

        /// <summary>Р29: ассет визуала щита обязан быть бессрочным визуалом — без значка и без геймплея.</summary>
        static void ValidateShieldVisual(List<InterflowIssue> issues, CompositeSkill skill, string ownerName)
        {
            if (skill.shield == null || skill.shield.visualEffector == null) return;

            Effector e = skill.shield.visualEffector;
            var wrong = new List<string>();

            if (!e.permanent) wrong.Add("оно не бессрочное (permanent выключен) — погаснет раньше щита");
            if (e.icon != null) wrong.Add("у него есть значок — это состояние, а не визуал");
            if (e.passiveEffectsOn) wrong.Add("у него включены пассивные изменения статов");
            if (e.stuns || e.mutes || e.disarms || e.blinds) wrong.Add("оно накладывает контроль");
            if (e.damageAmount > 0) wrong.Add("оно наносит периодический урон");

            if (wrong.Count == 0) return;

            issues.Add(new InterflowIssue(InterflowIssueSeverity.Error,
                $"{ownerName} (визуал щита): состояние «{e.name}» не годится в визуал — " + string.Join("; ", wrong) + ".",
                "Презентация_Поля_Проект §4.5 (Р29)", skill));
        }

        /// <summary>
        /// Наборы визуала у ПАССИВОК и старых классов плюс Р30 (одинаковость статуса по категориям).
        /// Отдельным проходом, а не внутри ValidateSkill: у пассивки своего «прогона умения» нет.
        /// </summary>
        static void ValidateEventPresentations(List<InterflowIssue> issues)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CompositePassive"))
            {
                var p = AssetDatabase.LoadAssetAtPath<CompositePassive>(AssetDatabase.GUIDToAssetPath(guid));
                if (p == null) continue;

                string owner = "Пассивка «" + p.name + "»";

                if (p.onDamaged != null)
                    ValidateEventPresentation(issues, p.onDamaged.presentation, owner,
                                              "визуал реакции «носителя ударили»", p.onDamaged.enabled, p);
                if (p.onDeath != null)
                {
                    ValidateEventPresentation(issues, p.onDeath.presentation, owner,
                                              "визуал реакции «носитель погиб»", p.onDeath.enabled, p);
                    ValidateEventPresentation(issues, p.onDeath.healPresentation, owner,
                                              "визуал лечения союзников при гибели", p.onDeath.enabled, p);

                    // Р28. К моменту гибели носителя уже нет: кастер уедет нулём, визуал у носителя не сыграет.
                    if (p.onDeath.presentation != null && p.onDeath.presentation.carrierVFX != null)
                        issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                            $"{owner} (визуал реакции «носитель погиб»): задан визуал У НОСИТЕЛЯ, но носителя " +
                            "к этому моменту уже нет — сыграет только визуал В ТОЧКЕ гибели.",
                            "Презентация_Поля_Проект §4.5 (Р28)", p));
                }
                if (p.onKill != null)
                    ValidateEventPresentation(issues, p.onKill.presentation, owner,
                                              "визуал реакции «носитель добил»", p.onKill.enabled, p);
                if (p.onHpBelow != null)
                    ValidateEventPresentation(issues, p.onHpBelow.presentation, owner,
                                              "визуал реакции «здоровье ниже доли»", p.onHpBelow.enabled, p);
                if (p.onHit != null)
                    ValidateEventPresentation(issues, p.onHit.presentation, owner,
                                              "визуал реакции «носитель попал по цели»", p.onHit.enabled, p);
                if (p.onAttackStart != null)
                    ValidateEventPresentation(issues, p.onAttackStart.presentation, owner,
                                              "визуал реакции «носитель начал атаку»", p.onAttackStart.enabled, p);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:ChargeAttack"))
            {
                var a = AssetDatabase.LoadAssetAtPath<ChargeAttack>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null)
                    ValidateEventPresentation(issues, a.impactPresentation, "Умение «" + a.name + "»",
                                              "визуал удара с разгона", true, a);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:LifestealPassive"))
            {
                var a = AssetDatabase.LoadAssetAtPath<LifestealPassive>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null)
                    ValidateEventPresentation(issues, a.healPresentation, "Умение «" + a.name + "»",
                                              "визуал вампиризма", true, a);
            }

            ValidateEffectorCategoryVisuals(issues);
        }

        /// <summary>
        /// Р30: одинаковый статус показан по-разному. Общей точки наложения у замедлений и периодики
        /// нет (её имеют только оглушение, немота, безоружие и слепота), поэтому одинаковость держится
        /// правилом контента «один ассет на один смысл» плюс этой проверкой — ноль строк рантайма.
        /// </summary>
        static void ValidateEffectorCategoryVisuals(List<InterflowIssue> issues)
        {
            var byCategory = new Dictionary<EffectorCategory, List<Effector>>();

            foreach (string guid in AssetDatabase.FindAssets("t:Effector"))
            {
                var e = AssetDatabase.LoadAssetAtPath<Effector>(AssetDatabase.GUIDToAssetPath(guid));
                if (e == null || e.category == EffectorCategory.None) continue;

                if (!byCategory.TryGetValue(e.category, out var list)) byCategory[e.category] = list = new List<Effector>();
                list.Add(e);
            }

            foreach (var pair in byCategory)
            {
                var list = pair.Value;
                if (list.Count < 2) continue;

                bool sameVfx = list.All(x => x.VFX == list[0].VFX);
                bool sameIcon = list.All(x => x.icon == list[0].icon);
                if (sameVfx && sameIcon) continue;

                string names = string.Join(", ", list.Select(x => "«" + x.name + "»"));
                foreach (var e in list)
                    issues.Add(new InterflowIssue(InterflowIssueSeverity.Warning,
                        $"Состояние «{e.name}»: у категории «{pair.Key}» несколько ассетов с РАЗНЫМ визуалом или значком " +
                        $"({names}) — одинаковый статус будет показан по-разному. Правило контента: один ассет на один смысл.",
                        "Презентация_Поля_Проект §4.5 (Р30)", e));
            }
        }
    }
}
