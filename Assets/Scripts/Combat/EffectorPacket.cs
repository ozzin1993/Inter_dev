namespace StrategyCore
{
    /// <summary>
    /// Пакет наложения состояния — описание одного накладываемого эффектора, адресованное юниту.
    /// Разбирает его приёмник <see cref="UnitReceiver"/> (схема «пакет и приёмник», §1, §3).
    ///
    /// Шаг 2 схемы: пакет несёт РОВНО те же поля, что сегодня принимает основная
    /// <c>Effector.EffectorAdd</c>, кроме самой цели — целью является юнит, которому принадлежит
    /// приёмник. Ни одного нового поля здесь нет и быть не должно: сопротивления и сокращение
    /// длительности — отдельная механика, её дизайн решается ПОСЛЕ переезда (§12 схемы:
    /// переезд отдельно от смены баланса).
    ///
    /// Ссылка на ассет <see cref="Effector"/> лежит в пакете сознательно: сегодняшняя сигнатура
    /// воронки несёт ссылку, и «один в один» означает ссылку. Переход на ключи вместо ссылок —
    /// отдельный будущий шаг (§7 схемы).
    ///
    /// Почему struct: образцы — <see cref="DamagePacket"/> и <see cref="ControlPacket"/>. Наложение
    /// рождается не только на каст, но и с каждой автоатаки (<c>Unit.Combat.cs:30</c>), в аурах,
    /// зонах и реакциях — то есть в горячем пути боя. Значимый тип не даёт выделения в куче
    /// на каждый удар (руководство Unity, programming best practices).
    /// Это данные времени исполнения, в Inspector они не живут — <c>[SerializeField]</c> не нужен.
    /// </summary>
    public struct EffectorPacket
    {
        /// <summary>Ассет состояния — ЧТО накладывается (то же, что параметр <c>effector</c> у EffectorAdd).</summary>
        public Effector effector;

        /// <summary>Юнит-источник наложения. Может быть null (эффектор от игрока, зона, сейв).</summary>
        public Unit unitOwner;

        /// <summary>Игрок-источник наложения. Считается убийцей, если состояние наносит урон.
        /// Обязан быть валидным слотом: приёмник индексирует им SlotManager.playerTeam при слипании.</summary>
        public int owner;

        /// <summary>Стартовое время наложения, секунды: с него держатель начинает отсчёт. Обычно 0;
        /// отличается от нуля при восстановлении из сохранения.</summary>
        public float currentTime;

        /// <summary>Множитель силы наложения: масштабирует пассивные изменения статов и урон в секунду.
        /// 1 — ровно то, что записано в ассете.</summary>
        public float powerMultiplier;

        /// <summary>Длительность этого наложения, секунды. Значение ≤ 0 — брать из ассета.
        /// У бессрочных эффекторов игнорируется.</summary>
        public float durationOverride;

        /// <summary>Собрать пакет из одной записи наложения. Порядок полей — как у параметров
        /// <c>Effector.EffectorAdd</c> после цели.</summary>
        public EffectorPacket(Effector effector, Unit unitOwner, int owner, float currentTime,
                              float powerMultiplier, float durationOverride)
        {
            this.effector = effector;
            this.unitOwner = unitOwner;
            this.owner = owner;
            this.currentTime = currentTime;
            this.powerMultiplier = powerMultiplier;
            this.durationOverride = durationOverride;
        }
    }
}
