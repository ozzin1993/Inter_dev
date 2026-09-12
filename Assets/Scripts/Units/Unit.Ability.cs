using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // This script is responsible for handling of the unit`s abilities
    public partial class Unit
    {
        [Header("Abilities")]
        [Tooltip("Abilities of this unit, you must define all abilities here. You will not be able to change them during the game.")]
        public Ability[] abilities;
        [Tooltip("Можно оставить пустым: пустые места — уровень 0.\nДля улучшаемых умений героя (Hero Levelable) задаёт НАЧАЛЬНЫЙ уровень; умение всё равно должно пройти свои требования.\nОстальные умения начинают с нуля и поднимаются сами, когда требования выполнены.\nУровень −1 («умение не выучено») снесён блоком Б9 (2026-09-06): открытое технологией умение сразу работает на базовых значениях.")]
        public int[] abilityLevel;

        [HideInInspector] public bool[] abilityLocked; // Defined automatically based on the technologies available for the player

        // Списки everyFrameAbilities/-Index/-IsItem (цикл аур) СНЕСЕНЫ блоком Б7 (2026-09-05): постоянные ауры —
        // пассивные умения с радиусом (CompositePassive, блок 8), режима «аура» у умений больше нет (целевая модель §9, §15).
        // В префабах юнитов старые поля остаются в YAML до пересохранения — Unity их молча отбрасывает.

        [HideInInspector] public List<float> cooldownAbility = new List<float>(); // Current cooldown of an ability
        [HideInInspector] public List<int> cooldownAbilityIndex = new List<int>(); // abilityCDIndex[index] = reference to ability[]
        [HideInInspector] public List<bool> cooldownAbilityIsItem = new List<bool>(); // If ability at index is item

        // Active ability technical variables: умение, которое юнит сейчас кастует (подход к цели, время замаха).
        // «Длящихся» умений (каналов) больше нет — снесены блоком Б6 (2026-09-04).
        [HideInInspector] public Ability activeAbility; // Ability that is being cast by this unit
        [HideInInspector] public int activeAbilityLevel; // Level of the ability when it was started
        [HideInInspector] public int activeAbilityIndex; // Index of the ability that is being cast by this unit
        [HideInInspector] public bool activeAbilityItem; // If current active ability is an item. DO NOT FORGET TO CHANGE THE INDEX IF ITEM MOVES ITS SLOT
        [HideInInspector] public Unit activeAbilityUnit; // If active ability requires unit
        [HideInInspector] public Vector3 activeAbilityLocation; // If active ability requires location
        // Флаг «умение исполняется прямо сейчас» (activeAbilityInUse) СНЕСЁН блоком Б6 (2026-09-04):
        // его включал только канал, а после сноса каналов — единственный оставшийся писатель
        // SetActiveAbility из загрузки сохранения, который восстанавливал неверный индекс умения.
        [HideInInspector] public float activeAbilityRange; // Range of active ability
        [HideInInspector] public float activeAbilityCastTime; // Cast timme of active ability
        [HideInInspector] public float activeAbilityDuration; // If this active ability has a duration
        [HideInInspector] public VFXReferencer activeAbilityVFX; // VFX of actively being used ability. Set by abilities` Activate/Deactivate

        private bool playCast = false; // To know if we are currently casting an ability
        private bool sendToClients = false; // To know if we triggered on clients the start of the cast

        // ============================= ABILITY USE =============================

        /// <summary>
        /// Command for the unit to cast an ability or an item.
        /// </summary>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this in item?</param>
        /// <param name="unit">Target unit, can be null.</param>
        /// <param name="location">Target location, can be Vector3.zero.</param>
        public bool UseAbilityItem(int abilityIndex, bool isItem, Unit unit, Vector3 location, bool issuedByPlayer = false)
        {
            if (muted) return false;

            // [Interflow fix 2026-09-09 stun-blocks-ability] Оглушение закрывает применение умения (дефект F15).
            // Прежде оглушение обрывало ТЕКУЩЕЕ умение (Unit.Control.StunEnter) и снимало агента, но вход сюда
            // не закрывало: ветка каста замирала косвенно (оглушённому юниту заморожен автомат состояний —
            // Unit.cs, гейт «!stunned && !isBeingBuilt» в Update), а МГНОВЕННОЕ умение (нулевые дальность и время
            // каста) исполняется прямо из этого вызова, мимо автомата, — и срабатывало у оглушённого и по приказу
            // игрока, и по авто-касту. Отсев стоит здесь, а не в приёмниках сети и не в AutoAbilityUser: сюда
            // приходят ВСЕ входы (правило 5).
            // Команда ОТБРАСЫВАЕТСЯ, а не запоминается (решение Artsiom 09.09.2026) — как у немоты выше.
            // Следствие, принятое вместе с решением: флаг ставится и на клиенте (Unit.Control.StunEnterClient),
            // поэтому клиент перестаёт слать команду серверу. Так же ведёт себя muted; источник истины — сервер.
            if (stunned) return false;

            if (isBeingBuilt) return false;

            // Clients send the command to the server
            if (NetworkConnectionHandler.isClient)
            {
                NetworkCommandSync.Instance.UseAbilityCommandSend(this, abilityIndex, isItem, unit, location);
                return true;
            }

            // Границы пула. Номер приходит от клиента (три приёмника умений в NetworkCommandSync) и уходит
            // в items/abilityLevel/abilityLocked без проверки, а у обычного умения ещё и разрешается рекурсивным
            // поиском по контейнерам — «в пределах массива» и «умение существует» это РАЗНЫЕ условия.
            // Проверка стоит в общей точке, а не в трёх приёмниках: сюда же приходят автокаст и менеджеры матча.
            if (isItem)
            {
                if (items == null || abilityIndex < 0 || abilityIndex >= items.Length) return false;
            }
            else
            {
                if (abilityLevel == null || abilityLocked == null || abilityIndex < 0
                    || abilityIndex >= abilityLevel.Length || abilityIndex >= abilityLocked.Length) return false;
                if (Utils.GetAbilityByIndex(this, abilityIndex) == null) return false;
            }

            if (isItem && items[abilityIndex] == null) return false;
            if (CheckAbilityItemRequirements(owner, abilityIndex, isItem)) return false;
            if (!IsCooldownGood(abilityIndex, isItem)) return false;

            Ability currentAbility;
            int currentLevel;
            if (isItem)
            {
                currentAbility = (Ability)items[abilityIndex];
                currentLevel = 0;
            }
            else
            {
                currentAbility = Utils.GetAbilityByIndex(this, abilityIndex);
                currentLevel = abilityLevel[abilityIndex];
            }

            // Готовность АВТО-умения: мана носителя заполнена целиком (целевая модель §9). Проверка жила
            // только в компоненте (AutoAbilityUser.TryCastEntry) и по сети обходилась — клиент слал каст
            // напрямую, умение срабатывало на неполной мане, а списание всё равно забирало всю.
            // Предикат тот же, что у списания (Unit.SubtractAbilityItemCost), — разъехаться они не могут.
            // Активируемые умения идут по откату и маны не требуют, поэтому гейт только для авто-умений
            // (решение Artsiom 09.09). Отказ молчаливый (его же решение): удержание кнопки залило бы консоль.
            if (!isItem && TryGetComponent(out AutoAbilityUser autoUser) && autoUser.IsAutoAbility(currentAbility)
                && (maxMana <= 0f || mana < maxMana)) return false;

            if (!firstAttack) AttackStop();
            OnCommand?.Invoke(issuedByPlayer);

            // Set active ability parameters
            activeAbility = currentAbility;
            activeAbilityIndex = abilityIndex;
            activeAbilityItem = isItem;
            activeAbilityLevel = currentLevel;
            activeAbilityLocation = location;
            activeAbilityUnit = unit;
            // Дальность, время каста и длительность — общей выборкой по уровням (Б8): нет строки — последняя заполненная
            // (раньше уровень выше длины массива давал 0: каст без подхода и без замаха).
            activeAbilityRange = InterflowAbility.LevelValue(currentAbility.castRange, currentLevel);
            activeAbilityCastTime = InterflowAbility.LevelValue(currentAbility.castTime, currentLevel);
            activeAbilityDuration = InterflowAbility.LevelValue(currentAbility.duration, currentLevel);
            activeAbilityDuration += activeAbilityCastTime;

            // If has cast range or cast time we change the state (умений-каналов больше нет — блок Б6)
            if (activeAbilityRange != 0 || activeAbilityCastTime != 0)
            {
                unitState = UnitStates.AbilityCasting;
                OnCommand += ResetAbilityState;
                if (activeAbilityUnit) activeAbilityUnit.OnReferenceChange += AbilityUnitReferenceChange;
                MakeAgent(false);
                // Оглушение, пришедшее ПОСЛЕ начала каста, обрывает его, но поля активного умения остаются
                // (EndActiveAbility(false, …)), и StunExit перезапускает замах с нуля. Команду, отданную
                // УЖЕ оглушённому, сюда не пускает отсев в начале метода (F15) — храниться ей негде.
                currentActionTime = 0;
            }
            // Otherwise we just use ability and do not stop the current action (Moving)
            else
            {
                // Use ability immediately
                UseAbilityImmediately(currentAbility, currentLevel, abilityIndex, isItem, unit, location, false);
            }

            return true;
        }

        /// <summary>
        /// Uses given ability immediately, last method called when user commmands to cast an ability.
        /// </summary>
        /// <param name="ability">Ability to use.</param>
        /// <param name="abilityLevel">Level of the ability.</param>
        /// <param name="abilityIndex">Global index of ability in the ability pool of the unit. You can get it by Utils.GetAbilityIndex.</param>
        /// <param name="isItem">Is this an item?</param>
        /// <param name="abilityTarget">Target unit of the ability, can be null.</param>
        /// <param name="abilityLocation">Target location of the ability, can be Vector3.zero.</param>
        /// <param name="interrupt">Should cast interrupt the unit? When true will call Idle() after cast.</param>
        public void UseAbilityImmediately(Ability ability, int abilityLevel, int abilityIndex, bool isItem, Unit abilityTarget, Vector3 abilityLocation, bool interrupt = false)
        {
            // Do the last custom check of the ability before using it
            bool customCheck = true;
            if (abilityTarget) customCheck = ability.Check(this, this.owner, abilityLevel, abilityTarget);
            else if (abilityLocation != Vector3.zero) customCheck = ability.Check(this, this.owner, abilityLevel, abilityLocation);
            else customCheck = ability.Check(this, this.owner, abilityLevel);
            if (customCheck == false)
            {
                // Умение отказалось от каста (например, названная цель не прошла отбор). Юнит к этому моменту
                // мог УЖЕ войти в состояние каста: UseAbilityItem ставит AbilityCasting при ненулевой дальности
                // или времени замаха и зовёт этот метод позже, из машины состояний. Простой return оставлял его
                // там навсегда — каждый кадр приходил сюда снова и снова получал отказ, а после закрытия
                // индивидуальных команд вывести его оттуда стало нечем. Завершаем каст штатно, тем же Idle(),
                // что и соседняя ветка требований в Unit.State.cs.
                if (!NetworkConnectionHandler.isClient && unitState == UnitStates.AbilityCasting) Idle();
                return;
            }

            // When server activates any ability, we send data to clients
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkDataSync.Instance.AbilityUseSend(this, ability, abilityLevel, abilityIndex, isItem, abilityTarget, abilityLocation, interrupt);
            }

            // if interrupt we make unit visible
            if (interrupt && isInvisible) SetInvisibility(false);

            // Ability initiate. Умения-каналы (continuous) и их теневой кастер снесены блоком Б6 (2026-09-04):
            // каст всегда разовый — применили и вышли.
            if (abilityTarget) ability.Use(this, this.owner, abilityLevel, abilityTarget);
            else if (abilityLocation != Vector3.zero) ability.Use(this, this.owner, abilityLevel, abilityLocation);
            else ability.Use(this, this.owner, abilityLevel);

            // Reset the states after cast
            EndActiveAbility(true, false);
            // if interrupt we Idle() after cast
            if (interrupt && !NetworkConnectionHandler.isClient) Idle();

            // Cooldown
            // Откат — общей выборкой по уровням (Б8): нет строки — последняя заполненная (раньше уровень выше длины гасил откат).
            float levelCooldown = InterflowAbility.LevelValue(ability.cooldown, abilityLevel);
            if (levelCooldown != 0) ChangeAbilityCooldown(levelCooldown, abilityIndex, isItem);
            // Subtract costs
            SubtractAbilityItemCost(owner, abilityIndex, isItem);
            // Item charge decrease
            if (isItem) ItemChargesChange(abilityIndex);
        }

        // ============================= EVERY FRAME (регенерация) =============================

        // Переключатели снесены блоком Б6 (2026-09-04), ауры — блоком Б7 (2026-09-05): методов регистрации
        // умений в цикле юнита (AddEveryFrameAbility, RemoveEveryFrameIfExists, IndexOfEveryFrameAbility) больше нет.
        // Каждый тик у юнита остаётся только регенерация здоровья и маны.

        /// <summary>
        /// Every gameManager.Tick: health and mana regeneration.
        /// Имя осталось от цикла умений «каждый кадр»: переключатели снесены блоком Б6 (2026-09-04),
        /// ауры — блоком Б7 (2026-09-05); подписка на тик живёт в Unit.Init/Lifecycle и ConstructionUnit.
        /// </summary>
        public void HandleEveryFrameAbilities()
        {
            // MP/HP regeneration
            if (healthRegen != 0) ChangeHP(healthRegen * GameManager.Instance.currentDeltaTime, true);
            if (manaRegen != 0) ChangeMP(manaRegen * GameManager.Instance.currentDeltaTime, true);
        }




    }
}
