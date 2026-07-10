using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // Кирпич B10 — Воскрешение павших: поднять союзников из реестра graves как свежие копии (приоритет — высший тир).
    // Партиал MatchManager (реестр graves и RemoveGraveAt приватны; нужен ConfigureSummonedUnit — правило 5).
    // Потребитель (Снегопад-Воскрешение, Фаза 4) зовёт ResurrectFromGraves. Только сервер (правило 6). Ассет не трогаем (правило 1).
    public partial class MatchManager
    {
        /// <summary>Поднять до count павших СВОЕЙ команды в радиусе вокруг center. Высший тир — первым (по флагу).
        /// temporary → поднятые живут raisedLifetime (штатный LifetimeUnit через ConfigureSummonedUnit); иначе постоянны.
        /// obeyCommands → слушают общие приказы. Лидерство — «бесплатно» (компенсация в ConfigureSummonedUnit).
        /// Каждая могила поднимается один раз (снимается из реестра). Возвращает число поднятых. Только сервер.</summary>
        public int ResurrectFromGraves(int team, Vector3 center, float radius, int count,
                                       bool highestTierFirst, bool temporary, float raisedLifetime, bool obeyCommands)
        {
            if (NetworkConnectionHandler.isClient) return 0;   // подъём/спавн/снятие могил — только сервер (правило 6)
            if (count <= 0 || radius <= 0f) return 0;
            if (team != 0 && team != 1) return 0;

            // Кандидаты: могилы своей команды в радиусе (реестр graves приватный — доступен в партиале, правило 5).
            float r2 = radius * radius;
            Vector2 c = new Vector2(center.x, center.z);
            List<GraveRecord> candidates = new List<GraveRecord>();
            for (int i = 0; i < graves.Count; i++)
            {
                GraveRecord rec = graves[i];
                if (rec == null || rec.data == null) continue;
                if (rec.data.team != team) continue;                       // только свои (дефолт §6.4)
                Vector2 gp = new Vector2(rec.data.position.x, rec.data.position.z);
                if ((gp - c).sqrMagnitude > r2) continue;
                candidates.Add(rec);
            }
            if (candidates.Count == 0) return 0;

            // Сортировка: высший тир первым (по флагу); тай (или без тир-приоритета) — ближе к центру.
            candidates.Sort((a, b) =>
            {
                if (highestTierFirst && a.data.tier != b.data.tier) return b.data.tier.CompareTo(a.data.tier);
                float da = (new Vector2(a.data.position.x, a.data.position.z) - c).sqrMagnitude;
                float db = (new Vector2(b.data.position.x, b.data.position.z) - c).sqrMagnitude;
                return da.CompareTo(db);
            });

            int raised = 0;
            for (int i = 0; i < candidates.Count && raised < count; i++)
            {
                GraveRecord rec = candidates[i];
                int owner = rec.data.owner;
                Unit u = Unit.Spawn(rec.data.unitTypeID, rec.data.position, 0f, owner);
                if (u == null)                                             // нет префаба по typeID / точка занята → пропуск + лог
                {
                    Debug.LogWarning($"[Resurrect] Не удалось поднять unitTypeID={rec.data.unitTypeID} (нет префаба/точка занята).");
                    continue;
                }
                // Обвязка как призванного: команды/лидерство/лайфтайм. temporary → raisedLifetime, иначе постоянный (0).
                ConfigureSummonedUnit(u, team, owner, obeyCommands, BottomTableAction.Attack, temporary ? raisedLifetime : 0f);

                // Могила поднята — снять из реестра (клиентам деспавн визуала штатным RPC graves).
                int idx = graves.IndexOf(rec);
                if (idx >= 0) RemoveGraveAt(idx);
                raised++;
            }

            if (raised > 0)
                Debug.Log($"[Resurrect] Команда {team}: поднято {raised} павших (тир-приоритет={highestTierFirst}, врем={temporary}).");
            return raised;
        }
    }
}
