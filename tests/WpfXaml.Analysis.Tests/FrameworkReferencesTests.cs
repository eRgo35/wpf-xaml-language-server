using WpfXaml.Analysis.Framework;

namespace WpfXaml.Analysis.Tests;

public class FrameworkReferencesTests
{
    [Fact]
    public void NuGetRoot_PrefersNuGetPackagesVariable() =>
        Assert.Equal("/x/pkgs", FrameworkReferences.GetNuGetPackagesRoot(
            v => v switch { "NUGET_PACKAGES" => "/x/pkgs", "DOTNET_CLI_HOME" => "/cli", _ => null }, "/home/u"));

    [Fact]
    public void NuGetRoot_UsesDotnetCliHome() =>
        Assert.Equal(Path.Combine("/cli", ".nuget", "packages"), FrameworkReferences.GetNuGetPackagesRoot(
            v => v == "DOTNET_CLI_HOME" ? "/cli" : null, "/home/u"));

    [Fact]
    public void NuGetRoot_FallsBackToUserProfile() =>
        Assert.Equal(Path.Combine("/home/u", ".nuget", "packages"), FrameworkReferences.GetNuGetPackagesRoot(_ => null, "/home/u"));

    [Fact]
    public void NuGetRoot_EmptyVariablesCountAsUnset() =>
        Assert.Equal(Path.Combine("/home/u", ".nuget", "packages"), FrameworkReferences.GetNuGetPackagesRoot(_ => "", "/home/u"));
}
