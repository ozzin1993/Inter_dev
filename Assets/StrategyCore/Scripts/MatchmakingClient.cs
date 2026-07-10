using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Клиентский поиск игры через Unity Matchmaker (классический ticket-flow, решение A1 2026-07-03):
    /// вход в UGS → тикет в очередь → поллинг статуса → по назначению (ip:port) — подключение
    /// через существующий NetworkConnectionHandler.StartClient(name, ip, port). Шов ассета не меняется.
    ///
    /// Код ассета не трогает (правило 1): только штатные публичные API.
    /// Серверного кода здесь нет — компонент чисто клиентский (правило 6).
    /// Кладётся в сцену Menu на отдельный GameObject (аналогично ServerBootstrap).
    /// </summary>
    public class MatchmakingClient : MonoBehaviour
    {
        [Header("Матчмейкинг")]
        [Tooltip("Имя очереди Matchmaker (Unity Dashboard → Matchmaker → Queues). Задаст Artsiom при настройке " +
                 "Dashboard (задача T2). Пусто — поиск не стартует, будет варнинг.")]
        [SerializeField] private string queueName = "";
        [Tooltip("Интервал опроса статуса тикета, сек. По доке Unity — не чаще раза в секунду.")]
        [SerializeField] private float pollIntervalSeconds = 1f;
        [Tooltip("Таймаут поиска, сек. 0 — без лимита: ждать до отмены игроком (решение Artsiom 2026-07-03).")]
        [SerializeField] private float timeoutSeconds = 0f;

        /// <summary>
        /// Статус поиска для UI (задача A2): «Поиск…», «Найден», «Ошибка: …», «Отменено».
        /// </summary>
        public event Action<string> OnStatusChanged;

        /// <summary>Идёт ли сейчас поиск игры.</summary>
        public bool IsSearching { get; private set; }

        // Id активного тикета матчмейкинга (null — тикета нет). Нужен для поллинга и удаления.
        private string ticketId;
        // Токен отмены текущего поиска: CancelMatchmaking и OnDestroy прерывают цикл поллинга.
        private CancellationTokenSource searchCts;

        // ===================== ПУБЛИЧНЫЙ API =====================

        /// <summary>
        /// Начать поиск игры. По назначению вызовет NetworkConnectionHandler.StartClient(playerName, ip, port).
        /// </summary>
        /// <param name="playerName">Имя игрока (уйдёт в ConnectionData для ConnectionApproval сервера).</param>
        public void FindMatch(string playerName)
        {
            // Повторный FindMatch во время поиска — игнор с логом (правило 8, идемпотентность).
            if (IsSearching)
            {
                Debug.LogWarning("[MatchmakingClient] Поиск уже идёт — повторный FindMatch проигнорирован.");
                return;
            }
            if (string.IsNullOrWhiteSpace(queueName))
            {
                Debug.LogWarning("[MatchmakingClient] Имя очереди (queueName) не задано в Inspector — поиск невозможен.");
                ReportStatus("Ошибка: не задано имя очереди матчмейкинга.");
                return;
            }
            // Сеть уже запущена (Host/Client/Server): NCH.StartClient потом молча не сработал бы,
            // а матч на сервере остался бы ждать игрока — не создаём тикет вовсе (правило 8).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[MatchmakingClient] Сеть уже запущена — поиск не начат.");
                ReportStatus("Ошибка: сеть уже запущена.");
                return;
            }

            IsSearching = true;
            searchCts?.Dispose(); // CTS предыдущего поиска: к этому моменту тот цикл завершён (IsSearching был false)
            searchCts = new CancellationTokenSource();
            _ = SearchAsync(playerName, searchCts.Token); // fire-and-forget; все исходы обрабатываются внутри
        }

        /// <summary>Отменить текущий поиск (тикет будет удалён).</summary>
        public void CancelMatchmaking()
        {
            // Cancel после назначения/завершения: поиск уже не идёт — отменять нечего (правило 8).
            if (!IsSearching)
            {
                Debug.Log("[MatchmakingClient] Поиск не идёт — отменять нечего (возможно, матч уже найден).");
                return;
            }
            searchCts?.Cancel();
        }

        // ===================== ВНУТРЕННЕЕ =====================

        // Инициализация UGS и анонимный вход. Идемпотентно: повторные вызовы безопасны (правило 8) —
        // инициализация пропускается, если уже выполнена; вход — если уже вошли.
        // Ошибки (нет Project ID, нет сети) уходят исключением наружу — их ловит вызывающий (шаг 5).
        private async Task EnsureUgsReadyAsync()
        {
            // При состоянии Initializing InitializeAsync вернёт уже идущую задачу — дождёмся её, не дублируя.
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            // Повторный SignInAnonymouslyAsync при активной сессии кинул бы исключение — потому guard.
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // Полный цикл поиска: вход в UGS → тикет → поллинг → назначение → StartClient.
        // Запускается из FindMatch; прерывается токеном (CancelMatchmaking/OnDestroy).
        // Классификация ошибок сервисов (нет Project ID/сети) — шаг 5.
        private async Task SearchAsync(string playerName, CancellationToken token)
        {
            try
            {
                ReportStatus("Поиск…");
                await EnsureUgsReadyAsync();
                token.ThrowIfCancellationRequested();

                // Тикет: id игрока — PlayerId анонимного входа (Matchmaker требует уникальные id).
                var players = new List<Player> { new Player(AuthenticationService.Instance.PlayerId, new Dictionary<string, object>()) };
                var options = new CreateTicketOptions(queueName, new Dictionary<string, object>());
                CreateTicketResponse created = await MatchmakerService.Instance.CreateTicketAsync(players, options);
                ticketId = created.Id;
                Debug.Log($"[MatchmakingClient] Тикет создан: {ticketId} (очередь «{queueName}»).");

                float startTime = Time.realtimeSinceStartup;
                while (true)
                {
                    // Пауза между опросами (по доке — не чаще раза в секунду); отменяемая.
                    await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), token);

                    TicketStatusResponse status = await MatchmakerService.Instance.GetTicketAsync(ticketId);
                    token.ThrowIfCancellationRequested();

                    if (status?.Type == typeof(IpPortAssignment))
                    {
                        if (HandleAssignment(playerName, (IpPortAssignment)status.Value))
                            return; // исход достигнут (успех или ошибка назначения)
                    }
                    else if (status?.Type != null && status.Type != typeof(NoneAssignment))
                    {
                        // Наш Cloud Code-модуль обязан отдавать IpPort (решение A1); иное — ошибка конфигурации пула.
                        ReportStatus($"Ошибка: неожиданный тип назначения ({status.Type.Name}).");
                        return;
                    }
                    // status == null или NoneAssignment: назначения ещё нет — ждём дальше.

                    if (timeoutSeconds > 0f && Time.realtimeSinceStartup - startTime >= timeoutSeconds)
                    {
                        ReportStatus("Ошибка: время поиска истекло.");
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                ReportStatus("Отменено");
            }
            // Шаг 5: классификация ошибок. Внятный статус в событие + LogWarning, исключения наружу
            // не выходят (fire-and-forget) — FindMatch не роняет игру без настроенного Dashboard.
            catch (ServicesInitializationException e)
            {
                Debug.LogWarning($"[MatchmakingClient] UGS не инициализируются (проект не привязан? Project Settings → Services): {e}");
                ReportStatus("Ошибка: сервисы Unity не инициализированы (нет Project ID?).");
            }
            catch (AuthenticationException e)
            {
                Debug.LogWarning($"[MatchmakingClient] Анонимный вход не удался: {e}");
                ReportStatus("Ошибка: не удалось войти в Unity Services.");
            }
            catch (MatchmakerServiceException e)
            {
                Debug.LogWarning($"[MatchmakingClient] Ошибка матчмейкера: {e}");
                ReportStatus($"Ошибка: матчмейкер недоступен ({e.Message}).");
            }
            catch (RequestFailedException e)
            {
                Debug.LogWarning($"[MatchmakingClient] Запрос к сервисам Unity не прошёл (нет сети?): {e}");
                ReportStatus($"Ошибка: сервис недоступен ({e.Message}).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MatchmakingClient] Непредвиденная ошибка поиска: {e}");
                ReportStatus($"Ошибка: {e.Message}");
            }
            finally
            {
                IsSearching = false;
                await DeleteTicketIfAnyAsync(); // не течь тикетами: удаление при любом исходе
            }
        }

        // Разбор назначения. true — поиск завершён (успех или ошибка), false — ждать дальше (InProgress).
        private bool HandleAssignment(string playerName, IpPortAssignment assignment)
        {
            switch (assignment.Status)
            {
                case IpPortAssignment.StatusOptions.Found:
                    if (string.IsNullOrEmpty(assignment.Ip) || !assignment.Port.HasValue)
                    {
                        ReportStatus("Ошибка: назначение найдено, но не содержит ip/port.");
                        return true;
                    }
                    if (NetworkConnectionHandler.instance == null)
                    {
                        ReportStatus("Ошибка: NetworkConnectionHandler не найден в сцене.");
                        return true;
                    }
                    ReportStatus("Найден");
                    Debug.Log($"[MatchmakingClient] Сервер: {assignment.Ip}:{assignment.Port.Value} — подключаюсь.");
                    // Существующий шов ассета: порт передаётся строкой (NCH парсит в ushort).
                    NetworkConnectionHandler.instance.StartClient(playerName, assignment.Ip, assignment.Port.Value.ToString());
                    return true;

                case IpPortAssignment.StatusOptions.Failed:
                    ReportStatus($"Ошибка: матч не собран ({assignment.Message}).");
                    return true;

                case IpPortAssignment.StatusOptions.Timeout:
                    ReportStatus("Ошибка: тикет просрочен матчмейкером.");
                    return true;

                default: // InProgress — матч собирается, продолжаем поллинг
                    return false;
            }
        }

        // Удаление тикета best-effort: неуспех удаления не меняет исход поиска, но логируется —
        // не глушим молча (правило 9). Id обнуляется до запроса, чтобы не удалить дважды.
        private async Task DeleteTicketIfAnyAsync()
        {
            if (string.IsNullOrEmpty(ticketId)) return;
            string id = ticketId;
            ticketId = null;
            try
            {
                await MatchmakerService.Instance.DeleteTicketAsync(id);
                Debug.Log($"[MatchmakingClient] Тикет {id} удалён.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MatchmakingClient] Не удалось удалить тикет {id}: {e.Message}");
            }
        }

        // Статус: лог + событие для UI.
        private void ReportStatus(string status)
        {
            Debug.Log($"[MatchmakingClient] Статус: {status}");
            OnStatusChanged?.Invoke(status);
        }

        private void OnDestroy()
        {
            // Выход из сцены во время поиска: прерываем цикл. Dispose не вызываем — SearchAsync может
            // ещё держать токен; CTS одного поиска доживает до следующего FindMatch (там Dispose).
            searchCts?.Cancel();
        }
    }
}
