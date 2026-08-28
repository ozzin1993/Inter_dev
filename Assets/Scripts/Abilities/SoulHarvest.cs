using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Пассив героя орков «Жатва душ»: когда союзный орк (фильтр по unitCategory) погибает — герой-носитель
    // восстанавливает healPerSoul ХП. Источник смерти любой: враг, «Выброс Скверны», «Обряд Костра».
    // Компонент на префабе героя (решение A). Смерти ловим штатно: MatchManager.OnUnitSpawned (новые союзники) +
    // Unit.OnDie каждого. Серверо-авторитетно (правило 6): подписки/лечение — только на сервере; ChangeHP синкает ХП.
    // Ядро StrategyCore не трогаем (правило 1). Числа — в Inspector (правило 3). MatchManager.cs не трогаем (правило 19).
    [RequireComponent(typeof(Unit))]
    public class SoulHarvest : MonoBehaviour
    {
        [Tooltip("Сколько ХП восстанавливает герой за смерть каждого союзного орка (по ТЗ 25).")]
        [SerializeField] float healPerSoul = 25f;

        [Tooltip("Категории союзных юнитов, чья смерть даёт лечение (по unitCategory). Пусто — любые союзники.")]
        [SerializeField] Unit.UnitCategory[] targetCategories;

        Unit hero;                                            // герой-носитель (получатель лечения)
        readonly HashSet<Unit> hooked = new HashSet<Unit>();  // союзники, на чей OnDie подписаны (для отписки)
        bool tickSubscribed;                                  // ждём первый Tick для инициализации подписок
        bool wired;                                           // подписки на смерти союзников установлены

        void Start()
        {
            if (NetworkConnectionHandler.isClient) return;    // пассив серверный (правило 6)
            hero = GetComponent<Unit>();
            if (hero == null) { Debug.LogWarning("[SoulHarvest] нет Unit на герое."); return; }

            // Подписку откладываем до первого тика: к нему Unit.Initialize гарантированно установил owner
            // (иначе начальный список союзников собрался бы по неверному владельцу — замечание code-review).
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Tick += WireOnFirstTick;
                tickSubscribed = true;
            }
            else Debug.LogWarning("[SoulHarvest] GameManager.Instance == null — подписки не запланированы.");
        }

        // Отложенная инициализация (первый Tick): owner уже задан. Подписываемся на новых и уже живых союзников.
        void WireOnFirstTick()
        {
            if (GameManager.Instance != null) GameManager.Instance.Tick -= WireOnFirstTick;
            tickSubscribed = false;
            if (wired) return;

            MatchManager mm = MatchManager.Instance;
            if (mm == null || hero == null) { Debug.LogWarning("[SoulHarvest] MatchManager/hero недоступны на первом тике."); return; }

            mm.OnUnitSpawned += OnUnitSpawned;                // новые союзники — через штатное событие спавна
            List<Unit> allies = mm.GetCommandUnitsForPlayer(hero.owner);   // уже живые союзники команды героя
            for (int i = 0; i < allies.Count; i++) Hook(allies[i]);
            wired = true;
        }

        // Новый юнит: если союзен герою (тот же владелец) — подписаться на его смерть.
        void OnUnitSpawned(int team, Unit u)
        {
            if (u == null || hero == null) return;
            if (u.owner != hero.owner) return;                // только союзники героя (как GetCommandUnitsForPlayer)
            Hook(u);
        }

        void Hook(Unit u)
        {
            if (u == null || u == hero) return;               // сам герой не «жатвится»
            if (hooked.Contains(u)) return;
            u.OnDie += OnAllyDied;
            hooked.Add(u);
        }

        // Смерть союзника: если категория разрешена и герой жив — восстановить ему ХП. Сервер-онли.
        void OnAllyDied(Unit dead, int playerThatKills, Unit unitThatKills, bool rewards)
        {
            if (dead != null) hooked.Remove(dead);            // OnDie одноразов — снимаем из набора
            if (NetworkConnectionHandler.isClient) return;
            if (hero == null || hero.dead || dead == null) return;   // мёртвый герой не лечится
            if (!CategoryAllowed(dead.unitCategory)) return;
            if (healPerSoul != 0f) hero.ChangeHP(healPerSoul);        // клампит до maxHealth, синкает
        }

        bool CategoryAllowed(Unit.UnitCategory c)
        {
            if (targetCategories == null || targetCategories.Length == 0) return true;
            for (int i = 0; i < targetCategories.Length; i++)
                if (targetCategories[i] == c) return true;
            return false;
        }

        void OnDestroy()
        {
            if (tickSubscribed && GameManager.Instance != null) GameManager.Instance.Tick -= WireOnFirstTick;
            tickSubscribed = false;
            if (!wired) return;
            if (MatchManager.Instance != null) MatchManager.Instance.OnUnitSpawned -= OnUnitSpawned;
            foreach (Unit u in hooked) if (u != null) u.OnDie -= OnAllyDied;
            hooked.Clear();
        }
    }
}
