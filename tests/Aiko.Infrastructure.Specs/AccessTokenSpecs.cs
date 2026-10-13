using Aiko.Infrastructure.Settings;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The access token is compared for equality, so a token shorter than the floor is not a weaker secret but no
/// secret at all: an empty header is equal to an empty token, and an empty token file was stored and checked
/// as if it were one. The store therefore replaces a token it would have refused to write - and keeps a token
/// it did write, because a store that rotated on every read would lock out the agent it just authenticated
/// (TASK-159).
/// </summary>
public sealed class AccessTokenSpecs
{
    [Fact]
    public async Task An_empty_token_file_is_replaced_by_a_real_token()
    {
        using var root = new Root();
        await File.WriteAllTextAsync(root.TokenPath, string.Empty);

        var token = await new AccessTokenStore(new AikoDataPaths(root.DatabasePath)).GetOrCreateAsync();

        Assert.True(
            token.Length >= AccessTokenStore.MinimumLength,
            $"'{token}' is shorter than the floor of {AccessTokenStore.MinimumLength}.");
        Assert.Equal(token, (await File.ReadAllTextAsync(root.TokenPath)).Trim());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("   ")]
    // Thirty-one characters: one short of the floor, which is the interesting case rather than the obvious one.
    [InlineData("0123456789012345678901234567890")]
    public async Task A_token_under_the_floor_is_replaced(string stored)
    {
        using var root = new Root();
        await File.WriteAllTextAsync(root.TokenPath, stored);

        var token = await new AccessTokenStore(new AikoDataPaths(root.DatabasePath)).GetOrCreateAsync();

        Assert.True(token.Length >= AccessTokenStore.MinimumLength);
        Assert.NotEqual(stored, token);
        Assert.Equal(token, (await File.ReadAllTextAsync(root.TokenPath)).Trim());
    }

    [Fact]
    public async Task A_token_at_the_floor_is_kept_as_it_is()
    {
        using var root = new Root();
        var stored = new string('a', AccessTokenStore.MinimumLength);
        await File.WriteAllTextAsync(root.TokenPath, stored);

        var token = await new AccessTokenStore(new AikoDataPaths(root.DatabasePath)).GetOrCreateAsync();

        Assert.Equal(stored, token);
    }

    /// <summary>A throwaway data directory, so the specs never touch the machine's own token.</summary>
    private sealed class Root : IDisposable
    {
        public Root()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(RootPath, "data"));
        }

        private string RootPath { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Aiko.AccessToken.Specs",
            Guid.NewGuid().ToString("N"));

        public string DatabasePath => System.IO.Path.Combine(RootPath, "data", "aiko.db");

        public string TokenPath => System.IO.Path.Combine(RootPath, "data", "access-token");

        public void Dispose() => TempRoot.Delete(RootPath);
    }
}
