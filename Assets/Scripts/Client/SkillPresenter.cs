using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-08-06] ЕДИНЫЙ клиентский презентер умений. Решение Artsiom: событийная инверсия.
    ///
    /// Единственная точка, где факты презентации превращаются в визуал. Подписан на SkillPresentationEvents;
    /// ЧТО именно рисовать, берёт из ассета умения по abilityID. Разделение жёсткое:
    /// сервер не знает про визуал, презентер не знает про геймплей (правило 6).
    ///
    /// Живёт в сборке Interflow.Client (defineConstraints: !UNITY_SERVER) — в билде выделенного сервера
    /// его нет вовсе, поэтому сетевой слой на него не ссылается и не может сослаться.
    /// Создаёт себя сам при старте приложения (как PresentationBridge регистрирует сервисы):
    /// ни сцену, ни префаб GameManager править не нужно.
    /// </summary>
    public partial class SkillPresenter : MonoBehaviour
    {
        public static SkillPresenter Instance { get; private set; }

        // Настройки презентации. Ассета может не быть — тогда работаем на значениях по умолчанию
        // (недоступен окажется только сектор конуса, ему нужен материал).
        SkillPresentationSettings settings;
        SkillPresentationSettings Settings => settings != null
            ? settings
            : settings = Resources.Load<SkillPresentationSettings>(SkillPresentationSettings.ResourcePath)
                      ?? ScriptableObject.CreateInstance<SkillPresentationSettings>();

        // Клиентские копии зон на земле: id зоны → объект. Геймплея в них нет: GroundDamageZone.Start
        // сам выходит на клиенте (isClient), тик урона не подписывается — остаётся чистый визуал.
        readonly Dictionary<int, GameObject> zoneVisuals = new Dictionary<int, GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (Utils.Headless) return;      // показывать некому
            if (Instance != null) return;    // уничтоженный объект Unity сравнивается с null как null — пересоздастся

            GameObject go = new GameObject("SkillPresenter");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SkillPresenter>();
        }

        void OnEnable()
        {
            SkillPresentationEvents.SkillFired += HandleSkillFired;
            SkillPresentationEvents.UnitReady += HandleUnitReady;
            SkillPresentationEvents.UnitGone += HandleUnitGone;
            SkillPresentationEvents.CastStarted += HandleCastStarted;
            SkillPresentationEvents.CastStopped += HandleCastStopped;
            SkillPresentationEvents.ZoneSpawned += HandleZoneSpawned;
            SkillPresentationEvents.ZoneDespawned += HandleZoneDespawned;
            SkillPresentationEvents.ShieldChanged += HandleShieldChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void OnDisable()
        {
            SkillPresentationEvents.SkillFired -= HandleSkillFired;
            SkillPresentationEvents.UnitReady -= HandleUnitReady;
            SkillPresentationEvents.UnitGone -= HandleUnitGone;
            SkillPresentationEvents.CastStarted -= HandleCastStarted;
            SkillPresentationEvents.CastStopped -= HandleCastStopped;
            SkillPresentationEvents.ZoneSpawned -= HandleZoneSpawned;
            SkillPresentationEvents.ZoneDespawned -= HandleZoneDespawned;
            SkillPresentationEvents.ShieldChanged -= HandleShieldChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>Поглощающий щит юнита изменился — серый сегмент на полоске здоровья рисует ShieldBarDisplay.</summary>
        static void HandleShieldChanged(Unit unit, float amount) => ShieldBarDisplay.LocalShow(unit, amount);

        // Сам презентер переживает смену сцены, а визуалы — нет: они уничтожаются вместе со сценой.
        // Без обнуления реестра там копились бы мёртвые ссылки, а id зон нового матча совпадали бы со старыми.
        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            zoneVisuals.Clear();
            ClearAreas();
        }

        // Таймеры областей и гейт тумана войны — покадрово (см. SkillPresenter.Areas.cs).
        void Update() => UpdateAreas();

        // ============================== СРАБАТЫВАНИЕ УМЕНИЯ ==============================

        /// <summary>
        /// Умение сработало — проигрываем всё, что описано в его ассете. Гейты тумана войны те же,
        /// что были в CompositeSkill.Execute до переноса: у хелперов VFX они внутри, у звуков — здесь.
        /// </summary>
        void HandleSkillFired(Unit caster, int abilityID, int level, Unit aimUnit, Vector3 aimPoint)
        {
            CompositeSkill skill = ResolveSkill(abilityID);
            if (skill == null) return;

            PlayProcAnimation(caster, skill.procAnimationState);

            // Визуал и звук замаха — из точки привязки на модели кастера.
            InterflowAbility.PlaySocketVFX(caster, skill.spawnSocket, skill.localOffset, skill.castVFX, skill.castVfxLifetime);
            if (caster != null && InterflowAbility.VisibleForLocalViewer(caster.transform.position))
                InterflowAbility.PlaySound(skill.castSound, caster.transform, skill.soundVolume);

            // Визуал и звук попадания — в точке приложения (она же центр области).
            InterflowAbility.PlayPointVFX(aimPoint, skill.impactVFX, skill.impactVfxLifetime);
            if (InterflowAbility.VisibleForLocalViewer(aimPoint))
                InterflowAbility.PlaySound(skill.impactSound, aimUnit != null ? aimUnit.transform : null, skill.soundVolume);

            SpawnVisualProjectile(skill, caster, level, aimUnit);

            // Область действия: та, что висела на замахе, доживает свой срок; у «умного выбора точки»
            // область появляется только сейчас — раньше точки не существовало.
            AreaOnSkillFired(skill, caster, level, aimPoint);
        }

        /// <summary>
        /// Визуальная копия снаряда для ЧИСТОГО клиента. Настоящий снаряд несёт урон, поэтому живёт
        /// только на сервере (правило 6) — на хосте он уже летит, копия там дала бы два снаряда.
        /// До этого у кастов «умный выбор» с кнопки клиент не видел полёта вовсе: цель выбирает сервер,
        /// и клиент выходил из Use раньше. Теперь цель приезжает фактом.
        /// </summary>
        static void SpawnVisualProjectile(CompositeSkill skill, Unit caster, int level, Unit aimUnit)
        {
            if (!NetworkConnectionHandler.isClient) return;

            // Те же условия, при которых снаряд создаёт сервер: иначе копия появилась бы там, где настоящего нет.
            if (skill.delivery != SkillDelivery.Projectile || skill.projectilePrefab == null) return;
            if (caster == null || aimUnit == null || !skill.projectileFollowsTarget) return;

            Transform socket = InterflowAbility.ResolveSocket(caster, skill.spawnSocket);
            Vector3 spawnPos = InterflowAbility.SocketPosition(caster, skill.spawnSocket, skill.localOffset);
            Quaternion spawnRot = socket != null ? socket.rotation : Quaternion.identity;

            // Урон и оглушение — нули: состояние мира клиент не считает. Тип урона всё же передаём —
            // штатный снаряд по прилёте обращается к нему, и без типа расчёт брони упал бы.
            Projectile visual = Projectile.Spawn(caster.owner, caster, skill.projectilePrefab, spawnPos, spawnRot,
                                                 aimUnit, false, 0f, skill.ProjectileDamageType(level), true);
            if (visual == null) return;

            visual.stunTime = 0f;

            // Штатный спавн подставляет снаряду эффекторы АВТОАТАКИ кастера. Живому кастеру они не
            // применяются, но если он погибнет в полёте, снаряд наложил бы их сам — на клиенте, мимо сервера.
            visual.attackEffectors = null;
        }

        // Анимация срабатывания. Невидимого в тумане не анимируем — паттерн ядра (Unit.State: cast, Unit.Combat: hit).
        // CrossFade перебивает текущую анимацию, включая замах атаки, — принятая цена (План §3).
        static void PlayProcAnimation(Unit unit, string state)
        {
            if (unit == null || unit.dead || string.IsNullOrEmpty(state)) return;
            if (!unit.FoWVisible || unit.animator == null) return;

            unit.animator.CrossFade(state, unit.crossFadeTime, 0, 0f);
        }

        // ============================== ЗОНЫ НА ЗЕМЛЕ ==============================

        /// <param name="level">Уровень умения. Для зоны сейчас не используется (префаб от уровня не зависит),
        /// но едет в событии вместе с остальными фактами — как у прочих сообщений канала.</param>
        void HandleZoneSpawned(int zoneId, int abilityID, int level, Vector3 position)
        {
            if (zoneVisuals.ContainsKey(zoneId)) return;   // повтор (в т.ч. досыл при подключении) — не дублируем

            GameObject prefab = ResolveZonePrefab(abilityID);
            if (prefab == null) return;

            zoneVisuals[zoneId] = Instantiate(prefab, position, Quaternion.identity);
        }

        void HandleZoneDespawned(int zoneId)
        {
            if (!zoneVisuals.TryGetValue(zoneId, out GameObject go)) return;
            if (go != null) Destroy(go);
            zoneVisuals.Remove(zoneId);
        }

        // ============================== ОБЩЕЕ ==============================

        // Описание визуала по сети не гоняем: оно уже лежит в ассете умения, одинаковом на всех пирах.
        // Умения не-конструкторы (кодовые пассивки) презентации пока не описывают — для них null.
        static CompositeSkill ResolveSkill(int abilityID)
        {
            if (GameManager.Instance == null) return null;
            if (!GameManager.Instance.gameAbilities.TryGetValue(abilityID, out Ability ability)) return null;

            return ability as CompositeSkill;
        }

        static GameObject ResolveZonePrefab(int abilityID)
        {
            CompositeSkill skill = ResolveSkill(abilityID);
            if (skill == null || skill.groundZone == null || !skill.groundZone.enabled) return null;

            return skill.groundZone.zonePrefab;
        }
    }
}
