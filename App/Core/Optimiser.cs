namespace ModuleOptimiser.Core;

public sealed class LoadoutResult
{
    public List<Module> Modules { get; set; } = new();
    public Dictionary<int, int> Totals { get; set; } = new();
    public long Score { get; set; }
    public int Points { get; set; }
}

// Finds 5-module loadouts that reach the stat minimums. Same approach as ZDPS's
// beam search: build the loadout one module at a time, keeping the best candidates.
public static class Optimiser
{
    public const int NumModules = 5;
    public const int MaxPerType = 5;   // identical modules kept (same as ZDPS)
    public const int BeamWidth = 4000; // candidates kept per step
    public const int StatCap = 20;     // no benefit above 20, so totals are capped here

    private sealed class BeamNode
    {
        public int[] Members = Array.Empty<int>();
        public int[] Totals = Array.Empty<int>();
        public double Progress;
        public long Score;
    }

    private readonly record struct Candidate(int Parent, int Module, double Progress, long Score);

    public static int TierPoints(int total) =>
        total >= 20 ? 11 : total >= 16 ? 6 : total >= 12 ? 3 : total >= 8 ? 1 : 0;

    // progress (optional) is told a fraction from 0 to 1, plus a short message.
    // weights (optional) gives each stat a multiplier. Stats not listed count as 1.
    public static List<LoadoutResult> Find(IReadOnlyList<Module> all, IReadOnlyDictionary<int, int> minimums, int top, Action<double, string> progress = null, IReadOnlyDictionary<int, int> weights = null)
    {
        // Every module can help (minimums are hard limits, but the rest of the build should still be the best),
        // so keep them all, at most MaxPerType of each identical kind.
        var relevant = new List<Module>();
        var copies = new Dictionary<string, int>();
        foreach (var module in all)
        {
            var signature = string.Join(";", module.Stats.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
            copies.TryGetValue(signature, out var count);
            if (count >= MaxPerType)
            {
                continue;
            }

            copies[signature] = count + 1;
            relevant.Add(module);
        }

        if (relevant.Count == 0)
        {
            return new List<LoadoutResult>();
        }

        var statIds = relevant.SelectMany(m => m.Stats.Keys).Distinct().OrderBy(x => x).ToArray();
        int s = statIds.Length;
        var index = new Dictionary<int, int>();
        for (int i = 0; i < s; i++)
        {
            index[statIds[i]] = i;
        }

        // A minimum on a stat no module has can never be met.
        foreach (var kv in minimums)
        {
            if (kv.Value > 0 && !index.ContainsKey(kv.Key))
            {
                return new List<LoadoutResult>();
            }
        }

        var vec = new int[relevant.Count][];
        for (int j = 0; j < relevant.Count; j++)
        {
            vec[j] = new int[s];
            foreach (var kv in relevant[j].Stats)
            {
                vec[j][index[kv.Key]] = kv.Value;
            }
        }

        var weight = new int[s];
        for (int i = 0; i < s; i++)
        {
            weight[i] = weights != null && weights.TryGetValue(statIds[i], out var w) ? w : 1;
        }

        var need = new int[s];
        var required = new bool[s];
        int requiredCount = 0;
        foreach (var kv in minimums)
        {
            if (kv.Value > 0 && index.TryGetValue(kv.Key, out var i))
            {
                need[i] = Math.Min(kv.Value, StatCap);
                required[i] = true;
                requiredCount++;
            }
        }

        // Points for each stat by its breakpoint: 20 = 11, 16 = 6, 12 = 3, 8 = 1.
        // Two 16s (12 points) beat one 20 (11 points). All stats count equally.
        // Raw totals break ties, so higher values still count when points are equal.
        long ScoreOf(int[] totals)
        {
            long points = 0;
            long raw = 0;
            for (int i = 0; i < s; i++)
            {
                int v = Math.Min(totals[i], StatCap);
                points += (long)TierPoints(v) * weight[i];
                raw += (long)v * weight[i];
            }
            return points * 1000 + raw;
        }

        double ProgressOf(int[] totals)
        {
            if (requiredCount == 0)
            {
                return 100;
            }

            double sum = 0;
            for (int i = 0; i < s; i++)
            {
                if (required[i])
                {
                    sum += Math.Min(totals[i], need[i]) * 100.0 / need[i];
                }
            }
            return sum / requiredCount;
        }

        bool MeetsAll(int[] totals)
        {
            for (int i = 0; i < s; i++)
            {
                if (required[i] && Math.Min(totals[i], StatCap) < need[i])
                {
                    return false;
                }
            }
            return true;
        }

        var beam = new List<BeamNode> { new BeamNode { Totals = new int[s] } };
        var scratch = new int[s];
        int depth = Math.Min(NumModules, relevant.Count);

        for (int step = 0; step < depth; step++)
        {
            var candidates = new List<Candidate>();
            for (int p = 0; p < beam.Count; p++)
            {
                if (p % 100 == 0)
                {
                    progress?.Invoke((step + p / (double)beam.Count) / depth,
                        $"Step {step + 1} of {depth}: checked {p:N0} of {beam.Count:N0} partial loadouts");
                }

                var node = beam[p];
                for (int j = 0; j < relevant.Count; j++)
                {
                    if (Array.IndexOf(node.Members, j) >= 0)
                    {
                        continue;
                    }

                    var v = vec[j];
                    for (int i = 0; i < s; i++)
                    {
                        scratch[i] = node.Totals[i] + v[i];
                    }

                    candidates.Add(new Candidate(p, j, ProgressOf(scratch), ScoreOf(scratch)));
                }
            }

            candidates.Sort((a, b) =>
                a.Progress != b.Progress ? b.Progress.CompareTo(a.Progress) : b.Score.CompareTo(a.Score));

            var next = new List<BeamNode>();
            var seen = new HashSet<string>();
            foreach (var c in candidates)
            {
                if (next.Count >= BeamWidth)
                {
                    break;
                }

                var parent = beam[c.Parent];
                var members = parent.Members.Append(c.Module).OrderBy(x => x).ToArray();
                if (!seen.Add(string.Join(",", members)))
                {
                    continue;
                }

                var totals = new int[s];
                for (int i = 0; i < s; i++)
                {
                    totals[i] = parent.Totals[i] + vec[c.Module][i];
                }

                next.Add(new BeamNode { Members = members, Totals = totals, Progress = c.Progress, Score = c.Score });
            }

            beam = next;
            if (beam.Count == 0)
            {
                break;
            }
        }

        progress?.Invoke(1, "Checking results...");

        var results = new List<LoadoutResult>();
        var reported = new HashSet<string>();
        foreach (var node in beam)
        {
            if (node.Members.Length != depth || !MeetsAll(node.Totals))
            {
                continue;
            }

            if (!reported.Add(string.Join(",", node.Members)))
            {
                continue;
            }

            var totals = new Dictionary<int, int>();
            for (int i = 0; i < s; i++)
            {
                if (node.Totals[i] > 0)
                {
                    totals[statIds[i]] = Math.Min(node.Totals[i], StatCap);
                }
            }

            int points = 0;
            for (int i = 0; i < s; i++)
            {
                points += TierPoints(Math.Min(node.Totals[i], StatCap)) * weight[i];
            }

            results.Add(new LoadoutResult
            {
                Modules = node.Members.Select(j => relevant[j]).ToList(),
                Totals = totals,
                Score = node.Score,
                Points = points,
            });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results.Take(top).ToList();
    }
}
