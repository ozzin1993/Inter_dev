using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-01 client-data] Мигратор «полного нуля»: переносит звуки/иконки из полей
    // сим-классов в ClientContentTable (Assets/ClientContent, вне Resources) и ОБНУЛЯЕТ исходные поля
    // на диске. Клиент получает данные обратно через ClientContentRehydrator (вешается этим же
    // мигратором в сцены Build Settings); сервер — не пакует и не грузит их вовсе.
    // Идемпотентен: пустое поле НЕ затирает ранее собранную запись таблицы.
    // ПОРЯДОК: 1) эта кнопка → 2) Tools/Interflow/Roles-стрип (новый клиентский тип попадёт в стрип)
    // → 3) пересборка Server и Client.
    public static class InterflowClientDataMigrator
    {
        const string TableDir = "Assets/ClientContent";
        const string TablePath = TableDir + "/ClientContentTable.asset";
        const string MediaDir = "Assets/Art/FromResources";

        [MenuItem("Tools/Interflow/Вынос клиентских данных (миграция)")]
        public static void Migrate()
        {
            // [Interflow fix 2026-08-01] Защита от потери данных: в Play Mode регидратор/Ability.Init уже
            // подменили поля В ПАМЯТИ (пустая иконка → missingTexture), и повторная миграция записала бы
            // заглушку в таблицу поверх настоящей иконки. Мигрируем только в спокойном редакторе.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Миграция] Нельзя запускать в Play Mode — выйди из режима игры и повтори.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(TableDir)) AssetDatabase.CreateFolder("Assets", "ClientContent");
            var table = AssetDatabase.LoadAssetAtPath<ClientContentTable>(TablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<ClientContentTable>();
                AssetDatabase.CreateAsset(table, TablePath);
            }

            int units = MigrateUnits(table);
            int abilities = MigrateAbilities(table);
            int soIcons = MigrateSoIcons(table);
            int weapons = MigrateWeaponSounds(table);
            int techs = MigrateFactionConfigs(table);
            int moved = MoveMediaOutOfResources();

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            int scenes = AttachRehydratorToScenes(table);

            Debug.Log($"[Миграция клиентских данных] Готово. Юниты: {units}, умения: {abilities}, SO-иконки: {soIcons}, " +
                      $"WeaponSound: {weapons}, тех-иконки: {techs}, медиа вынесено из Resources: {moved}, сцен обновлено: {scenes}. " +
                      "ДАЛЬШЕ: Tools → Interflow → Roles-стрип (обязательно!) → пересборка Server и Client.");
        }

        // ---------- юниты (префабы в Resources) ----------
        static int MigrateUnits(ClientContentTable table)
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null || root.GetComponentInChildren<Unit>(true) == null) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                foreach (Unit u in contents.GetComponentsInChildren<Unit>(true))
                {
                    // Ключ — ассет-компонент Unit корневого префаба (для регидрации по ссылке).
                    Unit assetUnit = root.GetComponentsInChildren<Unit>(true).FirstOrDefault(x => x.unitName == u.unitName);
                    if (assetUnit == null) continue;

                    bool hasData = u.icon != null || Any(u.readySound) || Any(u.moveSound) || Any(u.clickSound)
                                   || Any(u.deathSound) || Any(u.attackCommandSound) || Any(u.attackStartSound) || Any(u.attackEndSound);
                    if (!hasData) continue;

                    var e = table.units.FirstOrDefault(x => x.prefab == assetUnit);
                    if (e == null) { e = new ClientContentTable.UnitEntry { prefab = assetUnit }; table.units.Add(e); }
                    // Пустое поле не затирает собранное ранее (идемпотентность).
                    if (u.icon != null) e.icon = u.icon;
                    if (Any(u.readySound)) e.ready = u.readySound;
                    if (Any(u.moveSound)) e.move = u.moveSound;
                    if (Any(u.clickSound)) e.click = u.clickSound;
                    if (Any(u.deathSound)) e.death = u.deathSound;
                    if (Any(u.attackCommandSound)) e.attackCommand = u.attackCommandSound;
                    if (Any(u.attackStartSound)) e.attackStart = u.attackStartSound;
                    if (Any(u.attackEndSound)) e.attackEnd = u.attackEndSound;

                    u.icon = null;
                    u.readySound = new AudioClip[0];
                    u.moveSound = new AudioClip[0];
                    u.clickSound = new AudioClip[0];
                    u.deathSound = new AudioClip[0];
                    u.attackCommandSound = new AudioClip[0];
                    u.attackStartSound = new AudioClip[0];
                    u.attackEndSound = new AudioClip[0];
                    dirty = true;
                    count++;
                }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return count;
        }

        static bool Any(AudioClip[] a) => a != null && a.Length > 0;

        // [Interflow fix 2026-08-01] Заглушка «нет иконки» (её подставляет Ability.Init при пустом массиве) —
        // в таблицу НЕ пишем, иначе настоящая иконка потерялась бы при повторной миграции.
        static bool IsPlaceholder(Texture2D t)
        {
            if (t == null) return false;
            Texture2D missing = ReferenceManager.instance != null ? ReferenceManager.instance.missingTexture : null;
            return missing != null && t == missing;
        }

        // ---------- способности ----------
        static int MigrateAbilities(ClientContentTable table)
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Ability"))
            {
                var a = AssetDatabase.LoadAssetAtPath<Ability>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null || a.icon == null || a.icon.Length == 0) continue;
                if (a.icon.Length == 1 && IsPlaceholder(a.icon[0])) continue; // заглушка — настоящая иконка уже в таблице

                var e = table.abilities.FirstOrDefault(x => x.ability == a);
                if (e == null) { e = new ClientContentTable.AbilityEntry { ability = a }; table.abilities.Add(e); }
                e.icons = a.icon;
                a.icon = new Texture2D[0];
                EditorUtility.SetDirty(a);
                count++;
            }
            return count;
        }

        // ---------- одиночные иконки SO ----------
        static int MigrateSoIcons(ClientContentTable table)
        {
            int count = 0;
            count += MigrateIcon<Resource>(table, r => r.icon, (r, v) => r.icon = v);
            count += MigrateIcon<ArmorType>(table, r => r.icon, (r, v) => r.icon = v);
            count += MigrateIcon<DamageType>(table, r => r.icon, (r, v) => r.icon = v);
            count += MigrateIcon<Effector>(table, r => r.icon, (r, v) => r.icon = v);
            count += MigrateIcon<Attribute>(table, r => r.icon, (r, v) => r.icon = v);
            return count;
        }

        static int MigrateIcon<T>(ClientContentTable table, System.Func<T, Texture2D> get, System.Action<T, Texture2D> set) where T : ScriptableObject
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var so = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (so == null || get(so) == null) continue;
                if (IsPlaceholder(get(so))) continue; // заглушка — настоящая иконка уже в таблице

                var e = table.soIcons.FirstOrDefault(x => x.asset == so);
                if (e == null) { e = new ClientContentTable.SoIconEntry { asset = so }; table.soIcons.Add(e); }
                e.icon = get(so);
                set(so, null);
                EditorUtility.SetDirty(so);
                count++;
            }
            return count;
        }

        // ---------- звуки оружия ----------
        static int MigrateWeaponSounds(ClientContentTable table)
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponSound"))
            {
                var ws = AssetDatabase.LoadAssetAtPath<WeaponSound>(AssetDatabase.GUIDToAssetPath(guid));
                if (ws == null) continue;
                bool hasClips = (ws.groundHitClips != null && ws.groundHitClips.Length > 0)
                    || (ws.attackToArmorSound != null && ws.attackToArmorSound.Any(p => p != null && p.audioClips != null && p.audioClips.Length > 0));
                if (!hasClips) continue;

                var e = table.weaponSounds.FirstOrDefault(x => x.asset == ws);
                if (e == null) { e = new ClientContentTable.WeaponSoundEntry { asset = ws }; table.weaponSounds.Add(e); }
                e.attackToArmor = ws.attackToArmorSound;
                e.groundHit = ws.groundHitClips;

                // В SO остаётся ТОЛЬКО структура типов брони (логика сим-кода), клипы уходят.
                var stripped = new AttackToArmorSound[ws.attackToArmorSound != null ? ws.attackToArmorSound.Length : 0];
                for (int i = 0; i < stripped.Length; i++)
                    stripped[i] = new AttackToArmorSound { armorType = ws.attackToArmorSound[i] != null ? ws.attackToArmorSound[i].armorType : null, audioClips = new AudioClip[0] };
                ws.attackToArmorSound = stripped;
                ws.groundHitClips = new AudioClip[0];
                EditorUtility.SetDirty(ws);
                count++;
            }
            return count;
        }

        // ---------- деревья техов фракций ----------
        static int MigrateFactionConfigs(ClientContentTable table)
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:FactionConfig"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<FactionConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (cfg == null) continue;
                if (!table.factionConfigs.Contains(cfg)) table.factionConfigs.Add(cfg);

                bool dirty = false;
                if (cfg.techTiers != null)
                    foreach (var tier in cfg.techTiers)
                    {
                        if (tier == null) continue;
                        dirty |= TakeNodeIcon(table, cfg, tier.levelUpgrade, ref count);
                        dirty |= TakeBig(table, cfg, tier.optionA, ref count);
                        dirty |= TakeBig(table, cfg, tier.optionB, ref count);
                    }
                if (dirty) EditorUtility.SetDirty(cfg);
            }
            return count;
        }

        static bool TakeBig(ClientContentTable table, FactionConfig cfg, TechBigOption o, ref int count)
        {
            if (o == null) return false;
            bool d = TakeNodeIcon(table, cfg, o.node, ref count);
            d |= TakeNodeIcon(table, cfg, o.specializationA, ref count);
            d |= TakeNodeIcon(table, cfg, o.specializationB, ref count);
            return d;
        }

        static bool TakeNodeIcon(ClientContentTable table, FactionConfig cfg, TechNode nd, ref int count)
        {
            if (nd == null || nd.icon == null) return false;
            if (nd.technology == null)
            {
                Debug.LogWarning($"[Миграция] {cfg.name}: узел с иконкой БЕЗ технологии-ключа — иконка оставлена на месте (попадёт в серверный билд).");
                return false;
            }
            var e = table.techIcons.FirstOrDefault(x => x.tech == nd.technology);
            if (e == null) { e = new ClientContentTable.TechIconEntry { tech = nd.technology }; table.techIcons.Add(e); }
            e.icon = nd.icon;
            nd.icon = null;
            count++;
            return true;
        }

        // ---------- медиа-файлы, лежащие ФАЙЛАМИ в Resources (пакуются всегда) ----------
        static int MoveMediaOutOfResources()
        {
            string[] exts = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".wav", ".mp3", ".ogg" };
            int moved = 0;
            foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/Resources" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!exts.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                if (!AssetDatabase.IsValidFolder(MediaDir))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
                    AssetDatabase.CreateFolder("Assets/Art", "FromResources");
                }
                string dst = AssetDatabase.GenerateUniqueAssetPath(MediaDir + "/" + Path.GetFileName(path));
                string err = AssetDatabase.MoveAsset(path, dst); // GUID сохраняется — ссылки живут
                if (string.IsNullOrEmpty(err)) moved++;
                else Debug.LogWarning($"[Миграция] Не удалось вынести {path}: {err}");
            }
            return moved;
        }

        // ---------- регидратор в сцены Build Settings ----------
        static int AttachRehydratorToScenes(ClientContentTable table)
        {
            int n = 0;
            string startScene = EditorSceneManager.GetActiveScene().path;
            foreach (var s in EditorBuildSettings.scenes.Where(x => x.enabled))
            {
                var scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                // [Interflow fix 2026-08-01] Таблицу перезагружаем ПОСЛЕ OpenScene(Single): смена сцены могла
                // выгрузить прежний объект, и присвоение stale-ссылки писалось в сцену как {fileID: 0}
                // (регидратор в рантайме получал table == null → данные не возвращались, иконки пустели).
                var tableRef = AssetDatabase.LoadAssetAtPath<ClientContentTable>(TablePath);
                GameObject go = GameObject.Find("ClientContent");
                if (go == null) go = new GameObject("ClientContent");
                var r = go.GetComponent<ClientContentRehydrator>();
                if (r == null) r = go.AddComponent<ClientContentRehydrator>();
                var so = new SerializedObject(r);
                so.FindProperty("table").objectReferenceValue = tableRef;
                so.ApplyModifiedPropertiesWithoutUndo();
                so.Update();
                if (so.FindProperty("table").objectReferenceValue == null)
                    Debug.LogError($"[Миграция] Сцена {s.path}: ссылка на таблицу НЕ записалась — проверь вручную объект ClientContent!");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                n++;
            }
            if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
            return n;
        }
    }
}
