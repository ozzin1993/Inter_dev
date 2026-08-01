using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow strip-presentation, хук (b)] Вешается на ПРЕФАБ юнита. На безголовом сервере
    /// (ServerBootstrap.IsHeadlessServer) ПОСЛЕ инициализации юнита снимает «презентацию», которая зря грузит
    /// CPU без рендера: ParticleSystem (CPU-симуляция частиц) и Animator (evaluate вхолостую).
    ///
    /// Безопасность тайминга: длины анимаций (attack/death) и unitRadius вычисляются в Unit.Initialize/
    /// CalculateVisuals ДО снятия. Поэтому Animator ОТКЛЮЧАЕТСЯ (enabled=false), а НЕ удаляется — кэш длин и
    /// ссылка `animator` остаются, и Destroy(gameObject, deathAnimationLength) при смерти работает как прежде.
    /// Боевой тайминг от Animator не зависит (коррекция nextAttack по currentAnimAttackDelay в коде отключена).
    ///
    /// На клиенте/хосте-с-графикой компонент сам себя выключает в Awake (нулевая цена). Ядро ассета не правится
    /// (правило 1). Параметры — в Inspector (правило 3). Снятие происходит один раз, на первом игровом тике
    /// (GameManager.Tick) — Initialize отрабатывает при спавне/OnGameStart, т.е. раньше тиков.
    /// </summary>
    public class ServerPresentationStripper : MonoBehaviour
    {
        [Tooltip("Отключать Animator-компоненты юнита (evaluate вхолостую на сервере). Animator отключается, а не удаляется — длины анимаций уже закэшированы, тайминг боя/смерти не меняется.")]
        [SerializeField] private bool stripAnimators = true;

        [Tooltip("Останавливать и отключать ParticleSystem юнита (CPU-симуляция частиц без рендера — основной кандидат на экономию).")]
        [SerializeField] private bool stripParticleSystems = true;

        private bool _done;
        private bool _subscribed;

        private void Awake()
        {
            // Только на безголовом сервере. На клиенте/хосте-с-графикой презентация нужна — молчим (нулевая цена).
            if (!ServerBootstrap.IsHeadlessServer) enabled = false;
        }

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();

        // Подписка на игровой тик (когда GameManager уже есть). Снятие — на ПЕРВОМ тике, гарантированно после Initialize.
        private void TrySubscribe()
        {
            if (_subscribed || _done) return;
            if (!ServerBootstrap.IsHeadlessServer) return;
            if (GameManager.instance == null) return; // тик появится позже — повторим из Start
            GameManager.instance.Tick += OnTick;
            _subscribed = true;
        }

        private void OnTick()
        {
            Strip();
            Unsubscribe();
            _done = true;
            enabled = false; // одноразово
        }

        private void Strip()
        {
            if (stripParticleSystems)
            {
                ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] == null) continue;
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    systems[i].gameObject.SetActive(false);
                }
            }

            if (stripAnimators)
            {
                Animator[] animators = GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < animators.Length; i++)
                    if (animators[i] != null) animators[i].enabled = false; // отключаем, не удаляем
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (GameManager.instance != null) GameManager.instance.Tick -= OnTick;
            _subscribed = false;
        }

        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();
    }
}
