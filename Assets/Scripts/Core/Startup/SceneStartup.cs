using UnityEngine;

namespace StrategyCore
{
    /// <summary>
    /// Явный старт сцены: поднимает службы в объявленном порядке ДО того, как Unity вызовет
    /// Awake остальных объектов сцены.
    /// </summary>
    /// <remarks>
    /// Порядок задаётся списком в инспекторе, сверху вниз. Ранний вызов обеспечен атрибутом
    /// порядка выполнения: у стартовика он минимальный, поэтому его Awake гарантированно
    /// раньше Awake любого другого скрипта сцены (документация Unity, DefaultExecutionOrder).
    /// Служба, которой в списке нет, поднимет себя сама в своём Awake — но уже в порядке,
    /// который Unity не определяет.
    /// </remarks>
    [DefaultExecutionOrder(-10000)]
    public class SceneStartup : MonoBehaviour
    {
        [Tooltip("Службы сцены в порядке подъёма, сверху вниз. Каждая обязана реализовывать IStartupService.")]
        [SerializeField] private MonoBehaviour[] services = new MonoBehaviour[0];

        void Awake()
        {
            if (services == null || services.Length == 0)
            {
                Debug.LogError($"[SceneStartup] Список служб пуст на объекте «{name}»: сцена поднимется в порядке, который Unity не определяет.", this);
                return;
            }

            for (int i = 0; i < services.Length; i++)
            {
                MonoBehaviour service = services[i];

                // Пустая ссылка: либо позиция не заполнена в инспекторе, либо компонент вырезан
                // из сборки — клиентские сборки исключены из серверного билда (ADR-005).
                if (service == null)
                {
                    Debug.LogWarning($"[SceneStartup] Позиция {i} пуста на объекте «{name}» — пропущена.", this);
                    continue;
                }

                IStartupService startup = service as IStartupService;
                if (startup == null)
                {
                    Debug.LogError($"[SceneStartup] «{service.GetType().Name}» (позиция {i}) не реализует IStartupService — пропущен.", service);
                    continue;
                }

                startup.Startup();
            }
        }
    }
}
