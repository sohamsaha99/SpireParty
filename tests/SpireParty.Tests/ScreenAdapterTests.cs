using SpireParty.GameAdapters;
using Xunit;

namespace SpireParty.Tests;

/// <summary>The M3 screen adapters' self-checks, run against the installed game build (sts2.dll).</summary>
// Reading game IL (PatchProcessor.GetOriginalInstructions) from two tests at once races inside MonoMod
// ("ILGeneratorProxy already loaded"); tests that read IL share this collection so they run one at a time.
[Collection("GameIL")]
public class ScreenAdapterTests
{
    [Fact]
    public void RestSite_FindsTheOneSeatLookup() => Assert.Null(new RestSiteAdapter().SelfCheck());

    [Fact]
    public void Treasure_FindsLayoutsAndHolders() => Assert.Null(new TreasureAdapter().SelfCheck());

    [Fact]
    public void EnemyHealth_FindsTheScalingMethod() => Assert.Null(new EnemyHealthAdapter().SelfCheck());

    [Fact]
    public void CombatLayout_FindsThePlacementMethod() => Assert.Null(new CombatLayoutAdapter().SelfCheck());

    [Fact]
    public void MerchantLayout_FindsTheShopLayout() => Assert.Null(new MerchantLayoutAdapter().SelfCheck());
}
