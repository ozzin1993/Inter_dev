using System;
using Camera_TopDownNS;
using UnityEngine;
using UnityEngine.UIElements;

namespace StrategyCore
{
    // [Interflow 2026-08-01 ADR-005] Клиентская реализация сервисов Presentation.
    // Живёт в сборке Interflow.Client (defineConstraints: !UNITY_SERVER): в серверном билде
    // отсутствует, и хаб остаётся с null-сервисами — симуляция работает, презентации нет.
    // Каждый вызов null-safe к синглтонам: они появляются вместе со сценой.
    public static class PresentationBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Presentation.UI = new GameUIAdapter();
            Presentation.MenuUI = new MenuUIAdapter();
            Presentation.Selection = new SelectionAdapter();
            Presentation.Audio = new AudioAdapter();
            Presentation.Camera = new CameraAdapter();
        }

        class GameUIAdapter : IGameUI
        {
            static UIManager UI => UIManager.Instance;
            public void ShowNotify(string msg) { if (UI) UI.ShowNotifyMsg(msg); }
            public void AddChatMsg(string msg, int owner, bool allyChat) { if (UI) UI.AddChatMsg(msg, owner, allyChat); }
            public void AddChatServerMsg(string msg) { if (UI) UI.AddChatServerMsg(msg); }
            public void ShowChatBox() { if (UI) UI.ShowChatBox(); }
            public void UpdateResourceTab(int resourceID) { if (UI) UI.UpdateResourceTab(resourceID); }
            public void RefreshResourceTab() { if (UI) UI.RefreshResourceTab(); }
            public void ShowWaveTimer(int seconds) { if (UI) UI.ShowWaveTimer(seconds); }
            public void ShowTeamCommand(int teamIndex, int groupIndex, BottomTableAction action) { if (UI) UI.ShowTeamCommand(teamIndex, groupIndex, action); }
            public void ShowMainBuildingProgress(int teamIndex, int level, int experience, int experiencePerLevel) { if (UI) UI.ShowMainBuildingProgress(teamIndex, level, experience, experiencePerLevel); }
            public void ResetMiniMap() { if (UI) UI.ResetMiniMap(); }
            public void CreatePinger(int owner, Vector2 pos) { if (UI) UI.CreatePinger(owner, pos); }
            public bool IsLeveling => UI != null && UI.isLeveling;
            public void ShowLevelButton() { if (UI) UI.ShowLevelButton(); }
            public void HideLevelButton(bool levelingOff) { if (UI) UI.HideLevelButton(levelingOff); }
            public void RedrawAbilityView() { if (UI) UI.RedrawAbilityView(); }
            public void SubscribeToUnit() { if (UI) UI.SubscribeToUnit(); }
            public void UnsubscribeToUnit(Unit unit) { if (UI) UI.UnsubscribeToUnit(unit); }
            public void Resubscribe() { if (UI) UI.Resubscribe(); }
        }

        class MenuUIAdapter : IMenuUI
        {
            static UIManagerMenu M => UIManagerMenu.Instance;
            public bool MenuReady => M != null && M.UIDocument != null && M.UIDocument.rootVisualElement != null;
            public void ShowMenuLobby(int stage) { if (M) M.ShowMenuLobby(stage); }
            public void FillPlayerList() { if (M) M.FillPlayerList(); }
            public void AddChatMsg(string msg, int owner) { if (M) M.AddChatMsg(msg, owner); }
            public void AddChatServerMsg(string msg) { if (M) M.AddChatServerMsg(msg); }
            public void ShowUIDocument() { if (M) M.ShowUIDocument(); }
            public void HideUIDocument() { if (M) M.HideUIDocument(); }

            // Рантайм-кнопка меню (вёрстка перенесена 1:1 из ServerBootstrap.InjectButtonWhenReady):
            // Label + классы кнопок меню, вставка после insertAfterName, дедуп по имени.
            public bool TryAddMenuButton(string name, string text, string insertAfterName, Action onClick)
            {
                if (!MenuReady) return false;
                VisualElement root = M.UIDocument.rootVisualElement;
                VisualElement menu = root.Q("Menu");
                VisualElement menuButtons = menu != null ? menu.Q("MenuButtons") : null;
                if (menuButtons == null) return false;
                if (menuButtons.Q(name) != null) return true; // уже есть — пропуск

                Label btn = new Label(text);
                btn.name = name;
                btn.AddToClassList("buttonColors");
                btn.AddToClassList("menuButton");
                btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());

                VisualElement after = insertAfterName != null ? menuButtons.Q(insertAfterName) : null;
                int index = after != null ? menuButtons.IndexOf(after) + 1 : menuButtons.childCount;
                menuButtons.Insert(index, btn);
                return true;
            }

            // Отсчёт до старта матча в лобби (вёрстка перенесена 1:1 из NetworkDataSync.MatchCountdown).
            public void ShowMatchCountdown(int seconds)
            {
                if (!MenuReady) return;
                VisualElement lobby = M.UIDocument.rootVisualElement.Q("Lobby");
                if (lobby == null) return;

                Label label = lobby.Q<Label>("MatchCountdown");
                if (label == null)
                {
                    label = new Label { name = "MatchCountdown" };
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.style.fontSize = 20;
                    label.style.color = Color.white;
                    lobby.Add(label);
                }

                if (seconds > 0)
                {
                    label.text = $"Старт через {seconds}…";
                    label.style.display = DisplayStyle.Flex;
                }
                else
                {
                    label.style.display = DisplayStyle.None;
                }
            }
        }

        class SelectionAdapter : ISelection
        {
            static PlayerControl PC => PlayerControl.Instance;
            public Unit ActiveUnit => PC != null ? PC.activeUnit : null;
            public void AddToSelection(Unit unit, bool replaceSelection, bool noSound) { if (PC) PC.AddToSelection(unit, replaceSelection, noSound); }
            public void RemoveFromSelection(Unit unit) { if (PC) PC.RemoveFromSelection(unit); }
            public bool IsSelected(Unit unit) => PC != null && PC.IsSelected(unit);
            public void ResetSelection() { if (PC) PC.Reset(); }
        }

        class AudioAdapter : IGameAudio
        {
            static SoundFXManager S => SoundFXManager.Instance;
            static UnityEngine.Audio.AudioMixerGroup Grp(bool fx) => (fx && S != null) ? S.fxGroup : null;
            public void PlaySoundClip(AudioClip clip, float volume, bool fxGroup) { if (S) S.PlaySoundClip(clip, volume, Grp(fxGroup)); }
            public void PlaySoundClip(AudioClip clip, Transform at, float volume, bool fxGroup) { if (S) S.PlaySoundClip(clip, at, volume, Grp(fxGroup)); }
            public void PlaySoundClip(AudioClip[] clips, float volume, bool fxGroup) { if (S) S.PlaySoundClip(clips, volume, Grp(fxGroup)); }
            public void PlaySoundClip(AudioClip[] clips, Transform at, float volume, bool fxGroup) { if (S) S.PlaySoundClip(clips, at, volume, Grp(fxGroup)); }
            public Transform PlayLoopSoundClip(AudioClip[] clips, Transform at, float volume) => S != null ? S.PlayLoopSoundClip(clips, at, volume, Grp(true)) : null;
            public void PlayCommandSound(Unit unit, AudioClip[] clips, float volume, bool onlyVisible) { if (S) S.PlayCommandSound(unit, clips, volume, onlyVisible); }
            public void PlayVoiceClip(AudioClip clip, float volume) { if (S) S.PlaySoundClip(clip, volume, S.voiceGroup); }
        }

        class CameraAdapter : IGameCamera
        {
            static Camera_TopDown C => Camera_TopDown.Instance;
            public bool HasCursor => C != null;
            public Vector2 GetCursorPosition() => C != null ? C.GetCursorPosition() : Vector2.zero;
            public void SetLimits(Vector4 squareLimits) { if (C) C.squareLimits = squareLimits; }
            public void SetPosition(Vector3 pos) { if (C) C.transform.position = pos; }
        }
    }
}
