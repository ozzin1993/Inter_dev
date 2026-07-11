using UnityEngine;

namespace StrategyCore
{
    // Временный баф «Железного Приговора» на юните: снижает ВЕСЬ входящий урон на reductionPercent%
    // (штатный OnBeforeGetDamageCallbacks — берётся минимум из колбэков, до брони; как Evasion) и даёт
    // иммунитет к контролю (маркер ControlImmunity, рефкаунт) на duration секунд, затем самоудаляется.
    // Навешивается из IronVerdictActive (сервер, правило 6). Не стакается: повторное наложение продлевает остаток.
    public class IronVerdictBuff : MonoBehaviour
    {
        private Unit unit;
        private Ability ability;         // для точечного снятия колбэка урона (матч по Ability+Level, как Evasion)
        private int level;
        private float damageFactor = 1f; // множитель входящего урона (0.65 = -35%)
        private bool controlImmune;
        private float remaining;
        private bool applied;            // колбэк урона + иммунитет уже добавлены (нестак)
        private bool immunityAdded;
        private bool subscribed;

        /// <summary>Навесить «Железный Приговор» на юнит или продлить (берётся больший остаток времени).</summary>
        public static void Apply(Unit target, Ability ability, int level, float reductionPercent, bool controlImmune, float duration)
        {
            if (target == null || target.dead || duration <= 0f) return;
            IronVerdictBuff buff = target.GetComponent<IronVerdictBuff>();
            if (buff == null) buff = target.gameObject.AddComponent<IronVerdictBuff>();
            buff.Init(target, ability, level, reductionPercent, controlImmune, duration);
        }

        private void Init(Unit target, Ability ability, int level, float reductionPercent, bool controlImmune, float duration)
        {
            unit = target;
            this.ability = ability;
            this.level = level;
            damageFactor = Mathf.Clamp01(1f - reductionPercent / 100f); // 35 → 0.65
            this.controlImmune = controlImmune;
            remaining = Mathf.Max(remaining, duration);

            if (!applied)
            {
                // Снижение урона: штатный колбэк (минимум из колбэков, до брони). Наш метод возвращает dmg*factor.
                unit.OnBeforeGetDamageCallbacks.Add(new DamageModifyCallback
                {
                    Callback = ReduceIncomingDamage,
                    Ability = ability,
                    Level = level
                });

                // Иммунитет к контролю: рефкаунт-маркер (get-or-add). Проверяется в Unit.Stun ([Interflow fix]).
                if (controlImmune)
                {
                    ControlImmunity ci = unit.GetComponent<ControlImmunity>();
                    if (ci == null) ci = unit.gameObject.AddComponent<ControlImmunity>();
                    ci.Add();
                    immunityAdded = true;
                }
                applied = true;
            }

            if (!subscribed && GameManager.instance != null)
            {
                GameManager.instance.Tick += OnTick;
                subscribed = true;
            }
        }

        // Множитель входящего урона. Действует на ЛЮБОЙ урон (directAttack или нет) — «из любых источников».
        private float ReduceIncomingDamage(Unit u, int lvl, float dmg, bool directAttack) => dmg * damageFactor;

        private void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.instance == null) return;
            remaining -= GameManager.instance.currentDeltaTime;
            if (remaining <= 0f) Cleanup();
        }

        private void Cleanup()
        {
            RemoveEffects();
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
            Destroy(this);
        }

        // Снять колбэк урона (матч по Ability+Level, как Evasion) и иммунитет (рефкаунт). Идемпотентно (guard applied).
        private void RemoveEffects()
        {
            if (!applied) return;
            if (unit != null)
            {
                for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
                {
                    var c = unit.OnBeforeGetDamageCallbacks[i];
                    if (c.Ability == ability && c.Level == level)
                    {
                        unit.OnBeforeGetDamageCallbacks.RemoveAt(i);
                        break;
                    }
                }
                if (immunityAdded)
                {
                    ControlImmunity ci = unit.GetComponent<ControlImmunity>();
                    if (ci != null) ci.Remove();
                }
            }
            applied = false;
            immunityAdded = false;
        }

        private void OnDestroy()
        {
            RemoveEffects(); // страховка, если юнит уничтожен не через Cleanup
            if (subscribed && GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
