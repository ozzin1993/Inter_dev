using UnityEngine;

namespace StrategyCore
{
    // ===== КОНСТРУКТОР УМЕНИЯ: БЛОК 21 — УСЛОВИЯ КАСТА (правило 22 — партиал по фиче) ==
    // Одно место, где решается «можно ли вообще кастовать»: облик кастера, есть ли кому достаться
    // и насколько кастер ранен. Поля блока — SkillCastConditionsBlock (CompositeSkill.BlockTypes.cs).
    //
    // Читается в ДВУХ точках, обе — штатные (правило 2, правило 5):
    //   • гейт каста Unit.CheckAbilityItemRequirements — с текстом отказа владельцу, тем же путём,
    //     что «Шкала не готова»; повтор в момент удара (Unit.State.cs) приходит сам, ничего
    //     добавлять не нужно — условие, отпавшее за время замаха, снимает каст без траты маны;
    //   • CompositeSkill.AutoCastReady — МОЛЧА, до поиска цели (образец — гейт маны авто-умения).
    //
    // Третий читатель — оверлей кнопки (UIManager.Abilities). Он живёт на КЛИЕНТЕ, поэтому зовёт
    // тот же метод с параметром skipShape: облик клиенту не реплицируется (решение Artsiom 16.09),
    // и на чистом клиенте кнопка серая только по «есть цель в области» и «здоровье ниже», а отказ
    // по облику приходит текстом с сервера. Второго набора правил при этом нет — параметр один.
    public partial class CompositeSkill
    {
        /// <summary>
        /// Проходят ли условия каста блока 21.
        /// </summary>
        /// <param name="refusal">Короткий текст отказа для игрока; null — условия выполнены.</param>
        /// <param name="skipShape">
        /// Пропустить условия по облику. Истина ТОЛЬКО у оверлея кнопки на чистом клиенте:
        /// признак облика туда не приходит, и проверять его там нечем. Сервер проверяет всегда.
        /// </param>
        public bool CastConditionsMet(Unit caster, int castingPlayer, int level, out string refusal,
                                      bool skipShape = false)
        {
            refusal = null;

            if (castConditions == null || !castConditions.enabled) return true;
            if (caster == null) return true;

            if (!skipShape && !SkillCastConditionsBlock.ShapeAllowed(caster.polymorphed, caster.polymorphShape,
                                                                     castConditions.requireOriginalForm,
                                                                     castConditions.requiredShape))
            {
                refusal = castConditions.requireOriginalForm
                    ? "Нужен исходный облик"
                    : "Нужен облик: " + ShapeName(castConditions.requiredShape);
                if (InterflowDebug.FullOn) LogCastConditionsRefused(caster, refusal, !skipShape);
                return false;
            }

            if (castConditions.requireAreaTarget && !HasAreaTarget(caster, castingPlayer, level))
            {
                refusal = "Нет цели рядом";
                if (InterflowDebug.FullOn) LogCastConditionsRefused(caster, refusal, !skipShape);
                return false;
            }

            if (!SkillCastConditionsBlock.HpAllowed(caster.health, caster.maxHealth, castConditions.casterHpBelow))
            {
                refusal = "Здоровье не ниже " + Mathf.RoundToInt(castConditions.casterHpBelow * 100f) + "%";
                if (InterflowDebug.FullOn) LogCastConditionsRefused(caster, refusal, !skipShape);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Есть ли в области (или конусе) умения хотя бы одна цель. Считается ТОЙ ЖЕ выборкой,
        /// что и набор целей каста (CompositeSkill.Targets.GatherAroundCaster) — копии выборки нет,
        /// иначе условие и каст разъехались бы.
        ///
        /// В остальных режимах цели условие не читается: там цель и так ищется до каста
        /// («умный выбор») либо набор задан режимом («на себя», «вся команда»). Валидатор
        /// предупреждает об этом правилом Р3.
        ///
        /// Конус считается по ТЕКУЩЕМУ направлению взгляда: доворота ради проверки нет —
        /// у каста с кнопки цели и точки ещё не существует.
        /// </summary>
        bool HasAreaTarget(Unit caster, int castingPlayer, int level)
        {
            if (targetMode != SkillTargetMode.AreaAroundSelf && targetMode != SkillTargetMode.Cone) return true;

            return GatherAroundCaster(caster, castingPlayer, level, null);
        }

        /// <summary>Имя требуемого облика для текста отказа: отображаемое имя заготовки, иначе имя ассета.</summary>
        static string ShapeName(Unit shape)
        {
            if (shape == null) return "не задан";

            return string.IsNullOrEmpty(shape.unitName) ? shape.name : shape.unitName;
        }
    }
}
