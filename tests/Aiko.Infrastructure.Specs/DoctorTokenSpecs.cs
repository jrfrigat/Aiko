using Aiko.Application.Contracts;
using Aiko.Infrastructure.Diagnostics;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The doctor reports the token the daemon actually uses. A daemon started with AIKO_TOKEN takes it from there and
/// never writes the token file, so "Access token missing" was a false error for a working installation.
/// </summary>
public sealed class DoctorTokenSpecs
{
    [Fact]
    public void A_token_from_the_environment_is_reported_as_present_without_its_value()
    {
        var paths = new AikoDataPaths(Path.Combine(Path.GetTempPath(), $"aiko-doctor-{Guid.NewGuid():N}", "aiko.db"));

        var finding = WorkshopDoctor.TokenFinding(paths, environmentToken: "secret-value-123");

        Assert.Equal(DiagnosticSeverity.Ok, finding.Severity);
        Assert.Contains("AIKO_TOKEN", finding.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value-123", finding.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Neither_a_file_nor_the_environment_is_still_an_error()
    {
        var paths = new AikoDataPaths(Path.Combine(Path.GetTempPath(), $"aiko-doctor-{Guid.NewGuid():N}", "aiko.db"));

        Assert.Equal(DiagnosticSeverity.Error, WorkshopDoctor.TokenFinding(paths, environmentToken: null).Severity);
        Assert.Equal(DiagnosticSeverity.Error, WorkshopDoctor.TokenFinding(paths, environmentToken: "  ").Severity);
    }
}
