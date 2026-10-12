using Aiko.Infrastructure.Idempotency;
using Aiko.Infrastructure.Storage;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The idempotency records: a result written under a key comes back under that key, and the key never
/// reaches a path.
/// </summary>
public class IdempotencyStoreSpecs
{
    private const string Scope = "aiko_create_card|01a0aec7";

    [Fact]
    public async Task A_result_written_under_a_key_comes_back_under_it()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);

        await store.WriteAsync(Scope, "key-1", """{"cardId":"TASK-9"}""", CancellationToken.None);

        Assert.Equal(
            """{"cardId":"TASK-9"}""",
            await store.ReadAsync(Scope, "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task A_key_that_was_never_written_is_unknown()
    {
        using var directory = new TemporaryDirectory();

        Assert.Null(await StoreIn(directory).ReadAsync(Scope, "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task The_same_key_in_another_scope_is_another_call()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);

        await store.WriteAsync(Scope, "key-1", "first", CancellationToken.None);

        Assert.Null(await store.ReadAsync("aiko_add_comment|01a0aec7", "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task A_key_never_reaches_a_path()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);
        // A caller's key is data, not a name: a traversal attempt must not write outside the record root.
        const string HostileKey = "../../escape";

        await store.WriteAsync(Scope, HostileKey, "value", CancellationToken.None);

        var name = Path.GetFileName(Assert.Single(Directory.GetFiles(
            Path.Combine(directory.Path, "idempotency"))));
        Assert.Equal(64, name.Length - ".json".Length);
        Assert.True(name[..64].All(char.IsAsciiHexDigit));
        Assert.Equal("value", await store.ReadAsync(Scope, HostileKey, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_write_for_the_same_key_replaces_the_first()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);

        await store.WriteAsync(Scope, "key-1", "first", CancellationToken.None);
        await store.WriteAsync(Scope, "key-1", "second", CancellationToken.None);

        // The store keeps the last record it is given; that a key is not written twice is the caller's
        // guarantee - it recalls before it acts - not this one.
        Assert.Equal("second", await store.ReadAsync(Scope, "key-1", CancellationToken.None));
    }

    private static FileIdempotencyStore StoreIn(TemporaryDirectory directory) =>
        new(new AikoDataPaths(Path.Combine(directory.Path, "aiko.db")));

    /// <summary>A directory of its own, removed when the spec is done with it.</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("aiko-idempotency-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
