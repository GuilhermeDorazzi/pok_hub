using PokeHub.Core.Localization;
using Microsoft.Win32;
using PokeHub.Core.Decomp;

namespace PokeHub.Core.Runtimes;

/// <summary>Resultado da checagem de um requisito de manifesto.</summary>
/// <param name="Satisfied">true = presente, false = faltando, null = o hub não sabe verificar (informativo).</param>
public sealed record DependencyStatus(string Id, string Name, bool? Satisfied, string Detail, string? InstallUrl)
{
    public bool IsMissing => Satisfied == false;
}

/// <summary>
/// Verifica requisitos que o hub não instala sozinho (precisam de instalador da Microsoft, com admin):
/// .NET Framework 4.x e Visual C++ Redistributable. Requisitos gerenciados (java*, git) e "unix-shell" são delegados.
/// </summary>
public sealed class DependencyChecker(RuntimeManager runtimes, DecompBuildService builds)
{
    private const string DotNetUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48";
    private const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public DependencyStatus Check(string requirement)
    {
        if (RuntimeManager.IsManaged(requirement))
        {
            var spec = RuntimeManager.Known[requirement];
            var t = runtimes.GetInstalled(requirement);
            return new(requirement, spec.Name, t is not null, t is not null ? Loc.T("core.dep.installed", t.Version) : Loc.T("core.dep.hubInstalls"), null);
        }

        return requirement.ToLowerInvariant() switch
        {
            "dotnet-framework-4.8" => CheckDotNet(requirement, "NET Framework 4.8", 528040),
            "dotnet-framework-4.5" => CheckDotNet(requirement, ".NET Framework 4.5", 378389),
            "vcredist-x64" => CheckVcRedist(requirement),
            "unix-shell" => CheckUnixShell(requirement),
            _ => new(requirement, requirement, null, "", null),
        };
    }

    public IReadOnlyList<DependencyStatus> CheckAll(IEnumerable<string> requirements) => requirements.Select(Check).ToList();

    private DependencyStatus CheckDotNet(string id, string name, int minRelease)
    {
        var release = ReadDotNetRelease();
        if (release is null) return new(id, name, false, Loc.T("core.dep.notFound"), DotNetUrl);
        bool ok = release.Value >= minRelease;
        return new(id, name, ok, ok ? Loc.T("core.dep.present") : Loc.T("core.dep.oldVersion", release), ok ? null : DotNetUrl);
    }

    private DependencyStatus CheckVcRedist(string id)
    {
        bool ok = ReadRegistryDword(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", "Installed") == 1;
        return new(id, Loc.T("core.dep.vcredist"), ok, ok ? Loc.T("core.dep.present") : Loc.T("core.dep.notFound"), ok ? null : VcRedistUrl);
    }

    private DependencyStatus CheckUnixShell(string id)
    {
        var shells = builds.CachedShells;
        if (shells is null) return new(id, Loc.T("core.dep.shellName"), null, Loc.T("core.dep.checking"), null);
        bool ok = shells.Count > 0;
        return new(id, Loc.T("core.dep.shellName"), ok,
            ok ? Loc.T("core.dep.shellFound", string.Join(", ", shells.Select(s => s.Description))) : Loc.T("core.dep.shellMissing"),
            null);
    }

    /// <summary>Valor "Release" de HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full, ou null se não há .NET 4.x.</summary>
    public static int? ReadDotNetRelease() => ReadRegistryDword(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release");

    private static int? ReadRegistryDword(string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) is int v ? v : null;
        }
        catch { return null; }
    }
}
