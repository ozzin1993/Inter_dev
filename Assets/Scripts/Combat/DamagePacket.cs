namespace StrategyCore
{
    /// <summary>
    /// Одна запись урона внутри пакета: сколько и какого типа. Первая запись живёт полями самого
    /// пакета (<see cref="DamagePacket.amount"/>, <see cref="DamagePacket.damageType"/>), остальные —
    /// в массиве <see cref="DamagePacket.moreRecords"/>. Так одиночный урон (автоатака, урон в секунду,
    /// аура — горячий путь) не выделяет ничего, а умение с несколькими записями выделяет массив
    /// один раз на каст, не на тик.
    /// </summary>
    public struct DamageRecord
    {
        /// <summary>Величина урона до всех расчётов.</summary>
        public float amount;

        /// <summary>Тип урона — по нему берётся множитель из таблицы «тип брони × тип урона».</summary>
        public DamageType damageType;

        public DamageRecord(float amount, DamageType damageType)
        {
            this.amount = amount;
            this.damageType = damageType;
        }
    }

    /// <summary>
    /// ПОЛНЫЙ пакет урона — описание одного применения к одному юниту (схема «пакет и приёмник»,
    /// §3; шаг 4, решения Artsiom 03.09.2026, `Документы/Дизайн/Развилка_Урон_Полным_Пакетом.md`).
    /// Разбирает его приёмник <see cref="UnitReceiver"/> в своём порядке; отдаёт ему воронка
    /// <c>Unit.GetDamage(in DamagePacket, out float)</c> через очередь (<c>UnitReceiver.Queue.cs</c>).
    ///
    /// Что несёт (§13 схемы): записи урона; состояния автоатаки бьющего (вешаются приёмником ТОЛЬКО
    /// при прямой атаке, после урона, только живому — решение Р7); пробитие брони и шанс промаха
    /// бьющего — их кладёт ОТПРАВИТЕЛЬ через <see cref="Create"/> из реестров <see cref="InterflowCombat"/>,
    /// приёмник по бьющему ничего не ищет; поколение в очереди реакций (ставит очередь, решение Р5);
    /// умение-источник для диагностики очереди (решение Artsiom 05.09.2026, на расчёт не влияет).
    /// Разлёт (сплеш) в пакете НЕ живёт: соседей находит бьющий и шлёт по пакету каждому (Р7).
    ///
    /// Один тип пакета для ВСЕХ источников (решение Р8): одиночный урон — одна запись полями,
    /// умение — записи в массиве. Отдельного «пакета из обёртки» нет.
    ///
    /// Почему struct: пакет рождается на каждом тике урона в секунду (<c>Effector.cs</c>), в аурах
    /// и зонах — горячий путь; значимый тип не выделяет память на каждый удар (руководство Unity,
    /// programming best practices). Данные времени исполнения, в Inspector не живут.
    /// </summary>
    public struct DamagePacket
    {
        // ---------------------------------------------------------------- источник --

        /// <summary>Игрок, наносящий урон. Может быть −1 (нейтрал, безымянный источник).</summary>
        public int attackingPlayer;

        /// <summary>Юнит, наносящий урон. Может быть null (эффектор, зона на земле, отложенный урон, посмертный взрыв).</summary>
        public Unit attackingUnit;

        /// <summary>
        /// Прямая ли это атака: удар, выполненный самим бьющим юнитом (автоатака, снаряд автоатаки,
        /// их разлёт), а не умением, состоянием или реакцией. Влияет на провокацию, промах бьющего,
        /// правила входящего урона «только прямые атаки», состояния атаки и анимацию удара.
        /// </summary>
        public bool directAttack;

        /// <summary>
        /// [Interflow 2026-09-11] ПЕРИОДИЧЕСКИЙ ли источник: тик урона в секунду от состояния, тик ауры
        /// или тик зоны на земле. Ставят только четыре места сборки тиков; у разовых ударов (автоатака,
        /// снаряд, умение, реакция) — false.
        ///
        /// Заведён решением Artsiom 11.09.2026 под показ боя: разовый удар даёт в ленте свою группу строк,
        /// а периодика копится на сервере и уходит одной свёрнутой строкой. По остальным полям пакета
        /// эти источники не отличаются: у тика состояния умения нет вовсе, у ауры и зоны оно есть,
        /// как у обычного каста.
        /// </summary>
        public bool periodic;

        /// <summary>
        /// Умение-источник пакета (ассет <see cref="Ability"/>: умение, пассивка с реакцией, старый кирпич).
        /// null — источник без умения: автоатака и её снаряд, урон в секунду от состояния (состояние —
        /// не умение, решение Artsiom 05.09.2026), отложенный урон без умения. Только диагностика:
        /// предупреждение очереди пакетов называет его по имени ассета (решение Artsiom Р5а 03.09 и 05.09.2026).
        /// Кладёт отправитель через <see cref="Create"/>; на расчёт урона не влияет.
        /// </summary>
        public Ability sourceAbility;

        // ------------------------------------------------------------------- урон --

        /// <summary>Первая запись: величина урона до всех расчётов.</summary>
        public float amount;

        /// <summary>Первая запись: тип урона.</summary>
        public DamageType damageType;

        /// <summary>Остальные записи урона (умение с несколькими записями). null — запись одна.</summary>
        public DamageRecord[] moreRecords;

        // ------------------------------------------------------- от бьющего (§13) --

        /// <summary>Состояния автоатаки бьющего. Приёмник вешает их только при <see cref="directAttack"/>,
        /// после урона и только живому. null — нет.</summary>
        public Effector[] attackEffectors;

        /// <summary>Пробитие брони бьющего, доля 0..1 (реестр <see cref="InterflowCombat.ArmorPierceOf"/>).
        /// Кладёт отправитель. 0 — броня цели учитывается целиком.</summary>
        public float armorPierce;

        /// <summary>Шанс промаха бьющего, 0..1 (ослепление; реестр <see cref="InterflowCombat.MissChanceOf"/>).
        /// Кладёт отправитель. Приёмник складывает его с шансами ухода жертвы в ОДИН бросок
        /// (решение Р3) и учитывает только при прямой атаке.</summary>
        public float missChance;

        // ---------------------------------------------------------------- очередь --

        /// <summary>Поколение в очереди реакций: 0 — исходный пакет, 1 — порождён реакцией на него
        /// и так далее. Ставит очередь (<c>UnitReceiver.Queue.cs</c>), отправитель не трогает.</summary>
        public int generation;

        /// <summary>Число записей урона в пакете (первая полями + массив).</summary>
        public int RecordCount => 1 + (moreRecords != null ? moreRecords.Length : 0);

        /// <summary>Запись по индексу: 0 — поля пакета, дальше — массив.</summary>
        public DamageRecord Record(int index)
        {
            return index == 0 ? new DamageRecord(amount, damageType) : moreRecords[index - 1];
        }

        /// <summary>
        /// Собрать пакет из одной записи урона. Пробитие и промах бьющего берутся из реестров
        /// <see cref="InterflowCombat"/> здесь, у отправителя (§13 схемы), — ни одному месту вызова
        /// не нужно знать о реестрах. Состояния атаки — только при прямой атаке; для остальных
        /// источников параметр не имеет смысла и игнорируется.
        /// Умение-источник (<paramref name="sourceAbility"/>) — параметр ОБЯЗАТЕЛЬНЫЙ: каждое место сборки
        /// называет умение или пишет null явно, молчаливого пропуска нет (решение Artsiom 05.09.2026).
        /// </summary>
        public static DamagePacket Create(float amount, DamageType damageType, int attackingPlayer, Unit attackingUnit,
                                          bool directAttack, Ability sourceAbility, Effector[] attackEffectors = null,
                                          bool periodic = false)
        {
            DamagePacket p;
            p.attackingPlayer = attackingPlayer;
            p.attackingUnit = attackingUnit;
            p.directAttack = directAttack;
            p.periodic = periodic;
            p.sourceAbility = sourceAbility;
            p.amount = amount;
            p.damageType = damageType;
            p.moreRecords = null;
            p.attackEffectors = directAttack ? attackEffectors : null;
            p.armorPierce = InterflowCombat.ArmorPierceOf(attackingUnit);
            p.missChance = directAttack ? InterflowCombat.MissChanceOf(attackingUnit) : 0f;
            p.generation = 0;
            return p;
        }

        /// <summary>
        /// Собрать пакет из нескольких записей (умение с блоком «Урон»). Первая запись уходит в поля,
        /// остальные — в новый массив (выделение один раз на каст). Записей меньше одной — пакет
        /// с нулевым уроном, приёмник его ничем не отметит.
        /// </summary>
        public static DamagePacket Create(DamageRecord[] records, int recordCount, int attackingPlayer, Unit attackingUnit,
                                          bool directAttack, Ability sourceAbility, Effector[] attackEffectors = null,
                                          bool periodic = false)
        {
            DamagePacket p = Create(recordCount > 0 ? records[0].amount : 0f,
                                    recordCount > 0 ? records[0].damageType : null,
                                    attackingPlayer, attackingUnit, directAttack, sourceAbility, attackEffectors, periodic);

            if (recordCount > 1)
            {
                p.moreRecords = new DamageRecord[recordCount - 1];
                for (int i = 1; i < recordCount; i++) p.moreRecords[i - 1] = records[i];
            }

            return p;
        }

        /// <summary>Сумма всех записей до расчётов — то, что бьющий «заявил» (колбэки бьющего и доля лечения вторичных целей).</summary>
        public float TotalAmount
        {
            get
            {
                float sum = amount;
                if (moreRecords != null)
                    for (int i = 0; i < moreRecords.Length; i++) sum += moreRecords[i].amount;
                return sum;
            }
        }
    }
}
