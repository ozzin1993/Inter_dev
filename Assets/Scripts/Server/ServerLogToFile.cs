using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Пишет ВЕСЬ вывод Unity (Debug.Log/Warning/Error + необработанные исключения со стектрейсами) в файл.
    /// Наш компонент — код ассета НЕ тронут (правило 1). Вешать в сцену меню рядом с ServerBootstrap.
    /// Источник истины по событиям сервера, переживает смену сцен (DontDestroyOnLoad).
    /// </summary>
    public class ServerLogToFile : MonoBehaviour
    {
        public static ServerLogToFile instance;

        [Tooltip("Логировать только на headless-сервере. Сними галку, чтобы писать лог и в редакторе/на клиенте (для отладки самого логгера).")]
        [SerializeField] private bool serverOnly = true;

        [Tooltip("Папка для логов. Создаётся рядом с билдом (родитель папки *_Data); в редакторе — в корне проекта.")]
        [SerializeField] private string logFolderName = "Logs";

        [Tooltip("Префикс имени файла. Итоговое имя: <префикс>_ГГГГ-ММ-ДД_ЧЧ-ММ-СС.log")]
        [SerializeField] private string fileNamePrefix = "server";

        [Tooltip("Добавлять стектрейс и к обычным Log. Для Warning/Error/Exception/Assert стектрейс пишется всегда.")]
        [SerializeField] private bool stackTraceForInfo = false;

        [Tooltip("Сбрасывать на диск после каждого сообщения. Надёжно при крашах (лог не теряется), но чуть медленнее.")]
        [SerializeField] private bool autoFlush = true;

        private StreamWriter writer;
        private readonly object fileLock = new object();
        private string filePath;

        private void Awake()
        {
            // Один экземпляр на процесс, переживает смену сцен.
            if (instance != null) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);

            // По умолчанию пишем только на настоящем headless-сервере.
            if (serverOnly && !ServerBootstrap.IsHeadlessServer) return;

            OpenFile();

            // Threaded-вариант ловит сообщения со ВСЕХ потоков (вкл. фоновые) — иначе часть логов теряется.
            Application.logMessageReceivedThreaded += HandleLog;
        }

        private void OpenFile()
        {
            try
            {
                // Папка рядом с билдом: родитель <Product>_Data. В редакторе dataPath = <project>/Assets → корень проекта.
                string baseDir = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                string dir = Path.Combine(baseDir, logFolderName);
                Directory.CreateDirectory(dir);

                filePath = Path.Combine(dir, $"{fileNamePrefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

                // UTF-8 без BOM, чтобы кириллица читалась корректно в любом редакторе.
                writer = new StreamWriter(filePath, false, new UTF8Encoding(false)) { AutoFlush = autoFlush };
                writer.WriteLine($"=== Interflow server log === start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                writer.WriteLine($"unityVersion={Application.unityVersion} platform={Application.platform} " +
                                 $"headless={ServerBootstrap.IsHeadlessServer} args={string.Join(" ", Environment.GetCommandLineArgs())}");
                writer.Flush();

                // Это сообщение уйдёт в консоль (в файл не попадёт — подписка ниже), чтобы было видно путь к логу.
                Debug.Log($"[ServerLogToFile] Лог сервера пишется в: {filePath}");
            }
            catch (Exception e)
            {
                // OpenFile вне callback'а логов — здесь предупредить можно.
                Debug.LogWarning($"[ServerLogToFile] Не удалось открыть файл лога: {e.Message}");
                writer = null;
            }
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (writer == null) return;
            lock (fileLock)
            {
                if (writer == null) return;
                try
                {
                    writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{type}] {condition}");

                    bool withStack = type == LogType.Error || type == LogType.Exception || type == LogType.Assert || stackTraceForInfo;
                    if (withStack && !string.IsNullOrEmpty(stackTrace)) writer.Write(stackTrace);
                }
                catch
                {
                    // Логгер не должен валить игру и НЕ должен звать Debug.* отсюда (рекурсия в этот же callback). Глушим намеренно.
                }
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= HandleLog;
            CloseFile();
        }

        private void OnApplicationQuit()
        {
            CloseFile();
        }

        private void CloseFile()
        {
            lock (fileLock)
            {
                if (writer == null) return;
                try
                {
                    writer.WriteLine($"=== end {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    writer.Flush();
                    writer.Dispose();
                }
                catch { }
                writer = null;
            }
        }
    }
}
