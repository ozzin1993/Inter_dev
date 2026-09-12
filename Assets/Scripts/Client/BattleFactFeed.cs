using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StrategyCore
{
    /// <summary>
    /// [Interflow 2026-09-09 passive-facts] ЛЕНТА СРАБАТЫВАНИЙ: последние разовые факты боя строками
    /// в углу экрана. Надпись над юнитом живёт секунду и улетает — по ней не понять, что именно
    /// сработало полсекунды назад и у кого; лента держит историю и показывает имя носителя.
    ///
    /// [Interflow 2026-09-11] Строки собираются в ГРУППЫ ПО УДАРУ (решение Artsiom 11.09.2026):
    /// заголовок «кто, каким умением, по кому, на сколько, здоровье было → стало из максимума»
    /// и под ним детали расчёта с отступом. Раньше каждый факт был сам по себе, и по ленте нельзя
    /// было сказать, кто нанёс удар и из чего сложилось число.
    ///
    /// Группа опознаётся номером удара, который выдаёт приёмник (UnitReceiver.Queue.cs). Номер 0
    /// означает «факт вне удара» — такая строка живёт сама по себе и ни с чем не сливается.
    /// Детали приходят РАНЬШЕ заголовка (снижение урона считается до разбора записи), поэтому группа
    /// заводится по первой пришедшей строке, а заголовок в неё дописывается.
    ///
    /// Инструмент ПРОВЕРОЧНЫЙ, поэтому нарисован на IMGUI — ему не нужны ассеты UI Toolkit, вёрстка
    /// и место в дереве игрового интерфейса. Тот же выбор и по той же причине сделан для панели
    /// тестового полигона (InterflowTestArenaUI).
    ///
    /// Живёт в сборке Interflow.Client (defineConstraints: !UNITY_SERVER) — на выделенном сервере
    /// ленты нет. Создаёт себя сама при старте приложения, как SkillPresenter: ни сцену, ни префабы
    /// править не надо.
    ///
    /// Данные приходят из единственной точки — SkillPresenter уже собрал строку и цвет (правило 5:
    /// слова живут там же, где надписи над юнитом, а не вторым набором здесь).
    /// </summary>
    public class BattleFactFeed : MonoBehaviour
    {
        /// <summary>Одна строка внутри группы.</summary>
        struct Line
        {
            public string text;
            public Color color;
        }

        /// <summary>
        /// Группа строк одного удара: заголовок и детали. Заголовка может не быть вовсе —
        /// удар, где сработало только снижение урона, заголовок получит лишь с разбором записи.
        /// </summary>
        class Group
        {
            public int hitId;
            public bool hasHeader;
            public Line header;
            public readonly List<Line> details = new List<Line>();
            public float touchedUnscaled;     // время последней строки: по нему группа гаснет
        }

        /// <summary>
        /// Клавиша показа и скрытия ленты. F8, потому что соседние заняты: F9 — панель тестового
        /// полигона, F10 — игровое меню (подсказка «F10 - Menu» в углу экрана).
        /// </summary>
        const Key ToggleKey = Key.F8;

        /// <summary>Жёсткий предел хранилища ГРУПП. Настройка ограничивает ПОКАЗ, а этот предел — память:
        /// Push зовётся и до того, как появились настройки, и список иначе рос бы весь матч.</summary>
        const int MaxStored = 48;

        /// <summary>Отступ деталей, когда ассета настроек нет: лента обязана работать и без него.</summary>
        const string DefaultIndent = "        ";

        const float PanelWidth = 760f;   // [2026-09-11] шире: заголовок несёт двух юнитов, умение и здоровье
        const float LineHeight = 18f;

        static readonly List<Group> groups = new List<Group>();
        static BattleFactFeed instance;

        bool hidden;
        bool hiddenRead;                 // начальное состояние берётся из настроек один раз
        GUIStyle lineStyle;
        Texture2D background;

        /// <summary>Плоский список строк на отрисовку. Поле, а не локальная переменная: OnGUI зовётся часто.</summary>
        static readonly List<Line> flat = new List<Line>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (Utils.Headless) return;     // показывать некому
            if (instance != null) return;   // уничтоженный объект Unity сравнивается с null как null — пересоздастся

            GameObject go = new GameObject("BattleFactFeed");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BattleFactFeed>();
        }

        /// <summary>
        /// Заголовок группы: «кто → кому, на сколько, здоровье». Зовётся презентером — он уже собрал
        /// строку и цвет. Номер 0 — строка вне удара: она становится собственной группой без деталей.
        /// Заголовок у группы один: повторный вызов с тем же номером не перезаписывает первый —
        /// первым приходит исход удара («мимо», «неуязвим»), и он важнее позднейших уточнений.
        /// </summary>
        public static void PushHeader(int hitId, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;

            Group g = hitId != 0 ? Find(hitId) : null;
            if (g == null) g = Open(hitId);

            if (!g.hasHeader)
            {
                g.hasHeader = true;
                g.header.text = text;
                g.header.color = color;
                g.touchedUnscaled = Time.unscaledTime;
                return;
            }

            // Заголовок у группы уже есть: вторую претендующую строку не выбрасываем, а кладём деталью.
            // Так у удара не теряются ни записи со второй по последнюю (умение бьёт пакетом из нескольких),
            // ни отказ наложения состояния — он приходит ПОСЛЕ разбора записи, когда заголовок занят.
            AddDetail(g, text, color);
        }

        /// <summary>
        /// Строка-деталь под заголовком: ступень расчёта, сработавшее правило, наложенное состояние.
        /// Деталь может прийти раньше заголовка — группа тогда заводится ею.
        /// </summary>
        public static void PushDetail(int hitId, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;

            Group g = hitId != 0 ? Find(hitId) : null;
            if (g == null) g = Open(hitId);

            AddDetail(g, text, color);
        }

        /// <summary>
        /// Положить строку-деталь. Отступ приклеивается ЗДЕСЬ, а не при отрисовке: OnGUI зовётся
        /// по нескольку раз за кадр, и склейка строк там давала бы постоянный мусор на горячем пути.
        /// </summary>
        static void AddDetail(Group g, string text, Color color)
        {
            SkillPresentationSettings s = SkillPresenter.Instance != null ? SkillPresenter.Instance.FeedSettings : null;

            Line line;
            line.text = (s != null ? s.feedDetailIndent : DefaultIndent) + text;
            line.color = color;
            g.details.Add(line);
            g.touchedUnscaled = Time.unscaledTime;
        }

        /// <summary>Найти живую группу этого удара. Ищем с конца: свежие удары лежат в хвосте.</summary>
        static Group Find(int hitId)
        {
            for (int i = groups.Count - 1; i >= 0; i--)
                if (groups[i].hitId == hitId) return groups[i];

            return null;
        }

        /// <summary>Завести новую группу и обрезать хранилище с головы.</summary>
        static Group Open(int hitId)
        {
            Group g = new Group { hitId = hitId, touchedUnscaled = Time.unscaledTime };
            groups.Add(g);

            // Обрезаем с головы: список короткий, сдвиг дешевле кольцевого буфера и читается проще.
            while (groups.Count > MaxStored) groups.RemoveAt(0);

            return g;
        }

        /// <summary>Очистить ленту. Зовётся сменой сцены — строки прошлого матча в новом не нужны.</summary>
        public static void Clear() => groups.Clear();

        void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;

        void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;

        void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
            => Clear();

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[ToggleKey].wasPressedThisFrame) hidden = !hidden;
        }

        void OnGUI()
        {
            SkillPresentationSettings s = SkillPresenter.Instance != null ? SkillPresenter.Instance.FeedSettings : null;

            // Начальное состояние — из настроек, и только один раз: дальше решает клавиша.
            if (!hiddenRead && s != null)
            {
                hidden = s.factFeedHidden;
                hiddenRead = true;
            }

            if (groups.Count == 0) return;

            int maxLines = s != null ? Mathf.Max(1, s.factFeedLines) : 14;
            float keepSeconds = s != null ? Mathf.Max(1f, s.factFeedSeconds) : 10f;
            float dim = s != null ? s.feedDetailDim : 0.75f;
            float now = Time.unscaledTime;

            // Протухшие снимаем здесь, а не в Push: группа живёт по времени последней своей строки.
            // ДО проверки «лента скрыта»: иначе на скрытой ленте группы копились бы до предела хранилища
            // и при показе вывалили бы историю получасовой давности.
            for (int i = groups.Count - 1; i >= 0; i--)
                if (now - groups[i].touchedUnscaled > keepSeconds) groups.RemoveAt(i);

            if (hidden || groups.Count == 0) return;

            // Набираем окно ГРУППАМИ целиком, с конца: иначе верхняя граница могла бы разрезать группу,
            // и в ленте остались бы детали с отступом без заголовка, к которому они относятся.
            int budget = maxLines;
            int firstGroup = groups.Count;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                int need = (groups[i].hasHeader ? 1 : 0) + groups[i].details.Count;
                if (need > budget && firstGroup < groups.Count) break;   // следующая целиком не влезет

                budget -= need;
                firstGroup = i;
                if (budget <= 0) break;
            }

            // Разворачиваем выбранные группы в строки: заголовок, затем детали.
            flat.Clear();
            for (int i = firstGroup; i < groups.Count; i++)
            {
                Group g = groups[i];
                float age = (now - g.touchedUnscaled) / keepSeconds;
                float alpha = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(age));

                if (g.hasHeader) flat.Add(Faded(g.header, alpha, 1f));

                for (int d = 0; d < g.details.Count; d++)
                    flat.Add(Faded(g.details[d], alpha, dim));
            }

            if (flat.Count == 0) return;

            EnsureStyle();

            // Одна группа длиннее всего окна (умение с десятком записей) — показываем её хвост:
            // заголовок к этому моменту уже уехал, но иначе не влезло бы вообще ничего.
            int shown = Mathf.Min(maxLines, flat.Count);
            int first = flat.Count - shown;

            float x = Screen.width - PanelWidth - 10f;
            float y = 10f;
            float height = shown * LineHeight + 8f;

            GUI.DrawTexture(new Rect(x, y, PanelWidth, height), background);

            for (int i = 0; i < shown; i++)
            {
                Line line = flat[first + i];

                lineStyle.normal.textColor = line.color;
                GUI.Label(new Rect(x + 6f, y + 4f + i * LineHeight, PanelWidth - 12f, LineHeight), line.text, lineStyle);
            }
        }

        /// <summary>
        /// Цвет строки с учётом возраста группы и приглушения деталей: свежее читается в первую очередь,
        /// а заголовок — ярче своих деталей.
        /// </summary>
        static Line Faded(Line line, float alpha, float dim)
        {
            Color c = line.color;
            c.r *= dim;
            c.g *= dim;
            c.b *= dim;
            c.a = alpha;
            line.color = c;
            return line;
        }

        /// <summary>
        /// Стиль и подложка создаются один раз и переживают сцену: подложка — однопиксельная текстура,
        /// растягиваемая на панель. Тёмный полупрозрачный фон нужен ради читаемости — светлые надписи
        /// на светлой карте иначе теряются.
        /// </summary>
        void EnsureStyle()
        {
            if (lineStyle == null)
            {
                lineStyle = new GUIStyle(GUI.skin.label);
                lineStyle.fontSize = 12;
                lineStyle.alignment = TextAnchor.MiddleLeft;
                lineStyle.clipping = TextClipping.Clip;
                lineStyle.wordWrap = false;   // [2026-09-10] длинная строка обрезается, а не переносится на строку соседа
            }

            if (background == null)
            {
                background = new Texture2D(1, 1);
                background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
                background.Apply();
                background.hideFlags = HideFlags.HideAndDontSave;
            }
        }
    }
}
