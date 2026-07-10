using UnityEngine;

namespace StrategyCore
{
    // Кирпич B4 — «При добивании» (клич): когда носитель добивает врага, союзникам в радиусе накладывается
    // эффектор-«клич». Passive-SO (правило 2): регистрирует носителя-«криера» в серверном хабе смертей
    // (MatchManager.RegisterKiller); хаб на смерти жертвы зовёт TriggerCry добившего (переиспользует B3-хаб,
    // правило 5). Ассет StrategyCore не трогаем (правило 1). Числа/эффектор — только в Inspector (правило 3).
    [CreateAssetMenu(fileName = "OnKillCryPassive", menuName = "StrategyCore/Abilities/Interflow/OnKillCry (B4)")]
    public class OnKillCryPassive : Ability
    {
        public override AbilityType type => AbilityType.Passive;

        [Header("Клич при добивании (B4)")]
        [Tooltip("Эффектор-клич союзникам в радиусе (Stacks=false → повтор обновляет длительность, не стакает)")]
        public Effector cryEffector;

        [Tooltip("Радиус клича вокруг добившего, по уровням (юниты Unity). [БАЛАНС — Влад]")]
        public float[] cryRadius;

        [Tooltip("Накладывать ли клич на самого добившего")]
        public bool includeSelf = false;

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (MatchManager.instance != null) MatchManager.instance.RegisterKiller(unit, this, level);
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (MatchManager.instance != null) MatchManager.instance.UnregisterKiller(unit);
        }

        // Зовётся хабом смертей на сервере, когда killer добил врага. Накладывает cryEffector союзникам в радиусе.
        // Получателей считает только сервер (клиент не пересчитывает → без десинка, правило 6).
        public void TriggerCry(Unit killer, int level)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (killer == null || cryEffector == null) return;
            if (cryRadius == null || level >= cryRadius.Length) return;

            Vector2 pos = new Vector2(killer.transform.position.x, killer.transform.position.z);
            Unit exclude = includeSelf ? null : killer;
            // unitSelector (базовый, из Ability) — настроить в SO на СОЮЗНИКОВ носителя.
            Unit[] allies = Utils.GetUnitsInRadius(pos, cryRadius[level], killer.owner, unitSelector, -1, exclude);
            if (allies == null) return;
            for (int i = 0; i < allies.Length; i++)
                if (allies[i] != null) Effector.EffectorAdd(allies[i], cryEffector, killer, killer.owner, 0f);
        }
    }
}
