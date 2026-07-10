using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.VisualScripting;
using Unity.AI.Navigation;

namespace StrategyCore
{
    public enum SCEditorCats
    {
        Ability,
        ArmorType,
        Attribute,
        DamageType,
        Effectors,
        Projectiles,
        Resource,
        Technology,
        UnitPrefabs,
        VFX,
        WeaponSounds,
        Scenes
    }

    public class SCEditor : EditorWindow
    {
        public static SCEditor instance;

        private Sprite m_DefaultItemIcon;
        private Sprite m_DefaultAttackIcon;
        private Sprite m_DefaultArmorIcon;
        private Sprite m_DefaultAttrIcon;
        private Sprite m_DefaultTechIcon;
        private Sprite m_DefaultSoundIcon;
        private Sprite m_DefaultSceneIcon;

        private VisualElement m_activeCategory;
        public SCEditorCats m_activeCatIndex = 0;
        private bool noUpdate = false;
        string currentFilter = "All";

        private VisualElement m_AbilitiesEditor;
        private VisualElement m_ArmorTypesEditor;
        private VisualElement m_AttributesEditor;
        private VisualElement m_DamageTypesEditor;
        private VisualElement m_EffectorsEditor;
        private VisualElement m_ProjectilesEditor;
        private VisualElement m_ResourcesEditor;
        private VisualElement m_TechnologyEditor;
        private VisualElement m_UnitsEditor;
        private VisualElement m_WeaponSoundsEditor;
        private VisualElement m_ScenesEditor;

        private static List<Ability> m_AbilitiesDatabase = new List<Ability>();
        private static List<ArmorType> m_ArmorTypesDatabase = new List<ArmorType>();
        private static List<Attribute> m_AttributesDatabase = new List<Attribute>();
        private static List<DamageType> m_DamageTypesDatabase = new List<DamageType>();
        private static List<Effector> m_EffectorsDatabase = new List<Effector>();
        private static List<Projectile> m_ProjectilesDatabase = new List<Projectile>();
        private static List<Resource> m_ResourcesDatabase = new List<Resource>();
        private static List<Technology> m_TechnologyDatabase = new List<Technology>();
        private static List<Unit> m_UnitsDatabase = new List<Unit>();
        private static List<WeaponSound> m_WeaponSoundsDatabase = new List<WeaponSound>();
        private static List<SceneAsset> m_ScenesDatabase = new List<SceneAsset>();

        // Items list view
        private VisualElement m_ItemsList; // UI ItemsList pointer
        private ListView m_ItemListView; // Currently generated list view
        public static VisualTreeAsset m_ListEntryTemplate; // List entry template
        public static float m_ItemHeight = 50;

        private object m_activeItem;

        private Scene activeScene;
        public Scene selectedScene;
        private GameObject selectedGM;
        private GameObject navmeshHolder;

        [MenuItem("Tools/SCEditor")]
        public static void Init()
        {
            if (instance != null) return;

            instance = GetWindow<SCEditor>();
            instance.titleContent = new GUIContent("StrategyCore Editor");
        }

        private void OnDestroy()
        {
            if (m_activeCatIndex == SCEditorCats.Scenes)
            {
                CloseSceneWithSave(selectedScene);
            }
            EditorApplication.update -= VisibilityRedrawUpdate;
            instance = null;
        }

        public void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/StrategyCore/UI/Editor/SCEditor.uxml");
            VisualElement rootFromUXML = visualTree.Instantiate();
            rootVisualElement.Add(rootFromUXML);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/StrategyCore/UI/Editor/SCEditor.uss");
            rootVisualElement.styleSheets.Add(styleSheet);

            m_ListEntryTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/StrategyCore/UI/Editor/SCListEntryTemplate.uxml");

            // DEFAULT ICONS
            m_DefaultItemIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/Core/MissingTexture.png", typeof(Sprite));

            m_DefaultAttackIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/UI/Attack.png", typeof(Sprite));
            m_DefaultArmorIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/UI/Armor.png", typeof(Sprite));
            m_DefaultAttrIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/UI/Attributes.png", typeof(Sprite));
            m_DefaultTechIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/UI/TechIcon.png", typeof(Sprite));
            m_DefaultSoundIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/UI/Audio.png", typeof(Sprite));
            m_DefaultSceneIcon = (Sprite)AssetDatabase.LoadAssetAtPath("Assets/StrategyCore/Textures/UI/SceneIcon.png", typeof(Sprite));

            // Refs
            m_ItemsList = rootVisualElement.Q<VisualElement>("ItemsList");

            // EDITOR VIEWS
            m_AbilitiesEditor = rootVisualElement.Q<VisualElement>("AbilitiesEditor");
            m_ArmorTypesEditor = rootVisualElement.Q<VisualElement>("ArmorTypesEditor");
            m_AttributesEditor = rootVisualElement.Q<VisualElement>("AttributesEditor");
            m_DamageTypesEditor = rootVisualElement.Q<VisualElement>("DamageTypesEditor");
            m_EffectorsEditor = rootVisualElement.Q<VisualElement>("EffectorsEditor");
            m_ProjectilesEditor = rootVisualElement.Q<VisualElement>("ProjectilesEditor");
            m_ResourcesEditor = rootVisualElement.Q<VisualElement>("ResourcesEditor");
            m_TechnologyEditor = rootVisualElement.Q<VisualElement>("TechnologyEditor");
            m_UnitsEditor = rootVisualElement.Q<VisualElement>("UnitsEditor");
            m_WeaponSoundsEditor = rootVisualElement.Q<VisualElement>("WeaponSoundsEditor");
            m_ScenesEditor = rootVisualElement.Q<VisualElement>("ScenesEditor");

            // Filename
            m_AbilitiesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_ArmorTypesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_AttributesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_DamageTypesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_EffectorsEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_ProjectilesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_ResourcesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_TechnologyEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_UnitsEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_WeaponSoundsEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);
            m_ScenesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").RegisterCallback<BlurEvent>(FileNameChange);

            // Display name
            m_AbilitiesEditor.Q<VisualElement>("AbilityText").Q<VisualElement>("Body").RegisterCallback<BlurEvent>(DisplayNameChange, TrickleDown.TrickleDown);
            m_ArmorTypesEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);
            m_AttributesEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);
            m_DamageTypesEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);
            m_EffectorsEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);
            m_ResourcesEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);
            m_TechnologyEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);
            m_UnitsEditor.Q<VisualElement>("Content").Q<TextField>("DisplayName").RegisterCallback<BlurEvent>(DisplayNameChange);

            // Path clicks
            m_AbilitiesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_ArmorTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_AttributesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_DamageTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_EffectorsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_ProjectilesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_ResourcesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_TechnologyEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_WeaponSoundsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);
            m_ScenesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").RegisterCallback<ClickEvent>(PathClick);

            // Ability callbacks
            m_AbilitiesEditor.Q<VisualElement>("Technical").Q<PropertyField>("IsItemProp").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_AbilitiesEditor.Q<VisualElement>("Continuous").Q<PropertyField>("IsCont").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());

            // Effector callbacks
            m_EffectorsEditor.Q<VisualElement>("PassiveChanges").Q<VisualElement>("PassiveOn").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_EffectorsEditor.Q<VisualElement>("Parameters").Q<VisualElement>("Permanent").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());

            // Projectile callbacks
            m_ProjectilesEditor.Q<VisualElement>("Params").Q<VisualElement>("ArcOn").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_ProjectilesEditor.Q<VisualElement>("Visuals").Q<ObjectField>("RenderObj").RegisterValueChangedCallback(evt => VisibilityRedraw());

            m_ProjectilesEditor.Q<VisualElement>("Visuals").Q<Button>("RenPicker").clicked += () =>
            {
                Projectile projectile = (Projectile)m_activeItem;
                SCChildPicker.ShowPicker(selectedAsset =>
                {
                    projectile.renderObject = selectedAsset;
                    EditorUtility.SetDirty(projectile);
                }, projectile.gameObject);
            };

            // Unit callbacks
            m_UnitsEditor.Q<VisualElement>("Main").Q<VisualElement>("NetID").RegisterCallback<ChangeEvent<uint>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("General").Q<EnumField>("UnitType").RegisterValueChangedCallback(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("General").Q<VisualElement>("IsGround").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("General").Q<VisualElement>("IsWater").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("General").Q<VisualElement>("IsAir").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Behaviour").Q<VisualElement>("ViewBlock").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Behaviour").Q<VisualElement>("SingleBlock").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Movement").Q<VisualElement>("CanMove").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("AttackSelector").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("Melee").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("AttackType").RegisterCallback<ChangeEvent<Enum>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("MultiOn").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("BounceCount").RegisterCallback<ChangeEvent<int>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("Attack").Q<ObjectField>("Projectile").RegisterValueChangedCallback(evt => VisibilityRedraw());

            m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("IsStorage").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("IsCollectible").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("IsCollector").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());
            m_UnitsEditor.Q<VisualElement>("ConstructionUnit").Q<VisualElement>("IsBuilding").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());

            m_UnitsEditor.Q<VisualElement>("UnitsEditor").Q<Button>("Components").RegisterCallback<ClickEvent>(ComponentsButton);

            m_UnitsEditor.Q<VisualElement>("Visuals").Q<Button>("HorPicker").clicked += () =>
            {
                Unit unit = (Unit)m_activeItem;
                SCChildPicker.ShowPicker(selectedAsset =>
                {
                    unit.horizontalPart = selectedAsset.transform;
                    EditorUtility.SetDirty(unit);
                }, unit.gameObject);
            };

            m_UnitsEditor.Q<VisualElement>("Visuals").Q<Button>("VerPicker").clicked += () =>
            {
                Unit unit = (Unit)m_activeItem;
                SCChildPicker.ShowPicker(selectedAsset =>
                {
                    unit.verticalPart = selectedAsset.transform;
                    EditorUtility.SetDirty(unit);
                }, unit.gameObject);
            };

            m_UnitsEditor.Q<VisualElement>("Attack").Q<Button>("ProjPicker").clicked += () =>
            {
                Unit unit = (Unit)m_activeItem;
                CustomObjectPicker.ShowPicker(selectedAsset =>
                {
                    unit.projectileGO = selectedAsset;
                }, unit.attackType == AttackType.Continuous);
            };

            // LaunchSite picker
            PropertyField launchSite = m_UnitsEditor.Q<VisualElement>("Attack").Q<PropertyField>("LaunchSite");
            launchSite.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                ModifyArrayUI(launchSite);
            });

            // Resources callbacks
            m_ResourcesEditor.Q<VisualElement>("Limited").Q<VisualElement>("BoolLimited").RegisterCallback<ChangeEvent<bool>>(evt => VisibilityRedraw());

            // Scene callbacks
            m_ScenesEditor.Q<VisualElement>("Grid").Q<VisualElement>("Width").RegisterCallback<BlurEvent>(evt => SceneDataSet());
            m_ScenesEditor.Q<VisualElement>("Grid").Q<VisualElement>("Height").RegisterCallback<BlurEvent>(evt => SceneDataSet());
            m_ScenesEditor.Q<VisualElement>("Grid").Q<VisualElement>("CellSize").RegisterCallback<BlurEvent>(evt => SceneDataSet());
            m_ScenesEditor.Q<VisualElement>("GameManager").Q<VisualElement>("Invisible").RegisterCallback<ChangeEvent<bool>>(evt => SceneDataSet());

            // Register clicks
            rootVisualElement.Q<VisualElement>("Categories").RegisterCallback<ClickEvent>(CategoryClick);

            // Items ADD Remove refresh
            rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("ItemAdd").RegisterCallback<ClickEvent>(AddNewItem);
            rootVisualElement.Q<Button>("DeleteButton").RegisterCallback<ClickEvent>(DeleteItem);
            rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Refresh").RegisterCallback<ClickEvent>(Refresh);
            rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").RegisterCallback<ClickEvent>(Filter);

            // Initial Select
            CategorySelect(rootVisualElement.Q<VisualElement>("ArmorTypesCat"));
        }

        // CATEGORY CLICK
        private void CategoryClick(ClickEvent evt)
        {
            CategorySelect(evt.target as VisualElement);
        }

        public void CategorySelect(VisualElement category)
        {
            currentFilter = "All";
            bool validElement = false;

            if (category.name == "ScenesCat")
            {
                LoadScenes();
                m_activeCatIndex = SCEditorCats.Scenes;
                ScenesListView();
                rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                validElement = true;
            }
            else
            {
                if (selectedScene != null) CloseSceneWithSave(selectedScene);

                if (!validElement)
                {
                    if (category.name == "AbilitiesCat")
                    {
                        LoadAbilities();
                        m_activeCatIndex = SCEditorCats.Ability;
                        AbilitiesListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.Flex;
                        validElement = true;
                    }
                    else if (category.name == "ArmorTypesCat")
                    {
                        LoadArmors();
                        m_activeCatIndex = SCEditorCats.ArmorType;
                        ArmorListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "AttributesCat")
                    {
                        LoadAttributes();
                        m_activeCatIndex = SCEditorCats.Attribute;
                        AttributesListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "DamageTypesCat")
                    {
                        LoadDamages();
                        m_activeCatIndex = SCEditorCats.DamageType;
                        DamageListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "EffectorsCat")
                    {
                        LoadEffectors();
                        m_activeCatIndex = SCEditorCats.Effectors;
                        EffectorListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "ProjectilesCat")
                    {
                        LoadProjectiles();
                        m_activeCatIndex = SCEditorCats.Projectiles;
                        ProjectilesListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "ResourcesCat")
                    {
                        LoadResources();
                        m_activeCatIndex = SCEditorCats.Resource;
                        ResourcesListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "TechnologyCat")
                    {
                        LoadTechnology();
                        m_activeCatIndex = SCEditorCats.Technology;
                        TechnologyListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                    else if (category.name == "UnitsCat")
                    {
                        LoadUnits();
                        m_activeCatIndex = SCEditorCats.UnitPrefabs;
                        UnitsListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.Flex;
                        validElement = true;
                    }
                    else if (category.name == "WeaponSoundsCat")
                    {
                        LoadWeaponSounds();
                        m_activeCatIndex = SCEditorCats.WeaponSounds;
                        WeaponSoundListView();
                        rootVisualElement.Q<VisualElement>("ItemsWrapper").Q<Button>("Filter").style.display = DisplayStyle.None;
                        validElement = true;
                    }
                }
            }

            if (validElement)
            {
                if (m_activeCategory != null) m_activeCategory.RemoveFromClassList("activeCategory");
                m_activeCategory = category;
                m_activeCategory.AddToClassList("activeCategory");
                BindActiveItem(m_ItemListView.selectedItem);
            }
        }

        // SCENES =============================

        private void LoadScenes()
        {
            m_ScenesDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/StrategyCore/Scenes/" });

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                SceneAsset obj = (SceneAsset)AssetDatabase.LoadAssetAtPath(guid, typeof(SceneAsset));

                if (obj != null)
                {
                    if (obj.name != "Menu")
                    {
                        m_ScenesDatabase.Add(obj);
                    }
                }
            }
        }

        private void ScenesListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_ScenesDatabase[i].name;
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = m_DefaultSceneIcon.texture;
            };

            m_ItemListView = new ListView(m_ScenesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_ScenesDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // Sets the gamemanager and navmesh objects of the selected scene
        private void SetSceneGMNAV(string scenePath)
        {
            if (selectedScene != null)
            {
                CloseSceneWithSave(selectedScene);
            }

            // Get the active scene before opening a new one
            activeScene = EditorSceneManager.GetActiveScene();

            selectedScene = EditorSceneManager.GetSceneByPath(scenePath);

            if (!selectedScene.IsValid() || !selectedScene.isLoaded)
            {
                // Open the scene in additive mode (so it doesn’t unload current scenes)
                selectedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            // Iterate through the root objects of this scene only
            selectedGM = null;
            navmeshHolder = null;
            foreach (GameObject obj in selectedScene.GetRootGameObjects())
            {
                if (selectedGM == null)
                {
                    if (obj.name == "GameManager" || obj.transform.Find("GameManager") != null)
                    {
                        selectedGM = obj;
                    }
                }

                if (navmeshHolder == null)
                {
                    if (obj.name == "Navmesh")
                    {
                        navmeshHolder = obj;
                    }
                }

                if (navmeshHolder != null && selectedGM != null)
                {
                    break;
                }
            }

            if (selectedGM == null)
            {
                CloseSceneWithSave(selectedScene);
            }

            // Close the scene after checking
            // if (scene != activeScene)
            // {
            //     EditorSceneManager.CloseScene(scene, true);
            // }
        }

        public void CloseSceneWithSave(Scene scene)
        {
            if (selectedScene == null || selectedScene == activeScene) return;
            if (!scene.IsValid()) return;

            // Save the scene before closing
            if (scene.isDirty) // Check if there are unsaved changes
            {
                bool saved = EditorSceneManager.SaveScene(scene);
                if (!saved)
                {
                    return; // Don't close if saving failed
                }
            }

            // Close the scene
            selectedGM = null;
            navmeshHolder = null;
            EditorSceneManager.CloseScene(scene, true);
        }

        public void SceneDataSet()
        {
            Grid gridComponent = selectedGM.GetComponent<Grid>();
            FogOfWar fowComp = selectedGM.GetComponent<FogOfWar>();

            // To fight floating-point error > 0.001f
            if (Math.Abs((gridComponent.width / gridComponent.cellSize) % 1) > 0.001f || Math.Abs((gridComponent.height / gridComponent.cellSize) % 1) > 0.001f)
            {
                Debug.LogError("Division of width and height of the map by cell size must be a whole number!");
                return;
            }

            gridComponent.chunkSize = gridComponent.cellSize * 10;
            fowComp.cellSize = gridComponent.cellSize;

            NavmeshDataSet(selectedGM, navmeshHolder);
        }

        public static bool NavmeshDataSet(GameObject gameManager, GameObject navmeshHolder)
        {
            // Also we must rearrange and rebuild the navmesh (Unity issue, will throw a lot of log warnings)
            if (navmeshHolder != null)
            {
                Transform waterNav = navmeshHolder.transform.Find("WaterNavmesh");
                Transform groundNav = navmeshHolder.transform.Find("GroundNavmesh");
                Transform airNav = navmeshHolder.transform.Find("AirNavmesh");
                Transform InvisibilityNav = navmeshHolder.transform.Find("InvisibilityNavmesh");
                GameManager gm = gameManager.GetComponent<GameManager>();
                Grid gridComponent = gameManager.GetComponent<Grid>();

                // Similar code is in GameManager Navmesh initialization
                if (gm != null && waterNav != null && groundNav != null && airNav != null && InvisibilityNav != null)
                {
                    // Position navmesh in the center of the map and set the boundaries
                    NavMeshSurface groundNavmesh = groundNav.GetComponent<NavMeshSurface>();
                    NavMeshSurface waterNavmesh = waterNav.GetComponent<NavMeshSurface>();

                    groundNavmesh.center = new Vector3(gridComponent.width * 0.5f, 0, gridComponent.height * 0.5f);
                    groundNavmesh.size = new Vector3(gridComponent.width - 0.5f, Utils.raycastPointY, gridComponent.height - 0.5f);

                    waterNavmesh.center = groundNavmesh.center;
                    waterNavmesh.size = groundNavmesh.size;

                    groundNavmesh.BuildNavMesh();
                    waterNavmesh.BuildNavMesh();

                    EditorUtility.SetDirty(groundNavmesh);
                    EditorUtility.SetDirty(waterNavmesh);

                    // Air navmesh surface
                    Utils.airOffsetX = gridComponent.width * 1.5f;
                    NavMeshSurface airNavmesh = airNav.GetComponent<NavMeshSurface>();
                    airNavmesh.transform.position = new Vector3(Utils.airOffsetX, 0, 0);
                    airNavmesh.center = groundNavmesh.center;
                    airNavmesh.size = groundNavmesh.size;

                    BoxCollider box = airNavmesh.GetComponent<BoxCollider>();
                    box.center = airNavmesh.center;
                    box.size = new Vector3(airNavmesh.size.x, 0.01f, airNavmesh.size.z);
                    EditorUtility.SetDirty(box);

                    airNavmesh.BuildNavMesh();
                    EditorUtility.SetDirty(airNavmesh);

                    // Invisibility Navmesh
                    Utils.invisibilityOffsetY = gridComponent.height * 1.5f;
                    NavMeshSurface invisNav = InvisibilityNav.GetComponent<NavMeshSurface>();
                    if (gm.gameIncludesInvisible)
                    {
                        invisNav.gameObject.SetActive(true);
                        invisNav.transform.position = new Vector3(0, 0, 0);
                        invisNav.center = groundNavmesh.center;
                        invisNav.size = groundNavmesh.size;
                        invisNav.BuildNavMesh();
                        invisNav.transform.position = new Vector3(0, 0, Utils.invisibilityOffsetY);
                    }
                    else invisNav.gameObject.SetActive(false);
                    EditorUtility.SetDirty(invisNav);
                }
                else
                {
                    Debug.LogWarning("Where are your Ground, Water, Air and Invisibility navmeshes + GameManager component?");
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("You scene has no 'Navmesh', is it intentional?");
                return false;
            }

            return true;
        }

        // ABILITIES =============================
        private void LoadAbilities()
        {
            m_AbilitiesDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:Ability", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                Ability obj = (Ability)AssetDatabase.LoadAssetAtPath(guid, typeof(Ability));

                if (currentFilter == "All") m_AbilitiesDatabase.Add(obj);
                else
                {
                    if (obj.type == (AbilityType)Enum.Parse(typeof(AbilityType), currentFilter, true)) m_AbilitiesDatabase.Add(obj);
                }
            }
        }

        private void AbilitiesListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = (m_AbilitiesDatabase[i].abilityName.Length > 0) ? m_AbilitiesDatabase[i].abilityName[0] : "";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = (m_AbilitiesDatabase[i].icon.Length > 0) ? m_AbilitiesDatabase[i].icon[0] : m_DefaultItemIcon.texture;
                e.Q<Label>("ItemDescription").text = m_AbilitiesDatabase[i].name + ".asset"; // m_AbilitiesDatabase[i].GetType().Name + " (" + m_AbilitiesDatabase[i].type.ToString() + ")";
            };

            m_ItemListView = new ListView(m_AbilitiesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_AbilitiesDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // ARMOR TYPES =============================

        private void LoadArmors()
        {
            m_ArmorTypesDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:ArmorType", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                ArmorType obj = (ArmorType)AssetDatabase.LoadAssetAtPath(guid, typeof(ArmorType));
                m_ArmorTypesDatabase.Add(obj);
            }
        }

        private void ArmorListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_ArmorTypesDatabase[i].displayName;
                e.Q<Label>("ItemDescription").text = m_ArmorTypesDatabase[i].name + ".asset";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = (m_ArmorTypesDatabase[i].icon) ? m_ArmorTypesDatabase[i].icon : m_DefaultArmorIcon.texture;
            };

            m_ItemListView = new ListView(m_ArmorTypesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_ArmorTypesDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // ATTRIBUTES =============================

        private void LoadAttributes()
        {
            m_AttributesDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:Attribute", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                Attribute obj = (Attribute)AssetDatabase.LoadAssetAtPath(guid, typeof(Attribute));
                m_AttributesDatabase.Add(obj);
            }
        }

        private void AttributesListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_AttributesDatabase[i].displayName;
                e.Q<Label>("ItemDescription").text = m_AttributesDatabase[i].name + ".asset";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = (m_AttributesDatabase[i].icon) ? m_AttributesDatabase[i].icon : m_DefaultAttrIcon.texture;
            };

            m_ItemListView = new ListView(m_AttributesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_AttributesDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // DAMAGE TYPES =============================

        private void LoadDamages()
        {
            m_DamageTypesDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:DamageType", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                DamageType obj = (DamageType)AssetDatabase.LoadAssetAtPath(guid, typeof(DamageType));
                m_DamageTypesDatabase.Add(obj);
            }
        }

        private void DamageListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_DamageTypesDatabase[i].displayName;
                e.Q<Label>("ItemDescription").text = m_DamageTypesDatabase[i].name + ".asset";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = (m_DamageTypesDatabase[i].icon) ? m_DamageTypesDatabase[i].icon : m_DefaultAttackIcon.texture;
            };

            m_ItemListView = new ListView(m_DamageTypesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_DamageTypesDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // EFFECTORS =============================

        private void LoadEffectors()
        {
            m_EffectorsDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:Effector", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                Effector obj = (Effector)AssetDatabase.LoadAssetAtPath(guid, typeof(Effector));
                m_EffectorsDatabase.Add(obj);
            }
        }

        private void EffectorListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_EffectorsDatabase[i].displayName;
                e.Q<Label>("ItemDescription").text = m_EffectorsDatabase[i].name + ".asset";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = (m_EffectorsDatabase[i].icon) ? m_EffectorsDatabase[i].icon : m_DefaultItemIcon.texture;
            };

            m_ItemListView = new ListView(m_EffectorsDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_EffectorsDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // PROJECTILES =============================

        private void LoadProjectiles()
        {
            m_ProjectilesDatabase.Clear();

            // Get assets from Resources folder
            string[] guids = AssetDatabase.FindAssets("t:GameObject", null);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a Projectile component
                if (prefab != null)
                {
                    Projectile projectile = prefab.GetComponent<Projectile>();
                    if (projectile != null) m_ProjectilesDatabase.Add(projectile);
                }
            }
        }

        private void ProjectilesListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_ProjectilesDatabase[i].name;
                e.Q<Label>("ItemDescription").text = m_ProjectilesDatabase[i].name + ".asset";
                LoadPreview(e.Q<VisualElement>("ItemIcon"), m_ProjectilesDatabase[i].gameObject);
            };

            m_ItemListView = new ListView(m_ProjectilesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_ProjectilesDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // TECHNOLOGY =============================
        private void LoadTechnology()
        {
            m_TechnologyDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:Technology", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                Technology obj = (Technology)AssetDatabase.LoadAssetAtPath(guid, typeof(Technology));
                m_TechnologyDatabase.Add(obj);
            }
        }

        private void TechnologyListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_TechnologyDatabase[i].displayName;
                e.Q<Label>("ItemDescription").text = m_TechnologyDatabase[i].name + ".asset";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = m_DefaultTechIcon.texture;
            };

            m_ItemListView = new ListView(m_TechnologyDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_TechnologyDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // RESOURCES =============================
        private void LoadResources()
        {
            m_ResourcesDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:Resource", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                Resource obj = (Resource)AssetDatabase.LoadAssetAtPath(guid, typeof(Resource));
                m_ResourcesDatabase.Add(obj);
            }
        }

        private void ResourcesListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = (m_ResourcesDatabase[i].icon == null) ? m_DefaultItemIcon.texture : m_ResourcesDatabase[i].icon;
                e.Q<Label>("ItemName").text = m_ResourcesDatabase[i].displayName;
                e.Q<Label>("ItemDescription").text = m_ResourcesDatabase[i].name + ".asset";
            };

            m_ItemListView = new ListView(m_ResourcesDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_ResourcesDatabase.Count * m_ItemHeight;
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // UNITS =============================

        private void LoadUnits()
        {
            m_UnitsDatabase.Clear();

            // Get assets from Resources folder
            string[] guids = AssetDatabase.FindAssets("t:GameObject", null);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // Check if the GameObject has a Unit component
                if (prefab != null)
                {
                    Unit unit = prefab.GetComponent<Unit>();
                    if (unit != null)
                    {
                        if (currentFilter == "All") m_UnitsDatabase.Add(unit);
                        else
                        {
                            if (unit.unitType == (UnitType)Enum.Parse(typeof(UnitType), currentFilter, true)) m_UnitsDatabase.Add(unit);
                        }
                    }
                }
            }
        }

        private void UnitsListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_UnitsDatabase[i].unitName;
                e.Q<Label>("ItemType").text = m_UnitsDatabase[i].unitType.ToString();
                e.Q<Label>("ItemDescription").text = m_UnitsDatabase[i].name + ".asset";

                if (m_UnitsDatabase[i].icon) e.Q<VisualElement>("ItemIcon").style.backgroundImage = m_UnitsDatabase[i].icon;
                else LoadPreview(e.Q<VisualElement>("ItemIcon"), m_UnitsDatabase[i].gameObject);
            };

            m_ItemListView = new ListView(m_UnitsDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_UnitsDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        private void ComponentsButton(ClickEvent evt)
        {
            // create the menu and add items to it
            GenericDropdownMenu menu = new GenericDropdownMenu();

            Unit unit = (Unit)m_activeItem;

            // Attribute unit --------------
            if (unit.GetComponent<AttributeUnit>() != null)
            {
                menu.AddItem("AttributeUnit", true, () =>
                {
                    ShowSubMenu<AttributeUnit>(unit);
                });
            }
            else
            {
                menu.AddItem("AttributeUnit", false, () =>
                {
                    unit.AddComponent<AttributeUnit>();
                    Refresh();
                });
            }

            // Construction unit --------------
            if (unit.GetComponent<ConstructionUnit>() != null)
            {
                menu.AddItem("ConstructionUnit", true, () =>
                {
                    ShowSubMenu<ConstructionUnit>(unit);
                });
            }
            else
            {
                menu.AddItem("ConstructionUnit", false, () =>
                {
                    unit.AddComponent<ConstructionUnit>();
                    Refresh();
                });
            }

            // Leveling unit --------------
            if (unit.GetComponent<LevelingUnit>() != null)
            {
                menu.AddItem("LevelingUnit", true, () =>
                {
                    ShowSubMenu<LevelingUnit>(unit);
                });
            }
            else
            {
                menu.AddItem("LevelingUnit", false, () =>
                {
                    unit.AddComponent<LevelingUnit>();
                    Refresh();
                });
            }

            // Resource unit --------------
            if (unit.GetComponent<ResourceUnit>() != null)
            {
                menu.AddItem("ResourceUnit", true, () =>
                {
                    ShowSubMenu<ResourceUnit>(unit);
                });
            }
            else
            {
                menu.AddItem("ResourceUnit", false, () =>
                {
                    unit.AddComponent<ResourceUnit>();
                    Refresh();
                });
            }

            // Transport unit --------------
            if (unit.GetComponent<TransportUnit>() != null)
            {
                menu.AddItem("TransportUnit", true, () =>
                {
                    ShowSubMenu<TransportUnit>(unit);
                });
            }
            else
            {
                menu.AddItem("TransportUnit", false, () =>
                {
                    unit.AddComponent<TransportUnit>();
                    Refresh();
                });
            }

            // LifeTime unit --------------
            if (unit.GetComponent<LifetimeUnit>() != null)
            {
                menu.AddItem("LifetimeUnit", true, () =>
                {
                    ShowSubMenu<LifetimeUnit>(unit);
                });
            }
            else
            {
                menu.AddItem("LifetimeUnit", false, () =>
                {
                    unit.AddComponent<LifetimeUnit>();
                    Refresh();
                });
            }

            menu.DropDown(position: new Rect(Event.current.mousePosition + new Vector2(0, 10), Vector2.zero), targetElement: rootVisualElement, anchored: false);
        }

        private void ShowSubMenu<T>(Unit unit)
        {
            GenericDropdownMenu submenu = new GenericDropdownMenu();

            submenu.AddItem("Cancel", false, () => { });

            if (typeof(T) == typeof(TransportUnit)) submenu.AddItem("Remove", false, () => { DestroyImmediate(unit.GetComponent<TransportUnit>(), true); Refresh(); });
            else if (typeof(T) == typeof(LifetimeUnit)) submenu.AddItem("Remove", false, () => { DestroyImmediate(unit.GetComponent<LifetimeUnit>(), true); Refresh(); });
            else if (typeof(T) == typeof(ResourceUnit)) submenu.AddItem("Remove", false, () => { DestroyImmediate(unit.GetComponent<ResourceUnit>(), true); Refresh(); });
            else if (typeof(T) == typeof(LevelingUnit)) submenu.AddItem("Remove", false, () => { DestroyImmediate(unit.GetComponent<LevelingUnit>(), true); Refresh(); });
            else if (typeof(T) == typeof(ConstructionUnit)) submenu.AddItem("Remove", false, () => { DestroyImmediate(unit.GetComponent<ConstructionUnit>(), true); Refresh(); });
            else if (typeof(T) == typeof(AttributeUnit)) submenu.AddItem("Remove", false, () => { DestroyImmediate(unit.GetComponent<AttributeUnit>(), true); Refresh(); });

            // Position the submenu slightly to the right of the main menu
            Vector2 submenuPosition = Event.current.mousePosition + new Vector2(150, 0);
            submenu.DropDown(position: new Rect(submenuPosition, Vector2.zero), targetElement: rootVisualElement, anchored: false);
        }

        // WEAPON SOUNDS =============================

        private void LoadWeaponSounds()
        {
            m_WeaponSoundsDatabase.Clear();

            string[] objs = AssetDatabase.FindAssets("t:WeaponSound", null);

            foreach (string o in objs)
            {
                string guid = AssetDatabase.GUIDToAssetPath(o);
                WeaponSound obj = (WeaponSound)AssetDatabase.LoadAssetAtPath(guid, typeof(WeaponSound));
                m_WeaponSoundsDatabase.Add(obj);
            }
        }

        private void WeaponSoundListView()
        {
            if (m_ItemListView != null) m_ItemListView.selectionChanged -= ListSelectionChanged;
            m_ItemsList.Clear();

            Func<VisualElement> makeItem = () => m_ListEntryTemplate.CloneTree();

            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Q<Label>("ItemName").text = m_WeaponSoundsDatabase[i].name;
                e.Q<Label>("ItemDescription").text = m_WeaponSoundsDatabase[i].name + ".asset";
                e.Q<VisualElement>("ItemIcon").style.backgroundImage = m_DefaultSoundIcon.texture;
            };

            m_ItemListView = new ListView(m_WeaponSoundsDatabase, m_ItemHeight, makeItem, bindItem);
            m_ItemListView.selectionType = SelectionType.Single;
            m_ItemListView.style.height = m_WeaponSoundsDatabase.Count * (m_ItemHeight + 5);
            m_ItemListView.selectionChanged += ListSelectionChanged;
            m_ItemsList.Add(m_ItemListView);

            ShowCategory();
        }

        // SELECTION CHANGED ===========================================================================================================================

        private void ListSelectionChanged(IEnumerable<object> selectedItems)
        {
            BindActiveItem((object)selectedItems.First());
        }

        private void BindActiveItem(object activeItem)
        {
            // Show current category if cats were hidden
            if (m_activeItem == null && activeItem != null) ShowCategory();

            // New active elements
            m_activeItem = activeItem;
            if (activeItem == null)
            {
                ShowCategory(true);
                return;
            }

            // To make sure all events that trigger OnBlur are fired
            VisualElement focusedElement = (VisualElement)rootVisualElement.panel.focusController.focusedElement;
            if (focusedElement != null)
            {
                if (focusedElement.name == "FileNameInput")
                {
                    focusedElement.Blur();
                    ForcedFileNameChange(focusedElement);
                }
            }

            if (m_activeCatIndex == SCEditorCats.Ability)
            {
                Ability selected = (Ability)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_AbilitiesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = (selected.abilityName.Length > 0) ? selected.abilityName[0] : "";
                m_AbilitiesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemType").text = "Class: " + selected.GetType().Name + " (Type: " + selected.type.ToString() + ")";
                m_AbilitiesEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = (selected.icon.Length > 0) ? selected.icon[0] : m_DefaultItemIcon.texture;
                m_AbilitiesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_AbilitiesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                // Ability specific properties
                VisualElement container = m_AbilitiesEditor.Q<VisualElement>("AbilitySpecific").Q<VisualElement>("Body");
                container.Clear();

                SerializedProperty iterator = m_activeSerialized.GetIterator();
                bool startAdding = false;
                string targetVariable = "cost"; // Change this to the desired variable name

                if (iterator.NextVisible(true)) // Move to the first visible property
                {
                    do
                    {
                        if (!startAdding)
                        {
                            if (iterator.name == targetVariable)
                            {
                                startAdding = true; // Start adding properties after this variable
                            }
                            continue; // Skip properties until the target variable is found
                        }

                        // PROJECTILE VFX LINE PICK
                        if (iterator.type == "Projectile" || iterator.type == "PPtr<$Projectile>" || iterator.type == "VFXLine" || iterator.type == "PPtr<$VFXLine>")
                        {
                            var picker = new VisualElement();
                            picker.style.flexDirection = FlexDirection.Row;

                            SerializedProperty propertyCopy = iterator.Copy(); // Store a copy before modifying
                            Button pickerButton;
                            if (iterator.type == "Projectile" || iterator.type == "PPtr<$Projectile>")
                            {
                                pickerButton = new Button(() =>
                                {
                                    CustomObjectPicker.ShowPicker(selectedAsset =>
                                    {
                                        propertyCopy.objectReferenceValue = selectedAsset.GetComponent<Projectile>(); // Assign your instance here
                                        propertyCopy.serializedObject.ApplyModifiedProperties(); // Save changes
                                    }, false);
                                })
                                { text = "PICK" };
                            }
                            else
                            {
                                pickerButton = new Button(() =>
                                {
                                    CustomObjectPicker.ShowPicker(selectedAsset =>
                                    {
                                        propertyCopy.objectReferenceValue = selectedAsset.GetComponent<VFXLine>(); // Assign your instance here
                                        propertyCopy.serializedObject.ApplyModifiedProperties(); // Save changes
                                    }, true);
                                })
                                { text = "PICK" };
                            }

                            PropertyField propertyField = new PropertyField(iterator);
                            propertyField.bindingPath = iterator.name;
                            picker.Add(propertyField);
                            picker.Add(pickerButton);

                            container.Add(picker);
                        }
                        // UNIT PICKER
                        else if (iterator.type == "Unit" || iterator.type == "PPtr<Unit>" || iterator.type == "PPtr<$Unit>")
                        {
                            var picker = new VisualElement();
                            picker.style.flexDirection = FlexDirection.Row;

                            SerializedProperty propertyCopy = iterator.Copy(); // Store a copy before modifying
                            Button pickerButton;
                            pickerButton = new Button(() =>
                            {
                                UnitPicker.ShowPicker(selectedAsset =>
                                {
                                    propertyCopy.objectReferenceValue = selectedAsset; // Assign your instance here
                                    propertyCopy.serializedObject.ApplyModifiedProperties(); // Save changes
                                });
                            })
                            { text = "PICK" };

                            PropertyField propertyField = new PropertyField(iterator);
                            propertyField.bindingPath = iterator.name;
                            picker.Add(propertyField);
                            picker.Add(pickerButton);

                            container.Add(picker);
                        }
                        else
                        {
                            // Standard property field
                            PropertyField propertyField = new PropertyField(iterator);
                            propertyField.bindingPath = iterator.name;
                            container.Add(propertyField);
                        }
                    } while (iterator.NextVisible(false)); // Iterate through all properties
                }

                m_AbilitiesEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.ArmorType)
            {
                ArmorType selected = (ArmorType)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_ArmorTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
                m_ArmorTypesEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = (selected.icon) ? selected.icon : m_DefaultArmorIcon.texture;
                m_ArmorTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_ArmorTypesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_ArmorTypesEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.Attribute)
            {
                Attribute selected = (Attribute)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_AttributesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
                m_AttributesEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = (selected.icon) ? selected.icon : m_DefaultAttrIcon.texture;
                m_AttributesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_AttributesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_AttributesEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.DamageType)
            {
                DamageType selected = (DamageType)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_DamageTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
                m_DamageTypesEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = (selected.icon) ? selected.icon : m_DefaultAttackIcon.texture;
                m_DamageTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_DamageTypesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_DamageTypesEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.Effectors)
            {
                Effector selected = (Effector)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_EffectorsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
                m_EffectorsEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = (selected.icon) ? selected.icon : m_DefaultItemIcon.texture;
                m_EffectorsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_EffectorsEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_EffectorsEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.Projectiles)
            {
                Projectile selected = (Projectile)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_ProjectilesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.name;
                m_ProjectilesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                LoadPreview(m_ProjectilesEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon"), selected.gameObject);
                m_ProjectilesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_ProjectilesEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.Resource)
            {
                Resource selected = (Resource)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_ResourcesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
                m_ResourcesEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = (selected.icon) ? selected.icon : m_DefaultItemIcon.texture;
                m_ResourcesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_ResourcesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_ResourcesEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.Technology)
            {
                Technology selected = (Technology)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_TechnologyEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
                m_TechnologyEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = m_DefaultTechIcon.texture;
                m_TechnologyEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_TechnologyEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_TechnologyEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.UnitPrefabs)
            {
                Unit selected = (Unit)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.unitName;
                m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemType").text = selected.unitType.ToString();
                m_UnitsEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                if (selected.icon) m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = selected.icon;
                else LoadPreview(m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon"), selected.gameObject);

                m_UnitsEditor.Bind(m_activeSerialized);

                if (selected.GetComponent<TransportUnit>()) m_UnitsEditor.Q<VisualElement>("TransportUnit").Bind(new SerializedObject(selected.GetComponent<TransportUnit>()));
                if (selected.GetComponent<ResourceUnit>()) m_UnitsEditor.Q<VisualElement>("ResourceUnit").Bind(new SerializedObject(selected.GetComponent<ResourceUnit>()));
                if (selected.GetComponent<ConstructionUnit>()) m_UnitsEditor.Q<VisualElement>("ConstructionUnit").Bind(new SerializedObject(selected.GetComponent<ConstructionUnit>()));
                if (selected.GetComponent<LifetimeUnit>()) m_UnitsEditor.Q<VisualElement>("LifetimeUnit").Bind(new SerializedObject(selected.GetComponent<LifetimeUnit>()));
                if (selected.GetComponent<LevelingUnit>()) m_UnitsEditor.Q<VisualElement>("LevelingUnit").Bind(new SerializedObject(selected.GetComponent<LevelingUnit>()));
                if (selected.GetComponent<AttributeUnit>()) m_UnitsEditor.Q<VisualElement>("AttributeUnit").Bind(new SerializedObject(selected.GetComponent<AttributeUnit>()));
            }
            else if (m_activeCatIndex == SCEditorCats.WeaponSounds)
            {
                WeaponSound selected = (WeaponSound)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_WeaponSoundsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.name;
                m_WeaponSoundsEditor.Q<VisualElement>("ItemInfo").Q<VisualElement>("ItemIcon").style.backgroundImage = m_DefaultSoundIcon.texture;
                m_WeaponSoundsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(selected);
                m_WeaponSoundsEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = selected.name;

                m_WeaponSoundsEditor.Bind(m_activeSerialized);
            }
            else if (m_activeCatIndex == SCEditorCats.Scenes)
            {
                SceneAsset sa = (SceneAsset)m_activeItem;
                SetSceneGMNAV(AssetDatabase.GetAssetPath(sa));

                if (selectedGM != null)
                {
                    Grid gridComponent = selectedGM.GetComponent<Grid>();
                    FogOfWar fowComp = selectedGM.GetComponent<FogOfWar>();
                    GameManager gmComp = selectedGM.GetComponent<GameManager>();
                    GameResources grComp = selectedGM.GetComponent<GameResources>();
                    SoundFXManager soundComp = selectedGM.GetComponent<SoundFXManager>();

                    if (gridComponent == null || fowComp == null || gmComp == null || grComp == null || soundComp == null)
                    {
                        CloseSceneWithSave(selectedScene);
                    }
                    else
                    {
                        m_ScenesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = sa.name;
                        m_ScenesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemPath").text = AssetDatabase.GetAssetPath(sa);
                        m_ScenesEditor.Q<VisualElement>("Content").Q<TextField>("FileNameInput").value = sa.name;

                        m_ScenesEditor.Q<VisualElement>("Grid").Bind(new SerializedObject(gridComponent));
                        m_ScenesEditor.Q<VisualElement>("FoW").Bind(new SerializedObject(fowComp));
                        m_ScenesEditor.Q<VisualElement>("GameManager").Bind(new SerializedObject(gmComp));
                        m_ScenesEditor.Q<VisualElement>("GameResources").Bind(new SerializedObject(grComp));
                        m_ScenesEditor.Q<VisualElement>("Sound").Bind(new SerializedObject(soundComp));
                    }
                }
            }
        }

        // VISIBILITY =============================
        private void ShowCategory(bool reset = false)
        {
            m_AbilitiesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Ability) ? DisplayStyle.None : DisplayStyle.Flex;
            m_ArmorTypesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.ArmorType) ? DisplayStyle.None : DisplayStyle.Flex;
            m_AttributesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Attribute) ? DisplayStyle.None : DisplayStyle.Flex;
            m_DamageTypesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.DamageType) ? DisplayStyle.None : DisplayStyle.Flex;
            m_EffectorsEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Effectors) ? DisplayStyle.None : DisplayStyle.Flex;
            m_ProjectilesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Projectiles) ? DisplayStyle.None : DisplayStyle.Flex;
            m_ResourcesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Resource) ? DisplayStyle.None : DisplayStyle.Flex;
            m_TechnologyEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Technology) ? DisplayStyle.None : DisplayStyle.Flex;
            m_UnitsEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.UnitPrefabs) ? DisplayStyle.None : DisplayStyle.Flex;
            m_WeaponSoundsEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.WeaponSounds) ? DisplayStyle.None : DisplayStyle.Flex;
            m_ScenesEditor.style.display = (reset || m_activeCatIndex != SCEditorCats.Scenes) ? DisplayStyle.None : DisplayStyle.Flex;

            rootVisualElement.Q<Button>("DeleteButton").style.display = (reset) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ADD =============================
        private void AddNewItem(ClickEvent evt)
        {
            // <CHANGE> ADD ITEMS FOR ALL CATEROGIES
            if (m_activeCatIndex == SCEditorCats.Technology)
            {
                // Unique name
                string newName = "New Tech";
                string folderPath = "Assets/StrategyCore/Resources/Technology/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                Technology newItem = CreateInstance<Technology>();
                newItem.displayName = newName + " " + index;
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_TechnologyDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_TechnologyDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.Resource)
            {
                // Unique name
                string newName = "New Resource";
                string folderPath = "Assets/StrategyCore/Resources/Resource/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                Resource newItem = CreateInstance<Resource>();
                newItem.displayName = newName + " " + index;
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_ResourcesDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_ResourcesDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.ArmorType)
            {
                // Unique name
                string newName = "New Armor Type";
                string folderPath = "Assets/StrategyCore/Resources/ArmorTypes/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                ArmorType newItem = CreateInstance<ArmorType>();
                newItem.displayName = newName + " " + index;
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_ArmorTypesDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_ArmorTypesDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.Attribute)
            {
                // Unique name
                string newName = "New Attribute";
                string folderPath = "Assets/StrategyCore/Resources/Attributes/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                Attribute newItem = CreateInstance<Attribute>();
                newItem.displayName = newName + " " + index;
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_AttributesDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_AttributesDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.DamageType)
            {
                // Unique name
                string newName = "New Damage Type";
                string folderPath = "Assets/StrategyCore/Resources/DamageTypes/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                DamageType newItem = CreateInstance<DamageType>();
                newItem.displayName = newName + " " + index;
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_DamageTypesDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_DamageTypesDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.Effectors)
            {
                // Unique name
                string newName = "New Effector";
                string folderPath = "Assets/StrategyCore/Resources/Effectors/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                Effector newItem = CreateInstance<Effector>();
                newItem.displayName = newName + " " + index;
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_EffectorsDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_EffectorsDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.WeaponSounds)
            {
                // Unique name
                string newName = "New Weapon Sound";
                string folderPath = "Assets/StrategyCore/Resources/WeaponSound/";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
                {
                    index++;
                }

                // New item
                WeaponSound newItem = CreateInstance<WeaponSound>();
                AssetDatabase.CreateAsset(newItem, folderPath + newName + " " + index + ".asset");
                AssetDatabase.SaveAssets();

                //Add it to the item list
                m_WeaponSoundsDatabase.Add(newItem);

                //Refresh the ListView so everything is redrawn again
                m_ItemListView.Rebuild();
                m_ItemListView.style.height = m_WeaponSoundsDatabase.Count * m_ItemHeight;
            }
            else if (m_activeCatIndex == SCEditorCats.Projectiles)
            {

            }
            else if (m_activeCatIndex == SCEditorCats.UnitPrefabs)
            {

            }
            else if (m_activeCatIndex == SCEditorCats.Scenes)
            {
                string folderPath = "Assets/StrategyCore/Scenes/";
                string sampleScene = folderPath + "DontRemove/EmptySample.unity";
                string newName = "New EmptyScene";

                int index = 1;
                while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.unity") != null)
                {
                    index++;
                }

                // Duplicate the scene
                string newScenePath = folderPath + newName + " " + index + ".unity";
                if (AssetDatabase.CopyAsset(sampleScene, newScenePath))
                {
                    AssetDatabase.Refresh();

                    // Load the newly created scene asset
                    SceneAsset newSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(newScenePath);

                    if (newSceneAsset != null)
                    {
                        // Add it to the database
                        m_ScenesDatabase.Add(newSceneAsset);

                        // Refresh the ListView so everything is redrawn again
                        m_ItemListView.Rebuild();
                        m_ItemListView.style.height = m_ScenesDatabase.Count * m_ItemHeight;
                    }
                }
                else
                {
                    Debug.Log("Failed to copy EmptySample scene");
                }
            }
            else if (m_activeCatIndex == SCEditorCats.Ability)
            {
                // create the menu and add items to it
                GenericDropdownMenu menu = new GenericDropdownMenu();
                Dictionary<string, Type> abilityTypes = GetAbilityTypes();

                foreach (var entry in abilityTypes)
                {
                    string menuName = entry.Key;
                    Type abilityType = entry.Value;

                    menu.AddItem(menuName, false, () =>
                    {
                        CreateAbility(abilityType);
                    });
                }

                menu.DropDown(position: new Rect(Event.current.mousePosition, Vector2.zero), targetElement: rootVisualElement, anchored: false);
            }
        }

        // Create a new Ability ScriptableObject of the selected type
        private void CreateAbility(Type abilityType)
        {
            string folderPath = "Assets/StrategyCore/Resources/Ability/UserCreated/";
            string baseName = abilityType.Name;
            string newName = baseName;

            int index = 1;
            while (AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{folderPath}{newName} {index}.asset") != null)
            {
                index++;
            }

            ScriptableObject newAbility = ScriptableObject.CreateInstance(abilityType);
            Ability ability = (Ability)newAbility;
            ability.abilityName = new string[1] { newName + " " + index };
            AssetDatabase.CreateAsset(newAbility, $"{folderPath}{newName} {index}.asset");
            AssetDatabase.SaveAssets();

            //Add it to the item list
            m_AbilitiesDatabase.Add((Ability)newAbility);

            //Refresh the ListView so everything is redrawn again
            m_ItemListView.Rebuild();
            m_ItemListView.style.height = m_AbilitiesDatabase.Count * m_ItemHeight;
        }

        // Return context menu for abilities
        public static Dictionary<string, Type> GetAbilityTypes()
        {
            Dictionary<string, Type> abilityTypes = new Dictionary<string, Type>();

            foreach (Type type in AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes()))
            {
                if (typeof(Ability).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    CreateAssetMenuAttribute attribute = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                    if (attribute != null)
                    {
                        abilityTypes[attribute.menuName.Replace("StrategyCore/Abilities/", "")] = type;
                    }
                }
            }

            return abilityTypes;
        }

        // DELETE =============================

        private void DeleteItem(ClickEvent evt)
        {
            if (m_activeItem == null) return;

            // Create the menu and add items to it
            GenericDropdownMenu menu = new GenericDropdownMenu();

            menu.AddItem("Cancel", false, () => { });
            menu.AddSeparator("");
            menu.AddItem("Delete", false, () =>
            {
                if (m_activeItem == null) return;

                //Get the path of the fie and delete it through AssetDatabase
                string path = AssetDatabase.GetAssetPath((UnityEngine.Object)m_activeItem);
                string fileNameWithoutExtension = Path.GetFileName(path);
                if (fileNameWithoutExtension == "EmptySample.unity") return;

                if (m_activeCatIndex == SCEditorCats.Scenes)
                {
                    if (!selectedScene.IsValid()) return;
                    EditorSceneManager.CloseScene(selectedScene, true);
                }

                // Delete
                AssetDatabase.DeleteAsset(path);

                // Remove from database
                if (m_activeCatIndex == SCEditorCats.Ability) m_AbilitiesDatabase.Remove((Ability)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.ArmorType) m_ArmorTypesDatabase.Remove((ArmorType)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.Attribute) m_AttributesDatabase.Remove((Attribute)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.DamageType) m_DamageTypesDatabase.Remove((DamageType)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.Effectors) m_EffectorsDatabase.Remove((Effector)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.Projectiles) m_ProjectilesDatabase.Remove((Projectile)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.Resource) m_ResourcesDatabase.Remove((Resource)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.Technology) m_TechnologyDatabase.Remove((Technology)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.UnitPrefabs) m_UnitsDatabase.Remove((Unit)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.WeaponSounds) m_WeaponSoundsDatabase.Remove((WeaponSound)m_activeItem);
                else if (m_activeCatIndex == SCEditorCats.Scenes)
                {
                    m_ScenesDatabase.Remove((SceneAsset)m_activeItem);
                    // Remove folder if exists
                    string folderPath = "Assets/StrategyCore/Scenes/" + Path.GetFileNameWithoutExtension(path);
                    if (AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.DeleteAsset(folderPath);
                }

                m_ItemListView.Rebuild();
                BindActiveItem(m_ItemListView.selectedItem);
            });

            // Display the menu
            menu.DropDown(position: new Rect(Event.current.mousePosition, Vector2.zero), targetElement: rootVisualElement, anchored: false);
        }

        // PATH CLICK =============================

        private void PathClick(ClickEvent evt)
        {
            Label label = (Label)evt.target;

            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(label.text);
            if (obj != null)
            {
                Selection.activeObject = obj; // Select the object in the Editor
                EditorGUIUtility.PingObject(obj); // Highlights the object
            }
        }

        // FileName ==========================================

        private void FileNameChange(BlurEvent evt)
        {
            TextField textField = (TextField)evt.target;
            Label itemPath = textField.parent.parent.parent.Q<Label>("ItemPath");

            string fileName = Path.GetFileName(itemPath.text);
            if (fileName == "EmptySample.unity") return;

            AssetDatabase.RenameAsset(itemPath.text, textField.value);
            if (m_activeCatIndex == SCEditorCats.Scenes)
            {
                // Remove scene folder if exists
                string folderPath = "Assets/StrategyCore/Scenes/" + Path.GetFileNameWithoutExtension(itemPath.text);
                if (AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.RenameAsset(folderPath, textField.value);
            }
            AssetDatabase.SaveAssets();

            m_ItemListView.Rebuild();
            BindActiveItem(m_ItemListView.selectedItem);
        }

        private void ForcedFileNameChange(VisualElement element)
        {
            TextField textField = (TextField)element;
            Label itemPath = textField.parent.parent.parent.Q<Label>("ItemPath");

            if (textField.value == Path.GetFileNameWithoutExtension(itemPath.text)) return;

            string fileName = Path.GetFileName(itemPath.text);
            if (fileName == "EmptySample.unity") return;

            AssetDatabase.RenameAsset(itemPath.text, textField.value);
            if (m_activeCatIndex == SCEditorCats.Scenes)
            {
                // Remove scene folder if exists
                string folderPath = "Assets/StrategyCore/Scenes/" + Path.GetFileNameWithoutExtension(itemPath.text);
                if (AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.RenameAsset(folderPath, textField.value);
            }
            AssetDatabase.SaveAssets();

            m_ItemListView.Rebuild();
        }

        // DISPLAY NAME ============================================
        private void DisplayNameChange(BlurEvent evt)
        {
            m_ItemListView.Rebuild();

            if (m_activeCatIndex == SCEditorCats.Ability)
            {
                Ability selected = (Ability)m_activeItem;
                m_AbilitiesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = (selected.abilityName.Length > 0) ? selected.abilityName[0] : "";
            }
            else if (m_activeCatIndex == SCEditorCats.ArmorType)
            {
                ArmorType selected = (ArmorType)m_activeItem;
                m_ArmorTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
            }
            else if (m_activeCatIndex == SCEditorCats.Attribute)
            {
                Attribute selected = (Attribute)m_activeItem;
                m_AttributesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
            }
            else if (m_activeCatIndex == SCEditorCats.DamageType)
            {
                DamageType selected = (DamageType)m_activeItem;
                m_DamageTypesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
            }
            else if (m_activeCatIndex == SCEditorCats.Effectors)
            {
                Effector selected = (Effector)m_activeItem;
                m_EffectorsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
            }
            else if (m_activeCatIndex == SCEditorCats.Projectiles)
            {
                Projectile selected = (Projectile)m_activeItem;
                m_ProjectilesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.name;
            }
            else if (m_activeCatIndex == SCEditorCats.Resource)
            {
                Resource selected = (Resource)m_activeItem;
                m_ResourcesEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
            }
            else if (m_activeCatIndex == SCEditorCats.Technology)
            {
                Technology selected = (Technology)m_activeItem;
                m_TechnologyEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.displayName;
            }
            else if (m_activeCatIndex == SCEditorCats.UnitPrefabs)
            {
                Unit selected = (Unit)m_activeItem;
                m_UnitsEditor.Q<VisualElement>("ItemInfo").Q<Label>("ItemName").text = selected.unitName;
            }
        }

        // REFRESH ==========================================
        private void Refresh(ClickEvent evt)
        {
            m_ItemListView.Rebuild();
            BindActiveItem(m_ItemListView.selectedItem);
        }

        private void Refresh()
        {
            m_ItemListView.Rebuild();
            BindActiveItem(m_ItemListView.selectedItem);
        }

        // FILTER ==========================================

        private void Filter(ClickEvent evt)
        {
            // Create the menu and add items to it
            GenericDropdownMenu menu = new GenericDropdownMenu();

            if (m_activeCatIndex == SCEditorCats.UnitPrefabs)
            {
                menu.AddItem("All", false, () =>
                {
                    currentFilter = "All";
                    LoadUnits();
                    UnitsListView();
                    BindActiveItem(null);
                });
                for (int i = 0; i < Enum.GetValues(typeof(UnitType)).Length; i++)
                {
                    int index = i;
                    menu.AddItem(((UnitType)i).ToString(), false, () =>
                    {
                        currentFilter = ((UnitType)index).ToString();
                        LoadUnits();
                        UnitsListView();
                        BindActiveItem(null);
                    });
                }
            }
            else if (m_activeCatIndex == SCEditorCats.Ability)
            {
                menu.AddItem("All", false, () =>
                {
                    currentFilter = "All";
                    LoadAbilities();
                    AbilitiesListView();
                    BindActiveItem(null);
                });
                for (int i = 0; i < Enum.GetValues(typeof(AbilityType)).Length; i++)
                {
                    if ((AbilityType)i == AbilityType.Null) continue;

                    int index = i;
                    menu.AddItem(((AbilityType)i).ToString(), false, () =>
                    {
                        currentFilter = ((AbilityType)index).ToString();
                        LoadAbilities();
                        AbilitiesListView();
                        BindActiveItem(null);
                    });
                }
            }

            // Display the menu
            menu.DropDown(position: new Rect(Event.current.mousePosition, Vector2.zero), targetElement: rootVisualElement, anchored: false);
        }

        // VISIBILITY OF ELEMENTS =============================

        private void VisibilityRedrawUpdate()
        {
            noUpdate = false;
            EditorApplication.update -= VisibilityRedrawUpdate;
            if (m_activeCatIndex == SCEditorCats.Ability)
            {
                Ability selected = (Ability)m_activeItem;
                if (selected.isItem == true)
                {
                    m_AbilitiesEditor.Q<VisualElement>("Levels").style.display = DisplayStyle.None;
                    m_AbilitiesEditor.Q<VisualElement>("Technical").Q<VisualElement>("SlotNumberProp").style.display = DisplayStyle.None;
                    m_AbilitiesEditor.Q<VisualElement>("Technical").Q<VisualElement>("ItemParams").style.display = DisplayStyle.Flex;
                }
                else
                {
                    m_AbilitiesEditor.Q<VisualElement>("Levels").style.display = DisplayStyle.Flex;
                    m_AbilitiesEditor.Q<VisualElement>("Technical").Q<VisualElement>("SlotNumberProp").style.display = DisplayStyle.Flex;
                    m_AbilitiesEditor.Q<VisualElement>("Technical").Q<VisualElement>("ItemParams").style.display = DisplayStyle.None;
                }

                if (selected.continuous == true)
                {
                    m_AbilitiesEditor.Q<VisualElement>("Continuous").Q<VisualElement>("ContProps").style.display = DisplayStyle.Flex;
                }
                else
                {
                    m_AbilitiesEditor.Q<VisualElement>("Continuous").Q<VisualElement>("ContProps").style.display = DisplayStyle.None;
                }
            }
            else if (m_activeCatIndex == SCEditorCats.Effectors)
            {
                Effector selected = (Effector)m_activeItem;

                if (selected.permanent) m_EffectorsEditor.Q<VisualElement>("Parameters").Q<VisualElement>("Duration").style.display = DisplayStyle.None;
                else m_EffectorsEditor.Q<VisualElement>("Parameters").Q<VisualElement>("Duration").style.display = DisplayStyle.Flex;

                if (selected.passiveEffectsOn) m_EffectorsEditor.Q<VisualElement>("PassiveChanges").Q<VisualElement>("PassiveEffects").style.display = DisplayStyle.Flex;
                else m_EffectorsEditor.Q<VisualElement>("PassiveChanges").Q<VisualElement>("PassiveEffects").style.display = DisplayStyle.None;
            }
            else if (m_activeCatIndex == SCEditorCats.Projectiles)
            {
                Projectile selected = (Projectile)m_activeItem;

                if (selected.ArcSpeedCurve) m_ProjectilesEditor.Q<VisualElement>("Params").Q<VisualElement>("ASCurve").style.display = DisplayStyle.Flex;
                else m_ProjectilesEditor.Q<VisualElement>("Params").Q<VisualElement>("ASCurve").style.display = DisplayStyle.None;

                if (selected.renderObject == null) m_ProjectilesEditor.Q<VisualElement>("Header").Q<VisualElement>("ItemError").style.display = DisplayStyle.Flex;
                else m_ProjectilesEditor.Q<VisualElement>("Header").Q<VisualElement>("ItemError").style.display = DisplayStyle.None;
            }
            else if (m_activeCatIndex == SCEditorCats.UnitPrefabs)
            {
                Unit selected = (Unit)m_activeItem;

                string prefabPath = AssetDatabase.GetAssetPath(selected);
                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                selected = prefabRoot.GetComponent<Unit>();

                if (selected.netID != 0) selected.netID = 0;

                // Ground Water Air ==============================================
                if (selected.isGround)
                {
                    selected.isWater = false;
                    selected.isAir = false;
                }
                else if (selected.isWater)
                {
                    selected.isGround = false;
                    selected.isAir = false;
                }
                else if (selected.isAir)
                {
                    selected.isGround = false;
                    selected.isWater = false;
                }

                if ((selected.isGround || selected.isWater) && selected.unitType != UnitType.Building && selected.unitType != UnitType.Item)
                {
                    // Add agent navmesh
                    NavMeshAgent na = prefabRoot.GetComponent<NavMeshAgent>();
                    if (na == null) na = prefabRoot.AddComponent<NavMeshAgent>();
                    else na = prefabRoot.GetComponent<NavMeshAgent>();

                    if (selected.isGround) na.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID;
                    else na.agentTypeID = NavMesh.GetSettingsByIndex(1).agentTypeID;
                }
                else
                {
                    // Remove navmesh agent
                    NavMeshAgent na = prefabRoot.GetComponent<NavMeshAgent>();
                    if (na != null) DestroyImmediate(na, true);
                }

                // View Blocker [canmove] ==============================================
                if (selected.viewBlocker || selected.singleCellViewBlocker)
                {
                    if (selected.viewBlocker)
                    {
                        if (selected.gameObject.GetComponent<BoxCollider>() == null)
                        {
                            Debug.LogError("ViewBlocker for " + selected.unitName + " has been disabled. You must first add BoxCollider and configure it.");
                            selected.viewBlocker = false;
                        }
                        else selected.singleCellViewBlocker = false;
                    }
                    else if (selected.singleCellViewBlocker) selected.viewBlocker = false;

                    selected.canMove = false;
                    m_UnitsEditor.Q<VisualElement>("Movement").Q<VisualElement>("CanMove").style.display = DisplayStyle.None;
                }
                else if (selected.unitType != UnitType.Building) m_UnitsEditor.Q<VisualElement>("Movement").Q<VisualElement>("CanMove").style.display = DisplayStyle.Flex;

                // If Building
                if (selected.unitType == UnitType.Building)
                {
                    selected.canMove = false;
                    m_UnitsEditor.Q<VisualElement>("Movement").Q<VisualElement>("CanMove").style.display = DisplayStyle.None;
                }

                // CanMove
                if (selected.canMove) m_UnitsEditor.Q<VisualElement>("Movement").Q<VisualElement>("MoveParams").style.display = DisplayStyle.Flex;
                else m_UnitsEditor.Q<VisualElement>("Movement").Q<VisualElement>("MoveParams").style.display = DisplayStyle.None;

                // Attack - CanAttack
                if (selected.attackUnitSelector.AnySelectors()) m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("AttackData").style.display = DisplayStyle.Flex;
                else m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("AttackData").style.display = DisplayStyle.None;

                // Attack - melee
                if (selected.melee)
                {
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("AttackRange").style.display = DisplayStyle.None;
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("Technical").style.display = DisplayStyle.None;
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("MultiTarget").style.display = DisplayStyle.None;
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("Bounce").style.display = DisplayStyle.None;
                }
                else
                {
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("AttackRange").style.display = DisplayStyle.Flex;
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("Technical").style.display = DisplayStyle.Flex;
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("MultiTarget").style.display = DisplayStyle.Flex;
                    m_UnitsEditor.Q<VisualElement>("Attack").Q<VisualElement>("Bounce").style.display = DisplayStyle.Flex;

                    // Periodic
                    if (selected.attackType == AttackType.Periodic)
                    {
                        if (selected.launchSite.Length > 0 && selected.launchSite.Length != selected.periodicAttackCount) Debug.LogError("Unit " + selected.unitName + " (" + selected.name + ") launch site length does not match the periodic attack count. Fix!.");
                        if (selected.launchVFX.Length > 0 && selected.launchVFX.Length != selected.periodicAttackCount) Debug.LogError("Unit " + selected.unitName + " (" + selected.name + ") launch VFX length does not match the periodic attack count. Fix!.");
                    }

                    // Projectile test
                    if (selected.attackType != AttackType.Continuous)
                    {
                        if (selected.projectileGO != null && selected.projectileGO.GetComponent<Projectile>() == null)
                        {
                            Debug.Log("Ranged units must have Projectile set, you have chosen something else for " + selected.unitName);
                            selected.projectileGO = null;
                        }
                    }
                }

                // Continuous
                if (!selected.melee && selected.attackType == AttackType.Continuous)
                {
                    if (selected.multiTarget && selected.bounceCount > 0)
                    {
                        Debug.Log("Bounce modificator was turned off for " + selected.unitName + ". With continuous attack MultiTarget and Bounce can not be on at the same time.");
                        selected.bounceCount = 0;
                    }

                    // Projectile Test
                    if (selected.projectileGO != null && selected.projectileGO.GetComponent<VFXLine>() == null)
                    {
                        Debug.Log("Continuous must be paired with VFXLine, you have chosen something else for " + selected.unitName);
                        selected.projectileGO = null;
                    }
                }

                // Components
                if (selected.GetComponent<AttributeUnit>()) m_UnitsEditor.Q<VisualElement>("AttributeUnit").style.display = DisplayStyle.Flex;
                else m_UnitsEditor.Q<VisualElement>("AttributeUnit").style.display = DisplayStyle.None;

                if (selected.GetComponent<LevelingUnit>()) m_UnitsEditor.Q<VisualElement>("LevelingUnit").style.display = DisplayStyle.Flex;
                else m_UnitsEditor.Q<VisualElement>("LevelingUnit").style.display = DisplayStyle.None;

                if (selected.GetComponent<TransportUnit>()) m_UnitsEditor.Q<VisualElement>("TransportUnit").style.display = DisplayStyle.Flex;
                else m_UnitsEditor.Q<VisualElement>("TransportUnit").style.display = DisplayStyle.None;

                if (selected.GetComponent<LifetimeUnit>()) m_UnitsEditor.Q<VisualElement>("LifetimeUnit").style.display = DisplayStyle.Flex;
                else m_UnitsEditor.Q<VisualElement>("LifetimeUnit").style.display = DisplayStyle.None;

                if (selected.GetComponent<ConstructionUnit>())
                {
                    m_UnitsEditor.Q<VisualElement>("ConstructionUnit").style.display = DisplayStyle.Flex;
                    if (selected.GetComponent<ConstructionUnit>().isBuilding) m_UnitsEditor.Q<VisualElement>("ConstructionUnit").Q<VisualElement>("Building").style.display = DisplayStyle.Flex;
                    else m_UnitsEditor.Q<VisualElement>("ConstructionUnit").Q<VisualElement>("Building").style.display = DisplayStyle.None;
                }
                else m_UnitsEditor.Q<VisualElement>("ConstructionUnit").style.display = DisplayStyle.None;


                if (selected.GetComponent<ResourceUnit>())
                {
                    ResourceUnit resourceUnit = selected.GetComponent<ResourceUnit>();

                    m_UnitsEditor.Q<VisualElement>("ResourceUnit").style.display = DisplayStyle.Flex;

                    if (resourceUnit.isStorage)
                    {
                        resourceUnit.isCollectible = false;
                        resourceUnit.isCollector = false;
                    }
                    else if (resourceUnit.isCollectible)
                    {
                        resourceUnit.isStorage = false;
                        resourceUnit.isCollector = false;
                    }
                    else if (resourceUnit.isCollector)
                    {
                        resourceUnit.isStorage = false;
                        resourceUnit.isCollectible = false;
                    }

                    if (resourceUnit.isCollectible) m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("Collectible").style.display = DisplayStyle.Flex;
                    else m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("Collectible").style.display = DisplayStyle.None;

                    if (resourceUnit.isCollector) m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("Collector").style.display = DisplayStyle.Flex;
                    else m_UnitsEditor.Q<VisualElement>("ResourceUnit").Q<VisualElement>("Collector").style.display = DisplayStyle.None;
                }
                else m_UnitsEditor.Q<VisualElement>("ResourceUnit").style.display = DisplayStyle.None;

                EditorUtility.SetDirty(selected);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            else if (m_activeCatIndex == SCEditorCats.Resource)
            {
                Resource selected = (Resource)m_activeItem;

                if (selected.limited) m_ResourcesEditor.Q<VisualElement>("Limited").Q<VisualElement>("MaxLimit").style.display = DisplayStyle.Flex;
                else m_ResourcesEditor.Q<VisualElement>("Limited").Q<VisualElement>("MaxLimit").style.display = DisplayStyle.None;
            }
        }

        private void VisibilityRedraw()
        {
            if (noUpdate) return;
            noUpdate = true;
            EditorApplication.update += VisibilityRedrawUpdate;
        }

        // PREVIEW GENERATOR =======================================
        public static void LoadPreview(VisualElement itemIcon, UnityEngine.Object asset)
        {
            int instanceID = asset.GetInstanceID();

            void WaitForPreview()
            {
                Texture2D preview = AssetPreview.GetAssetPreview(asset);

                if (preview != null)
                {
                    itemIcon.style.backgroundImage = new StyleBackground(preview);
                    EditorApplication.update -= WaitForPreview; // Stop checking
                }
            }

            EditorApplication.update += WaitForPreview; // Start checking every frame
        }

        // CURRENT EDITOR
        private VisualElement CurrentEditor()
        {
            if (m_activeCatIndex == SCEditorCats.Ability) return m_AbilitiesEditor;
            if (m_activeCatIndex == SCEditorCats.ArmorType) return m_ArmorTypesEditor;
            if (m_activeCatIndex == SCEditorCats.Attribute) return m_AttributesEditor;
            if (m_activeCatIndex == SCEditorCats.DamageType) return m_DamageTypesEditor;
            if (m_activeCatIndex == SCEditorCats.Effectors) return m_EffectorsEditor;
            if (m_activeCatIndex == SCEditorCats.Projectiles) return m_ProjectilesEditor;
            if (m_activeCatIndex == SCEditorCats.Resource) return m_ResourcesEditor;
            if (m_activeCatIndex == SCEditorCats.Technology) return m_TechnologyEditor;
            if (m_activeCatIndex == SCEditorCats.UnitPrefabs) return m_UnitsEditor;
            if (m_activeCatIndex == SCEditorCats.WeaponSounds) return m_WeaponSoundsEditor;
            if (m_activeCatIndex == SCEditorCats.Scenes) return m_ScenesEditor;
            return null;
        }

        // Array object picker

        private void ModifyArrayUI(PropertyField arrayField) // , SerializedProperty arrayProp)
        {
            var listContainer = arrayField.Q("unity-content-container"); // Get the container for the array elements
            if (listContainer == null) return;

            listContainer.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                int i = 0;
                foreach (var child in listContainer.Children())
                {
                    int index = i;
                    var objectField = child.Q<ObjectField>(); // Find the ObjectField inside the array entry
                    if (objectField == null || child.Q<Button>() != null) continue; // Avoid adding multiple buttons

                    // Create a new button next to the field
                    var pickButton = new Button(() =>
                    {
                        Unit unit = (Unit)m_activeItem;
                        if (unit != null)
                        {
                            SCChildPicker.ShowPicker(selectedAsset =>
                            {
                                unit.launchSite[index] = selectedAsset.transform;
                                EditorUtility.SetDirty(unit);
                            }, unit.gameObject);
                        }

                        //SCChildPicker.ShowPicker(unit.gameObject, unit, true, null);
                        //SCChildPicker.ShowPicker(selected =>
                        //{
                        //    objectField.value = selected; // Assign picked object
                        //    arrayProp.serializedObject.ApplyModifiedProperties(); // Save changes
                        //});
                    })
                    {
                        text = "PICK"
                    };

                    pickButton.style.marginLeft = 5;
                    child.Add(pickButton); // Add the button next to the object field
                    i++;
                }
            });
        }

        // Propagate changes to in-scene instances of the prefab
        private void RegisterAutoApply(VisualElement container)
        {
            foreach (var child in container.Children())
            {
                if (child is PropertyField propertyField)
                {
                    propertyField.RegisterValueChangeCallback(evt => ApplyChanges());
                }
                else if (child is TextField textField)
                {
                    textField.RegisterCallback<ChangeEvent<string>>(evt => ApplyChanges());
                }
                else if (child is ObjectField objectField)
                {
                    objectField.RegisterValueChangedCallback(evt => ApplyChanges());
                }
            }
        }

        private void ApplyChanges()
        {
            if (m_activeCatIndex == SCEditorCats.Ability)
            {
                Ability selected = (Ability)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_activeSerialized.ApplyModifiedProperties(); // Apply changes globally

                if (PrefabUtility.IsPartOfPrefabInstance(selected))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(selected);
                }
            }
            else if (m_activeCatIndex == SCEditorCats.UnitPrefabs)
            {
                Unit selected = (Unit)m_activeItem;
                SerializedObject m_activeSerialized = new SerializedObject(selected);

                m_activeSerialized.ApplyModifiedProperties(); // Apply changes globally

                if (PrefabUtility.IsPartOfPrefabInstance(selected))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(selected);
                }
            }
        }
    }
}
