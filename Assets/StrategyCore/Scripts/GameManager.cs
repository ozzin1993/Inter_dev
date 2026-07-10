// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/GameManager.cs. Реестр: wiki concepts/asset-fork-debt.
using Camera_TopDownNS;
using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager instance;

        [Header("Camera")]
        public Camera_TopDown camera_TopDown;
        [Tooltip("Какая часть края карты показывается игрокам. 5 — оптимально: юниты не перекрываются интерфейсом.")]
        public float cameraEdge = 5;

        [Header("Teams")]
        [Tooltip("Если включено, игроки сами выбирают свою команду")]
        public bool chooseTeams = true;
        [Tooltip("Задаёт имена команд и заняты ли слоты ботами")]
        public TeamsAndPlayers[] teamsAndPlayers;

        [Header("Factions")]
        public FactionData[] factionData;

        [Header("Win Conditions")]
        [Tooltip("Если включено: команда побеждает, когда уничтожены все вражеские юниты и здания")]
        public bool allUnitsBuildingsDead;
        [Tooltip("Если включено: команда побеждает, когда уничтожены все вражеские здания")]
        public bool allBuildingsDestroyed;
        [Tooltip("Если опции выше выключены — должны ли для победы погибнуть конкретные юниты")]
        public SpeficicUnitsDead[] specificUnitsDead;
        [HideInInspector] public HashSet<UInt16>[] specificUnitsToDie; // We copy from the list above into a hashset for quick look up
        private bool specificUnitsShouldDie; // If game has specific unit to die winning condition

        [Header("Game Setup")]
        [Tooltip("Включите, если в игре есть невидимые юниты. Отключение повышает производительность")]
        public bool gameIncludesInvisible = true;
        [Tooltip("По умолчанию в тумане войны видны только статичные разрушаемые / статичные объекты; при включении сквозь туман будут видны и здания")]
        public bool showBuildingsInFow = false;
        [Tooltip("Возвращать ли при отмене строительства все потраченные ресурсы.\nЕсли выключено — вернётся доля ресурсов в зависимости от текущего времени строительства")]
        public bool constructionCancelFullReturn = false;

        // HandleRewardGain in Utils.cs defines the logic
        [Header("Rewards")]
        [Tooltip("Получают ли опыт все, кто рядом с погибшим юнитом, если они из разных команд. Если выключено — опыт получает только убивший")]
        public bool spreadEXP = true;
        [Tooltip("Если spreadEXP включён — на каком расстоянии юниты могут получать опыт от убитого юнита")]
        public float spreadRange = 5;
        [Tooltip("Получают ли все юниты в радиусе одинаковое количество опыта; если выключено — опыт делится между всеми юнитами")]
        public bool equalEXP = false;
        [Tooltip("Сколько опыта и денежной награды даётся за убийство союзных юнитов. 0 = 0%, 1 = 100%.")]
        public float allyKillReward = 0;

        [Header("Shop")]
        [Tooltip("В каком радиусе от магазина юниты могут покупать и продавать предметы")]
        public float shopRadius = 3;
        [Tooltip("За какую долю исходной цены можно продать предмет. 0 = 0%, 1 = 100%")]
        public float sellPriceReduction = 1;

        // Technical
        private bool onlyOnce = false;
        [HideInInspector] public bool gameStartCall = false; // If we are not coming from lobby, we want to call GameStart() once
        public static int maxProcessCount = 14; // 14 processes maximum
        public static int maxInventorySize = 6;

        public static float tickRate = 0.1f; // Every 100ms we call effectors, auras and other parts of the game. Not related to network tickrate
        [HideInInspector] public float currentDeltaTime = 0; // Current time for effectors and auras
        [HideInInspector] public int tickCount = 0;
        [HideInInspector] public bool tickThisFrame = false; // True when tick happened this frame
        public Action Tick; // Effectors and Auras subscribe to this
        public Action OnTeamChange; // Called when current team

        public int[] unitCount; // The number of units and buildings certain player has
        public int[] buildingCount; // The number of buildings certain player has
        private bool _checkWinUpdate;

        // Reference to agent type IDs
        [HideInInspector] public int[] agentTypes;

        // Hold player amount of units that player controls, by unit type.
        // Example: playerUnits[playerID][UnitTypeID] is 5, means there are 5 units of this type that belong to specified player.
        //public static Dictionary<int, int>[] playerUnits = new Dictionary<int, int>[14];

        // Stores all shadowcasters - to send information about them to clients
        [HideInInspector] public List<ShadowCaster> shadowCasters = new List<ShadowCaster>();
        [HideInInspector] public List<int> shadowCasterIDs = new List<int>();

        // Defines percentage relation of damage to armor types. Defined in start method below.
        // Example: damageToArmor[armor.index * DTAWidth + damage.index] is 1, meaning Standard damage type deals 100% of damage to standard armor type.
        [HideInInspector] public float[] damageToArmor;
        [HideInInspector] public int DTAWidth;

        // Damage in Time
        [HideInInspector] public List<(int, Unit, Unit, float, DamageType)> damageInList = new List<(int, Unit, Unit, float, DamageType)>(); // Player that damages, Unit that damages, Unit damaged, Damage amount, Damage type
        [HideInInspector] public List<float> damageInTime = new List<float>(); // Time in which damage should occur

        // Called whenever NavMesh is updated
        // public static Action OnNavmeshUpdate;

        // Store abilities and ids - for finding the ability by its id
        public Dictionary<int, Ability> gameAbilities = new Dictionary<int, Ability>();
        // GamePrefabs - collection of all possible units of the game
        public Dictionary<int, Unit> gameUnits = new Dictionary<int, Unit>();
        // GameEffectors - collection of all possible effectors of the game
        public Dictionary<int, Effector> gameEffectors = new Dictionary<int, Effector>();
        // ArmorTypes - collection of all possible ArmorTypes of the game
        public Dictionary<int, ArmorType> armorTypes = new Dictionary<int, ArmorType>();
        // DamageTypes - collection of all possible DamageTypes of the game
        public Dictionary<int, DamageType> damageTypes = new Dictionary<int, DamageType>();
        // Projectiles - collection of all possible Projectiles of the game
        public Dictionary<int, Projectile> gameProjectiles = new Dictionary<int, Projectile>();
        // VFXLines - collection of all possible VFXLines of the game
        public Dictionary<int, VFXLine> gameVFXLines = new Dictionary<int, VFXLine>();

        // Start is called before the first frame update
        void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }

            // For performance reasons
            Utils.cachedMainCamera = Camera.main;

            if (ReferenceManager.instance == null) ReferenceManager.instance = GetComponent<ReferenceManager>();
            if (Grid.instance == null) Grid.instance = GetComponent<Grid>();

            // Camera bounds
            if (camera_TopDown == null)
            {
                GameObject obj = GameObject.Find("CameraRig");
                if (obj != null)
                {
                    camera_TopDown = obj.GetComponent<Camera_TopDown>();
                    if (camera_TopDown == null)
                    {
                        Debug.LogError("Your camera is not named CameraRig! You must manually set in GameManager camera_TopDown.");
                    }
                }
            }
            if (camera_TopDown != null)
            {
                camera_TopDown.squareLimits = new Vector4(Grid.instance.height + cameraEdge, Grid.instance.width + cameraEdge, cameraEdge, cameraEdge);
            }

            Initialize();
            // Data dictionaries
            DictionariesInitialize();
            // Navmesh
            NavmeshInitialization();
            // VFX
            VFXReferencerInit();
        }

        void Start()
        {
            // Properly position GameManager in the center of the map
            transform.position = new Vector3(Grid.instance.width * 0.5f, 0, Grid.instance.height * 0.5f);
            if (gameStartCall) GameStart(); // gameStartCall == true only when we are not coming from lobby i.e. testing in UnityEditor
        }

        // Update is called once per frame
        void Update()
        {
            if (!SlotManager.instance.gameOn) return;

            if (onlyOnce == false)
            {
                CheckWinningConditions();
                onlyOnce = true;
            }

            currentDeltaTime += Time.deltaTime;
            tickThisFrame = false;
            if (currentDeltaTime >= tickRate)
            {
                Tick?.Invoke();
                tickThisFrame = true;
                currentDeltaTime = 0;
                tickCount++;
            }
            DamageInUpdate();
        }

        private void LateUpdate()
        {
            // Check winning conditions in late update, so we can properly catch draws
            if (_checkWinUpdate)
            {
                CheckWinningConditions();
                _checkWinUpdate = false;
            }
        }

        // ============================= INITIALIZE ==============================================================================

        void DictionariesInitialize()
        {
            // Awake method of the scriptable objects is broken. So we call Init() of abilities manually

            // ABILITIES ----- Call Init() of the abilities
            Ability[] abilities = Resources.LoadAll<Ability>("Ability");
            for (int i = 0; i < abilities.Length; i++)
            {
                gameAbilities.Add(abilities[i].id, abilities[i]);
                abilities[i].Init();
            }

            // No Longer: ATTRIBUTES ----- Call AbilityPassiveEffects.Init() for attributes - Because of unity bug, values will be set by default values to 0, for percentages we must change them to 1
            // Attribute[] attributes = Resources.LoadAll<Attribute>("Attributes");
            // for (int i = 0; i < attributes.Length; i++) AbilityPassiveEffects.Init(attributes[i].passiveEffects);

            // EFFECTORS ----- Call AbilityPassiveEffects.Init() for effectors
            Effector[] effectors = Resources.LoadAll<Effector>("Effectors");
            for (int i = 0; i < effectors.Length; i++)
            {
                gameEffectors.Add(effectors[i].id, effectors[i]);
                // AbilityPassiveEffects.Init(effectors[i].passiveEffects); // No longer needed
            }

            // DAMAGE TYPES ----- 
            DamageType[] damage = Resources.LoadAll<DamageType>("DamageTypes");
            for (int i = 0; i < damage.Length; i++)
            {
                damage[i].index = i;
                damageTypes.Add(damage[i].id, damage[i]);
            }

            // ARMORS TYPES ----- 
            ArmorType[] armor = Resources.LoadAll<ArmorType>("ArmorTypes");
            for (int i = 0; i < armor.Length; i++)
            {
                armor[i].index = i;
                armorTypes.Add(armor[i].id, armor[i]);
            }

            // UNITS ----- 
            GameObject[] unitPrefabs = Resources.LoadAll<GameObject>("UnitPrefabs");
            for (int i = 0; i < unitPrefabs.Length; i++)
            {
                Unit unit = unitPrefabs[i].GetComponent<Unit>();
                if (unit && !gameUnits.ContainsKey(unit.unitTypeID)) gameUnits.Add(unit.unitTypeID, unit);
            }

            // WEAPON SOUND -----
            WeaponSound[] weaponSounds = Resources.LoadAll<WeaponSound>("WeaponSound");

            for (int i = 0; i < weaponSounds.Length; i++)
            {
                weaponSounds[i].weaponSound = new AttackToArmorSound[armorTypes.Count];

                for (int a = 0; a < armor.Length; a++)
                {
                    bool soundDefined = false;

                    // Check if sound for this armor type was defined by user
                    for (int s = 0; s < weaponSounds[i].attackToArmorSound.Length; s++)
                    {
                        if (weaponSounds[i].attackToArmorSound[s].armorType == armor[a])
                        {
                            // Check if audio clips were added by the user
                            if (weaponSounds[i].attackToArmorSound[s].audioClips.Length > 0)
                            {
                                weaponSounds[i].weaponSound[armor[a].index] = weaponSounds[i].attackToArmorSound[s];
                            }
                            soundDefined = true;
                            break;
                        }
                    }

                    // Sound was not defined for this armor type
                    if (!soundDefined)
                    {
                        // Check if ground hit sound was defined
                        if (weaponSounds[i].groundHitClips.Length > 0)
                        {
                            // Copy of groundHitClips
                            weaponSounds[i].weaponSound[armor[a].index] = new AttackToArmorSound();
                            weaponSounds[i].weaponSound[armor[a].index].audioClips = weaponSounds[i].groundHitClips;
                        }
                        else
                        {
                            // Null
                            weaponSounds[i].weaponSound[armor[a].index] = new AttackToArmorSound();
                        }
                    }
                }
            }

            // PROJECTILES ----- 
            GameObject[] projectiles = Resources.LoadAll<GameObject>("Projectiles");
            for (int i = 0; i < projectiles.Length; i++)
            {
                Projectile projectile = projectiles[i].GetComponent<Projectile>();
                if (projectile && !gameProjectiles.ContainsKey(projectile.id)) gameProjectiles.Add(projectile.id, projectile);
            }

            // VFXLines -----
            GameObject[] vfxLines = Resources.LoadAll<GameObject>("VFXLine");
            for (int i = 0; i < vfxLines.Length; i++)
            {
                VFXLine vfxLine = vfxLines[i].GetComponent<VFXLine>();
                if (vfxLine && !gameVFXLines.ContainsKey(vfxLine.id)) gameVFXLines.Add(vfxLine.id, vfxLine);
            }

            // DAMAGE TO ARMOR -----
            DamageToArmor(damage, armor);
        }

        // Sets the relationship between damage types and armor types
        void DamageToArmor(DamageType[] damage, ArmorType[] armor)
        {
            DTAWidth = damage.Length;
            damageToArmor = new float[damage.Length * armor.Length];

            for (int i = 0; i < damage.Length; i++)
            {
                for (int q = 0; q < armor.Length; q++)
                {
                    bool _continue = false;

                    for (int r = 0; r < damage[i].damageEffectivness.Length; r++)
                    {
                        // We check if damage effectiveness for this armor type was specified by the player
                        if (armor[q] == damage[i].damageEffectivness[r].type)
                        {
                            damageToArmor[armor[q].index * DTAWidth + damage[i].index] = damage[i].damageEffectivness[r].value;

                            _continue = true;
                            break;
                        }
                    }

                    if (!_continue)
                    {
                        // Damage to armor relation was not specified by the player, we set it to 1
                        damageToArmor[armor[q].index * DTAWidth + damage[i].index] = 1f;
                    }
                }
            }
        }

        void NavmeshInitialization()
        {
            // Similar code is in SCEditor.cs SceneDataSet
            Transform navmeshParent = GameObject.Find("Navmesh").transform;

            // Agent Types
            agentTypes = new int[3];
            int count = NavMesh.GetSettingsCount();
            for (var i = 0; i < count; i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                agentTypes[i] = id;
                if (Utils.agentTypeRadius < NavMesh.GetSettingsByIndex(i).agentRadius) Utils.agentTypeRadius = NavMesh.GetSettingsByIndex(i).agentRadius;
            }

            // Position navmesh in the center of the map and set the boundaries
            NavMeshSurface groundNavmesh = navmeshParent.Find("GroundNavmesh").GetComponent<NavMeshSurface>();
            NavMeshSurface waterNavmesh = navmeshParent.Find("WaterNavmesh").GetComponent<NavMeshSurface>();

            groundNavmesh.center = new Vector3(Grid.instance.width * 0.5f, 0, Grid.instance.height * 0.5f);
            groundNavmesh.size = new Vector3(Grid.instance.width - 0.5f, Utils.raycastPointY, Grid.instance.height - 0.5f);

            waterNavmesh.center = groundNavmesh.center;
            waterNavmesh.size = groundNavmesh.size;

            groundNavmesh.BuildNavMesh();
            waterNavmesh.BuildNavMesh();

            // Air navmesh surface
            Utils.airOffsetX = Grid.instance.width * 1.5f;
            NavMeshSurface airNavmesh = navmeshParent.Find("AirNavmesh").GetComponent<NavMeshSurface>();
            airNavmesh.transform.position = new Vector3(Utils.airOffsetX, 0, 0);
            airNavmesh.center = groundNavmesh.center;
            airNavmesh.size = groundNavmesh.size;

            BoxCollider box = airNavmesh.GetComponent<BoxCollider>();
            box.center = airNavmesh.center;
            box.size = new Vector3(airNavmesh.size.x, 0.01f, airNavmesh.size.z);

            airNavmesh.BuildNavMesh();

            // Invisibility Navmesh
            Utils.invisibilityOffsetY = Grid.instance.height * 1.5f;
            NavMeshSurface invisNav = navmeshParent.Find("InvisibilityNavmesh").GetComponent<NavMeshSurface>();
            if (gameIncludesInvisible)
            {
                invisNav.gameObject.SetActive(true);

                // Not needed since we do this in scene play and save?
                // invisNav.transform.position = new Vector3(0, 0, 0);
                // invisNav.center = groundNavmesh.center;
                // invisNav.size = groundNavmesh.size;
                // invisNav.BuildNavMesh();
                // invisNav.transform.position = new Vector3(0, 0, Utils.invisibilityOffsetY);

                // Copy Environment/
                GameObject envGO = GameObject.Find("/Environment");
                if (envGO != null)
                {
                    GameObject env = Instantiate(envGO);
                    env.transform.position += new Vector3(0, 0, Utils.invisibilityOffsetY);
                }
            }
            else invisNav.gameObject.SetActive(false);
        }

        // Sets unique id for all VFXReferencer types
        void VFXReferencerInit()
        {
            VFXReferencer[] vfx = Resources.LoadAll<VFXReferencer>("VFX");
            List<int> ids = new List<int>(vfx.Length);

            for (int i = 0; i < vfx.Length; i++)
            {
                int newID = UnityEngine.Random.Range(1, 99999);
                while (ids.Contains(newID))
                {
                    newID = UnityEngine.Random.Range(1, 99999);
                }

                ids.Add(newID);
                vfx[i].id = newID;
            }
        }

        public void Initialize()
        {
            unitCount = new int[Enum.GetNames(typeof(Players)).Length];
            buildingCount = new int[Enum.GetNames(typeof(Players)).Length];

            // Copy to hashset units that must die for winning conditions
            if (specificUnitsDead.Length > 0)
            {
                specificUnitsShouldDie = true;
                specificUnitsToDie = new HashSet<UInt16>[specificUnitsDead.Length];
                for (int i = 0; i < specificUnitsDead.Length; i++)
                {
                    specificUnitsToDie[i] = new HashSet<UInt16>();
                    for (int q = 0; q < specificUnitsDead[i].unitIds.Length; q++)
                    {
                        specificUnitsToDie[i].Add(specificUnitsDead[i].unitIds[q]);
                    }
                }
            }
            else specificUnitsShouldDie = false;
        }

        // Called only once when the scene starts and everything finishes loading. Loading a game will not fire this method
        public void GameStart()
        {
            // Find level data
            GameObject spawnPoints = GameObject.Find("LevelData/SpawnPoints");
            if (spawnPoints == null || spawnPoints.transform.childCount == 0) return;
            int maxSpawnPositions = spawnPoints.transform.childCount;

            // Camera
            Vector3 pos = spawnPoints.transform.GetChild(SlotManager.instance.playerPosition[SlotManager.instance.currentPlayer]).position;
            camera_TopDown.transform.position = new Vector3(pos.x, 0, pos.z);

            // Only on server
            if (NetworkConnectionHandler.isClient) return;
            // Used for spawning faction units
            if (factionData == null || factionData.Length == 0) return;

            for (int i = 0; i < SlotManager.instance.slotType.Length; i++)
            {
                if (SlotManager.instance.slotType[i] != SlotType.Empty)
                {
                    // Checks
                    if (SlotManager.instance.playerPosition[i] >= maxSpawnPositions)
                    {
                        Debug.LogWarning("For player at the slot " + i + " we could not spawn the faction units, since the player position " + SlotManager.instance.playerFaction[i] + " does not exist in the scene.");
                        continue;
                    }

                    // Spawn units for this faction
                    Transform playerPosition = spawnPoints.transform.GetChild(SlotManager.instance.playerPosition[i]);
                    UnitsForSpawn[] ufs = factionData[SlotManager.instance.playerFaction[i]].UnitsForSpawn;
                    for (int q = 0; q < ufs.Length; q++)
                    {
                        if (ufs[q].subSpawnIndex >= playerPosition.childCount)
                        {
                            Debug.LogWarning("For player at the slot " + i + " we could not spawn the faction units at index " + q + ", since the subSpawn position " + ufs[q].subSpawnIndex + " does not exist in the scene.");
                            continue;
                        }
                        Vector3 spawnPos = playerPosition.GetChild(ufs[q].subSpawnIndex).position;
                        for (int c = 0; c < ufs[q].count; c++)
                        {
                            Unit.Spawn(ufs[q].unitToSpawn, spawnPos, 0, i);
                        }
                    }
                }
            }
            if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerListSend(0, true);
        }

        // ============================= WINNING CONDITIONS ==============================================================================

        public void AddUnitCount(Unit unit)
        {
            if (unit.unitType == UnitType.Unit)
            {
                GameManager.instance.unitCount[unit.owner]++;
            }
            else if (unit.unitType == UnitType.Building)
            {
                GameManager.instance.unitCount[unit.owner]++;
                GameManager.instance.buildingCount[unit.owner]++;
            }
        }

        public void RemoveUnitCount(Unit unit)
        {
            if (unit.unitType == UnitType.Unit)
            {
                GameManager.instance.unitCount[unit.owner]--;
            }
            else if (unit.unitType == UnitType.Building)
            {
                GameManager.instance.unitCount[unit.owner]--;
                GameManager.instance.buildingCount[unit.owner]--;
            }

            _checkWinUpdate = true;
        }

        public void CheckWinningConditions()
        {
            if (NetworkConnectionHandler.isClient) return;

            // Should check and fire win/lose trigger only after 1 frame to properly catch draws, also for units to properly execute all Die functions ?
            if (allUnitsBuildingsDead)
            {
                // Buildings and Units
                CheckTheUnitCount(unitCount);
            }
            else if (allBuildingsDestroyed)
            {
                // Only buildings
                CheckTheUnitCount(buildingCount);
            }

            void CheckTheUnitCount(int[] refList)
            {
                int playersRemaining = 0;
                int remainingPlayer = -1;
                for (int i = 0; i < refList.Length - 2; i++) // -2 because the last two are for neutrals
                {
                    if (refList[i] != 0)
                    {
                        remainingPlayer = i;
                        playersRemaining++;
                        continue;
                    }
                    if (SlotManager.instance.playerLost[i]) continue;
                    // This player lost
                    PlayerLoses(i);
                }

                if (playersRemaining == 0)
                {
                    // Draw
                    // Everyone is dead, everyone loses.
                }
                else if (playersRemaining == 1)
                {
                    // Remaining teams wins
                    PlayerWins(remainingPlayer);
                }
            }
        }

        // Specific units should die winning condition
        public void SubscribeToSpecificWinningConditions(Unit unit)
        {
            if (specificUnitsShouldDie == false || NetworkConnectionHandler.isClient) return;

            for (int i = 0; i < specificUnitsToDie.Length; i++)
            {
                if (specificUnitsToDie[i].Contains(unit.netID))
                {
                    unit.OnDie += OnSpecificUnitDie;
                }
            }
        }

        public void OnSpecificUnitDie(Unit unitThatDies, int playerThatKills, Unit unitThatKills, bool rewards)
        {
            for (int i = 0; i < specificUnitsToDie.Length; i++)
            {
                if (specificUnitsToDie[i].Contains(unitThatDies.netID))
                {
                    specificUnitsToDie[i].Remove(unitThatDies.netID);

                    if (specificUnitsToDie[i].Count == 0)
                    {
                        // No more units to die, win condition
                        if (specificUnitsDead[i].killerTeam)
                        {
                            TeamWins(SlotManager.instance.playerTeam[playerThatKills]);
                        }
                        else
                        {
                            TeamWins(specificUnitsDead[i].winningTeam);
                        }
                    }
                }
            }
        }

        // The 3 functions below are called by the server only
        public void PlayerLoses(int playerIndex)
        {
            SlotManager.instance.playerLost[playerIndex] = true;
            UIManager.instance.AddChatServerMsg("Player " + SlotManager.instance.playerName[playerIndex] + " has lost the game!");

            // Check if allies of this player still playing
            int[] allies = SlotManager.instance.GetPlayerAllies(playerIndex);

            bool allyStillPlaying = false;
            for (int i = 0; i < allies.Length; i++)
            {
                if (SlotManager.instance.playerLost[allies[i]] == false)
                {
                    allyStillPlaying = true;
                    break;
                }
            }

            if (allyStillPlaying)
            {
                // Only this player lost, the rest of the player's team are still alive
                if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerLosesSend(playerIndex);
                else if (playerIndex == SlotManager.instance.currentPlayer) SlotManager.instance.PlayerLoses();
            }
            else
            {
                // The whole team lost, it is game over for them
                for (int i = 0; i < allies.Length; i++)
                {
                    if (SlotManager.instance.slotType[allies[i]] == SlotType.Player)
                    {
                        if (NetworkDataSync.instance) NetworkDataSync.instance.TeamLosesSend(playerIndex);
                        else if (playerIndex == SlotManager.instance.currentPlayer) SlotManager.instance.TeamLoses();
                    }
                }
            }
        }

        private void PlayerWins(int playerIndex)
        {
            int[] allies = SlotManager.instance.GetPlayerAllies(playerIndex);
            for (int i = 0; i < allies.Length; i++)
            {
                if (SlotManager.instance.slotType[allies[i]] == SlotType.Player)
                {
                    if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerWinsSend(playerIndex);
                    else if (i == SlotManager.instance.currentPlayer) SlotManager.instance.PlayerWins();
                }
            }
        }

        private void TeamWins(int teamIndex)
        {
            // Team wins, Everyone else loses
            for (int i = 0; i < SlotManager.instance.playerTeam.Length; i++)
            {
                if (SlotManager.instance.slotType[i] == SlotType.Player)
                {
                    if (SlotManager.instance.playerTeam[i] == teamIndex)
                    {
                        if (NetworkDataSync.instance) NetworkDataSync.instance.PlayerWinsSend(i);
                        else if (i == SlotManager.instance.currentPlayer) SlotManager.instance.PlayerWins();
                    }
                    else
                    {
                        if (NetworkDataSync.instance) NetworkDataSync.instance.TeamLosesSend(i);
                        else if (i == SlotManager.instance.currentPlayer) SlotManager.instance.TeamLoses();
                    }
                }
            }
        }

        // ============================= GAMEPLAY ==============================================================================

        // Damages the unit in specified amount of time
        private void DamageInUpdate()
        {
            for (int i = damageInList.Count - 1; i >= 0; i--)
            {
                damageInTime[i] -= Time.deltaTime;
                if (damageInTime[i] < 0)
                {
                    damageInList[i].Item3.GetDamage(damageInList[i].Item4, damageInList[i].Item5, damageInList[i].Item1, damageInList[i].Item2, false, out _);
                    damageInList.RemoveAt(i);
                    damageInTime.RemoveAt(i);
                }
            }
        }

        private void ChangeTeam(int team)
        {
            // Do not use
            SlotManager.instance.currentTeam = team;
            OnTeamChange?.Invoke();
        }

        // ============================= SCENE ==============================================================================

        // Deletes all units and associated data
        public void ClearScene()
        {
            // Reset selection
            PlayerControl.instance.Reset();

            // For each unit
            foreach (KeyValuePair<UInt16, Unit> entry in SlotManager.instance.unitNetID)
            {
                // Unsubscribe from events this unit was subscribed to
                entry.Value.Unsubscribe();
                entry.Value.OnDie -= GameManager.instance.OnSpecificUnitDie;
                SlotManager.instance.OnGameStart -= entry.Value.StartCallback;
                GameManager.instance.OnTeamChange -= entry.Value.TeamChanged;
                GameManager.instance.Tick -= entry.Value.CooldownCalculate;

                if (entry.Value.hpSync) entry.Value.HPSyncFalse();
                if (entry.Value.mpSync) entry.Value.MPSyncFalse();
                if (entry.Value.xpSync) entry.Value.XPSyncFalse();

                // Attack data
                if (entry.Value.attackSoundRef) Destroy(entry.Value.attackSoundRef.gameObject);
                // Active ability data
                if (entry.Value.activeAbilityVFX) Destroy(entry.Value.activeAbilityVFX.gameObject);
                // Static copy
                if (entry.Value.staticCopy) Destroy(entry.Value.staticCopy.gameObject);
                // Unit itself
                Destroy(entry.Value.gameObject);
            }

            // Reset resources
            GameResources.instance.playerResources = new int[Enum.GetNames(typeof(StrategyCore.Players)).Length * GameResources.instance.gameResources.Length];
            GameResources.instance.playerResourceLimits = new int[Enum.GetNames(typeof(StrategyCore.Players)).Length * GameResources.instance.gameResources.Length];
            UIManager.instance.RefreshResourceTab();
            // Reset technology
            TechnologyManager.instance.Initialize();
            // Reset FoW
            FogOfWar.instance.Initialize();
            // Reset Grid
            Grid.instance.Initialize();
            // Reset GM
            GameManager.instance.Initialize();
            // Reset NetworkHandler.unitNetID
            SlotManager.instance.unitNetID = new Dictionary<UInt16, Unit>();
        }
    }
}
