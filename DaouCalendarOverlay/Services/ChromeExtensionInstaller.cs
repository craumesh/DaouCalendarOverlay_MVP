using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DaouCalendarOverlay.Services;

public sealed class ChromeExtensionInstaller
{
    private static readonly (string ResourceName, string FileName)[] Files =
    {
        ("DaouCalendarOverlay.ChromeExtension.manifest.json", "manifest.json"),
        ("DaouCalendarOverlay.ChromeExtension.service-worker-v710.js", "service-worker-v710.js"),
        ("DaouCalendarOverlay.ChromeExtension.icon16.png", "icon16.png"),
        ("DaouCalendarOverlay.ChromeExtension.icon32.png", "icon32.png"),
        ("DaouCalendarOverlay.ChromeExtension.icon48.png", "icon48.png"),
        ("DaouCalendarOverlay.ChromeExtension.icon128.png", "icon128.png")
    };

    public string ExtensionDirectory { get; }

    public ChromeExtensionInstaller()
    {
        ExtensionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaouCalendarOverlay",
            "ChromeExtension");
    }

    public void EnsureExtracted()
    {
        Directory.CreateDirectory(ExtensionDirectory);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var (resourceName, fileName) in Files)
        {
            using var input = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Chrome extension resource not found: {resourceName}");
            var outputPath = Path.Combine(ExtensionDirectory, fileName);
            using var output = File.Create(outputPath);
            input.CopyTo(output);
        }

        foreach (var obsolete in new[]
        {
            "content.js", "main-world.js", "bridge-config.js", "bridge-config.json",
            "service-worker.js", "service-worker-v602.js", "service-worker-v610.js",
            "service-worker-v620.js", "service-worker-v630.js", "service-worker-v700.js",
            "setup.html", "setup.js"
        })
        {
            var obsoletePath = Path.Combine(ExtensionDirectory, obsolete);
            if (File.Exists(obsoletePath))
                File.Delete(obsoletePath);
        }
    }

    public void OpenExtensionDirectory()
    {
        EnsureExtracted();
        Process.Start(new ProcessStartInfo
        {
            FileName = ExtensionDirectory,
            UseShellExecute = true
        });
    }
}
