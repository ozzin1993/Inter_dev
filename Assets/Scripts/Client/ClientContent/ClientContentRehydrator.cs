using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrategyCore
{
    // [Interflow 2026-08-01 client-data] Регидратор: на КЛИЕНТЕ возвращает мигрированные данные
    // (звуки/иконки) обратно в поля сим-объектов В ПАМЯТИ — весь остальной код (UIManager,
    // PlayerControl, мост звука) продолжает читать привычные поля и не менялся.
    // Живёт на объекте «ClientContent» в сценах (вешает мигратор); на сервере компонент вырезается
    // Roles-стрипом → данные не пакуются и не грузятся.
    // v2: DontDestroyOnLoad + повтор на каждую загрузку сцены — ПРЕДРАССТАВЛЕННЫЕ юниты сцены (замки,
    // башни) десериализуются с пустыми полями РАНЬШЕ правки ассетов, их заливаем отдельно по unitName.
    // Каждая секция в try/catch: один сбой не рушит остальные; счётчики в логе — для диагностики.
    // ⚠️ В редакторе после Play Mode поля ассетов остаются заполненными В ПАМЯТИ (на диск не пишется);
    // повторный запуск мигратора снова обнуляет поля на диске.
    [DefaultExecutionOrder(-1000)] // раньше ВСЕХ Awake сцены: MatchManager в Awake клонирует деревья техов
    public class ClientContentRehydrator : MonoBehaviour
    {
        [Tooltip("Таблица клиентских данных (создаёт и заполняет мигратор).")]
        [SerializeField] private ClientContentTable table;

        private static ClientContentRehydrator instance;
        private static bool assetsDone;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (table == null)
            {
                Debug.LogError("[ClientContent] ТАБЛИЦА НЕ НАЗНАЧЕНА на объекте ClientContent — звуки/иконки будут пустыми. Перезапусти мигратор (Tools → Interflow).");
                return;
            }
            RehydrateAssets();
            RehydrateSceneUnits();
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (table == null) return;
            RehydrateAssets();     // на случай, если Awake отработал до полной загрузки (идемпотентно)
            RehydrateSceneUnits(); // юниты новой сцены запечены с пустыми полями — заливаем по unitName
        }

        private void OnDestroy()
        {
            if (instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ---------- ассеты (префабы юнитов, SO) — один раз за запуск ----------
        private void RehydrateAssets()
        {
            if (assetsDone) return;
            assetsDone = true;

            int units = 0, abilities = 0, so = 0, weapons = 0, cfgs = 0;

            try
            {
                foreach (var e in table.units)
                {
                    if (e == null || e.prefab == null) continue;
                    Apply(e, e.prefab);
                    units++;
                }
            }
            catch (Exception ex) { Debug.LogError($"[ClientContent] Сбой регидрации юнитов: {ex}"); }

            try
            {
                foreach (var e in table.abilities)
                    if (e != null && e.ability != null) { e.ability.icon = e.icons; abilities++; }
            }
            catch (Exception ex) { Debug.LogError($"[ClientContent] Сбой регидрации способностей: {ex}"); }

            try
            {
                foreach (var e in table.soIcons)
                {
                    if (e == null || e.asset == null) continue;
                    switch (e.asset)
                    {
                        case Resource r: r.icon = e.icon; so++; break;
                        case ArmorType a: a.icon = e.icon; so++; break;
                        case DamageType d: d.icon = e.icon; so++; break;
                        case Effector f: f.icon = e.icon; so++; break;
                        case Attribute t: t.icon = e.icon; so++; break;
                        default: Debug.LogWarning($"[ClientContent] Неизвестный тип SO в soIcons: {e.asset.GetType().Name} ({e.asset.name})."); break;
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[ClientContent] Сбой регидрации SO-иконок: {ex}"); }

            try
            {
                foreach (var e in table.weaponSounds)
                    if (e != null && e.asset != null) { e.asset.attackToArmorSound = e.attackToArmor; e.asset.groundHitClips = e.groundHit; weapons++; }
            }
            catch (Exception ex) { Debug.LogError($"[ClientContent] Сбой регидрации звуков оружия: {ex}"); }

            try
            {
                if (table.factionConfigs.Count > 0 && table.techIcons.Count > 0)
                {
                    var icons = new Dictionary<Technology, Texture2D>();
                    foreach (var e in table.techIcons)
                        if (e != null && e.tech != null) icons[e.tech] = e.icon;

                    foreach (var cfg in table.factionConfigs)
                    {
                        if (cfg == null || cfg.techTiers == null) continue;
                        foreach (var tier in cfg.techTiers)
                        {
                            if (tier == null) continue;
                            Node(tier.levelUpgrade, icons);
                            Big(tier.optionA, icons);
                            Big(tier.optionB, icons);
                        }
                        cfgs++;
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[ClientContent] Сбой регидрации тех-иконок: {ex}"); }

            Debug.Log($"[ClientContent] Регидрация ассетов: юниты={units}, способности={abilities}, SO-иконки={so}, оружие={weapons}, конфиги={cfgs}.");
        }

        // ---------- предрасставленные юниты активных сцен (запечены с пустыми полями) ----------
        private void RehydrateSceneUnits()
        {
            try
            {
                var byName = new Dictionary<string, ClientContentTable.UnitEntry>();
                foreach (var e in table.units)
                    if (e != null && e.prefab != null && !string.IsNullOrEmpty(e.prefab.unitName)) byName[e.prefab.unitName] = e;
                if (byName.Count == 0) return;

                int n = 0;
                foreach (Unit u in FindObjectsOfType<Unit>(true))
                {
                    if (u == null || string.IsNullOrEmpty(u.unitName)) continue;
                    if (u.icon != null) continue; // уже с данными (инстанс регидрированного префаба) — не трогаем
                    if (!byName.TryGetValue(u.unitName, out var e)) continue;
                    Apply(e, u);
                    n++;
                }
                if (n > 0) Debug.Log($"[ClientContent] Регидрация сценных юнитов: {n}.");
            }
            catch (Exception ex) { Debug.LogError($"[ClientContent] Сбой регидрации сценных юнитов: {ex}"); }
        }

        private static void Apply(ClientContentTable.UnitEntry e, Unit u)
        {
            u.icon = e.icon;
            u.readySound = e.ready;
            u.moveSound = e.move;
            u.clickSound = e.click;
            u.deathSound = e.death;
            u.attackCommandSound = e.attackCommand;
            u.attackStartSound = e.attackStart;
            u.attackEndSound = e.attackEnd;
        }

        private static void Big(TechBigOption o, Dictionary<Technology, Texture2D> icons)
        {
            if (o == null) return;
            Node(o.node, icons);
            Node(o.specializationA, icons);
            Node(o.specializationB, icons);
        }

        private static void Node(TechNode nd, Dictionary<Technology, Texture2D> icons)
        {
            if (nd == null || nd.technology == null) return;
            if (icons.TryGetValue(nd.technology, out Texture2D icon)) nd.icon = icon;
        }
    }
}
