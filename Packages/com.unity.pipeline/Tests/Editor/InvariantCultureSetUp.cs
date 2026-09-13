using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace Unity.Pipeline.Tests.Editor
{
    /// <summary>
    /// Пинит инвариантную культуру на время EditMode-прогона этой сборки. Несколько тестов сравнивают
    /// строковый вывод, форматируемый В САМИХ тест-скриптах (например <c>float.ToString()</c> и
    /// <c>double.ToString("R")</c> внутри run_script/интерпретатора), с эталоном на точке-разделителе.
    /// На локали с десятичной запятой (ru-RU) без пина такие тесты падают, хотя продакшн-код пакета
    /// уже форматирует протокол инвариантно. Это только детерминизм прогона, а не правка ассертов.
    ///
    /// SetUpFixture в этом namespace распространяется на него и все вложенные (в т.ч. .Scripts).
    /// </summary>
    [SetUpFixture]
    public class InvariantCultureSetUp
    {
        CultureInfo m_PrevCulture;
        CultureInfo m_PrevUICulture;

        [OneTimeSetUp]
        public void PinInvariantCulture()
        {
            m_PrevCulture = Thread.CurrentThread.CurrentCulture;
            m_PrevUICulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        [OneTimeTearDown]
        public void RestoreCulture()
        {
            Thread.CurrentThread.CurrentCulture = m_PrevCulture;
            Thread.CurrentThread.CurrentUICulture = m_PrevUICulture;
        }
    }
}
