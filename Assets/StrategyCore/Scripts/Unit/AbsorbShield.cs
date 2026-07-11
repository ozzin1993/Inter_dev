using UnityEngine;

namespace StrategyCore
{
    // Кирпич B17 — Поглощающий щит: временно поглощает до X единиц входящего урона, затем спадает
    // (по исчерпанию объёма ИЛИ по таймеру duration). Навешивается из способности/ауры/заклинания —
    // потребители: Костяной Щит, Двойное плетение (C3), Кристальный Щит, Благословение Леса (C5), N5.
    // Хук — ЖЕРТВА: Unit.OnBeforeGetDamageCallbacks. Тайминг — GameManager.Tick (образец FlameCloakBuff).
    // Серверо-авторитетно (правило 6): объём щита меняет только сервер; на клиенте HP приходит синком.
    // Ассет StrategyCore не трогаем (правило 1). Ограничение канала: тип урона недоступен → щит поглощает
    // ЛЮБОЙ тип (фильтр «только магия/дот» на этом хуке невозможен — компромисс, зафиксирован).
    public class AbsorbShield : MonoBehaviour
    {
        private Unit unit;
        private float remaining;      // оставшийся объём поглощения
        private float remainingTime;  // остаток таймера (0 = без таймера, только по исчерпанию)
        private bool subscribed;
        private System.Func<Unit, int, float, bool, float> hook;

        /// <summary>Навесить/обновить щит: объём amount на duration секунд (duration 0 = без таймера).</summary>
        public static void Apply(Unit target, float amount, float duration)
        {
            if (target == null || target.dead || amount <= 0f) return;
            if (NetworkConnectionHandler.isClient) return; // объём щита — серверный (правило 6)
            AbsorbShield s = target.GetComponent<AbsorbShield>();
            if (s == null) s = target.gameObject.AddComponent<AbsorbShield>();
            s.Init(target, amount, duration);
        }

        private void Init(Unit target, float amount, float duration)
        {
            unit = target;
            remaining = Mathf.Max(remaining, amount);                                  // обновление берёт больший объём (не складываем)
            if (duration > 0f) remainingTime = Mathf.Max(remainingTime, duration);

            if (hook == null) hook = Absorb;
            bool has = false;                                                          // идемпотентность
            for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
                if (unit.OnBeforeGetDamageCallbacks[i].Callback == hook) { has = true; break; }
            if (!has) unit.OnBeforeGetDamageCallbacks.Add(new DamageModifyCallback { Callback = hook, Ability = null, Level = 0 });

            if (!subscribed && remainingTime > 0f && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                subscribed = true;
            }
        }

        // Поглощение входящего урона. Только сервер меняет состояние (правило 6).
        private float Absorb(Unit self, int level, float dmg, bool directAttack)
        {
            if (NetworkConnectionHandler.isClient) return dmg;
            if (remaining <= 0f || dmg <= 0f) return dmg;
            float absorbed = Mathf.Min(remaining, dmg);
            remaining -= absorbed;
            if (remaining <= 0f) Cleanup();
            return dmg - absorbed;
        }

        private void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.instance == null) return;
            if (remainingTime > 0f)
            {
                remainingTime -= GameManager.instance.currentDeltaTime;
                if (remainingTime <= 0f) Cleanup();
            }
        }

        private void Cleanup()
        {
            if (unit != null)
                for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
                    if (unit.OnBeforeGetDamageCallbacks[i].Callback == hook) { unit.OnBeforeGetDamageCallbacks.RemoveAt(i); break; }
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
