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

    [Fact]
    public async Task An_unreadable_record_says_where_it_is()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);
        await store.WriteAsync(Scope, "key-1", "value", CancellationToken.None);
        var record = Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "idempotency")));
        await File.WriteAllTextAsync(record, "{ this is not json");

        // Reading it as "unknown" would run the call a second time; the failure has to name the file.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReadAsync(Scope, "key-1", CancellationToken.None).AsTask());

        Assert.Contains(record, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gate_holds_a_second_call_with_the_same_key_until_the_first_lets_go()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);
        var steps = new List<string>();
        var firstIsHolding = new TaskCompletionSource();
        var firstMayLetGo = new TaskCompletionSource();

        var first = Task.Run(async () =>
        {
            await using var gate = await store.EnterAsync(Scope, "key-1", CancellationToken.None);
            lock (steps)
            {
                steps.Add("first holds");
            }

            firstIsHolding.SetResult();
            await firstMayLetGo.Task;
            lock (steps)
            {
                steps.Add("first lets go");
            }
        });
        await firstIsHolding.Task;

        var second = Task.Run(async () =>
        {
            await using var gate = await store.EnterAsync(Scope, "key-1", CancellationToken.None);
            lock (steps)
            {
                steps.Add("second holds");
            }
        });
        await Task.Delay(100);

        // The second call is waiting on the key, not running beside the first.
        Assert.Equal(new[] { "first holds" }, steps);

        firstMayLetGo.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { "first holds", "first lets go", "second holds" }, steps);
    }

    [Fact]
    public async Task Another_key_does_not_wait_for_the_first()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);
        await using var first = await store.EnterAsync(Scope, "key-1", CancellationToken.None);

        // A different key is a different call: holding one must not hold the other.
        var other = await store.EnterAsync(Scope, "key-2", CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await other.DisposeAsync();
    }

    [Fact]
    public async Task A_key_is_free_again_once_the_call_that_held_it_let_go()
    {
        using var directory = new TemporaryDirectory();
        var store = StoreIn(directory);

        await using (var gate = await store.EnterAsync(Scope, "key-1", CancellationToken.None))
        {
            await store.WriteAsync(Scope, "key-1", "value", CancellationToken.None);
        }

        await using var next = await store.EnterAsync(Scope, "key-1", CancellationToken.None);
        Assert.Equal("value", await store.ReadAsync(Scope, "key-1", CancellationToken.None));
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
