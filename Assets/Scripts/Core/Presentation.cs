using System;
using Unity.Netcode;
using UnityEngine;

namespace StrategyCore
{
    // [Interflow 2026-08-01 ADR-005] Мост «симуляция → презентация» (изоляция клиент/сервер).
    // Симуляция и сетевой слой НЕ ссылаются на клиентские классы (UIManager, UIManagerMenu,
    // SoundFXManager, PlayerControl, Camera_TopDown): вместо прямых вызовов — сервисы этого хаба.
    // Клиентская сборка регистрирует реализации (PresentationBridge). На выделенном сервере
    // реализаций нет — вызовы null-safe уходят в пустоту, а серверные релеи выполняются ЗДЕСЬ,
    // поэтому рассылка клиентам не теряется (правило 6: серверная авторитетность).
    public static class Presentation
    {
        // ---- Сервисы презентации (регистрирует клиентская сборка; на сервере остаются null) ----
        public static IGameUI UI;
        public static IMenuUI MenuUI;
        public static ISelection Selection;
        public static IGameAudio Audio;
        public static IGameCamera Camera;

        // ---- Серверо-авторитетные обёртки: релей по сети + локальная отрисовка.
        // Логика перенесена 1:1 из UIManager.ShowNotifyMsg / UIManager(Menu).AddChatServerMsg
        // (бывшие форк-гейты «релей до презентации»). ----

        /// Локальное уведомление (без адресата и сети).
        public static void NotifyMsg(string msg) => UI?.ShowNotify(msg);

        /// Уведомление игроку: если адресат не локальный и вызвано сервером — релей GameMsgSend.
        /// Поведение оригинала сохранено полностью (включая локальный показ после серверного релея).
        public static void NotifyMsg(string msg, int player, bool calledByServer = false)
        {
            if (SlotManager.instance.currentPlayer != player)
            {
                if (calledByServer)
                {
                    if (NetworkManager.Singleton && NetworkManager.Singleton.IsServer && NetworkDataSync.instance && NetworkDataSync.instance.IsSpawned) NetworkDataSync.instance.GameMsgSend(msg, player);
                }
                else return;
            }
            UI?.ShowNotify(msg);
        }

        /// Серверное сообщение в чат МАТЧА: релей всем клиентам + локальная отрисовка.
        public static void ChatServerMsg(string msg)
        {
            if (NetworkDataSync.instance && NetworkDataSync.instance.IsSpawned && NetworkManager.Singleton && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ServerMsgSend(msg);
            UI?.AddChatServerMsg(msg);
        }

        /// Серверное сообщение в чат МЕНЮ: релей всем клиентам + локальная отрисовка.
        public static void MenuChatServerMsg(string msg)
        {
            if (NetworkDataSync.instance && NetworkDataSync.instance.IsSpawned && NetworkManager.Singleton && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.ServerMsgSend(msg);
            MenuUI?.AddChatServerMsg(msg);
        }

        /// Серверное сообщение в актуальный чат (меню или матч) — паттерн NetworkConnectionHandler.
        public static void ChatServerMsgAuto(string msg)
        {
            if (SlotManager.instance != null && SlotManager.instance.gameStarted == GameState.Menu) MenuChatServerMsg(msg);
            else ChatServerMsg(msg);
        }
    }

    /// Игровой HUD (адаптер над UIManager). Все методы — только локальная отрисовка.
    public interface IGameUI
    {
        void ShowNotify(string msg);
        void AddChatMsg(string msg, int owner, bool allyChat);
        void AddChatServerMsg(string msg);
        void ShowChatBox();
        void UpdateResourceTab(int resourceID);
        void RefreshResourceTab();
        void ShowWaveTimer(int seconds);
        void ResetMiniMap();
        void CreatePinger(int owner, Vector2 pos);
        bool IsLeveling { get; }
        void ShowLevelButton();
        void HideLevelButton(bool instant);
        void RedrawAbilityView();
        void SubscribeToUnit();
        void UnsubscribeToUnit(Unit unit);
        void Resubscribe();
    }

    /// UI меню/лобби (адаптер над UIManagerMenu).
    public interface IMenuUI
    {
        bool MenuReady { get; }
        void ShowMenuLobby(int stage);
        void FillPlayerList();
        void AddChatMsg(string msg, int owner);
        void AddChatServerMsg(string msg);
        void ShowUIDocument();
        void HideUIDocument();
        /// Вставка рантайм-кнопки в меню (кнопка SERVER из ServerBootstrap): true — вставлена или уже есть, false — меню ещё не готово.
        bool TryAddMenuButton(string name, string text, string insertAfterName, Action onClick);
        /// Отсчёт до старта матча в лобби (seconds <= 0 — спрятать); вёрстка — на клиенте.
        void ShowMatchCountdown(int seconds);
    }

    /// Выделение/выбор юнитов (адаптер над PlayerControl).
    public interface ISelection
    {
        Unit ActiveUnit { get; }
        void AddToSelection(Unit unit, bool clearCurrent, bool subscribe);
        void RemoveFromSelection(Unit unit);
        bool IsSelected(Unit unit);
        void ResetSelection();
    }

    /// Звук (адаптер над SoundFXManager). fxGroup=true — микшер-группа эффектов, false — группа по умолчанию.
    public interface IGameAudio
    {
        void PlaySoundClip(AudioClip clip, float volume, bool fxGroup = true);
        void PlaySoundClip(AudioClip clip, Transform at, float volume, bool fxGroup = true);
        void PlaySoundClip(AudioClip[] clips, float volume, bool fxGroup = true);
        void PlaySoundClip(AudioClip[] clips, Transform at, float volume, bool fxGroup = true);
        Transform PlayLoopSoundClip(AudioClip[] clips, Transform at, float volume);
        void PlayCommandSound(Unit unit, AudioClip[] clips, float volume, bool onlyVisible);
        /// Одиночный клип в голосовую группу микшера (бывш. voiceGroup: завершение стройки/апгрейда/исследования).
        void PlayVoiceClip(AudioClip clip, float volume);
    }

    /// Камера (адаптер над Camera_TopDown).
    public interface IGameCamera
    {
        bool HasCursor { get; }
        Vector2 GetCursorPosition();
        void SetLimits(Vector4 squareLimits);
        void SetPosition(Vector3 pos);
    }
}
