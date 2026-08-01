using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

namespace StrategyCore
{
    /// <summary>
    /// [S2] Серверный жизненный цикл под self-hosted аллокацию (Контракт_Аллокации.md §2/§4.2/§11).
    /// Тёплый процесс: поллит аллокацию → поднимает сервер + /ready → ждёт конца матча (смерть замка)
    /// → /deallocate → Application.Quit(). Включается аргументом запуска -allocator <url>;
    /// без него ServerBootstrap работает как раньше (локальный тест T1) — регресса нет.
    ///
    /// Partial к ServerBootstrap (правило 5: одна точка входа, файл < 400 строк). Ассет не трогаем (правило 1).
    /// Весь код — серверный: режим включается только на выделенном сервере (аргументом запуска).
    /// </summary>
    public partial class ServerBootstrap
    {
        [Header("Жизненный цикл (allocator)")]
        [Tooltip("Аргумент запуска с URL локального allocator'а, например: -allocator http://127.0.0.1:8090. " +
                 "Передан → режим lifecycle (ждём аллокацию поллингом, не стартуем сервер сразу). " +
                 "Не передан → обычное поведение (локальный тест T1).")]
        [SerializeField] private string allocatorArg = "-allocator";
        [Tooltip("Путь эндпоинта опроса аллокации (Контракт §4.2). Полный запрос: <url><путь>?port=N&pid=P.")]
        [SerializeField] private string myAllocationPath = "/my-allocation";
        [Tooltip("Путь эндпоинта подтверждения готовности сервера (Контракт §4.2). Тело: {port, matchId}.")]
        [SerializeField] private string readyPath = "/ready";
        [Tooltip("Путь эндпоинта освобождения слота (Контракт §4.2). Тело: {matchId}.")]
        [SerializeField] private string deallocatePath = "/deallocate";
        [Tooltip("Интервал опроса /my-allocation, сек (Контракт §7.7; дефолт allocator'а — 2с).")]
        [SerializeField] private float pollIntervalSeconds = 2f;
        [Tooltip("Как часто (сек) писать в лог об ожидании/недоступности allocator'а, чтобы не спамить каждый опрос.")]
        [SerializeField] private float logThrottleSeconds = 30f;

        [Header("Конец матча")]
        [Tooltip("Задержка перед Application.Quit после конца матча, сек — дать клиентам увидеть финал.")]
        [SerializeField] private float quitDelaySeconds = 5f;
        [Tooltip("Сколько ждать спавна замков (юнитов победного условия specificUnitsDead) для подписки на их смерть, сек.")]
        [SerializeField] private float castleSubscribeTimeoutSeconds = 30f;

        private string allocatorBaseUrl; // URL из аргумента (без завершающего '/'). Пусто → режим выключен.
        private ushort listenPort;       // порт прослушивания (S1), один на весь процесс
        private string allocatedMatchId; // matchId текущей аллокации
        private bool allocationHandled;  // защита от повторной обработки аллокации (двойной 200)
        private bool matchEndHandled;    // защита от повторного deallocate/Quit (двойная победа)

        // JSON-модели (JsonUtility): только нужные поля. Ростер не парсим — в слоты не применяем (задача O1).
        [Serializable] private class AllocationResponse { public string matchId; }
        [Serializable] private class ReadyRequest { public int port; public string matchId; }
        [Serializable] private class DeallocateRequest { public string matchId; }

        // Разобрать аргумент -allocator <url>. Валидный URL → включить режим lifecycle
        // (DontDestroyOnLoad + старт поллинга) и вернуть true (обычный автозапуск пропускается).
        // Аргумента нет → false (текущее поведение T1). Битый конфиг → ошибка, режим не стартует.
        private bool TryEnterLifecycleMode()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != allocatorArg) continue;

                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    Debug.LogError($"[ServerBootstrap] Аргумент {allocatorArg} передан без URL — режим lifecycle не запущен.");
                    return true; // намерение было lifecycle, но конфиг битый: НЕ падать в обычный автозапуск
                }

                allocatorBaseUrl = args[i + 1].TrimEnd('/');
                if (!TryResolveListenPort(out listenPort))
                {
                    Debug.LogError("[ServerBootstrap] Невалидный порт (-port) — режим lifecycle не запущен.");
                    return true;
                }

                // 1 процесс = 1 матч: сервер не возвращается в меню, дублей не будет — держим объект
                // живым между сценами (меню → матч), чтобы дожить до конца матча.
                DontDestroyOnLoad(gameObject);
                Debug.Log($"[ServerBootstrap] Режим lifecycle: allocator={allocatorBaseUrl}, порт={listenPort}, опрос каждые {pollIntervalSeconds}с.");
                StartCoroutine(PollAllocationLoop());
                return true;
            }

            return false; // аргумента нет — обычное поведение (T1)
        }

        // Корутина опроса аллокации. Ошибка сети → лог с троттлингом, продолжаем (Контракт §6);
        // 204 → аллокации ещё нет, ждём; 200 → передаём в ApplyAllocation и прекращаем опрос.
        private IEnumerator PollAllocationLoop()
        {
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var wait = new WaitForSeconds(pollIntervalSeconds);
            float lastLog = -99999f;

            Debug.Log($"[ServerBootstrap] Старт опроса аллокации: port={listenPort}, pid={pid}.");
            while (true)
            {
                string url = $"{allocatorBaseUrl}{myAllocationPath}?port={listenPort}&pid={pid}";
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.ConnectionError)
                    {
                        if (Time.unscaledTime - lastLog > logThrottleSeconds)
                        {
                            Debug.LogWarning($"[ServerBootstrap] Allocator недоступен ({url}): {req.error}. Продолжаю опрос.");
                            lastLog = Time.unscaledTime;
                        }
                    }
                    else if (req.responseCode == 204)
                    {
                        if (Time.unscaledTime - lastLog > logThrottleSeconds)
                        {
                            Debug.Log("[ServerBootstrap] Аллокации пока нет (204) — жду.");
                            lastLog = Time.unscaledTime;
                        }
                    }
                    else if (req.responseCode == 200)
                    {
                        string body = req.downloadHandler.text;
                        Debug.Log($"[ServerBootstrap] Аллокация получена (200), {body.Length} симв.");
                        StartCoroutine(ApplyAllocation(body));
                        yield break;
                    }
                    else
                    {
                        if (Time.unscaledTime - lastLog > logThrottleSeconds)
                        {
                            Debug.LogWarning($"[ServerBootstrap] Неожиданный ответ allocator'а: {req.responseCode} {req.error}. Продолжаю опрос.");
                            lastLog = Time.unscaledTime;
                        }
                    }
                }
                yield return wait;
            }
        }

        // Обработка аллокации: разобрать matchId, поднять сервер, при успехе — POST /ready и запуск слежения за концом матча.
        // Ростер в слоты не применяем (O1) — только логируем факт (без имён/playerId, §10).
        private IEnumerator ApplyAllocation(string body)
        {
            if (allocationHandled) yield break; // идемпотентность (двойной 200)
            allocationHandled = true;

            AllocationResponse parsed = null;
            try { parsed = JsonUtility.FromJson<AllocationResponse>(body); }
            catch (Exception e) { Debug.LogError($"[ServerBootstrap] Ответ /my-allocation не разобран: {e.Message} ({body.Length} симв.)."); }

            if (parsed == null || string.IsNullOrEmpty(parsed.matchId))
            {
                Debug.LogError("[ServerBootstrap] В ответе /my-allocation нет matchId — сервер не поднимаем (слот уйдёт в ready_timeout).");
                yield break;
            }

            allocatedMatchId = parsed.matchId;
            Debug.Log($"[ServerBootstrap] Аллокация принята: matchId={allocatedMatchId}, ростер {body.Length} симв. (в слоты не применяем — O1).");

            StartAsServer();
            yield return null; // дать кадр на инициализацию NGO/транспорта

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsListening)
            {
                Debug.LogError("[ServerBootstrap] Сервер не поднялся (StartServer) — /ready не отправляем.");
                yield break;
            }

            yield return PostReady(allocatedMatchId);
            StartCoroutine(WatchMatchEnd()); // ждём конца матча → deallocate + Quit
        }

        // POST /ready {port, matchId} — после успешного StartServer(). Слот allocated → ready.
        private IEnumerator PostReady(string matchId)
        {
            string url = $"{allocatorBaseUrl}{readyPath}";
            string json = JsonUtility.ToJson(new ReadyRequest { port = listenPort, matchId = matchId });
            using (UnityWebRequest req = MakeJsonPost(url, json))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200)
                    Debug.Log($"[ServerBootstrap] /ready ок: слот {listenPort} → ready (matchId={matchId}).");
                else
                    Debug.LogWarning($"[ServerBootstrap] /ready не удался: {req.responseCode} {req.error}. Слот освободится по ready_timeout (§6.2).");
            }
        }

        // ===================== КОНЕЦ МАТЧА =====================

        // Штатный сигнал конца матча (§10-решение): смерть замка через публичный Unit.OnDie.
        // Ждём старта матча, резолвим замки по specificUnitsDead (тот же источник, что у GameManager)
        // и подписываемся на их OnDie. Замок = одно здание на условие → его смерть = матч решён.
        private IEnumerator WatchMatchEnd()
        {
            // Ждём фактического старта матча (сцена матча загружена, юниты спавнятся).
            while (SlotManager.instance == null || SlotManager.instance.gameStarted != GameState.Started)
                yield return null;

            if (GameManager.instance == null || GameManager.instance.specificUnitsDead == null ||
                GameManager.instance.specificUnitsDead.Length == 0)
            {
                Debug.LogWarning("[ServerBootstrap] specificUnitsDead не настроен — конец матча по замку не отследить. " +
                                 "Слот освободится по match_ttl (§6.5).");
                yield break;
            }

            // Собрать netID замков из победных условий.
            List<ushort> castleIds = new List<ushort>();
            foreach (SpeficicUnitsDead cond in GameManager.instance.specificUnitsDead)
                if (cond != null && cond.unitIds != null)
                    foreach (ushort id in cond.unitIds) castleIds.Add(id);

            // Дождаться спавна замков и подписаться на их OnDie (с таймаутом на случай отсутствия).
            HashSet<ushort> subscribed = new HashSet<ushort>();
            float waited = 0f;
            while (subscribed.Count < castleIds.Count && waited < castleSubscribeTimeoutSeconds)
            {
                foreach (ushort id in castleIds)
                {
                    if (subscribed.Contains(id)) continue;
                    if (SlotManager.instance.unitNetID.TryGetValue(id, out Unit castle) && castle != null)
                    {
                        castle.OnDie += OnCastleDie;
                        subscribed.Add(id);
                        Debug.Log($"[ServerBootstrap] Подписка на смерть замка netID={id}.");
                    }
                }
                if (subscribed.Count < castleIds.Count) { waited += Time.unscaledDeltaTime; yield return null; }
            }

            if (subscribed.Count < castleIds.Count)
                Debug.LogWarning($"[ServerBootstrap] Подписался на {subscribed.Count}/{castleIds.Count} замков " +
                                 $"(таймаут {castleSubscribeTimeoutSeconds}с). Остальные не заспавнились/не резолвятся.");
        }

        // Замок разрушен → матч окончен. Идемпотентно (несколько замков / повторный вызов → один deallocate).
        private void OnCastleDie(Unit unitThatDies, int playerThatKills, Unit unitThatKills, bool rewards)
        {
            if (matchEndHandled) return;
            matchEndHandled = true;
            Debug.Log($"[ServerBootstrap] Замок разрушен (netID={unitThatDies.netID}) — конец матча.");
            StartCoroutine(EndMatchTeardown());
        }

        // Финализация: /deallocate → задержка (клиенты видят финал) → Application.Quit(0).
        private IEnumerator EndMatchTeardown()
        {
            if (!string.IsNullOrEmpty(allocatedMatchId))
                yield return PostDeallocate(allocatedMatchId);
            else
                Debug.LogWarning("[ServerBootstrap] matchId пуст — /deallocate пропущен, всё равно Quit.");

            if (quitDelaySeconds > 0f)
                yield return new WaitForSeconds(quitDelaySeconds);

            Debug.Log("[ServerBootstrap] Application.Quit(0).");
            Application.Quit(0);
        }

        // POST /deallocate {matchId}. Ошибка → всё равно Quit (слот освободится по pid-детекту, §6.3).
        private IEnumerator PostDeallocate(string matchId)
        {
            string url = $"{allocatorBaseUrl}{deallocatePath}";
            string json = JsonUtility.ToJson(new DeallocateRequest { matchId = matchId });
            using (UnityWebRequest req = MakeJsonPost(url, json))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200)
                    Debug.Log($"[ServerBootstrap] /deallocate ок (matchId={matchId}).");
                else
                    Debug.LogWarning($"[ServerBootstrap] /deallocate не удался: {req.responseCode} {req.error}. Всё равно Quit (слот — по pid-детекту, §6.3).");
            }
        }

        // Сформировать POST с JSON-телом (application/json). Используется для /ready и /deallocate.
        private static UnityWebRequest MakeJsonPost(string url, string json)
        {
            UnityWebRequest req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }
    }
}
