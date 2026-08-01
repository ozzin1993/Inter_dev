using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Умение героя орков «Выброс Скверны» (Active). Кастер — герой (heroUnit), каст из таблицы умений героя
    // (CastHeroAbilityById) либо авто (AutoAbilityUser). Герой теряет selfCost ХП и детонирует ближайшего к
    // вражеской точке союзного орка (фильтр по unitCategory): взрыв «Скверны» по врагам + физ. по своим оркам;
    // сам детонатор гибнет (D4) и штатно запускает «Жатву душ». Серверо-авторитетно (правило 6).
    // Ядро StrategyCore не трогаем (правило 1). Числа — в .asset (правило 3). Взрыв — общий SkvernaExplosion (правило 5).
    [CreateAssetMenu(fileName = "CorruptionBurstActive", menuName = "StrategyCore/Abilities/CorruptionBurstActive")]
    public class CorruptionBurstActive : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Active; } }

        [Header("Выброс Скверны — стоимость и цель")]
        [Tooltip("Сколько ХП теряет герой при касте (по ТЗ 60). При недостатке ХП герой может погибнуть — поведение по умолчанию.")]
        public float selfCost = 60f;
        [Tooltip("Категории союзных юнитов, среди которых ищется жертва детонации (по unitCategory). Пусто — любые союзники.")]
        public Unit.UnitCategory[] targetCategories;

        [Header("Взрыв — по врагам")]
        [Tooltip("Урон по врагам (по ТЗ 70). Тип — «Скверна» (поле ниже): игнор ТИПА брони (множители = 1).")]
        public float enemyDamage = 70f;
        [Tooltip("Тип урона по врагам — DamageType «Скверна».")]
        public DamageType enemyDamageType;
        [Tooltip("Кого задевает взрыв как врагов (обычно Enemy + Unit + Ground/Water/Air).")]
        public UnitSelector enemySelector;

        [Header("Взрыв — по своим оркам")]
        [Tooltip("Урон по своим оркам вокруг (по ТЗ 25). Тип — физический (поле ниже).")]
        public float allyDamage = 25f;
        [Tooltip("Тип урона по своим оркам (физический).")]
        public DamageType allyDamageType;
        [Tooltip("Кого задевает взрыв как своих (обычно Ally/Own + Unit).")]
        public UnitSelector allySelector;

        // База Ability:
        //  radius[level]  — радиус взрыва (по ТЗ «2 клетки» → в юнитах Unity; задать в .asset);
        //  cooldown[level]— КД (по ТЗ 7с);
        //  unitSelector   — НЕ используется (враги/свои — enemy/allySelector; кандидаты жертвы — из MatchManager).

        public override void Use(Unit castingUnit, int castingPlayer, int level)
        {
            if (NetworkConnectionHandler.isClient) return;     // выбор жертвы/урон/смерть — только сервер (правило 6)
            MatchManager mm = MatchManager.instance;
            if (mm == null) { Debug.LogWarning("[CorruptionBurst] MatchManager.instance == null."); return; }
            if (castingUnit == null || castingUnit.dead) { Debug.LogWarning("[CorruptionBurst] нет живого кастера-героя."); return; }

            float rad = (radius != null && radius.Length > level) ? radius[level] : 0f;
            if (rad <= 0f) { Debug.LogWarning($"[CorruptionBurst] radius[{level}] ≤ 0 — взрыв не выполнен. Задай радиус в .asset."); return; }

            // Жертва: ближайший к вражеской точке живой союзник нужной категории (D2). Сам герой-кастер исключён.
            Vector2 enemyPoint = mm.AttackTarget(castingPlayer);
            Unit victim = PickNearestAllyToPoint(mm.GetCommandUnitsForPlayer(castingPlayer), enemyPoint, castingUnit);
            if (victim == null) { Debug.Log("[CorruptionBurst] нет подходящего союзника для детонации — каст без эффекта."); return; }

            // Стоимость ХП героя (штатный ChangeHP; клампит/синкает). Спец-защиты от самоубийства НЕ добавляем (правило 7).
            if (selfCost > 0f) castingUnit.ChangeHP(-selfCost);

            // Порядок: сначала урон по площади, затем смерть детонатора — «Жатва душ» срабатывает ПОСЛЕ взрыва.
            Vector2 center = new Vector2(victim.transform.position.x, victim.transform.position.z);
            SkvernaExplosion.Detonate(castingUnit, center, rad,
                enemyDamage, enemyDamageType, enemySelector,
                allyDamage, allyDamageType, allySelector,
                victim);
            victim.Die(-1, null, false);   // детонатор жертвуется (D4), без наград врагу; запускает «Жатву душ»

            Debug.Log($"[CorruptionBurst] герой={castingUnit.name} (-{selfCost}HP) детонировал {victim.name} " +
                      $"в ({center.x:F1},{center.y:F1}), радиус={rad}, урон врагам={enemyDamage}/свои={allyDamage}.");
        }

        // Ближайший к точке живой союзник, чья unitCategory разрешена; кастер (герой) исключается.
        Unit PickNearestAllyToPoint(List<Unit> allies, Vector2 point, Unit caster)
        {
            Unit best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < allies.Count; i++)
            {
                Unit u = allies[i];
                if (u == null || u.dead || u == caster) continue;
                if (!CategoryAllowed(u.unitCategory)) continue;
                Vector2 p = new Vector2(u.transform.position.x, u.transform.position.z);
                float d = (p - point).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = u; }
            }
            return best;
        }

        // Разрешена ли категория (пустой список = любые союзники).
        bool CategoryAllowed(Unit.UnitCategory c)
        {
            if (targetCategories == null || targetCategories.Length == 0) return true;
            for (int i = 0; i < targetCategories.Length; i++)
                if (targetCategories[i] == c) return true;
            return false;
        }
    }
}
