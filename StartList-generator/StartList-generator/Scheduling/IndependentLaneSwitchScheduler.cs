using StartList_Core.Models;
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
            static int CeilDiv(int a, int b) => b <= 0 ? int.MaxValue : (a + b - 1) / b;
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

            int finalCross, final150 = 0, final170 = 0, final200 = 0;

            if (is60Legacy)
            {
                final150 = Math.Clamp(_plan.AfterSwitchBarieraLanes, 0, _plan.TotalLanes);
                finalCross = _plan.TotalLanes - final150;
            }
            else
            {
                final170 = Math.Clamp(_plan.AfterSwitchBariera170Lanes, 0, _plan.TotalLanes);
                final200 = Math.Clamp(_plan.AfterSwitchBariera200Lanes, 0, _plan.TotalLanes - final170);
                finalCross = _plan.TotalLanes - final170 - final200;
            }

            int T0 = 0;
            if (finalCross > 0) T0 = Math.Max(T0, CeilDiv(initCrossRem, finalCross));
            if (final150 > 0) T0 = Math.Max(T0, CeilDiv(init150Rem, final150));
            if (final170 > 0) T0 = Math.Max(T0, CeilDiv(init170Rem, final170));
            if (final200 > 0) T0 = Math.Max(T0, CeilDiv(init200Rem, final200));

            int latest1500 = (final150 > 0) ? (T0 - CeilDiv(init150Rem, final150)) : int.MaxValue;
            int latest1700 = (final170 > 0) ? (T0 - CeilDiv(init170Rem, final170)) : int.MaxValue;
            int latest2000 = (final200 > 0) ? (T0 - CeilDiv(init200Rem, final200)) : int.MaxValue;

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
                if (_plan.SwitchRule != SwitchRuleType.Manual)
                {
                    int elapsedHeats = heatNo - 1;

                    for (int l = 0; l < _plan.TotalLanes; l++)
                    {
                        if (laneSwitched[l]) continue;
                        if (laneObstacle[l] == laneTarget[l]) continue;

                        int curRem = poolsByObs.TryGetValue(laneObstacle[l], out var curPools) ? Remaining(curPools) : 0;
                        int tgtRem = poolsByObs.TryGetValue(laneTarget[l], out var tgtPools) ? Remaining(tgtPools) : 0;

                        // (A) nutnost: current prázdný, target má co dělat
                        if (curRem == 0 && tgtRem > 0)
                        {
                            laneObstacle[l] = laneTarget[l];
                            laneSwitched[l] = true;
                            report.SwitchAtHeat ??= heatNo;
                            continue;
                        }

                        // (B) deadline: použij KONSTANTNÍ latest*0 (počítané z initCounts)
                        // "balanced" start = kdy začít, aby bariéra doběhla cca stejně jako crossbar po přepnutí
                        int balanced150 = int.MaxValue;
                        int balanced170 = int.MaxValue;
                        int balanced200 = int.MaxValue;

                        if (finalCross > 0)
                        {
                            // kolik heatů by trvalo doběhnout crossbar, když už jedeme ve finálním layoutu
                            int crossTail = CeilDiv(initCrossRem, finalCross);

                            if (final150 > 0 && init150Rem > 0)
                                balanced150 = Math.Max(0, crossTail - CeilDiv(init150Rem, final150));

                            if (final170 > 0 && init170Rem > 0)
                                balanced170 = Math.Max(0, crossTail - CeilDiv(init170Rem, final170));

                            if (final200 > 0 && init200Rem > 0)
                                balanced200 = Math.Max(0, crossTail - CeilDiv(init200Rem, final200));
                        }

                        // přepni nejpozději na latest*, ale ideálně už na balanced*
                        bool deadline = laneTarget[l] switch
                        {
                            ObstacleType.Barrier150 => init150Rem > 0 && elapsedHeats >= Math.Min(latest1500, balanced150),
                            ObstacleType.Barrier170 => init170Rem > 0 && elapsedHeats >= Math.Min(latest1700, balanced170),
                            ObstacleType.Barrier200 => init200Rem > 0 && elapsedHeats >= Math.Min(latest2000, balanced200),
                            _ => false
                        };

                        if (deadline && tgtRem > 0)
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

        //private IReadOnlyList<Heat> GenerateInternal(IReadOnlyList<Competitor> competitors, SchedulingReport report)
        //{
        //    if (competitors is null) throw new ArgumentNullException(nameof(competitors));
        //    if (report is null) throw new ArgumentNullException(nameof(report));
        //    if (competitors.Count == 0) return Array.Empty<Heat>();
        //    if (_plan.TotalLanes <= 0) throw new InvalidOperationException("TrackPlan.TotalLanes must be > 0.");

        //    // Pools: ordered by category order; each pool is List so we can skip items due to rules
        //    var brevnoPools = BuildPoolsInOrder(competitors, _order, ObstacleType.Crossbar);

        //    // barrier pools (pro 60m je to typicky Barrier150)
        //    var bariera150Pools = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier150);
        //    var bariera170Pools = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier170);
        //    var bariera200Pools = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier200);

        //    int brevnoRemaining = brevnoPools.Sum(p => p.Count);
        //    int bariera150Remaining = bariera150Pools.Sum(p=>p.Count);
        //    int bariera170Remaining = bariera170Pools.Sum(p => p.Count);
        //    int bariera200Remaining = bariera200Pools.Sum(p => p.Count);
        //    var heats = new List<Heat>();

        //    // For category order on crossbar side
        //    int currentBrevnoPoolIdx = 0;

        //    // Cooldown tracking: store clubs used in last (cooldownHeats-1) heats
        //    int cooldownWindow = Math.Max(0, (_rules.ClubCooldownHeats >= 0 ? _rules.ClubCooldownHeats : 0) - 1);
        //    var lastHeatsClubs = new Queue<HashSet<string>>(); // each heat -> set of clubs used

        //    int heatNo = 1;
        //    int bariera150Lanes = _plan.InitialBarieraLanes;
        //    int bariera170Lanes = _plan.InitialBariera170Lanes;
        //    int bariera200Lanes = _plan.InitialBariera200Lanes;
        //    int crossbarLanes = _plan.TotalLanes - bariera150Lanes - bariera170Lanes - bariera200Lanes;

        //    while (brevnoRemaining > 0 || bariera150Remaining > 0 || bariera170Remaining > 0 || bariera200Remaining > 0)
        //    {
        //        // switch decision
        //        if (bariera150Lanes != _plan.AfterSwitchBarieraLanes &&
        //            ShouldSwitch(brevnoRemaining, bariera150Remaining, _plan.AfterSwitchBarieraLanes, crossbarLanes))
        //        {
        //            bariera150Lanes = _plan.AfterSwitchBarieraLanes;
        //            report.SwitchAtHeat ??= heatNo; // poprvé
        //        }
        //        // switch decision
        //        if (bariera170Lanes != _plan.AfterSwitchBariera170Lanes &&
        //            ShouldSwitch(brevnoRemaining, bariera170Remaining, _plan.AfterSwitchBariera170Lanes, crossbarLanes))
        //        {
        //            bariera170Lanes = _plan.AfterSwitchBariera170Lanes;
        //            report.SwitchAtHeat ??= heatNo; // poprvé
        //        }
        //        // switch decision
        //        if (bariera200Lanes != _plan.AfterSwitchBariera200Lanes &&
        //            ShouldSwitch(brevnoRemaining, bariera200Remaining, _plan.AfterSwitchBariera200Lanes, crossbarLanes))
        //        {
        //            bariera200Lanes = _plan.AfterSwitchBarieraLanes;
        //            report.SwitchAtHeat ??= heatNo; // poprvé
        //        }

        //        int totalLanes = _plan.TotalLanes;
        //        int brevnoLanes = Math.Max(0, totalLanes - bariera150Lanes - bariera170Lanes - bariera200Lanes);

        //        bool cooldownOverridden = false;

        //    retry_heat:
        //        var laneCompetitors = new List<Competitor>(totalLanes);

        //        // cooldown override jen pro tento heat (kvůli deadlocku)
        //        var forbiddenClubs = cooldownOverridden
        //            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        //            : BuildForbiddenClubs(lastHeatsClubs);

        //        var usedClubsThisHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //        // ===== Crossbar picks =====
        //        for (int lane = 1; lane <= brevnoLanes; lane++)
        //        {
        //            var requestedPoolLabel = PoolLabel(brevnoPools, currentBrevnoPoolIdx, "Crossbar");

        //            var picked = PickMostFrequentAllowedFromPools_WithReport(
        //                heatNo,
        //                requestedPoolLabel,
        //                brevnoPools,
        //                ref currentBrevnoPoolIdx,
        //                usedClubsThisHeat,
        //                forbiddenClubs,
        //                report);

        //            if (picked is null)
        //            {
        //                report.EmptyLaneCount++;
        //                report.EmptyLanes.Add(new EmptyLaneInfo(
        //                    heatNo,
        //                    lane,
        //                    cooldownOverridden
        //                        ? "No eligible Crossbar competitor (even without cooldown)"
        //                        : "No eligible Crossbar competitor (rules/cooldown)"));

        //                // zbytek crossbar lanes nech prázdný
        //                break;
        //            }

        //            laneCompetitors.Add(picked);
        //            brevnoRemaining--;
        //        }

        //        // ===== Barrier picks (last lanes) =====
        //        for (int i = 0; i < bariera150Lanes; i++)
        //        {
        //            int laneNo = brevnoLanes + 1 + i;
        //            var requestedPoolLabel = PoolLabel(brevnoPools, currentBrevnoPoolIdx, "Barrier150");

        //            var picked = PickMostFrequentAllowedFromPools_WithReport(
        //                heatNo,
        //                "Barrier150",
        //                bariera150Pools,
        //                ref
        //                usedClubsThisHeat,
        //                forbiddenClubs,
        //                report);

        //            if (picked is null)
        //            {
        //                report.EmptyLaneCount++;
        //                report.EmptyLanes.Add(new EmptyLaneInfo(
        //                    heatNo,
        //                    laneNo,
        //                    cooldownOverridden
        //                        ? "No eligible Barrier competitor (even without cooldown)"
        //                        : "No eligible Barrier competitor (rules/cooldown)"));
        //                break;
        //            }

        //            laneCompetitors.Add(picked);
        //            barieraRemaining--;
        //        }

        //        // ===== DEADLOCK handling =====
        //        // Když se nepodařilo vybrat nikoho (laneCompetitors.Count==0),
        //        // je to typicky cooldown-deadlock -> 1× zkus bez cooldownu.
        //        if (laneCompetitors.Count == 0)
        //        {
        //            if (!cooldownOverridden)
        //            {
        //                cooldownOverridden = true;

        //                // pokud Warnings nemáš, dej to do Errors nebo Console.WriteLine
        //                report.Warnings?.Add($"Heat {heatNo}: DEADLOCK -> overriding cooldown for this heat.");

        //                goto retry_heat;
        //            }

        //            report.Errors.Add($"Heat {heatNo}: DEADLOCK even without cooldown. Stopping.");
        //            break;
        //        }

        //        // placeholder category (Heat has 1 category, but in mixed heats it's just a placeholder)
        //        var placeholder = laneCompetitors.FirstOrDefault()?.Category ?? competitors.First().Category;
        //        heats.Add(new Heat(heatNo++, placeholder, laneCompetitors));

        //        // cooldown window update
        //        UpdateCooldownWindow(lastHeatsClubs, laneCompetitors, cooldownWindow);
        //    }

        //    return heats;
        //}

        private bool ShouldSwitch(int brevnoRemaining, int barieraRemaining, int barieraLanes, int totalLanes)
        {
            if (barieraRemaining <= 0) return false;

            return _plan.SwitchRule switch
            {
                SwitchRuleType.BarieraRemainingEqualsBrevnoRemainingDivRemainingLanes
                    => barieraRemaining >= (int)Math.Ceiling(brevnoRemaining / (double)(totalLanes - barieraLanes)),
                _ => false
            };
        }

        // ---------- switching logic ----------

        private void TryAutoSwitchOneLane(
            LaneState[] lanes,
            TargetCounts target,
            List<List<Competitor>> crossbarPools,
            List<Competitor> barrier150Pool,
            List<Competitor> barrier170Pool,
            List<Competitor> barrier200Pool,
            int heatNo,
            SchedulingReport report)
        {
            if (_plan.SwitchRule == SwitchRuleType.Manual)
                return;

            // aktuální počty lanes
            var current = CountLaneObstacles(lanes);

            // cílové deficity
            int need150 = Math.Max(0, target.Barrier150 - current.Barrier150);
            int need170 = Math.Max(0, target.Barrier170 - current.Barrier170);
            int need200 = Math.Max(0, target.Barrier200 - current.Barrier200);

            // pokud už jsme v cílovém layoutu, není co přepínat
            if (need150 == 0 && need170 == 0 && need200 == 0)
                return;

            // remaining
            int remCross = RemainingCount(crossbarPools);
            int rem150 = barrier150Pool.Count;
            int rem170 = barrier170Pool.Count;
            int rem200 = barrier200Pool.Count;

            // Auto heuristika:
            // - preferuj doplnit tu překážku, kde je deficit a zároveň backlog
            // - preferuj 170 před 200 (200 je "dražší"), ale pouze pokud 200 už má deficit/backlog
            // - přepni vždy jen 1 lane za heat (a každá lane max 1×)
            (ObstacleType targetObstacle, int deficit, int remaining, int cost)[] candidates = new[]
            {
            (ObstacleType.Barrier150, need150, rem150, cost: 1),
            (ObstacleType.Barrier170, need170, rem170, cost: 1),
            (ObstacleType.Barrier200, need200, rem200, cost: 2),
        };

            // filtr: deficit>0 a remaining>0 (má smysl)
            var viable = candidates
                .Where(x => x.deficit > 0 && x.remaining > 0)
                .ToList();

            if (viable.Count == 0)
                return;

            // zvol kandidáta: největší "tlak" = remaining/deficit, tie-breaker nižší cost
            var chosen = viable
                .OrderByDescending(x => x.remaining)          // backlog
                .ThenByDescending(x => x.deficit)            // deficit
                .ThenBy(x => x.cost)                         // levnější dřív
                .First();

            // najdi lane, kterou lze přepnout: preferuj Crossbar -> chosenObstacle
            // (nepřepínáme bariéry mezi sebou, aby to bylo predikovatelné)
            var idx = Array.FindIndex(lanes, l => !l.HasSwitched && l.Obstacle == ObstacleType.Crossbar);
            if (idx < 0)
                return;

            // přepni
            lanes[idx] = lanes[idx] with { Obstacle = chosen.targetObstacle, HasSwitched = true };

            report.SwitchAtHeat ??= heatNo;
            report.SwitchEvents ??= new List<string>();
            report.SwitchEvents.Add($"Heat {heatNo}: Lane {idx + 1} switched Crossbar -> {chosen.targetObstacle}");
        }

        // ---------- lane state & counts ----------

        private sealed record LaneState(int LaneNo, ObstacleType Obstacle, bool HasSwitched);

        private sealed record TargetCounts(int Crossbar, int Barrier150, int Barrier170, int Barrier200);

        private static LaneState[] BuildInitialLaneStates(TrackPlan plan)
        {
            int n = plan.TotalLanes;

            // 100m variant if any 170/200 lanes are configured
            bool is100 = plan.InitialBariera170Lanes > 0 || plan.InitialBariera200Lanes > 0 ||
                            plan.AfterSwitchBariera170Lanes > 0 || plan.AfterSwitchBariera200Lanes > 0;

            var lanes = new List<LaneState>(n);

            if (!is100)
            {
                // 60m legacy: Barrier150 count from InitialBarieraLanes
                int b = Clamp(plan.InitialBarieraLanes, 0, n);
                int cross = n - b;

                // lane order: first crossbars, then barriers (keeps your old "barrier at end" feel)
                for (int i = 1; i <= cross; i++) lanes.Add(new LaneState(i, ObstacleType.Crossbar, HasSwitched: false));
                for (int i = cross + 1; i <= n; i++) lanes.Add(new LaneState(i, ObstacleType.Barrier150, HasSwitched: false));
                return lanes.ToArray();
            }
            else
            {
                int b170 = Clamp(plan.InitialBariera170Lanes, 0, n);
                int b200 = Clamp(plan.InitialBariera200Lanes, 0, n - b170);
                int cross = n - b170 - b200;

                for (int i = 1; i <= cross; i++) lanes.Add(new LaneState(i, ObstacleType.Crossbar, false));
                for (int i = cross + 1; i <= cross + b170; i++) lanes.Add(new LaneState(i, ObstacleType.Barrier170, false));
                for (int i = cross + b170 + 1; i <= n; i++) lanes.Add(new LaneState(i, ObstacleType.Barrier200, false));
                return lanes.ToArray();
            }
        }

        private static TargetCounts BuildTargetCounts(TrackPlan plan)
        {
            int n = plan.TotalLanes;

            bool is100 = plan.InitialBariera170Lanes > 0 || plan.InitialBariera200Lanes > 0 ||
                            plan.AfterSwitchBariera170Lanes > 0 || plan.AfterSwitchBariera200Lanes > 0;

            if (!is100)
            {
                int b150 = Clamp(plan.AfterSwitchBarieraLanes, 0, n);
                return new TargetCounts(
                    Crossbar: n - b150,
                    Barrier150: b150,
                    Barrier170: 0,
                    Barrier200: 0
                );
            }
            else
            {
                int b170 = Clamp(plan.AfterSwitchBariera170Lanes, 0, n);
                int b200 = Clamp(plan.AfterSwitchBariera200Lanes, 0, n - b170);
                int cross = n - b170 - b200;

                return new TargetCounts(
                    Crossbar: cross,
                    Barrier150: 0,
                    Barrier170: b170,
                    Barrier200: b200
                );
            }
        }

        private static (int Crossbar, int Barrier150, int Barrier170, int Barrier200) CountLaneObstacles(LaneState[] lanes)
        {
            int c = 0, b150 = 0, b170 = 0, b200 = 0;
            foreach (var l in lanes)
            {
                switch (l.Obstacle)
                {
                    case ObstacleType.Crossbar: c++; break;
                    case ObstacleType.Barrier150: b150++; break;
                    case ObstacleType.Barrier170: b170++; break;
                    case ObstacleType.Barrier200: b200++; break;
                }
            }
            return (c, b150, b170, b200);
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        // ---------- pool builders ----------

        private static List<List<Competitor>> BuildPoolsInOrder(
            IReadOnlyList<Competitor> competitors,
            IReadOnlyList<CategoryKey> orderedKeys,
            ObstacleType obstacle)
        {
            var pools = new List<List<Competitor>>();

            foreach (var key in orderedKeys)
            {
                var list = competitors
                    .Where(c => c.Category.ObstacleType == obstacle && c.Category.Key.Equals(key))
                    .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
                    .ToList();

                if (list.Count > 0)
                    pools.Add(list);
            }

            return pools;
        }

        private static List<Competitor> FlattenPools(
            IReadOnlyList<Competitor> competitors,
            IReadOnlyList<CategoryKey> orderedKeys,
            ObstacleType obstacle)
        {
            var pools = BuildPoolsInOrder(competitors, orderedKeys, obstacle);
            var flat = new List<Competitor>();
            foreach (var p in pools) flat.AddRange(p);
            return flat;
        }

        private static int RemainingCount(List<List<Competitor>> pools) => pools.Sum(p => p.Count);

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

        private Competitor? PickMostFrequentAllowedFromPools_WithReport(
            int heatNo,
            string requestedPoolLabel,
            List<List<Competitor>> pools,
            ref int currentIdx,
            HashSet<string> usedClubs,
            HashSet<string> forbiddenClubs,
            SchedulingReport report)
        {
            while (currentIdx < pools.Count && pools[currentIdx].Count == 0) currentIdx++;
            if (currentIdx >= pools.Count) return null;

            var picked = PickMostFrequentAllowedFromPool_WithReport(
                heatNo,
                usedPoolLabel: PoolLabel(pools, currentIdx, "Crossbar"),
                pool: pools[currentIdx],
                usedClubs,
                forbiddenClubs,
                report);

            if (picked is not null) return picked;

            for (int i = currentIdx + 1; i < pools.Count; i++)
            {
                if (pools[i].Count == 0) continue;

                picked = PickMostFrequentAllowedFromPool_WithReport(
                    heatNo,
                    usedPoolLabel: PoolLabel(pools, i, "Crossbar"),
                    pool: pools[i],
                    usedClubs,
                    forbiddenClubs,
                    report);

                if (picked is not null)
                {
                    report.FallbackPickCount++;
                    report.FallbackPicks.Add(new FallbackPickInfo(
                        heatNo,
                        RequestedPool: requestedPoolLabel,
                        UsedPool: PoolLabel(pools, i, "Crossbar"),
                        CompetitorName: $"{picked.FirstName} {picked.LastName}",
                        Club: picked.Club));

                    return picked;
                }
            }

            return null;
        }

        // ---------- shared helpers ----------

        private static HashSet<string> BuildForbiddenClubs(Queue<HashSet<string>> lastHeatsClubs)
        {
            var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in lastHeatsClubs)
                forbidden.UnionWith(set);
            return forbidden;
        }

        private static string NormalizeClub(string? club) => (club ?? string.Empty).Trim();

        private static void UpdateCooldownWindow(Queue<HashSet<string>> lastHeatsClubs, IReadOnlyList<Competitor> competitorsInHeat, int cooldownWindow)
        {
            if (cooldownWindow <= 0) return;

            var clubsInThisHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in competitorsInHeat)
            {
                var club = NormalizeClub(c.Club);
                if (club.Length > 0)
                    clubsInThisHeat.Add(club);
            }

            lastHeatsClubs.Enqueue(clubsInThisHeat);
            while (lastHeatsClubs.Count > cooldownWindow)
                lastHeatsClubs.Dequeue();
        }

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
    }
}
