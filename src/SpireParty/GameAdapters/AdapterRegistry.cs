using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireParty.GameAdapters;

/// <summary>Outcome of checking and applying all adapters at startup.</summary>
public sealed class AdapterReport
{
    public List<string> Applied { get; } = new();
    /// <summary>Adapter name → reason it was not applied.</summary>
    public Dictionary<string, string> Failed { get; } = new();
    /// <summary>Feature → the failed adapters it depends on.</summary>
    public Dictionary<string, List<string>> DisabledFeatures { get; } = new();

    public bool IsFeatureEnabled(string feature) => !DisabledFeatures.ContainsKey(feature);

    /// <summary>One readable message for the player, or null if everything works.</summary>
    public string? DisabledMessage()
    {
        if (DisabledFeatures.Count == 0) return null;
        var lines = DisabledFeatures.Select(kv =>
            $"- {kv.Key}: needs {string.Join(", ", kv.Value.Select(a => $"{a} ({Failed[a]})"))}");
        return "SpireParty does not support this game build fully. Disabled:\n" + string.Join("\n", lines) +
               "\nThe normal game is unaffected.";
    }
}

/// <summary>
/// Runs every adapter's self-check, applies only the ones that pass, and works out which
/// features to disable. A feature is disabled if ANY adapter it needs failed, so a feature
/// never runs half-patched. Exceptions are contained: SpireParty must never crash the game.
/// </summary>
public static class AdapterRegistry
{
    public static AdapterReport CheckAndApply(IEnumerable<GameAdapter> adapters, Action<GameAdapter> apply)
    {
        var report = new AdapterReport();
        var list = adapters.ToList();

        var passed = new List<GameAdapter>();
        foreach (var adapter in list)
        {
            string? reason;
            try { reason = adapter.SelfCheck(); }
            catch (Exception e) { reason = $"self-check threw {e.GetType().Name}: {e.Message}"; }

            if (reason == null) passed.Add(adapter);
            else report.Failed[adapter.Name] = reason;
        }

        // Disable features touched by a failed adapter, then skip every adapter that only
        // serves disabled features, so nothing is patched for a feature that cannot work.
        foreach (var adapter in list.Where(a => report.Failed.ContainsKey(a.Name)))
            foreach (var feature in adapter.Features)
            {
                if (!report.DisabledFeatures.TryGetValue(feature, out var deps))
                    report.DisabledFeatures[feature] = deps = new List<string>();
                deps.Add(adapter.Name);
            }

        foreach (var adapter in passed)
        {
            if (adapter.Features.Count > 0 && adapter.Features.All(f => report.DisabledFeatures.ContainsKey(f)))
                continue;
            try
            {
                apply(adapter);
                report.Applied.Add(adapter.Name);
            }
            catch (Exception e)
            {
                report.Failed[adapter.Name] = $"patch failed: {e.GetType().Name}: {e.Message}";
                foreach (var feature in adapter.Features)
                {
                    if (!report.DisabledFeatures.TryGetValue(feature, out var deps))
                        report.DisabledFeatures[feature] = deps = new List<string>();
                    deps.Add(adapter.Name);
                }
            }
        }
        return report;
    }
}
