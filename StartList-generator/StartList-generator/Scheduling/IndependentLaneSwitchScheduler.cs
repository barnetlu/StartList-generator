using StartList_Core.Models;
using StartList_Core.Models.Enums;
using StartList_Core.Scheduling.Config;
using StartList_Core.Scheduling.Interfaces;
using StartList_Core.Scheduling.Report;
using System;
using System.Collections.Generic;
using System.Text;

namespace StartList_Core.Scheduling
{
        /// Scheduler, kde jsou dráhy (lanes) nezávislé:
        /// - každá dráha má přiřazený typ překážky (Crossbar / Barrier150 / Barrier170 / Barrier200)
        /// - každá dráha může změnit překážku max 1× (switch)
        /// - switch se provádí po jedné dráze, pokud to dává smysl (nebo vůbec v Manual režimu)
        /// </summary>
    public sealed class IndependentLaneSwitchScheduler : IScheduler
    {
        private readonly TrackPlan _plan;
        private readonly IReadOnlyList<CategoryKey> _order;
        private readonly SchedulingRules _rules;

        public IndependentLaneSwitchScheduler(TrackPlan plan, IReadOnlyList<CategoryKey> categoryOrder, SchedulingRules rules)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _order = categoryOrder ?? throw new ArgumentNullException(nameof(categoryOrder));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public (IReadOnlyList<Heat> Heats, SchedulingReport Report) GenerateWithReport(IReadOnlyList<Competitor> competitors)
        {
            var report = new SchedulingReport();
            var heats = GenerateInternal(competitors, report);
            ValidateNoDuplicateClubInHeat(heats, report);
            return (heats, report);
        }

        public IReadOnlyList<Heat> Generate(IReadOnlyList<Competitor> competitors) => GenerateWithReport(competitors).Heats;

        // ---------- core ----------

        private IReadOnlyList<Heat> GenerateInternal(IReadOnlyList<Competitor> competitors, SchedulingReport report)
        {
            if (competitors is null) throw new ArgumentNullException(nameof(competitors));
            if (competitors.Count == 0) return Array.Empty<Heat>();
            if (_plan.TotalLanes <= 0) throw new InvalidOperationException("TrackPlan.TotalLanes must be > 0.");

            // ---------------- helpers (lokální, copy/paste) ----------------
            static string NormClub(string? club) => (club ?? string.Empty).Trim();

            static HashSet<string> BuildForbidden(Queue<HashSet<string>> lastHeatsClubs)
            {
                var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var set in lastHeatsClubs) forbidden.UnionWith(set);
                return forbidden;
            }

            static void UpdateCooldown(Queue<HashSet<string>> lastHeatsClubs, IReadOnlyList<Competitor> inHeat, int cooldownWindow)
            {
                if (cooldownWindow <= 0) return;

                var clubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in inHeat)
                {
                    var club = NormClub(c.Club);
                    if (club.Length > 0) clubs.Add(club);
                }

                lastHeatsClubs.Enqueue(clubs);
                while (lastHeatsClubs.Count > cooldownWindow)
                    lastHeatsClubs.Dequeue();
            }

            static List<List<Competitor>> BuildPoolsInOrder(
                IReadOnlyList<Competitor> comps,
                IReadOnlyList<CategoryKey> orderedKeys,
                ObstacleType obstacle)
            {
                var pools = new List<List<Competitor>>();

                foreach (var key in orderedKeys)
                {
                    var list = comps
                        .Where(c => c.Category.ObstacleType == obstacle && c.Category.Key.Equals(key))
                        .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
                        .ToList();

                    if (list.Count > 0) pools.Add(list);
                }

                return pools;
            }

            static int Remaining(List<List<Competitor>> pools) => pools.Sum(p => p.Count);

            static string PoolLabel(
                Dictionary<ObstacleType, List<List<Competitor>>> poolsByObs,
                Dictionary<ObstacleType, int> idxByObs,
                ObstacleType obs)
            {
                if (!poolsByObs.TryGetValue(obs, out var pools)) return $"{obs}: <none>";
                var idx = idxByObs.TryGetValue(obs, out var v) ? v : 0;
                if (idx < 0 || idx >= pools.Count) return $"{obs}: <none>";
                var any = pools[idx].FirstOrDefault();
                return any is null ? $"{obs}: <empty>" : $"{obs}: {any.Category.Code}";
            }

            Competitor? PickFromSinglePool(
                List<Competitor> pool,
                HashSet<string> usedClubs,
                HashSet<string> forbiddenClubs,
                SchedulingReport rep)
            {
                if (pool.Count == 0) return null;

                // counts per club
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in pool)
                {
                    var club = NormClub(c.Club);
                    if (!counts.TryAdd(club, 1)) counts[club]++;
                }

                foreach (var club in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key))
                {
                    if (_rules.MaxOneClubPerHeat && club.Length > 0 && usedClubs.Contains(club))
                    {
                        rep.SkippedBecauseDuplicateInHeat++;
                        continue;
                    }
                    if (club.Length > 0 && forbiddenClubs.Contains(club))
                    {
                        rep.SkippedBecauseCooldown++;
                        continue;
                    }

                    for (int i = 0; i < pool.Count; i++)
                    {
                        var c = pool[i];
                        var cClub = NormClub(c.Club);
                        if (!string.Equals(cClub, club, StringComparison.OrdinalIgnoreCase)) continue;

                        if (_rules.MaxOneClubPerHeat && cClub.Length > 0 && usedClubs.Contains(cClub))
                        {
                            rep.SkippedBecauseDuplicateInHeat++;
                            continue;
                        }
                        if (cClub.Length > 0 && forbiddenClubs.Contains(cClub))
                        {
                            rep.SkippedBecauseCooldown++;
                            continue;
                        }

                        pool.RemoveAt(i);
                        if (_rules.MaxOneClubPerHeat && cClub.Length > 0) usedClubs.Add(cClub);
                        return c;
                    }
                }

                return null;
            }

            Competitor? PickFromPoolsInOrder(
                int heatNo,
                string requestedPoolLabel,
                List<List<Competitor>> pools,
                ref int currentIdx,
                HashSet<string> usedClubs,
                HashSet<string> forbiddenClubs,
                SchedulingReport rep)
            {
                while (currentIdx < pools.Count && pools[currentIdx].Count == 0) currentIdx++;
                if (currentIdx >= pools.Count) return null;

                // 1) zkus current pool
                var picked = PickFromSinglePool(pools[currentIdx], usedClubs, forbiddenClubs, rep);
                if (picked is not null) return picked;

                // 2) fallback do dalších poolů v pořadí
                for (int i = currentIdx + 1; i < pools.Count; i++)
                {
                    if (pools[i].Count == 0) continue;

                    picked = PickFromSinglePool(pools[i], usedClubs, forbiddenClubs, rep);
                    if (picked is not null)
                    {
                        rep.FallbackPickCount++;
                        rep.FallbackPicks.Add(new FallbackPickInfo(
                            heatNo,
                            RequestedPool: requestedPoolLabel,
                            UsedPool: pools[i].FirstOrDefault()?.Category?.Code ?? "<unknown>",
                            CompetitorName: $"{picked.FirstName} {picked.LastName}",
                            Club: picked.Club));

                        return picked;
                    }
                }

                return null;
            }

            // ---------------- 1) Pools by obstacle ----------------
            var poolsByObs = new Dictionary<ObstacleType, List<List<Competitor>>>
            {
                [ObstacleType.Crossbar] = BuildPoolsInOrder(competitors, _order, ObstacleType.Crossbar),
            };

            var p150 = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier150);
            if (p150.Count > 0) poolsByObs[ObstacleType.Barrier150] = p150;

            var p170 = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier170);
            if (p170.Count > 0) poolsByObs[ObstacleType.Barrier170] = p170;

            var p200 = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier200);
            if (p200.Count > 0) poolsByObs[ObstacleType.Barrier200] = p200;

            var idxByObs = poolsByObs.Keys.ToDictionary(k => k, _ => 0);

            // ---------------- 2) Layout: initial + target ----------------
            bool is60Legacy = poolsByObs.ContainsKey(ObstacleType.Barrier150)
                              && !poolsByObs.ContainsKey(ObstacleType.Barrier170)
                              && !poolsByObs.ContainsKey(ObstacleType.Barrier200);

            List<ObstacleType> initialLayout;
            List<ObstacleType> targetLayout;

            if (is60Legacy)
            {
                int initB = Math.Clamp(_plan.InitialBarieraLanes, 0, _plan.TotalLanes);
                int finB = Math.Clamp(_plan.AfterSwitchBarieraLanes, 0, _plan.TotalLanes);

                initialLayout = Enumerable.Repeat(ObstacleType.Crossbar, _plan.TotalLanes - initB)
                    .Concat(Enumerable.Repeat(ObstacleType.Barrier150, initB))
                    .ToList();

                targetLayout = Enumerable.Repeat(ObstacleType.Crossbar, _plan.TotalLanes - finB)
                    .Concat(Enumerable.Repeat(ObstacleType.Barrier150, finB))
                    .ToList();
            }
            else
            {
                int init170 = Math.Clamp(_plan.InitialBariera170Lanes, 0, _plan.TotalLanes);
                int init200 = Math.Clamp(_plan.InitialBariera200Lanes, 0, _plan.TotalLanes - init170);
                int initCross = _plan.TotalLanes - init170 - init200;

                int fin170 = Math.Clamp(_plan.AfterSwitchBariera170Lanes, 0, _plan.TotalLanes);
                int fin200 = Math.Clamp(_plan.AfterSwitchBariera200Lanes, 0, _plan.TotalLanes - fin170);
                int finCross = _plan.TotalLanes - fin170 - fin200;

                // konvence: crossbar zleva, bariéry vpravo
                initialLayout = Enumerable.Repeat(ObstacleType.Crossbar, initCross)
                    .Concat(Enumerable.Repeat(ObstacleType.Barrier170, init170))
                    .Concat(Enumerable.Repeat(ObstacleType.Barrier200, init200))
                    .ToList();

                targetLayout = Enumerable.Repeat(ObstacleType.Crossbar, finCross)
                    .Concat(Enumerable.Repeat(ObstacleType.Barrier170, fin170))
                    .Concat(Enumerable.Repeat(ObstacleType.Barrier200, fin200))
                    .ToList();
            }



            var laneObstacle = initialLayout.ToArray();
            var laneTarget = targetLayout.ToArray();
            var laneSwitched = new bool[_plan.TotalLanes];

            // ---------------- 3) Deadline planning (KONSTANTNÍ!) ----------------
            int initCrossRem = poolsByObs.TryGetValue(ObstacleType.Crossbar, out var cb0) ? Remaining(cb0) : 0;
            int init150Rem = poolsByObs.TryGetValue(ObstacleType.Barrier150, out var b1500) ? Remaining(b1500) : 0;
            int init170Rem = poolsByObs.TryGetValue(ObstacleType.Barrier170, out var b1700) ? Remaining(b1700) : 0;
            int init200Rem = poolsByObs.TryGetValue(ObstacleType.Barrier200, out var b2000) ? Remaining(b2000) : 0;

            var switchPlan = BuildSwitchPlan(initCrossRem, init150Rem, init170Rem, init200Rem, _plan, is60Legacy);

            // ---------------- 4) Cooldown tracking ----------------
            int cooldownWindow = Math.Max(0, (_rules.ClubCooldownHeats >= 0 ? _rules.ClubCooldownHeats : 0) - 1);
            var lastHeatsClubs = new Queue<HashSet<string>>();

            // ---------------- 5) Main loop ----------------
            int heatNo = 1;
            var heats = new List<Heat>();

            int totalRemaining = poolsByObs.Values.Sum(Remaining);

            while (totalRemaining > 0)
            {
                bool cooldownOverridden = false;

            retry_heat:
                var forbiddenClubs = cooldownOverridden
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : BuildForbidden(lastHeatsClubs);

                var usedClubsThisHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ---- Switch decision (per lane) ----
                // Přepínáme každou dráhu kde laneObstacle != laneTarget ve správném heatu.
                // Funguje oběma směry: switchON (crossbar→barrier) i switchOFF (barrier→crossbar).
                if (_plan.SwitchRule != SwitchRuleType.Manual)
                {
                    for (int l = 0; l < _plan.TotalLanes; l++)
                    {
                        if (laneObstacle[l] == laneTarget[l]) continue;

                        int? switchHeat = laneObstacle[l] switch
                        {
                            ObstacleType.Barrier150 => switchPlan.Switch150Heat, // switchOFF: barrier→crossbar
                            ObstacleType.Barrier170 => switchPlan.Switch170Heat,
                            ObstacleType.Barrier200 => switchPlan.Switch200Heat,
                            _ => laneTarget[l] switch                             // switchON: crossbar→barrier
                            {
                                ObstacleType.Barrier150 => switchPlan.Switch150Heat,
                                ObstacleType.Barrier170 => switchPlan.Switch170Heat,
                                ObstacleType.Barrier200 => switchPlan.Switch200Heat,
                                _ => null
                            }
                        };

                        if (heatNo == switchHeat)
                        {
                            laneObstacle[l] = laneTarget[l];
                            laneSwitched[l] = true;
                            report.SwitchAtHeat ??= heatNo;
                        }
                    }
                }

                // ---- Fill lanes (lane-aware) ----
                var lanes = new Competitor?[_plan.TotalLanes];

                for (int lane = 0; lane < _plan.TotalLanes; lane++)
                {
                    var obs = laneObstacle[lane];

                    if (!poolsByObs.TryGetValue(obs, out var pools) || pools.Count == 0)
                    {
                        report.EmptyLaneCount++;
                        report.EmptyLanes.Add(new EmptyLaneInfo(heatNo, lane + 1, $"No pools for {obs}"));
                        lanes[lane] = null;
                        continue;
                    }

                    var requested = PoolLabel(poolsByObs, idxByObs, obs);

                    var idx = idxByObs[obs];

                    var picked = PickFromPoolsInOrder(
                        heatNo,
                        requestedPoolLabel: requested,
                        pools: pools,
                        currentIdx: ref idx,
                        usedClubs: usedClubsThisHeat,
                        forbiddenClubs: forbiddenClubs,
                        rep: report);

                    idxByObs[obs] = idx;

                    if (picked is null)
                    {
                        report.EmptyLaneCount++;
                        report.EmptyLanes.Add(new EmptyLaneInfo(heatNo, lane + 1,
                            cooldownOverridden
                                ? $"No eligible {obs} competitor (even without cooldown)"
                                : $"No eligible {obs} competitor (rules/cooldown)"));
                        lanes[lane] = null;
                        continue;
                    }

                    lanes[lane] = picked;
                    totalRemaining--;
                }

                var laneCompetitors = lanes.Where(x => x is not null).Select(x => x!).ToList();

                // ---- DEADLOCK handling ----
                if (laneCompetitors.Count == 0 && totalRemaining > 0)
                {
                    if (!cooldownOverridden)
                    {
                        cooldownOverridden = true;
                        report.Warnings.Add($"Heat {heatNo}: DEADLOCK -> overriding cooldown for this heat.");
                        goto retry_heat;
                    }

                    report.Errors.Add($"Heat {heatNo}: DEADLOCK even without cooldown. Stopping.");
                    break;
                }

                var placeholder = laneCompetitors.FirstOrDefault()?.Category ?? competitors.First().Category;

                // NOTE: lane-aware Heat ctor
                heats.Add(new Heat(heatNo++, placeholder, lanes));

                UpdateCooldown(lastHeatsClubs, laneCompetitors, cooldownWindow);
            }

            ValidateNoDuplicateClubInHeat(heats, report);
            return heats;
        }

        // ---------- picking logic WITH REPORT (copied from your scheduler) ----------

        private static string PoolLabel(List<List<Competitor>> pools, int idx, string obstacle)
        {
            if (idx < 0 || idx >= pools.Count) return $"{obstacle}: <none>";
            var any = pools[idx].FirstOrDefault();
            return any is null ? $"{obstacle}: <empty>" : $"{obstacle}: {any.Category.Code}";
        }

        private Competitor? PickMostFrequentAllowedFromPool_WithReport(
            int heatNo,
            string usedPoolLabel,
            List<Competitor> pool,
            HashSet<string> usedClubs,
            HashSet<string> forbiddenClubs,
            SchedulingReport report)
        {
            if (pool.Count == 0) return null;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in pool)
            {
                var club = NormalizeClub(c.Club);
                if (!counts.TryAdd(club, 1)) counts[club]++;
            }

            foreach (var club in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key))
            {
                if (_rules.MaxOneClubPerHeat && club.Length > 0 && usedClubs.Contains(club))
                {
                    report.SkippedBecauseDuplicateInHeat++;
                    continue;
                }
                if (club.Length > 0 && forbiddenClubs.Contains(club))
                {
                    report.SkippedBecauseCooldown++;
                    continue;
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    var c = pool[i];
                    var cClub = NormalizeClub(c.Club);
                    if (!string.Equals(cClub, club, StringComparison.OrdinalIgnoreCase)) continue;

                    if (_rules.MaxOneClubPerHeat && cClub.Length > 0 && usedClubs.Contains(cClub))
                    {
                        report.SkippedBecauseDuplicateInHeat++;
                        continue;
                    }
                    if (cClub.Length > 0 && forbiddenClubs.Contains(cClub))
                    {
                        report.SkippedBecauseCooldown++;
                        continue;
                    }

                    pool.RemoveAt(i);
                    if (_rules.MaxOneClubPerHeat && cClub.Length > 0) usedClubs.Add(cClub);
                    return c;
                }
            }

            return null;
        }

         private static string NormalizeClub(string? club) => (club ?? string.Empty).Trim();


        private static void ValidateNoDuplicateClubInHeat(IReadOnlyList<Heat> heats, SchedulingReport report)
        {
            foreach (var h in heats)
            {
                var dup = h.Competitors
                    .Select(c => (c.Club ?? "").Trim())
                    .Where(x => x.Length > 0)
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key}×{g.Count()}")
                    .ToList();

                if (dup.Count > 0)
                    report.Errors.Add($"Heat {h.Number}: duplicate SDH in heat: {string.Join(", ", dup)}");
            }
        }

        private sealed record SwitchPlan(
            int TotalHeats,
            int? Switch150Heat,
            int? Switch170Heat,
            int? Switch200Heat
        );

        private SwitchPlan BuildSwitchPlan(int remCross, int rem150, int rem170, int rem200, TrackPlan plan, bool is60Legacy)
        {
            int L = plan.TotalLanes;

            // Počty bariérových drah před a po přepnutí
            int init150 = is60Legacy ? Math.Clamp(plan.InitialBarieraLanes, 0, L) : 0;
            int fin150  = is60Legacy ? Math.Clamp(plan.AfterSwitchBarieraLanes, 0, L) : 0;

            int init170 = !is60Legacy ? Math.Clamp(plan.InitialBariera170Lanes, 0, L) : 0;
            int fin170  = !is60Legacy ? Math.Clamp(plan.AfterSwitchBariera170Lanes, 0, L) : 0;

            int init200 = !is60Legacy ? Math.Clamp(plan.InitialBariera200Lanes, 0, L - init170) : 0;
            int fin200  = !is60Legacy ? Math.Clamp(plan.AfterSwitchBariera200Lanes, 0, L - fin170) : 0;

            // Počet bariérových drah které jsou aktivní PŘED přepnutím (pro výpočet počtu heatů s bariérou)
            // switchON = přidáváme bariéru (fin > init) → bariéra běží NA KONCI
            // switchOFF = odebíráme bariéru (init > fin) → bariéra běží NA ZAČÁTKU
            bool switchOn150  = fin150 >= init150;
            bool switchOn170  = fin170 >= init170;
            bool switchOn200  = fin200 >= init200;

            int activeLanes150 = switchOn150 ? fin150  : init150;
            int activeLanes170 = switchOn170 ? fin170  : init170;
            int activeLanes200 = switchOn200 ? fin200  : init200;

            int H150 = (activeLanes150 > 0) ? CeilDiv(rem150, activeLanes150) : 0;
            int H170 = (activeLanes170 > 0) ? CeilDiv(rem170, activeLanes170) : 0;
            int H200 = (activeLanes200 > 0) ? CeilDiv(rem200, activeLanes200) : 0;

            // Lower bound
            int T = CeilDiv(remCross + rem150 + rem170 + rem200, L);
            T = Math.Max(T, Math.Max(H150, Math.Max(H170, H200)));
            if (T < 1) T = 1;

            // Najdi nejmenší T s dostatečnou crossbar kapacitou
            while (true)
            {
                int crossCap = 0;

                // switchON:  switch nastane v heatu (T - H + 1), bariéra aktivní OD tohoto heatu
                // switchOFF: switch nastane v heatu (H + 1),     bariéra aktivní DO tohoto heatu (exkluzivně)
                int sw150 = (H150 > 0) ? (switchOn150  ? (T - H150 + 1) : (H150 + 1)) : int.MaxValue;
                int sw170 = (H170 > 0) ? (switchOn170  ? (T - H170 + 1) : (H170 + 1)) : int.MaxValue;
                int sw200 = (H200 > 0) ? (switchOn200  ? (T - H200 + 1) : (H200 + 1)) : int.MaxValue;

                for (int h = 1; h <= T; h++)
                {
                    // Spočítej kolik bariérových drah je aktivních v tomto heatu
                    int barrier = 0;
                    if (switchOn150)  barrier += (h >= sw150) ? fin150  : init150;
                    else              barrier += (h <  sw150) ? init150 : fin150;

                    if (switchOn170)  barrier += (h >= sw170) ? fin170  : init170;
                    else              barrier += (h <  sw170) ? init170 : fin170;

                    if (switchOn200)  barrier += (h >= sw200) ? fin200  : init200;
                    else              barrier += (h <  sw200) ? init200 : fin200;

                    crossCap += Math.Max(0, L - barrier);
                }

                if (crossCap >= remCross)
                {
                    return new SwitchPlan(
                        TotalHeats: T,
                        Switch150Heat: (H150 > 0) ? sw150 : null,
                        Switch170Heat: (H170 > 0) ? sw170 : null,
                        Switch200Heat: (H200 > 0) ? sw200 : null
                    );
                }

                T++;
            }
        }
        private static int CeilDiv(int a, int b) => (b <= 0) ? 0 : (a + b - 1) / b;
    }
}
