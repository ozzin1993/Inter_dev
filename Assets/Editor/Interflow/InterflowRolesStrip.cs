using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using Unity.Multiplayer;
using Unity.Multiplayer.Editor;

namespace StrategyCore
{
    // [Interflow 2026-08-01] Roles-стрип (ADR-005, вариант 1). Одна кнопка готовит Multiplayer Roles:
    //  1) включает механизм ролей (com.unity.dedicated-server);
    //  2) автострип рендера/UI/аудио для роли Server (встроенные компоненты Unity);
    //  3) помечает ролью Client ВСЕ компоненты клиентских сборок (Interflow.Client, Interflow.Input) —
    //     при сборке серверного билда Unity физически удалит их из сцен и префабов ДО паковки.
    //     Это лечит порчу соседних компонентов от missing-script («Read 40 bytes but expected 140»)
    //     и снимает с сервера клиентскую нагрузку;
    //  4) назначает роли билд-профилям по имени: содержит "server" → Server, иначе Client.
    // Идемпотентно: перезапускать после добавления новых клиентских скриптов.
    // ВАЖНО: серверный билд собирать при АКТИВНОЙ клиентской платформе (Windows) — в редакторе
    // должны существовать клиентские классы, иначе стрип не найдёт их типы.
    public static class InterflowRolesStrip
    {
        static readonly string[] ClientAssemblies = { "Interflow.Client", "Interflow.Input" };

        [MenuItem("Tools/Interflow/Roles-стрип: настроить (ADR-005)")]
        public static void Configure()
        {
            EditorMultiplayerRolesManager.EnableMultiplayerRoles = true;
            EditorMultiplayerRolesManager.AutomaticSelection.Server.StripRenderingComponents = true;
            EditorMultiplayerRolesManager.AutomaticSelection.Server.StripUIComponents = true;
            EditorMultiplayerRolesManager.AutomaticSelection.Server.StripAudioComponents = true;

            // Все конкретные компоненты клиентских сборок → роль Client.
            var custom = EditorMultiplayerRolesManager.AutomaticSelection.GetCustomComponents();
            int marked = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!ClientAssemblies.Contains(asm.GetName().Name)) continue;
                foreach (var t in asm.GetTypes())
                {
                    if (t.IsAbstract || !t.IsSubclassOf(typeof(Component))) continue;
                    custom[t] = MultiplayerRoleFlags.Client;
                    marked++;
                }
            }
            // [Interflow 2026-08-01 server-opt] Встроенные компоненты-носители клиентских ДАННЫХ, которые
            // авто-стрип не покрывает: MeshFilter (держит ссылки на меши статичных моделей — вырезание при
            // сборке отвязывает меши от сцен/префабов, они не пакуются и не грузятся на сервере; сим-код
            // MeshFilter не использует — grep 0; MeshCollider НЕ трогаем, это логика) и LODGroup.
            // ParticleSystem НЕ стрипаем: на него ссылаются поля сим-классов (Projectile.impactVFX,
            // ReferenceManager.moveVFX) — вырезание сломало бы ссылки; на сервере его глушит
            // ServerPresentationStripper + партиклы без Renderer'ов не рисуются.
            custom[typeof(MeshFilter)] = MultiplayerRoleFlags.Client;
            custom[typeof(LODGroup)] = MultiplayerRoleFlags.Client;
            marked += 2;
            EditorMultiplayerRolesManager.AutomaticSelection.SetCustomComponents(custom);

            // Роли билд-профилей: «…server…» в имени → Server, прочие → Client.
            foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile == null) continue;
                var role = profile.name.ToLowerInvariant().Contains("server")
                    ? MultiplayerRoleFlags.Server : MultiplayerRoleFlags.Client;
                try
                {
                    if (EditorMultiplayerRolesManager.GetMultiplayerRoleForBuildProfile(profile) != role)
                        EditorMultiplayerRolesManager.SetMultiplayerRoleForBuildProfile(profile, role);
                    Debug.Log($"[Roles-стрип] Профиль «{profile.name}» → роль {role}.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Roles-стрип] Профиль «{profile.name}»: роль не назначена ({e.Message}). Назначь вручную: Build Profiles → профиль → Multiplayer Role.");
                }
            }

            SaveContentSelection();
            // Включение ролей (MultiplayerManager.asset: EnableMultiplayerRoles) движок держит в памяти до
            // Save Project — сохраняем принудительно, иначе перезапуск редактора до сборки теряет включение
            // и стрип НЕ применяется (подтверждено прогоном 2026-08-01: 14 missing-script в серверном логе).
            EditorApplication.ExecuteMenuItem("File/Save Project");
            Debug.Log($"[Roles-стрип] Готово. Клиентских типов с ролью Client: {marked} " +
                      $"(сборки: {string.Join(", ", ClientAssemblies)}); автострип Server: Render+UI+Audio. " +
                      "Серверный билд собирай при активной клиентской платформе (Windows).");
        }

        // Сохранение настроек пакета + бейк списка стрипа во внутренний MultiplayerManager.
        // Повторяет путь UI пакета: SetDirty(instance) → SaveIfDirty() (internal → рефлексия).
        static void SaveContentSelection()
        {
            var so = Resources.FindObjectsOfTypeAll<ScriptableObject>()
                .FirstOrDefault(o => o.GetType().FullName == "Unity.Multiplayer.Editor.ContentSelectionSettings");
            if (so == null)
            {
                Debug.LogWarning("[Roles-стрип] Не нашёл ContentSelectionSettings — сохрани проект вручную (Ctrl+S), настройки добъются при сохранении.");
                return;
            }
            EditorUtility.SetDirty(so);
            var save = so.GetType().GetMethod("SaveIfDirty",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (save != null) save.Invoke(so, null);
            else AssetDatabase.SaveAssets();
        }
    }
}
