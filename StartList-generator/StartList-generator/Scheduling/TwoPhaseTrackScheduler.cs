using StartList_Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StartList_Core.Scheduling
{
    public sealed class TwoPhaseTrackScheduler
    {
        private readonly TrackPlan _plan;
        private readonly IReadOnlyList<CategoryKey> _order;
        private readonly SchedulingRules _rules;

        public TwoPhaseTrackScheduler(TrackPlan plan, IReadOnlyList<CategoryKey> categoryOrder, SchedulingRules rules)
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

        private IReadOnlyList<Heat> GenerateInternal(IReadOnlyList<Competitor> competitors, SchedulingReport report)
        {
            if (competitors is null) throw new ArgumentNullException(nameof(competitors));
            if (competitors.Count == 0) return Array.Empty<Heat>();
            if (_plan.TotalLanes <= 0) throw new InvalidOperationException("TrackPlan.TotalLanes must be > 0.");

            // Pools: ordered by category order; each pool is List so we can skip items due to rules
            var brevnoPools = BuildPoolsInOrder(competitors, _order, ObstacleType.Crossbar);
            var barieraPools = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier150); // typicky jen ST_M

            // barrier: flatten in order
            var barieraPool = new List<Competitor>();
            foreach (var p in barieraPools) barieraPool.AddRange(p);

            int brevnoRemaining = brevnoPools.Sum(p => p.Count);
            int barieraRemaining = barieraPool.Count;

            var heats = new List<Heat>();

            // For category order on crossbar side
            int currentBrevnoPoolIdx = 0;

            // Cooldown tracking: store clubs used in last (cooldownHeats-1) heats
            int cooldownWindow = Math.Max(0, (_rules.ClubCooldownHeats >= 0 ? _rules.ClubCooldownHeats : 0) - 1);
            var lastHeatsClubs = new Queue<HashSet<string>>(); // each heat -> set of clubs used

            int heatNo = 1;
            int barieraLanes = _plan.InitialBarieraLanes;

            while (brevnoRemaining > 0 || barieraRemaining > 0)
            {
                // switch decision
                if (barieraLanes != _plan.AfterSwitchBarieraLanes && ShouldSwitch(brevnoRemaining, barieraRemaining))
                {
                    barieraLanes = _plan.AfterSwitchBarieraLanes;
                    report.SwitchAtHeat ??= heatNo; // poprvé
                }

                int totalLanes = _plan.TotalLanes;
                int brevnoLanes = Math.Max(0, totalLanes - barieraLanes);

                var laneCompetitors = new List<Competitor>(totalLanes);

                var forbiddenClubs = BuildForbiddenClubs(lastHeatsClubs);
                var usedClubsThisHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ===== Crossbar picks =====
                for (int lane = 1; lane <= brevnoLanes; lane++)
                {
                    var requestedPoolLabel = PoolLabel(brevnoPools, currentBrevnoPoolIdx, "Crossbar");

                    var picked = PickMostFrequentAllowedFromPools_WithReport(
                        heatNo,
                        requestedPoolLabel,
                        brevnoPools,
                        ref currentBrevnoPoolIdx,
                        usedClubsThisHeat,
                        forbiddenClubs,
                        report);

                    if (picked is null)
                    {
                        report.EmptyLaneCount++;
                        report.EmptyLanes.Add(new EmptyLaneInfo(heatNo, lane, "No eligible Crossbar competitor (rules)"));
                        break; // zbytek crossbar lanes nech prázdný
                    }

                    laneCompetitors.Add(picked);
                    brevnoRemaining--;
                }

                // ===== Barrier picks (last lanes) =====
                for (int i = 0; i < barieraLanes; i++)
                {
                    int laneNo = brevnoLanes + 1 + i;

                    var picked = PickMostFrequentAllowedFromPool_WithReport(
                        heatNo,
                        "Barrier150",
                        barieraPool,
                        usedClubsThisHeat,
                        forbiddenClubs,
                        report);

                    if (picked is null)
                    {
                        report.EmptyLaneCount++;
                        report.EmptyLanes.Add(new EmptyLaneInfo(heatNo, laneNo, "No eligible Barrier competitor (rules)"));
                        break;
                    }

                    laneCompetitors.Add(picked);
                    barieraRemaining--;
                }

                var placeholder = laneCompetitors.FirstOrDefault()?.Category ?? competitors.First().Category;
                heats.Add(new Heat(heatNo++, placeholder, laneCompetitors));

                // cooldown window update (jak máš)
                UpdateCooldownWindow(lastHeatsClubs, laneCompetitors, cooldownWindow);
            }

            return heats;
        }

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

            // counts per club
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in pool)
            {
                var club = NormalizeClub(c.Club);
                if (!counts.TryAdd(club, 1)) counts[club]++;
            }

            foreach (var club in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key))
            {
                // pre-check reasons
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

                    // final guards
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
            // posuň currentIdx na první non-empty
            while (currentIdx < pools.Count && pools[currentIdx].Count == 0) currentIdx++;

            if (currentIdx >= pools.Count) return null;

            // 1) zkus current pool
            var picked = PickMostFrequentAllowedFromPool_WithReport(
                heatNo,
                usedPoolLabel: PoolLabel(pools, currentIdx, "Crossbar"),
                pool: pools[currentIdx],
                usedClubs,
                forbiddenClubs,
                report);

            if (picked is not null) return picked;

            // 2) fallback do dalších poolů (pořadí)
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


        public IReadOnlyList<Heat> Generate(IReadOnlyList<Competitor> competitors) => GenerateWithReport(competitors).Heats;
        //{
        //    if (competitors is null) throw new ArgumentNullException(nameof(competitors));
        //    if (competitors.Count == 0) return Array.Empty<Heat>();
        //    if (_plan.TotalLanes <= 0) throw new InvalidOperationException("TrackPlan.TotalLanes must be > 0.");

        //    // Pools: ordered by category order; each pool is List so we can skip items due to rules
        //    var brevnoPools = BuildPoolsInOrder(competitors, _order, ObstacleType.Crossbar);
        //    var barieraPools = BuildPoolsInOrder(competitors, _order, ObstacleType.Barrier150); // typicky jen ST_M

        //    // barrier: flatten in order
        //    var barieraPool = new List<Competitor>();
        //    foreach (var p in barieraPools) barieraPool.AddRange(p);

        //    int brevnoRemaining = brevnoPools.Sum(p => p.Count);
        //    int barieraRemaining = barieraPool.Count;

        //    int barieraLanes = _plan.InitialBarieraLanes;
        //    int heatNo = 1;
        //    var heats = new List<Heat>();

        //    // For category order on crossbar side
        //    int currentBrevnoPoolIdx = 0;

        //    // Cooldown tracking: store clubs used in last (cooldownHeats-1) heats
        //    int cooldownWindow = Math.Max(0, (_rules.ClubCooldownHeats >= 0 ? _rules.ClubCooldownHeats : 0) - 1);
        //    var lastHeatsClubs = new Queue<HashSet<string>>(); // each heat -> set of clubs used

        //    while (brevnoRemaining > 0 || barieraRemaining > 0)
        //    {
        //        // switch decision (when to start using barrier lane(s))
        //        if (barieraLanes != _plan.AfterSwitchBarieraLanes && ShouldSwitch(brevnoRemaining, barieraRemaining))
        //            barieraLanes = _plan.AfterSwitchBarieraLanes;

        //        int totalLanes = _plan.TotalLanes;
        //        int brevnoLanes = Math.Max(0, totalLanes - barieraLanes);

        //        var laneCompetitors = new List<Competitor>(totalLanes);

        //        // Forbidden due to cooldown (clubs from previous heats)
        //        var forbiddenClubs = BuildForbiddenClubs(lastHeatsClubs);

        //        // Hard rule: max 1 competitor per club per heat
        //        var usedClubsThisHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //        // ===== Fill Crossbar lanes first (Lane1..), so barrier ends up last =====
        //        for (int s = 0; s < brevnoLanes; s++)
        //        {
        //            // move current idx to next non-empty pool
        //            while (currentBrevnoPoolIdx < brevnoPools.Count && brevnoPools[currentBrevnoPoolIdx].Count == 0)
        //                currentBrevnoPoolIdx++;

        //            if (currentBrevnoPoolIdx >= brevnoPools.Count)
        //                break;

        //            // Prefer current pool (to keep category order), but if it can't provide legal candidate, try next pools.
        //            var picked = PickMostFrequentAllowedFromPools(
        //                pools: brevnoPools,
        //                startIdx: currentBrevnoPoolIdx,
        //                usedClubs: usedClubsThisHeat,
        //                forbiddenClubs: forbiddenClubs,
        //                preferStartPoolFirst: true);

        //            if (picked is null)
        //                break; // leave remaining lanes empty rather than breaking rules

        //            laneCompetitors.Add(picked);
        //            brevnoRemaining--;
        //        }

        //        // ===== Fill Barrier lanes last (typically 0 or 1) =====
        //        for (int s = 0; s < barieraLanes; s++)
        //        {
        //            if (barieraPool.Count == 0) break;

        //            var picked = PickMostFrequentAllowedFromPool(
        //                pool: barieraPool,
        //                usedClubs: usedClubsThisHeat,
        //                forbiddenClubs: forbiddenClubs);

        //            if (picked is null)
        //                break; // leave empty rather than breaking rules

        //            laneCompetitors.Add(picked);
        //            barieraRemaining--;
        //        }

        //        // placeholder category (Heat has 1 category, but in mixed heats it's just a placeholder)
        //        var placeholder = laneCompetitors.FirstOrDefault()?.Category ?? competitors.First().Category;

        //        heats.Add(new Heat(heatNo++, placeholder, laneCompetitors));

        //        // update cooldown window with clubs used in this heat
        //        if (cooldownWindow > 0)
        //        {
        //            var clubsInHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        //            foreach (var c in laneCompetitors)
        //            {
        //                var club = NormalizeClub(c.Club);
        //                if (club.Length > 0) clubsInHeat.Add(club);
        //            }

        //            lastHeatsClubs.Enqueue(clubsInHeat);
        //            while (lastHeatsClubs.Count > cooldownWindow)
        //                lastHeatsClubs.Dequeue();
        //        }
        //    }

        //    return heats;
        //}

        private bool ShouldSwitch(int brevnoRemaining, int barieraRemaining)
        {
            if (barieraRemaining <= 0) return false;

            return _plan.SwitchRule switch
            {
                SwitchRuleType.BarieraRemainingEqualsBrevnoRemainingDiv2
                    => barieraRemaining >= (int)Math.Ceiling(brevnoRemaining / 2.0),
                _ => false
            };
        }

        private static HashSet<string> BuildForbiddenClubs(Queue<HashSet<string>> lastHeatsClubs)
        {
            var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in lastHeatsClubs)
                forbidden.UnionWith(set);
            return forbidden;
        }

        // ---- Pool builders (ordered, but as List<Competitor>) ----
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

        // ---- Picking logic: "most frequent club" + rules ----

        private Competitor? PickMostFrequentAllowedFromPools(
            List<List<Competitor>> pools,
            int startIdx,
            HashSet<string> usedClubs,
            HashSet<string> forbiddenClubs,
            bool preferStartPoolFirst)
        {
            // 1) Try start pool first (to keep category order)
            if (preferStartPoolFirst && startIdx < pools.Count && pools[startIdx].Count > 0)
            {
                var picked = PickMostFrequentAllowedFromPool(pools[startIdx], usedClubs, forbiddenClubs);
                if (picked is not null) return picked;
            }

            // 2) If not possible, try next pools in order
            for (int i = startIdx + 1; i < pools.Count; i++)
            {
                if (pools[i].Count == 0) continue;

                var picked = PickMostFrequentAllowedFromPool(pools[i], usedClubs, forbiddenClubs);
                if (picked is not null) return picked;
            }

            // 3) Optionally try earlier pools (rarely needed; keeps system from getting stuck)
            // You can comment this out if you want STRICT category order above everything.
            for (int i = 0; i < startIdx; i++)
            {
                if (pools[i].Count == 0) continue;

                var picked = PickMostFrequentAllowedFromPool(pools[i], usedClubs, forbiddenClubs);
                if (picked is not null) return picked;
            }

            return null;
        }

        /// <summary>
        /// Pick competitor from a single pool:
        /// - choose the club with the highest remaining count in this pool
        /// - pick the first allowed competitor from that club
        /// - respects: maxOneClubPerHeat + cooldown forbidden clubs
        /// </summary>
        private Competitor? PickMostFrequentAllowedFromPool(
            List<Competitor> pool,
            HashSet<string> usedClubs,
            HashSet<string> forbiddenClubs)
        {
            if (pool.Count == 0) return null;

            // Build counts per club (within this pool)
            // NOTE: empty club treated as its own "free" bucket
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in pool)
            {
                var club = NormalizeClub(c.Club);
                if (!counts.TryAdd(club, 1)) counts[club]++;
            }

            // Try clubs by descending count (most frequent first)
            foreach (var club in counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key))
            {
                // rule checks for this club
                if (_rules.MaxOneClubPerHeat && club.Length > 0 && usedClubs.Contains(club))
                    continue;

                if (club.Length > 0 && forbiddenClubs.Contains(club))
                    continue;

                // pick first competitor with that club (pool is already ordered by LastName/FirstName)
                for (int i = 0; i < pool.Count; i++)
                {
                    var c = pool[i];
                    var cClub = NormalizeClub(c.Club);

                    if (!string.Equals(cClub, club, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // final guard (in case of empty club etc.)
                    if (_rules.MaxOneClubPerHeat && cClub.Length > 0 && usedClubs.Contains(cClub))
                        continue;
                    if (cClub.Length > 0 && forbiddenClubs.Contains(cClub))
                        continue;

                    pool.RemoveAt(i);

                    if (_rules.MaxOneClubPerHeat && cClub.Length > 0)
                        usedClubs.Add(cClub);

                    return c;
                }
            }

            return null;
        }

        private static string NormalizeClub(string? club) => (club ?? string.Empty).Trim();

        private static void UpdateCooldownWindow(Queue<HashSet<string>> lastHeatsClubs, IReadOnlyList<Competitor> competitorsInHeat, int cooldownWindow)
        {
            // cooldown vypnutý
            if (cooldownWindow <= 0)
                return;

            // posbírej SDH z aktuálního rozběhu
            var clubsInThisHeat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in competitorsInHeat)
            {
                var club = NormalizeClub(c.Club);
                if (club.Length > 0)
                    clubsInThisHeat.Add(club);
            }

            // přidej aktuální heat do okna
            lastHeatsClubs.Enqueue(clubsInThisHeat);

            // udrž velikost okna
            while (lastHeatsClubs.Count > cooldownWindow)
                lastHeatsClubs.Dequeue();
        }
    }
}
