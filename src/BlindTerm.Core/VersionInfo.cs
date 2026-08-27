using System.Reflection;

namespace BlindTerm.Core;

/// <summary>The version stamped into the executable by MSBuild.</summary>
public static class VersionInfo
{
    public static string Current
        => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
            ?? "0.2.3";

    public static string Display => $"v{Current.TrimStart('v', 'V')}";
}
