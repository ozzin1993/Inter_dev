using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-09-11] СВЁРТКА ПЕРИОДИЧЕСКОГО УРОНА. Решение Artsiom 11.09.2026: горение, аура,
    /// зона на земле и прочий урон по времени не дают строки на каждый тик — сервер копит их и отдаёт
    /// в показ одной строкой за интервал.
    ///
    /// Почему на сервере, а не в ленте у клиента: тик аур и состояний идёт каждые 0,1 секунды, и при
    /// свёртке у клиента сервер всё равно слал бы до десяти сообщений в секунду на юнита. Свёртка здесь
    /// режет трафик в источнике — уходит одно сообщение за интервал на пару «источник — жертва».
    ///
    /// Таймера у свёртки нет: отправку выталкивает следующий тик (проверка не чаще раза в кадр).
    /// Источник кончился — хвост уйдёт со следующим тиком любого другого источника, либо повиснет
    /// до конца боя; это принятая цена отказа от собственного обновления (правило 5: не заводить
    /// ещё один объект в сцене ради одного таймера).
    ///
    /// Только сервер: сюда заходят из приёмника, а он с шага 4 схемы серверный.
    /// </summary>
    public static class PeriodicDamageRollup
    {
        /// <summary>Пара «источник — жертва»: по ней копится одна строка.</summary>
        struct Key : IEquatable<Key>
        {
            public int victim;     // GetInstanceID жертвы
            public int attacker;   // GetInstanceID бьющего, 0 — источника-юнита нет
            public int ability;    // id умения, −1 — умения нет

            public bool Equals(Key other) => victim == other.victim && attacker == other.attacker && ability == other.ability;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => (victim * 397) ^ (attacker * 31) ^ ability;
        }

        /// <summary>Накопленное по паре: сколько снято, сколько тиков, с какого момента копим.</summary>
        struct Entry
        {
            public Unit victim;
            public Unit attacker;
            public int abilityID;
            public float dealt;
            public int ticks;
            public float started;      // Time.time первого тика этой строки
            public float healthAfter;  // здоровье жертвы после последнего тика
            public float healthBefore; // здоровье жертвы до ПЕРВОГО тика строки
            public float healthMax;
        }

        static readonly Dictionary<Key, Entry> entries = new Dictionary<Key, Entry>();

        /// <summary>Список ключей на выталкивание. Поле, а не локальная переменная: путь горячий.</summary>
        static readonly List<Key> ripe = new List<Key>();

        /// <summary>В каком кадре последний раз проверяли сроки: перебор словаря не нужен на каждый тик.</summary>
        static int lastCheckedFrame = -1;

        /// <summary>
        /// Добавить тик периодического урона. Зовёт приёмник вместо отправки строки на каждый тик.
        /// </summary>
        /// <param name="healthBefore">Здоровье цели до этого тика — нужно первому тику строки.</param>
        public static void Add(Unit victim, Unit attacker, int abilityID, float dealt,
                               float healthBefore, float healthAfter, float healthMax)
        {
            if (victim == null || dealt <= 0f) return;

            Key key;
            key.victim = victim.GetInstanceID();
            key.attacker = attacker != null ? attacker.GetInstanceID() : 0;
            key.ability = abilityID;

            if (entries.TryGetValue(key, out Entry e))
            {
                e.dealt += dealt;
                e.ticks++;
                e.healthAfter = healthAfter;
                e.healthMax = healthMax;
                entries[key] = e;
            }
            else
            {
                e.victim = victim;
                e.attacker = attacker;
                e.abilityID = abilityID;
                e.dealt = dealt;
                e.ticks = 1;
                e.started = Time.unscaledTime;
                e.healthBefore = healthBefore;
                e.healthAfter = healthAfter;
                e.healthMax = healthMax;
                entries.Add(key, e);
            }

            FlushRipe();
        }

        /// <summary>
        /// Отдать в показ всё, что копится дольше интервала. Проверка раз в кадр: тиков за кадр бывает
        /// много (аура задевает десяток целей), а сроки от этого не меняются.
        /// </summary>
        static void FlushRipe()
        {
            if (lastCheckedFrame == Time.frameCount) return;
            lastCheckedFrame = Time.frameCount;


            float interval = Mathf.Max(0.5f, InterflowDebug.periodicRollupSeconds);

            // Время НЕмасштабированное — тем же меряет сроки лента. По Time.time на паузе
            // (timeScale = 0) строки ленты гасли бы, а свёртки не выталкивались никогда.
            float now = Time.unscaledTime;

            ripe.Clear();
            foreach (KeyValuePair<Key, Entry> pair in entries)
                if (now - pair.Value.started >= interval) ripe.Add(pair.Key);

            for (int i = 0; i < ripe.Count; i++)
            {
                Entry e = entries[ripe[i]];
                entries.Remove(ripe[i]);
                Send(in e);
            }
        }

        /// <summary>
        /// Вытолкнуть накопленное по ОДНОЙ жертве прямо сейчас. Зовётся приёмником перед гибелью цели:
        /// после Die её сетевой номер снят с учёта, и отправить строку уже нельзя — накопленное пропало бы.
        /// </summary>
        public static void FlushVictim(Unit victim)
        {
            if (victim == null || entries.Count == 0) return;

            int id = victim.GetInstanceID();

            ripe.Clear();
            foreach (KeyValuePair<Key, Entry> pair in entries)
                if (pair.Key.victim == id) ripe.Add(pair.Key);

            for (int i = 0; i < ripe.Count; i++)
            {
                Entry e = entries[ripe[i]];
                entries.Remove(ripe[i]);
                Send(in e);
            }
        }

        /// <summary>Отдать одну накопленную строку в показ. Отправить нечем — строка просто теряется.</summary>
        static void Send(in Entry e)
        {
            if (e.victim == null) return;                             // жертва уничтожена — строке некуда идти
            if (NetworkDataSync.Instance == null) return;             // сеть не поднята: показывать нечем

            {
                DamageStepInfo info;
                info.victim = e.victim;
                info.attacker = e.attacker;
                info.abilityID = e.abilityID;
                info.hitId = 0;                                       // у свёртки своего удара нет
                info.recordIndex = 0;
                info.recordCount = 1;
                info.declared = 0f;                                   // ступени расчёта в свёртке не копятся
                info.afterRules = 0f;
                info.afterCallbacks = 0f;
                info.afterArmor = 0f;
                info.dealt = e.dealt;
                info.healthBefore = e.healthBefore;
                info.healthAfter = e.healthAfter;
                info.healthMax = e.healthMax;
                info.ticks = e.ticks;
                info.died = false;                                    // смерть от тика идёт своей строкой, минуя свёртку

                NetworkDataSync.Instance.UnitDamageStepSend(in info);
            }
        }

        /// <summary>
        /// Выбросить накопленное. Зовётся сменой сцены: пары прошлого матча в новом не нужны,
        /// а ссылки на уничтоженных юнитов в словаре жить не должны.
        /// </summary>
        public static void ResetAll()
        {
            entries.Clear();
            ripe.Clear();
            lastCheckedFrame = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void WireSceneReset()
        {
            ResetAll();

            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => ResetAll();
    }
}
