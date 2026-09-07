using UnityEngine;

namespace StrategyCore
{
    // Кирпич B11 — «Страх»: цель на время бежит прочь от источника и не действует (немота + безоружие).
    // Пер-цель баф (образец FlameCloakBuff). Уважает ControlImmunity (решение Artsiom). Не стакается
    // (продлевает остаток). Только сервер (правило 6).
    // [Interflow fix 2026-09-03 control-as-effectors] Немота и безоружие — служебные СОСТОЯНИЯ на срок страха,
    // а не прямая запись флагов (решение Artsiom 03.09.2026). Вместе с прямой записью ушла и вся машинерия
    // «сними только свой вклад» (weSetMuted / weSetDisarmed / RemoveOwnStatus, ограничение §10 без рефкаунта):
    // у состояний свой срок жизни, они не затирают чужой контроль и не затираются чужим.
    // Одиночная цель / радиус — забота потребителя (дефолт §6.4 — одиночная); этот баф применяется к ОДНОЙ цели.
    public class FearBuff : MonoBehaviour
    {
        private Unit unit;
        private Vector2 fleeFrom;
        private float fleeDistance;
        private bool muteAndDisarm;
        private float remaining;
        private bool subscribed;

        /// <summary>Напугать цель: бегство прочь от sourcePos на duration; опц. muted+disarmed. Только сервер.
        /// ControlImmunity.Active → страх НЕ применяется (per-unit).</summary>
        public static void Apply(Unit target, Vector3 sourcePos, float duration, float fleeDistance, bool muteAndDisarm)
        {
            if (NetworkConnectionHandler.isClient) return;
            if (target == null || target.dead || duration <= 0f) return;
            // Шаг 2 схемы «пакет и приёмник»: иммунитет спрашивается у приёмника, а не своим поиском
            // компонента (§3.1 схемы). Отсев мёртвых выше обязан остаться перед этой строкой:
            // обращение к приёмнику создаёт компонент на игровом объекте, на трупе этого делать нельзя.
            if (target.ReceiverEnsure().ControlImmune) return; // иммунитет к контролю

            FearBuff buff = target.GetComponent<FearBuff>();
            if (buff == null) buff = target.gameObject.AddComponent<FearBuff>();
            buff.Init(target, sourcePos, duration, fleeDistance, muteAndDisarm);
        }

        private void Init(Unit target, Vector3 sourcePos, float duration, float fleeDistance, bool muteAndDisarm)
        {
            unit = target;
            fleeFrom = new Vector2(sourcePos.x, sourcePos.z);
            this.fleeDistance = fleeDistance;
            this.muteAndDisarm = muteAndDisarm;
            remaining = Mathf.Max(remaining, duration);   // не стакается — берём больший остаток

            if (muteAndDisarm)
            {
                // Срок контроля равен сроку страха. Источника-юнита у страха нет (Apply знает только точку,
                // от которой бежать) — владельцем идёт слот самой цели, как у прочих безымянных источников.
                // Иммунитет уже спрошен в Apply, повторная проверка в приёмнике его же и подтвердит.
                unit.Mute(duration, null, unit.owner);
                unit.Disarm(duration, null, unit.owner);
            }

            if (!subscribed && GameManager.Instance != null)
            {
                GameManager.Instance.Tick += OnTick;
                subscribed = true;
            }
            FleeStep(); // побежать сразу
        }

        private void OnTick()
        {
            if (unit == null || unit.dead) { Cleanup(); return; }
            if (GameManager.Instance == null) return;
            FleeStep();
            remaining -= GameManager.Instance.currentDeltaTime;
            if (remaining <= 0f) Cleanup();
        }

        // Каждый тик гоним прочь от источника (чтобы AI не разворачивался к врагу). Только сервер.
        private void FleeStep()
        {
            if (NetworkConnectionHandler.isClient) return;
            if (unit == null || !unit.canMove) return;    // неподвижный не бежит (остаётся muted/disarmed)
            Vector2 self = new Vector2(unit.transform.position.x, unit.transform.position.z);
            Vector2 dir = self - fleeFrom;
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(unit.transform.forward.x, unit.transform.forward.z);
            dir.Normalize();
            unit.Move(self + dir * fleeDistance);
        }

        private void Cleanup()
        {
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (subscribed && GameManager.Instance != null) GameManager.Instance.Tick -= OnTick;
            subscribed = false;
        }
    }
}
