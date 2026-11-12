using Aiko.Infrastructure.Agents;
using Capastrum.Acp;
using Xunit;

namespace Aiko.Infrastructure.Specs;

public class AgentPermissionPolicySpecs
{
    [Theory]
    [InlineData("dotnet build")]
    [InlineData("git status")]
    [InlineData("cat src/file.cs")]
    public void Commands_inside_the_project_are_allowed(string command)
    {
        var result = AgentPermissionPolicy.Decide(
            Path.Combine(Path.GetTempPath(), "aiko-project"),
            command);

        Assert.True(ReferenceEquals(ConfirmationResult.Allowed, result));
    }

    [Theory]
    [InlineData("cat ../secret.txt")]
    [InlineData("rm -rf /")]
    [InlineData("type C:\\Windows\\win.ini")]
    public void Paths_outside_the_project_are_denied(string command)
    {
        var result = AgentPermissionPolicy.Decide(
            Path.Combine(Path.GetTempPath(), "aiko-project"),
            command);

        Assert.True(ReferenceEquals(ConfirmationResult.Denied, result));
    }

    [Fact]
    public void A_request_without_a_command_is_allowed()
    {
        var result = AgentPermissionPolicy.Decide(
            Path.Combine(Path.GetTempPath(), "aiko-project"),
            null);

        Assert.True(ReferenceEquals(ConfirmationResult.Allowed, result));
    }
}
