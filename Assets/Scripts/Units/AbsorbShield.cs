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

        // Реакция на ПРОБИТИЕ щита (объём исчерпан врагом), а не на истечение таймера.
        // Добавлено 2026-07-24 для «Жертвенного покрова» (Тир 3 [Б]): при пробитии — вспышка вокруг носителя.
        private System.Action<Unit> onDepleted;

        // Реакция на ЛЮБОЕ снятие щита (пробит, истёк, носитель умер). Нужна тем, кто вешает
        // сопутствующие эффекты на время щита и обязан их снять — иначе они живут вечно.
        private System.Action<Unit> onEnded;

        /// <summary>Навесить/обновить щит: объём amount на duration секунд (duration 0 = без таймера).</summary>
        public static void Apply(Unit target, float amount, float duration)
        {
            Apply(target, amount, duration, null);
        }

        /// <summary>
        /// Навесить/обновить щит с реакцией на ПРОБИТИЕ (объём исчерпан уроном).
        /// Реакция не срабатывает, если щит просто истёк по времени.
        /// </summary>
        public static void Apply(Unit target, float amount, float duration, System.Action<Unit> onShieldDepleted)
        {
            Apply(target, amount, duration, onShieldDepleted, null);
        }

        /// <summary>
        /// Навесить/обновить щит с двумя реакциями: на ПРОБИТИЕ (объём исчерпан) и на ЛЮБОЕ снятие
        /// (пробит, истёк по времени, носитель погиб). Вторая нужна тем, кто вешает сопутствующие эффекты
        /// на время щита и обязан их снять.
        /// </summary>
        public static void Apply(Unit target, float amount, float duration,
                                 System.Action<Unit> onShieldDepleted, System.Action<Unit> onShieldEnded)
        {
            if (target == null || target.dead || amount <= 0f) return;
            if (NetworkConnectionHandler.isClient) return; // объём щита — серверный (правило 6)

            // Ищем именно ЖИВОЙ компонент: помеченный на снятие ещё висит на объекте до конца кадра
            AbsorbShield s = null;
            AbsorbShield[] all = target.GetComponents<AbsorbShield>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !all[i].destroyed) { s = all[i]; break; }

            if (s == null) s = target.gameObject.AddComponent<AbsorbShield>();
            s.Init(target, amount, duration, onShieldDepleted, onShieldEnded);
        }

        /// <summary>Есть ли на юните активный поглощающий щит (для условий вроде «бонус по целям без щита»).</summary>
        public static bool IsActiveOn(Unit target)
        {
            if (target == null) return false;

            // Компонентов может оказаться два: помеченный на снятие (Destroy отложен до конца кадра) и свежий.
            // Смотрим все, иначе «уже под щитом» и «бонус по целям без щита» читают состояние трупа.
            AbsorbShield[] all = target.GetComponents<AbsorbShield>();
            for (int i = 0; i < all.Length; i++)
            {
                AbsorbShield s = all[i];
                if (s != null && !s.destroyed && s.remaining > 0f) return true;
            }

            return false;
        }

        private void Init(Unit target, float amount, float duration,
                          System.Action<Unit> onShieldDepleted, System.Action<Unit> onShieldEnded)
        {
            unit = target;
            remaining = Mathf.Max(remaining, amount);                                  // обновление берёт больший объём (не складываем)
            if (duration > 0f) remainingTime = Mathf.Max(remainingTime, duration);

            // Щит пробили, но тик снятия ещё не наступил, а сверху уже накинули новый —
            // иначе ближайший тик снёс бы свежий щит и выдал фантомную реакцию «пробитие»
            if (remaining > 0f) pendingDepleted = false;

            // Повторный каст поверх живого щита: старые реакции снимаем, чтобы они не остались висеть
            if (onShieldDepleted != null || onShieldEnded != null)
            {
                System.Action<Unit> previousEnded = onEnded;
                onDepleted = onShieldDepleted;
                onEnded = onShieldEnded;
                if (previousEnded != null && unit != null) previousEnded(unit);
            }

            if (hook == null) hook = Absorb;
            bool has = false;                                                          // идемпотентность
            for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
                if (unit.OnBeforeGetDamageCallbacks[i].Callback == hook) { has = true; break; }
            if (!has) unit.OnBeforeGetDamageCallbacks.Add(new DamageModifyCallback { Callback = hook, Ability = null, Level = 0 });

            // Подписываемся ВСЕГДА, даже у бессрочного щита: тик — единственное место,
            // где безопасно снимать хук и дёргать реакции (см. pendingDepleted)
            if (!subscribed && GameManager.Instance != null)
            {
                GameManager.Instance.Tick += OnTick;
                subscribed = true;
            }

            // Сегмент щита на полоске здоровья: новая величина (хосту — напрямую, клиентам — каналом статусов).
            NotifyShieldBar(unit, remaining);
        }

        // Щит исчерпан внутри расчёта урона: снимать хук и звать реакции прямо сейчас нельзя
        private bool pendingDepleted;

        // Поглощение входящего урона. Только сервер меняет состояние (правило 6).
        private float Absorb(Unit self, int level, float dmg, bool directAttack)
        {
            if (NetworkConnectionHandler.isClient) return dmg;
            if (remaining <= 0f || dmg <= 0f) return dmg;

            float absorbed = Mathf.Min(remaining, dmg);
            remaining -= absorbed;

            // Помечаем факт поглощения: по нему Unit.GetDamage поймёт, что удар СОСТОЯЛСЯ,
            // даже если до ХП ничего не дошло (иначе реакции на удар по щиту не срабатывают вовсе)
            if (absorbed > 0f) InterflowCombat.MarkAbsorbed(self);

            // ВАЖНО: этот метод вызывается ИЗ ЦИКЛА foreach по OnBeforeGetDamageCallbacks внутри Unit.GetDamage.
            // Снять хук отсюда (Cleanup → RemoveAt) значит уронить перечисление InvalidOperationException'ом
            // прямо посреди расчёта урона. Поэтому только помечаем — реальное снятие на ближайшем тике.
            if (remaining <= 0f) pendingDepleted = true;

            // Щит потаял — обновляем серый сегмент на полоске (RPC и событие список колбэков не трогают).
            NotifyShieldBar(self, remaining);

            return dmg - absorbed;
        }

        private void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.Instance == null) return;

            // Щит пробит на прошлом кадре — теперь мы вне цикла колбэков и можем спокойно всё снять
            if (pendingDepleted)
            {
                pendingDepleted = false;
                System.Action<Unit> reaction = onDepleted;
                Unit carrier = unit;
                Cleanup();
                if (reaction != null && carrier != null && !carrier.dead) reaction(carrier);

                return;
            }

            if (remainingTime > 0f)
            {
                remainingTime -= GameManager.Instance.currentDeltaTime;
                if (remainingTime <= 0f) Cleanup();
            }
        }

        /// <summary>
        /// Сообщить о новой величине щита: чистым клиентам — единым каналом статусов, этому пиру —
        /// фактом презентации (рисует клиентская сборка, см. ShieldBarDisplay). Только сервер (правило 6).
        /// </summary>
        private static void NotifyShieldBar(Unit target, float amount)
        {
            if (NetworkConnectionHandler.isClient || target == null) return;

            if (NetworkDataSync.Instance != null) NetworkDataSync.Instance.UnitShieldSend(target, amount);
            SkillPresentationEvents.RaiseShieldChanged(target, amount);
        }

        private bool destroyed; // компонент уже помечен на снятие: Destroy(this) отложен до конца кадра

        private void Cleanup()
        {
            if (destroyed) return;
            destroyed = true;

            // Сегмент на полоске: щита больше нет. Мёртвому носителю не шлём — сегмент умирает вместе с ним.
            if (unit != null && !unit.dead) NotifyShieldBar(unit, 0f);

            if (unit != null)
                for (int i = 0; i < unit.OnBeforeGetDamageCallbacks.Count; i++)
                    if (unit.OnBeforeGetDamageCallbacks[i].Callback == hook) { unit.OnBeforeGetDamageCallbacks.RemoveAt(i); break; }
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;

            // Щит снят по любой причине — даём снять сопутствующие эффекты (реакция на бьющих и т.п.).
            // Носителя передаём даже если он уже уничтожен (unit == null): подписчику важно почистить СВОИ словари.
            System.Action<Unit> ended = onEnded;
            onEnded = null;
            if (ended != null) ended(unit);

            Destroy(this);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;

            // Страховка: объект снесли мимо Cleanup (умер носитель, выгрузили сцену) — реакции всё равно снимаем
            if (!destroyed)
            {
                destroyed = true;
                System.Action<Unit> ended = onEnded;
                onEnded = null;
                if (ended != null) ended(unit);
            }
        }
    }
}
