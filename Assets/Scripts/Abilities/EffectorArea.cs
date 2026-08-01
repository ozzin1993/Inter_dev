using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    /// <summary>
    /// Способность по площади, накладывающая эффекторы всем подходящим целям в радиусе
    /// (и, по желанию, наносящая разовый урон).
    /// Потребители: Тир 3 [А] «Арктическая метель» (замедление всех врагов в зоне),
    /// Тир 6 [А] «Паровой выхлоп» (ослепление ближников вокруг).
    ///
    /// Зачем: в ассете есть Area-способности только с фиксированным действием
    /// (<c>DisarmArea</c>, <c>MuteArea</c>, <c>FlameRing</c>) — универсальной «наложить эффектор по площади» нет.
    ///
    /// Штатные поля Ability: <c>radius</c> — радиус зоны, <c>unitSelector</c> — кого задевает,
    /// <c>cooldown</c>/<c>manaCost</c>/<c>castRange</c> — как обычно. Автокаст — компонентом AutoAbilityUser.
    /// </summary>
    [CreateAssetMenu(fileName = "EffectorArea", menuName = "StrategyCore/Abilities/Interflow/EffectorArea (Эффекторы по площади)")]
    public class EffectorArea : Ability
    {
        public override AbilityType type { get { return AbilityType.Area; } }

        [Header("Действие по площади")]
        [Tooltip("Эффекторы, накладываемые каждой подходящей цели в радиусе.")]
        public Effector[] effectors;

        [Tooltip("Разовый урон каждой цели, по уровням. 0 — только эффекторы.")]
        public float[] damage = new float[1] { 0f };

        [Tooltip("Тип разового урона. Обязателен, если урон больше нуля.")]
        public DamageType damageType;

        [Header("Фильтр (сверх Unit Selector)")]
        [Tooltip("Задевать только юнитов ближнего боя (поле melee на юните). Для «ослепить тех, кто рядом машет оружием».")]
        public bool onlyMeleeUnits;

        [Tooltip("Ослепление целей: шанс промаха 0..1. 0 — не ослеплять. Работает вместе с эффекторами.")]
        [Range(0f, 1f)]
        public float blindChance = 0f;

        [Tooltip("Длительность ослепления в секундах. Действует только если шанс промаха больше нуля.")]
        public float blindDuration = 0f;

        [Tooltip("VFX в точке применения. Пусто — без визуала. Создаётся штатным Instantiate, как во FlameRing.")]
        public VFXReferencer areaVFX;

        [Tooltip("Через сколько секунд убрать VFX зоны. 0 — не создавать/не удалять по таймеру.")]
        public float vfxLifetime = 2f;

        public override void Use(Unit castingUnit, int castingPlayer, int level, Vector3 location)
        {
            float r = ValueAt(radius, level, 0f);
            if (r <= 0f) return;

            // Визуал создаём и на клиенте: Use штатно исполняется на обоих пирах (как во FlameRing),
            // иначе клиент не увидит зону. Всё, что меняет состояние, — ниже, под серверным гейтом.
            // Туман войны: проверяем клетку САМОЙ ЗОНЫ, а не кастера — зона может лежать
            // в 10 метрах от него, и видимость у них разная.
            if (areaVFX != null && vfxLifetime > 0f &&
                FogOfWar.instance != null && SlotManager.instance != null &&
                FogOfWar.instance.IsVisible(FogOfWar.GetCellByPosition(location), SlotManager.instance.currentTeam))
            {
                VFXReferencer vfx = Instantiate(areaVFX, location + new Vector3(0f, 0.1f, 0f), Quaternion.identity);
                Destroy(vfx.gameObject, vfxLifetime);
            }

            if (NetworkConnectionHandler.isClient) return; // урон/эффекторы/ослепление — только сервер (правило 6)

            Unit[] units = Utils.GetUnitsInRadius(new Vector2(location.x, location.z), r, castingPlayer, unitSelector);
            if (units == null || units.Length == 0) return;

            float dmg = ValueAt(damage, level, 0f);
            int affected = 0;

            for (int i = 0; i < units.Length; i++)
            {
                Unit u = units[i];
                if (u == null || u.dead) continue;
                if (onlyMeleeUnits && !u.melee) continue;

                if (dmg > 0f && damageType != null) u.GetDamage(dmg, damageType, castingPlayer, castingUnit, false, out float _);
                if (u.dead) continue; // погиб от этого же урона — дальше по нему не работаем

                if (effectors != null && effectors.Length > 0) Effector.EffectorAdd(castingPlayer, u, effectors);
                if (blindChance > 0f && blindDuration > 0f) BlindDebuff.Apply(u, blindChance, blindDuration);

                affected++;
            }

            if (affected > 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ForceSync();
        }

        static float ValueAt(float[] arr, int level, float fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            if (level < 0) level = 0;
            if (level >= arr.Length) level = arr.Length - 1;

            return arr[level];
        }
    }
}
