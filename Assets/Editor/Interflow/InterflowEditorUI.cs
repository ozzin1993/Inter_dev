using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // ============================= INTERFLOW EDITOR — ОБЩИЕ UI-ХЕЛПЕРЫ (фаза E4) ==
    // Единый источник общей логики вкладок Interflow Editor (правило 5): группировка полей по [Header]-блокам
    // и создание папок. Используют вкладки «Юниты», «Умения и эффекторы», «Справочники» — без дублирования.
    public static class InterflowEditorUI
    {
        public const string DEFAULT_GROUP = "Основные";

        // Разбивает видимые поля SerializedObject на сворачиваемые фолды по [Header] (границы — рефлексией по типу
        // цели, не хардкод). targetType берём из самого объекта (партиалы/подклассы подхватываются). Привязка (Undo/
        // сохранение) — на возвращаемый контейнер.
        public static VisualElement BuildGroupedFields(SerializedObject so, string defaultTitle = DEFAULT_GROUP)
        {
            var container = new VisualElement();
            Type targetType = so.targetObject != null ? so.targetObject.GetType() : null;
            VisualElement currentBody = null;

            // Ц1: русские подписи применяем ТОЛЬКО к умениям. Словарь собран под поля Ability,
            // а имена вроде icon / cost / duration / radius есть и у юнитов, фракций и справочников —
            // там та же подпись была бы неверной. Остальные вкладки работают как раньше.
            bool translate = so.targetObject is Ability;

            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;

                string header = HeaderFor(targetType, it.name);   // не null → начало нового блока
                if (currentBody == null || header != null)
                {
                    var foldout = new Foldout { text = header ?? defaultTitle, value = true, style = { marginBottom = 2 } };
                    currentBody = new VisualElement();
                    foldout.Add(currentBody);
                    container.Add(foldout);
                }

                // suppressDecorators=true для поля-носителя [Header]: заголовок уже вынесен в шапку фолда,
                // иначе PropertyField нарисовал бы его повторно.
                currentBody.Add(MakeField(it, translate ? FieldLabel(it) : null, header != null));
            }

            container.Bind(so);
            return container;
        }

        // ======================== РУССКИЕ ПОДПИСИ ПОЛЕЙ (Ц1) ========================

        // Unity делает подпись из ИМЕНИ сериализованного поля, поэтому в UI видны английские
        // «Target Mode» / «Target Strategy», хотя [Header] и [Tooltip] у нас русские (правило 4).
        // Переименовать сами поля НЕЛЬЗЯ — слетит сериализация всех уже собранных ассетов,
        // поэтому подпись задаётся вторым аргументом PropertyField. Словарь живёт в коде, а не в ассете,
        // потому что ключи — имена полей класса: в ассете они молча разъехались бы с кодом.
        static readonly Dictionary<string, string> FIELD_LABELS = new Dictionary<string, string>
        {
            // Цель (ось «Когда срабатывает» снесена блоком Б7, 2026-09-05)
            { "targetMode",               "Кого задевает" },
            { "buttonCast",               "Умение по кнопке (замок или герой)" },
            { "targetStrategy",           "Алгоритм выбора одной цели" },
            { "searchOrigin",             "Откуда считать «ближайшего»" },
            { "strategyUseCurrentHealth", "Мерить текущее ХП, не максимальное" },
            { "strategyHpThreshold",      "Порог ХП (доля от максимума)" },
            { "avoidTargetState",         "Избегать цель с состоянием" },
            { "avoidTargetEffector",      "Какое состояние искать (для варианта «указанное»)" },
            { "avoidNoFreeTarget",        "Если свободных целей нет" },
            // Селекторы: принадлежность и роли
            { "targetCategories",         "Селектор ролей (пусто — любые)" },
            { "targetPrefabs",            "Селектор заготовок (пусто — любые)" },
            { "targetFrontAngle",         "Цель только впереди: угол сектора, градусы (0 — выкл.)" },
            { "independentAreaTargets",   "Область набирается своим селектором" },
            { "areaSelector",             "Кого задевает область (свой селектор)" },
            { "maxTargets",               "Максимум целей (0 — без лимита)" },
            { "multiPick",                "Кого оставить при лимите" },
            { "includeSelf",              "Включать самого кастера" },
            { "coneAngle",                "Угол конуса, градусы" },
            { "directionMatters",         "Направление кастера важно (доворот к цели)" },
            { "radius",                   "Радиус" },
            { "castRange",                "Дальность каста" },
            { "unitSelector",             "Кто вообще может быть целью" },
            // Доставка
            { "delivery",                 "Доставка" },
            { "projectilePrefab",         "Префаб снаряда" },
            { "projectileFollowsTarget",  "Снаряд самонаводится" },
            { "projectileDirectAttack",   "Снаряд считается прямой атакой" },
            // Реакция 5 пассивки. ВНИМАНИЕ: вложенный блок onHit рисуется штатным PropertyField целиком,
            // поэтому подписи его полей берёт сам Unity — эти строки сработают только там, где поле
            // попадает в LabelOf (блоки с массивами по уровням). Оставлены как единый словарь подписей.
            { "procSkill",                "Прок исполняет умение" },
            { "procAtProjectileImpact",   "Срабатывать при прилёте снаряда, а не при ударе" },
            // Шкала каста и презентация
            { "castTime",                 "Время каста, с" },
            { "cooldown",                 "Откат, с" },
            // «manaCost» снят вместе с полем (Б5, 2026-09-04): цены в мане у умений нет.
            { "duration",                 "Длительность, с" },
            // [Interflow 2026-09-17] Десять плоских полей презентации сведены в набор EventPresentation.
            // Сам набор рисуется штатным PropertyField целиком, поэтому подписи его полей берёт Unity
            // из [Tooltip]; строки ниже нужны там, где имя поля попадает в LabelOf.
            // ВНИМАНИЕ: «presentation» — имя поля не только у самого умения, но и у блоков-хозяев.
            // Подпись «Визуал каста» верна ТОЛЬКО для набора самого умения, у блоков подпись берётся
            // по паре «блок + поле» из FIELD_LABELS_BY_PARENT (решение Artsiom 34 от 17.09.2026);
            // эта строка осталась запасным вариантом для поля на самом ассете.
            { "presentation",             "Визуал каста" },
            { "animationState",           "Стейт анимации" },
            { "socket",                   "Точка на модели носителя" },
            { "localOffset",              "Смещение от точки, м" },
            { "carrierVFX",               "Визуал у носителя" },
            { "carrierVfxLifetime",       "Время жизни визуала у носителя, с" },
            { "pointVFX",                 "Визуал в точке события" },
            { "pointVfxLifetime",         "Время жизни визуала в точке, с" },
            { "carrierSound",             "Звук у носителя" },
            { "pointSound",               "Звук в точке события" },
            { "soundVolume",              "Громкость звуков, 0..1" },
            { "visualEffector",           "Состояние-визуал на время щита" },
            { "onAttackStart",            "Реакция 6 — носитель начал атаку" },
            { "onAllyDeath",              "Реакция 7 — погиб союзник" },
            { "healPresentation",         "Визуал лечения союзника" },
            // Реакция 2: второй радиус (решение Artsiom 36 от 17.09.2026)
            { "allyHealRadius",           "Радиус лечения союзников (пусто — как радиус урона)" },
            // Реакция 3: прибавка к урону за убийство (решения Artsiom 37 и 38 от 17.09.2026)
            { "damagePercentPerKill",     "Прибавка к урону за убийство (доля, складывается)" },
            { "damagePerKillMaxStacks",   "Предел: сколько убийств в счёт (0 — без предела)" },
            // Реакция 7: погиб союзник (решение Artsiom 40 от 17.09.2026)
            { "victimPrefabs",            "Чьи смерти считаются (пусто — любые)" },
            { "victimSelector",           "Кого считать союзником" },
            // «healFlat» и «healPercentOfMaxHp» сюда НЕ кладутся: первое имя занято блоком 14 умения
            // (secondary.healFlat), и общая подпись уехала бы в чужую карточку. Оба — в FIELD_LABELS_BY_PARENT.

            // Базовые поля Ability. Перевод сделан ПО АНГЛИЙСКИМ [Tooltip] самого ядра,
            // а не по догадке о смысле имени поля. Сами тултипы не трогаем — это была бы правка ядра.
            { "id",                       "Идентификатор (id)" },
            { "abilityName",              "Название по уровням" },
            { "description",              "Описание по уровням" },
            { "icon",                     "Иконка по уровням" },
            { "slotNumber",               "Ячейка в панели (−1 — по порядку)" },
            { "isItem",                   "Это предмет, а не умение (предметы вне скоупа игры)" },
            { "useUponPickUp",            "Применить сразу при подборе" },
            { "dropOnDeath",              "Выпадает при смерти носителя" },
            { "charges",                  "Заряды (0 — без зарядов)" },
            { "maxLevels",                "Максимум уровней" },
            { "heroLevelable",            "Уровень качается прокачкой юнита" },
            { "dontTurn",                 "Не поворачивать кастера к цели" },
            // «continuous», «interruptible», «requiresCastingUnit» и «manaCostPerSecond» сняты вместе с полями
            // (Б6, 2026-09-04): переключателей и умений-каналов в игре нет.
            { "cost",                     "Цена в ресурсах" },
            { "requiredTech",             "Требуемые технологии по уровням" },

            // Собственные поля производственных типов (вкладка «Производство»), тоже по тултипам ядра.
            { "building",                 "Что строится (по уровням)" },
            { "unitToTrain",              "Кого обучать" },
            { "unitCount",                "Сколько юнитов появится" },
            { "costForSingleUnit",        "Цена указана за одного юнита" },
            { "unlockTech",               "Что открывает: уровень 1 → первый элемент" },
            { "upgradeUnit",              "Во что апгрейдится здание" },
            { "upgradeTime",              "Время апгрейда, с" },
            { "transformUnit",            "Чей вид принимает кастер" },
            { "transformTime",            "Длительность превращения, с" },
            { "transformSound",           "Звук превращения" },
            { "transformVFX",             "Визуал превращения" },
            { "passiveEffects",           "Пассивные эффекты на время превращения" },
            // Блок 20 — шкала (CompositeSkill.SkillGaugeBlock)
            { "requireFull",              "Требовать полную шкалу" },
            { "spendAll",                 "Потратить всю в ноль" },
            // Блок 21 — условия каста (CompositeSkill.SkillCastConditionsBlock)
            { "requireOriginalForm",      "Только в исходном облике" },
            { "requiredShape",            "Нужен облик заготовки" },
            { "requireAreaTarget",        "Нужна цель в области" },
            { "casterHpBelow",            "Здоровье кастера ниже доли" },
            // Блок 9 пассивки — шкала (PassiveGaugeBlock)
            { "maxGauge",                 "Максимальная шкала" },
            { "hitGain",                  "Прирост за попадание" },
            { "killGain",                 "Прирост за убийство" },
            { "tickGain",                 "Прирост в секунду (в бою)" },
            { "combatMemory",             "Память боя, с" },
            { "onlyRanged",               "Только дальним юнитам" },
            { "notWhileMorphed",          "Не копить в облике" },
            // Семья «здоровье носителя и иммунитеты» (решения Artsiom 41–46 от 17.09.2026).
            // Имена уникальны по проекту, поэтому хватает одиночного ключа; исключение —
            // «visualEffector», оно занято визуалом щита и разведено по паре «блок + поле» ниже.
            { "carrierCondition",             "Когда блок работает" },
            { "carrierHpBelow",               "Порог: здоровье носителя ниже доли" },
            { "healCondition",                "Вампиризм: когда работает" },
            { "healCarrierHpBelow",           "Вампиризм: порог здоровья носителя" },
            { "healUpgradeTechnology",        "Вампиризм: технология улучшения" },
            { "healFromDamagePercentUpgraded","Вампиризм: улучшенная доля возврата" },
            // Блок 10 — характеристики от нехватки здоровья (PassiveMissingHpStatsBlock)
            { "missingHpStats",               "Блок 10 — характеристики от нехватки здоровья" },
            { "damageByMissingHp",            "Кривая: нехватка здоровья → множитель урона" },
            { "armorByMissingHp",             "Кривая: нехватка здоровья → прибавка брони числом" },
            { "attackSpeedByMissingHp",       "Кривая: нехватка здоровья → прибавка скорости атаки долей" },
            { "missingHpStep",                "Шаг ступени по нехватке здоровья (0 — плавно)" },
            { "presentationMissingHpThreshold","Порог показа длящегося визуала" },
            // Блок 11 — рассечение (PassiveCleaveBlock, решения Artsiom 59–62 от 18.09.2026).
            // Имена уникальны по проекту, поэтому хватает одиночного ключа; исключения —
            // «radius», «selector» и «presentation»: они заняты полями умения и разведены
            // по паре «блок + поле» ниже.
            { "cleave",                       "Блок 11 — рассечение" },
            { "centerOnCaster",               "Центр круга — носитель, а не цель удара" },
            { "damageFraction",               "Доля от урона основного удара" },
            { "requiredCasterEffector",       "Требуемое состояние НА НОСИТЕЛЕ (пусто — условия нет)" },
            { "upgradeTechnology",            "Рассечение: технология улучшения" },
            { "upgradedRadius",               "Рассечение: улучшенный радиус (полным числом)" },
            { "upgradedDamageFraction",       "Рассечение: улучшенная доля урона (полным числом)" },

            // Организация контента (не игровое поле)
            { "editorFactions",           "Фракции (ручная метка)" }
        };

        // Подписи по паре «БЛОК + ПОЛЕ» (решение Artsiom 34 от 17.09.2026). Одного имени поля не хватает:
        // «presentation» лежит и у самого умения, и у двенадцати блоков-хозяев, и подпись «Визуал каста»
        // расползалась по карточкам всех блоков. Переименовать сами поля нельзя — значения уже лежат
        // в ассетах, а смена имени поля рвёт сериализацию. Ключ — «имя блока.имя поля», ровно так, как
        // это звучит в propertyPath сериализованного свойства.
        //
        // Ключ отсюда ПЕРЕБИВАЕТ одиночное имя; нет пары — работает запасной словарь FIELD_LABELS,
        // поэтому ни одна существующая подпись не теряется.
        static readonly Dictionary<string, string> FIELD_LABELS_BY_PARENT = new Dictionary<string, string>
        {
            // Умение (CompositeSkill): блоки 7, 14, 22
            { "heal.presentation",              "Визуал лечения цели" },
            { "secondary.castPresentation",     "Визуал блока: один раз на кастере" },
            { "secondary.presentation",         "Визуал попадания по вторичной цели" },
            { "projectileImpact.presentation",  "Визуал прилёта снаряда" },

            // Пассивка (CompositePassive): реакции 1–6. У реакции 2 наборов два.
            { "onDamaged.presentation",         "Визуал ответа на удар" },
            { "onDeath.presentation",           "Визуал гибели — в точке гибели" },
            { "onDeath.healPresentation",       "Визуал лечения союзника при гибели" },
            { "onKill.presentation",            "Визуал добивания" },
            { "onHpBelow.presentation",         "Визуал падения здоровья ниже порога" },
            { "onHit.presentation",             "Визуал попадания по цели" },
            { "onAttackStart.presentation",     "Визуал начала атаки" },
            { "onAllyDeath.presentation",       "Визуал гибели союзника — на носителе" },

            // У реакции 7 свои «радиус» и «лечение»: одиночные имена заняты полями умения
            // («Радиус» у области каста) и полями реакции 2, и без пары подпись уехала бы не туда.
            { "onAllyDeath.radius",             "Радиус засчёта гибели (0 — вся карта)" },
            { "onAllyDeath.healFlat",           "Лечение носителю числом" },
            { "onAllyDeath.healPercentOfMaxHp", "Лечение носителю долей его максимума" },

            // Семья «здоровье носителя»: длящийся визуал у трёх хозяев (решение Artsiom 46).
            // Одиночное имя «visualEffector» занято визуалом щита умения — без пары подпись
            // уехала бы в карточки блоков пассивки.
            { "controlImmunity.visualEffector", "Визуал иммунитета к контролю (пока условие выполнено)" },
            { "resistances.visualEffector",     "Визуал сопротивлений (пока условие выполнено)" },
            { "missingHpStats.visualEffector",  "Визуал ярости (пока нехватка выше порога)" },

            // Блок 11 «рассечение»: три имени заняты полями самого умения, и без пары подпись
            // уехала бы не туда — «presentation» дала бы «Визуал каста», «radius» — радиус области каста.
            { "cleave.presentation",            "Визуал рассечения (один раз за срабатывание)" },
            { "cleave.radius",                  "Радиус рассечения, м" },
            { "cleave.selector",                "Кого задевает рассечение" }
        };

        /// <summary>Русская подпись поля ПО ИМЕНИ; null — перевода нет, пусть Unity рисует свою.</summary>
        /// <remarks>Запасной вариант: родителя здесь не видно, см. перегрузку со свойством.</remarks>
        public static string FieldLabel(string fieldName)
            => fieldName != null && FIELD_LABELS.TryGetValue(fieldName, out var s) ? s : null;

        /// <summary>
        /// Русская подпись поля С УЧЁТОМ БЛОКА-РОДИТЕЛЯ (решение Artsiom 34 от 17.09.2026):
        /// сперва пара «блок + поле», потом одиночное имя. null — перевода нет, пусть Unity рисует свою.
        /// </summary>
        public static string FieldLabel(SerializedProperty prop)
        {
            if (prop == null) return null;

            string parent = ParentFieldName(prop.propertyPath);
            if (parent != null && FIELD_LABELS_BY_PARENT.TryGetValue(parent + "." + prop.name, out var byParent))
                return byParent;

            return FieldLabel(prop.name);
        }

        /// <summary>
        /// Имя блока-родителя из пути свойства: «secondary.presentation» → «secondary».
        /// Служебные звенья массива («Array», «data[i]») пропускаются — родителем записи списка
        /// считается сам список. Поле лежит на самом ассете (точки в пути нет) — родителя нет, null.
        /// </summary>
        static string ParentFieldName(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;

            string[] parts = propertyPath.Split('.');
            for (int i = parts.Length - 2; i >= 0; i--)
            {
                if (parts[i] == "Array" || parts[i].StartsWith("data[")) continue;
                return parts[i];
            }
            return null;
        }

        /// <summary>
        /// Поле с явной подписью и необязательным подавлением декораторов ([Header]/[Space]).
        /// label = null — подпись по умолчанию (из имени поля).
        /// У умений (Ability) числовые массивы «по уровням» рисуются НЕ списком Unity, а по правилу блока Б8
        /// (целевая модель §9, решение Artsiom 2026-09-06): одно поле «базовое» (элемент 0) плюс свёрнутый блок
        /// «по уровням» (элементы 1..N). Вложенные блоки конструктора и их списки записей раскрываются так же.
        /// </summary>
        public static VisualElement MakeField(SerializedProperty prop, string label, bool suppressDecorators)
        {
            if (prop.serializedObject.targetObject is Ability)
            {
                if (IsLeveledNumberArray(prop)) return MakeLeveledArray(prop.Copy(), label);
                if (IsStructWithLeveledArrays(prop)) return MakeStructFields(prop.Copy(), label);
                if (IsStructListWithLeveledArrays(prop)) return MakeStructList(prop.Copy(), label);
            }

            var pf = label == null ? new PropertyField(prop.Copy()) : new PropertyField(prop.Copy(), label);
            if (suppressDecorators)
            {
                void OnGeo(GeometryChangedEvent e)
                {
                    pf.UnregisterCallback<GeometryChangedEvent>(OnGeo);
                    var dec = pf.Q(className: "unity-decorator-drawers-container");
                    if (dec != null) dec.style.display = DisplayStyle.None;
                }
                pf.RegisterCallback<GeometryChangedEvent>(OnGeo);
            }
            return pf;
        }

        // ======================== ЗНАЧЕНИЯ ПО УРОВНЯМ: «БАЗОВОЕ + БЛОК» (Б8) ========================
        // Данные остаются массивами (решение Artsiom 2026-09-06): элемент 0 — базовое значение, остальные — блок по
        // уровням; выборка в игре — InterflowAbility.LevelValue (нет строки — последняя заполненная). Здесь только вид.

        static readonly Color COL_LEVELS_DIM = new Color(0.62f, 0.62f, 0.62f);

        /// <summary>Числовой массив по уровням: float[] или int[].</summary>
        static bool IsLeveledNumberArray(SerializedProperty prop)
            => prop.isArray && prop.propertyType == SerializedPropertyType.Generic
               && (prop.arrayElementType == "float" || prop.arrayElementType == "int");

        /// <summary>Вложенный блок (не массив), внутри которого есть хотя бы один числовой массив по уровням.</summary>
        static bool IsStructWithLeveledArrays(SerializedProperty prop)
            => !prop.isArray && prop.propertyType == SerializedPropertyType.Generic && ContainsLeveledArray(prop);

        /// <summary>Список записей (массив блоков), у элементов которого есть числовые массивы по уровням.</summary>
        static bool IsStructListWithLeveledArrays(SerializedProperty prop)
        {
            if (!prop.isArray || prop.propertyType != SerializedPropertyType.Generic) return false;
            if (prop.arrayElementType == "float" || prop.arrayElementType == "int" || prop.arrayElementType == "string") return false;
            if (prop.arrayElementType.StartsWith("PPtr")) return false;   // ссылки на ассеты — обычный список
            if (prop.arraySize == 0) return false;                        // пустой — нечего раскрывать, обычный список
            return ContainsLeveledArray(prop.GetArrayElementAtIndex(0));
        }

        static bool ContainsLeveledArray(SerializedProperty parent)
        {
            foreach (var child in Children(parent))
            {
                if (IsLeveledNumberArray(child)) return true;
                if (!child.isArray && child.propertyType == SerializedPropertyType.Generic && ContainsLeveledArray(child)) return true;
            }
            return false;
        }

        // Видимые дети блока (без захода в массивы: у них свои дети «size» и элементы).
        static IEnumerable<SerializedProperty> Children(SerializedProperty parent)
        {
            var end = parent.GetEndProperty();
            var child = parent.Copy();
            if (!child.NextVisible(true)) yield break;
            while (!SerializedProperty.EqualContents(child, end))
            {
                yield return child.Copy();
                if (!child.NextVisible(false)) break;
            }
        }

        // Подпись берётся по паре «блок + поле» (решение 34): именно здесь набор внутри карточки блока
        // получает подпись СВОЕГО хозяина, а не общую «Визуал каста».
        static string LabelOf(SerializedProperty prop, string label)
            => label ?? FieldLabel(prop) ?? ObjectNames.NicifyVariableName(prop.name);

        /// <summary>
        /// Числовой массив по уровням: элемент 0 — поле «базовое», элементы 1..N — свёрнутый блок «по уровням»
        /// с кнопками добавить/убрать уровень. Пустой массив — кнопка «задать базовое».
        /// </summary>
        static VisualElement MakeLeveledArray(SerializedProperty prop, string label)
        {
            var root = new VisualElement { tooltip = prop.tooltip };
            string title = LabelOf(prop, label);
            SerializedObject so = prop.serializedObject;
            string path = prop.propertyPath;

            void Rebuild()
            {
                root.Clear();
                var arr = so.FindProperty(path);
                if (arr == null) return;

                if (arr.arraySize == 0)
                {
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                    row.Add(new Label(title) { style = { minWidth = 150, color = COL_LEVELS_DIM } });
                    row.Add(new Button(() => { arr.arraySize = 1; so.ApplyModifiedProperties(); Rebuild(); })
                        { text = "задать базовое", tooltip = "Массив пуст — умение работает как без этого параметра (0). Нажми, чтобы задать базовое значение." });
                    root.Add(row);
                    return;
                }

                // Базовое значение — элемент 0, под подписью самого поля.
                root.Add(new PropertyField(arr.GetArrayElementAtIndex(0), title) { tooltip = prop.tooltip });

                // Блок по уровням — элементы 1..N. Свёрнут, пока строк нет.
                int extra = arr.arraySize - 1;
                var fold = new Foldout
                {
                    text = extra > 0 ? $"По уровням: {extra}" : "По уровням: нет",
                    value = extra > 0,
                    style = { marginLeft = 12, marginBottom = 2 },
                    tooltip = "Значения для уровней 2, 3, … У умений и пассивок юнитов: нет строки для нужного уровня — берётся " +
                              "последняя заполненная, никогда ноль; блок пуст — умение всегда работает на базовом значении. " +
                              "Процессы (обучение, исследование, стройка) читают строку ровно своего уровня."
                };
                for (int i = 1; i < arr.arraySize; i++)
                    fold.Add(new PropertyField(arr.GetArrayElementAtIndex(i), $"Уровень {i + 1}"));

                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
                buttons.Add(new Button(() =>
                {
                    int n = arr.arraySize;
                    arr.arraySize = n + 1;
                    // Новая строка повторяет предыдущую — правило «последняя заполненная» делает это ожидаемым стартом.
                    var last = arr.GetArrayElementAtIndex(n - 1);
                    var added = arr.GetArrayElementAtIndex(n);
                    if (added.propertyType == SerializedPropertyType.Float) added.floatValue = last.floatValue;
                    else if (added.propertyType == SerializedPropertyType.Integer) added.intValue = last.intValue;
                    so.ApplyModifiedProperties();
                    Rebuild();
                }) { text = "+ уровень" });
                if (extra > 0)
                    buttons.Add(new Button(() => { arr.arraySize = arr.arraySize - 1; so.ApplyModifiedProperties(); Rebuild(); })
                        { text = "− последний" });
                fold.Add(buttons);
                root.Add(fold);

                root.Bind(so);
            }

            Rebuild();
            return root;
        }

        /// <summary>
        /// Вложенный блок: дети рисуются по тому же правилу (массивы по уровням — «базовое + блок»).
        /// label пустая — дети кладутся прямо в контейнер (блок конструктора уже сидит в своём фолде); иначе — свой фолд.
        /// </summary>
        static VisualElement MakeStructFields(SerializedProperty prop, string label)
        {
            VisualElement body;
            VisualElement result;
            if (string.IsNullOrEmpty(label))
            {
                body = new VisualElement();
                result = body;
            }
            else
            {
                var fold = new Foldout { text = LabelOf(prop, label), value = true, tooltip = prop.tooltip };
                body = fold;
                result = fold;
            }

            foreach (var child in Children(prop))
            {
                if (IsLeveledNumberArray(child)) body.Add(MakeLeveledArray(child, null));
                else if (IsStructWithLeveledArrays(child)) body.Add(MakeStructFields(child, LabelOf(child, null)));
                else if (IsStructListWithLeveledArrays(child)) body.Add(MakeStructList(child, LabelOf(child, null)));
                else body.Add(new PropertyField(child, LabelOf(child, null)));
            }
            return result;
        }

        /// <summary>
        /// Список записей (например записи урона): каждая запись — фолд с детьми по тому же правилу,
        /// внизу кнопки «добавить запись» / «убрать последнюю».
        /// </summary>
        static VisualElement MakeStructList(SerializedProperty prop, string label)
        {
            var fold = new Foldout { text = LabelOf(prop, label), value = true, tooltip = prop.tooltip };
            SerializedObject so = prop.serializedObject;
            string path = prop.propertyPath;

            void Rebuild()
            {
                fold.Clear();
                var arr = so.FindProperty(path);
                if (arr == null) return;

                for (int i = 0; i < arr.arraySize; i++)
                    fold.Add(MakeStructFields(arr.GetArrayElementAtIndex(i), $"Запись {i + 1}"));

                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
                buttons.Add(new Button(() => { arr.arraySize = arr.arraySize + 1; so.ApplyModifiedProperties(); Rebuild(); })
                    { text = "+ запись" });
                if (arr.arraySize > 0)
                    buttons.Add(new Button(() => { arr.arraySize = arr.arraySize - 1; so.ApplyModifiedProperties(); Rebuild(); })
                        { text = "− последняя" });
                fold.Add(buttons);

                fold.Bind(so);
            }

            Rebuild();
            return fold;
        }

        // Текст [Header] поля targetType по имени сериализованного поля; null — если заголовка нет.
        static string HeaderFor(Type targetType, string fieldName)
        {
            if (targetType == null) return null;
            var f = GetSerializedField(targetType, fieldName);
            if (f == null) return null;
            var headers = f.GetCustomAttributes(typeof(HeaderAttribute), true);
            return headers.Length > 0 ? ((HeaderAttribute)headers[0]).header : null;
        }

        // Поиск поля по имени вверх по иерархии (partial-поля и поля базовых классов — на одном/родительском типе).
        static FieldInfo GetSerializedField(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject); t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        // Русская метка значения enum по [InspectorName]; иначе — имя значения.
        public static string EnumLabel(Type enumType, string valueName)
        {
            var members = enumType.GetMember(valueName);
            if (members.Length > 0)
            {
                var attr = members[0].GetCustomAttribute<InspectorNameAttribute>();
                if (attr != null) return attr.displayName;
            }
            return valueName;
        }

        // Создать папку (по частям), если её нет.
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") return;
            string cur = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        /// <summary>
        /// Наименьший свободный id среди ассетов фильтра. Детерминированно: одинаковый вход — одинаковый выход.
        /// Общий для всех вкладок (правило 5): раньше жил приватной копией в «Умениях и эффекторах»,
        /// теперь его же зовут «Пассивные умения» и «Производство».
        /// </summary>
        /// <param name="filter">Фильтр AssetDatabase, например «t:Ability» или «t:Effector».</param>
        /// <param name="idOf">Как достать id из найденного ассета.</param>
        public static int NextFreeId(string filter, Func<UnityEngine.Object, int> idOf)
        {
            var used = new HashSet<int>();
            foreach (var g in AssetDatabase.FindAssets(filter))
            {
                var o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(g));
                if (o != null) used.Add(idOf(o));
            }

            int id = 1;
            while (used.Contains(id)) id++;

            return id;
        }
    }
}
