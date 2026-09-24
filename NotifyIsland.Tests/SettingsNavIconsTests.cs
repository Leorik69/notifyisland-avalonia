using System.IO;
using Xunit;

namespace NotifyIsland.Tests;

/// <summary>Smoke: Lucide Settings nav SVGs are vendored (1.11.0).</summary>
public class SettingsNavIconsTests
{
    private static readonly string[] NavIcons =
    [
        "layout-dashboard", "cloud-sun", "move", "palette", "music",
        "type", "sparkles", "volume-2", "shapes"
    ];

    [Fact]
    public void Lucide_settings_nav_svgs_exist()
    {
        var root = FindIconsRoot();
        Assert.True(Directory.Exists(root), $"Lucide folder missing: {root}");
        foreach (var name in NavIcons)
        {
            var path = Path.Combine(root, name + ".svg");
            Assert.True(File.Exists(path), $"Missing Settings nav icon: {path}");
            var xml = File.ReadAllText(path);
            Assert.Contains("<svg", xml, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("currentColor", xml, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindIconsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Assets", "Icons", "Lucide");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "Icons", "Lucide"));
    }
}
