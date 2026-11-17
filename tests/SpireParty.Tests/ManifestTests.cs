using System.Text.Json;

namespace SpireParty.Tests;

public class ManifestTests
{
    static JsonElement Manifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SpireParty.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void ManifestVersionMatchesAssemblyVersion()
    {
        // The game compares gameplay mods as "id-version" at join, and our handshake will
        // report the assembly version; the two must never drift apart.
        Assert.Equal(ModEntry.Version, Manifest().GetProperty("version").GetString());
    }

    [Fact]
    public void ManifestDeclaresGameplayModWithDllAndNoDependencies()
    {
        var m = Manifest();
        Assert.Equal("SpireParty", m.GetProperty("id").GetString());
        Assert.True(m.GetProperty("affects_gameplay").GetBoolean());
        Assert.True(m.GetProperty("has_dll").GetBoolean());
        Assert.Equal(0, m.GetProperty("dependencies").GetArrayLength());
    }
}
