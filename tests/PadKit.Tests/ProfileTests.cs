using System.Text.Json;
using PadKit.Flow;

namespace PadKit.Tests;

public class ProfileTests
{
    private static string WriteJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "padkit-profile-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Overlay_LaterWins_UntouchedKeysPreserved_NameJoined()
    {
        var basePath = WriteJson("""{ "A": "base", "B": "base", "C": 1 }""");
        var overlayPath = WriteJson("""{ "B": "local" }""");
        var profile = Profile.Load(new[] { basePath, overlayPath });

        Assert.True(profile.TryGet("A", out var a));
        Assert.Equal("base", a.GetString());
        Assert.True(profile.TryGet("B", out var b));
        Assert.Equal("local", b.GetString());
        Assert.True(profile.TryGet("C", out var c));
        Assert.Equal(1, c.GetInt32());
        Assert.Equal(
            Path.GetFileNameWithoutExtension(basePath) + "+" + Path.GetFileNameWithoutExtension(overlayPath),
            profile.Name);
    }

    [Fact]
    public void SinglePath_Load_Unchanged()
    {
        var path = WriteJson("""{ "A": "x" }""");
        var profile = Profile.Load(path);
        Assert.True(profile.TryGet("A", out var a));
        Assert.Equal("x", a.GetString());
        Assert.Equal(Path.GetFileNameWithoutExtension(path), profile.Name);
    }

    [Fact]
    public void EmptyPaths_Throws()
    {
        Assert.Throws<ArgumentException>(() => Profile.Load(Array.Empty<string>()));
    }
}
