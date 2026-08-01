using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-01 client-data] Таблица клиентских данных («полный ноль» на сервере).
    // Сюда мигратор (Tools → Interflow → Вынос клиентских данных) переносит звуки и иконки из полей
    // сим-классов (Unit-префабы, Ability, Resource, ArmorType, DamageType, Effector, Attribute,
    // WeaponSound, TechNode фракций), после чего исходные поля ОБНУЛЯЮТСЯ на диске.
    // Ассет лежит ВНЕ Resources и попадает в билд ТОЛЬКО по ссылке из ClientContentRehydrator
    // (клиентский компонент сцены): при сборке сервера Roles-стрип вырезает компонент → таблица,
    // все клипы и текстуры НЕ пакуются в серверный билд.
    [CreateAssetMenu(fileName = "ClientContentTable", menuName = "Interflow/Client Content Table")]
    public class ClientContentTable : ScriptableObject
    {
        [Serializable]
        public class UnitEntry
        {
            [Tooltip("Префаб юнита-владельца (ключ).")] public Unit prefab;
            [Tooltip("Иконка юнита (из Unit.icon).")] public Texture2D icon;
            public AudioClip[] ready;
            public AudioClip[] move;
            public AudioClip[] click;
            public AudioClip[] death;
            public AudioClip[] attackCommand;
            public AudioClip[] attackStart;
            public AudioClip[] attackEnd;
        }

        [Serializable]
        public class AbilityEntry
        {
            [Tooltip("Способность-владелец (ключ).")] public Ability ability;
            [Tooltip("Иконки по уровням (из Ability.icon).")] public Texture2D[] icons;
        }

        [Serializable]
        public class SoIconEntry
        {
            [Tooltip("SO-владелец: Resource / ArmorType / DamageType / Effector / Attribute (ключ).")]
            public ScriptableObject asset;
            public Texture2D icon;
        }

        [Serializable]
        public class WeaponSoundEntry
        {
            [Tooltip("WeaponSound-владелец (ключ).")] public WeaponSound asset;
            [Tooltip("Пары «тип брони → клипы удара» (из attackToArmorSound).")] public AttackToArmorSound[] attackToArmor;
            [Tooltip("Клипы удара по земле/без типа (из groundHitClips).")] public AudioClip[] groundHit;
        }

        [Serializable]
        public class TechIconEntry
        {
            [Tooltip("Технология-ключ узла дерева фракции.")] public Technology tech;
            [Tooltip("Иконка узла (из TechNode.icon).")] public Texture2D icon;
        }

        public List<UnitEntry> units = new List<UnitEntry>();
        public List<AbilityEntry> abilities = new List<AbilityEntry>();
        public List<SoIconEntry> soIcons = new List<SoIconEntry>();
        public List<WeaponSoundEntry> weaponSounds = new List<WeaponSoundEntry>();
        public List<TechIconEntry> techIcons = new List<TechIconEntry>();
        [Tooltip("Запечённые конфиги фракций — их деревья техов регидрируются по techIcons.")]
        public List<FactionConfig> factionConfigs = new List<FactionConfig>();
    }
}
