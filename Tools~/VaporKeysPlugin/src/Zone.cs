using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Psi.CSharp;

namespace VaporKeysPlugin
{
    /// <summary>
    /// Declares that this plugin's components require the C# language zone, so the platform only activates them
    /// where C# analysis is available (Rider and ReSharper both satisfy this).
    /// </summary>
    [ZoneMarker]
    public class ZoneMarker : IRequire<ILanguageCSharpZone>
    {
    }
}
