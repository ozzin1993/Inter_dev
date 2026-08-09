using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Постоянный иммунитет к контролю, пока способность разблокирована.
    /// Потребитель: Тир 6 [А] Паровой Титан («полный иммунитет к контролю»).
    ///
    /// Механизм уже есть — компонент-счётчик <see cref="ControlImmunity"/>, который проверяется
    /// в <c>Unit.Stun</c> (маркер <c>[Interflow fix 2026-07-06 control-immunity]</c>) и в нашем страхе (FearBuff).
    /// Эта способность просто держит счётчик поднятым: включается технологией или лежит на префабе без Required Tech.
    ///
    /// Границы (важно): покрываются оглушение и страх. Замедления — отдельная механика
    /// (<see cref="SlowImmunity"/>), нокбэк уважает иммунитет по своему флагу в EveryNthAttack.
    /// </summary>
    public class ControlImmunityPassive : InterflowAbility
    {
        public override AbilityType type { get { return AbilityType.Passive; } }

        // Уже идущее оглушение не снимаем: серверного «снять стан досрочно» в ассете нет
        // (Stun(bool) — клиентский показ VFX), а свой таймер трогать нельзя. Иммунитет действует
        // на НОВЫЕ попытки контроля; текущий стан, если он был, доигрывает.

        // Кому уже выдан иммунитет. Ассет зовёт Unlock повторно (способность без Required Tech
        // разблокируется заново на каждое OnTechUnlock), а счётчик ControlImmunity — рефкаунт:
        // без этой защиты refs рос бы бесконечно и иммунитет стал бы неснимаемым.
        readonly System.Collections.Generic.HashSet<Unit> applied = new System.Collections.Generic.HashSet<Unit>();

        public override void Init()
        {
            base.Init();
            applied.Clear();
        }

        public override void Unlock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || !applied.Add(unit)) return;

            ControlImmunity ci = unit.GetComponent<ControlImmunity>();
            if (ci == null) ci = unit.gameObject.AddComponent<ControlImmunity>();

            ci.Add();
        }

        public override void Lock(Unit unit, int castingPlayer, int level)
        {
            if (unit == null || !applied.Remove(unit)) return;

            ControlImmunity ci = unit.GetComponent<ControlImmunity>();
            if (ci != null) ci.Remove();
        }
    }
}
