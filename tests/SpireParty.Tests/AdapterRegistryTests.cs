using HarmonyLib;
using SpireParty.GameAdapters;

namespace SpireParty.Tests;

public class AdapterRegistryTests
{
    sealed class Fake(string name, string? failure, params string[] features) : GameAdapter
    {
        public override string Name => name;
        public override IReadOnlyList<string> Features => features;
        public override string? SelfCheck() => failure == "throw" ? throw new InvalidOperationException("boom") : failure;
        public override void Apply(Harmony harmony) { }
    }

    static AdapterReport Run(params GameAdapter[] adapters) =>
        AdapterRegistry.CheckAndApply(adapters, _ => { });

    [Fact]
    public void AllPassing_AllApplied_NoMessage()
    {
        var r = Run(new Fake("a", null, "direct-connect"), new Fake("b", null, "party-saves"));
        Assert.Equal(new[] { "a", "b" }, r.Applied);
        Assert.Null(r.DisabledMessage());
    }

    [Fact]
    public void FailedAdapter_DisablesItsFeatures_AndSkipsAdaptersOnlyServingThem()
    {
        var r = Run(
            new Fake("menu", "method X not found", "direct-connect"),
            new Fake("lobby", null, "direct-connect"),
            new Fake("save", null, "party-saves"));

        Assert.False(r.IsFeatureEnabled("direct-connect"));
        Assert.True(r.IsFeatureEnabled("party-saves"));
        Assert.DoesNotContain("lobby", r.Applied);   // would patch half a feature
        Assert.Contains("save", r.Applied);
        Assert.Contains("method X not found", r.DisabledMessage());
    }

    [Fact]
    public void AdapterServingAnEnabledFeature_IsStillApplied()
    {
        var r = Run(new Fake("x", "missing", "a"), new Fake("shared", null, "a", "b"));
        Assert.Contains("shared", r.Applied);
        Assert.True(r.IsFeatureEnabled("b"));
    }

    [Fact]
    public void ThrowingSelfCheckOrApply_IsContained()
    {
        var r = AdapterRegistry.CheckAndApply(
            new GameAdapter[] { new Fake("t", "throw", "f1"), new Fake("p", null, "f2") },
            a => { if (a.Name == "p") throw new InvalidOperationException("patch failed"); });

        Assert.Contains("self-check threw", r.Failed["t"]);
        Assert.Contains("patch failed", r.Failed["p"]);
        Assert.False(r.IsFeatureEnabled("f1"));
        Assert.False(r.IsFeatureEnabled("f2"));
    }
}
