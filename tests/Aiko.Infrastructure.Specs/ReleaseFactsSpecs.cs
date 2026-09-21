using System.Net;
using Aiko.Application.Contracts;
using Aiko.Infrastructure.Release;
using Xunit;

namespace Aiko.Infrastructure.Specs;

/// <summary>
/// The facts a release screen shows: where releases are published, what the last one was, and what commit the
/// tree is on.
/// </summary>
/// <remarks>
/// Both halves are checked without a network and without git: the tree state is read out of a temporary
/// <c>.git</c> mock, and the GitHub probe runs against a stub handler. That is the point of the two
/// implementations - the release screen has to answer on a machine with no git and no internet, and "I do not
/// know" has to be a value rather than an empty line.
/// </remarks>
public sealed class ReleaseFactsSpecs
{
    private const string Sha = "3f1c9a4b8e7d6c5b4a39281706f5e4d3c2b1a090";
    private const string ShortSha = "3f1c9a4";

    [Fact]
    public void The_branch_and_commit_come_from_the_ref_file()
    {
        using var tree = new GitTree();
        tree.WriteHead("ref: refs/heads/main");
        tree.WriteFile("refs/heads/main", Sha);

        var head = new GitRefReader().Read(tree.Root);

        Assert.True(head.Known);
        Assert.Equal("main", head.Branch);
        Assert.Equal(ShortSha, head.Commit);
        Assert.Null(head.Failure);
    }

    [Fact]
    public void A_packed_ref_is_read_when_the_loose_file_is_gone()
    {
        using var tree = new GitTree();
        tree.WriteHead("ref: refs/heads/trunk");
        // What a repack leaves behind: the loose ref is gone and the commit lives in one packed list.
        tree.WriteFile(
            "packed-refs",
            "# pack-refs with: peeled fully-peeled sorted",
            $"{Sha} refs/heads/trunk",
            $"^0000000000000000000000000000000000000000");

        var head = new GitRefReader().Read(tree.Root);

        Assert.True(head.Known);
        Assert.Equal("trunk", head.Branch);
        Assert.Equal(ShortSha, head.Commit);
    }

    [Fact]
    public void A_detached_head_has_a_commit_and_no_branch()
    {
        using var tree = new GitTree();
        tree.WriteHead(Sha);

        var head = new GitRefReader().Read(tree.Root);

        // Detached is a state, not a failure: there is a commit, and a screen shows it without a branch chip.
        Assert.True(head.Known);
        Assert.Null(head.Branch);
        Assert.Equal(ShortSha, head.Commit);
    }

    [Fact]
    public void A_git_file_points_at_the_real_git_directory()
    {
        using var tree = new GitTree();
        // A linked worktree and a submodule keep a file here that names the directory holding HEAD.
        tree.WriteFile("HEAD", "ref: refs/heads/worktree-branch");
        tree.WriteFile("refs/heads/worktree-branch", Sha);
        var pointer = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"), ".git");
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        try
        {
            File.WriteAllText(pointer, $"gitdir: {tree.DotGit}");
            var worktree = Path.GetDirectoryName(pointer)!;

            var head = new GitRefReader().Read(worktree);

            Assert.True(head.Known);
            Assert.Equal("worktree-branch", head.Branch);
            Assert.Equal(ShortSha, head.Commit);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pointer)!, true);
        }
    }

    [Fact]
    public void A_directory_that_is_not_a_repository_says_so()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var head = new GitRefReader().Read(directory);

            // "Unknown" is a value with a reason, not an empty commit a screen would draw as blank.
            Assert.False(head.Known);
            Assert.Null(head.Commit);
            Assert.False(string.IsNullOrWhiteSpace(head.Failure));
            Assert.Contains("not a git repository", head.Failure, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_ref_that_cannot_be_read_is_unknown_rather_than_empty()
    {
        using var tree = new GitTree();
        tree.WriteHead("ref: refs/heads/gone");

        var head = new GitRefReader().Read(tree.Root);

        Assert.False(head.Known);
        Assert.Null(head.Commit);
        Assert.Null(head.Branch);
        Assert.Contains("refs/heads/gone", head.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_half_stated_repository_is_not_an_address()
    {
        // Only one half named is not a repository called "owner/": it is a section that is not configured.
        Assert.Null(new ReleaseSettings("jrfrigat", null).Slug);
        Assert.Null(new ReleaseSettings(null, "Aiko").Slug);
        Assert.Null(new ReleaseSettings("   ", "  ").Slug);
        Assert.Null(ReleaseSettings.SafeDefault.Slug);
        Assert.Equal("jrfrigat/Aiko", new ReleaseSettings(" jrfrigat ", "Aiko").Slug);
    }

    [Fact]
    public async Task A_redirect_names_the_latest_release()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            Headers = { Location = new Uri("https://github.com/jrfrigat/Aiko/releases/tag/v0.3.1") }
        });
        var probe = new GitHubReleaseProbe(new HttpClient(handler));

        var latest = await probe.ReadAsync(new ReleaseSettings("jrfrigat", "Aiko"), CancellationToken.None);

        Assert.True(latest.Known);
        Assert.Equal("v0.3.1", latest.Tag);
        Assert.Equal("https://github.com/jrfrigat/Aiko/releases/tag/v0.3.1", latest.Url);
        Assert.Null(latest.Failure);

        // HEAD on the redirect endpoint and not the API: the unauthenticated API's 60-per-hour budget is
        // shared by everyone behind one public address, which is why the installer reads this address too.
        Assert.Equal(HttpMethod.Head, handler.LastRequest!.Method);
        Assert.Equal(
            "https://github.com/jrfrigat/Aiko/releases/latest",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_404_is_unknown_with_the_status_as_its_reason()
    {
        var probe = new GitHubReleaseProbe(new HttpClient(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var latest = await probe.ReadAsync(new ReleaseSettings("jrfrigat", "Aiko"), CancellationToken.None);

        Assert.False(latest.Known);
        Assert.Null(latest.Tag);
        Assert.Null(latest.Url);
        Assert.False(string.IsNullOrWhiteSpace(latest.Failure));
        Assert.Contains("404", latest.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_network_refusal_is_unknown_and_does_not_escape()
    {
        var probe = new GitHubReleaseProbe(new HttpClient(
            new StubHandler(_ => throw new HttpRequestException("No such host is known."))));

        var latest = await probe.ReadAsync(new ReleaseSettings("jrfrigat", "Aiko"), CancellationToken.None);

        Assert.False(latest.Known);
        Assert.Contains("No such host is known.", latest.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unconfigured_repository_is_unknown_and_asks_nobody()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = new GitHubReleaseProbe(new HttpClient(handler));

        var latest = await probe.ReadAsync(ReleaseSettings.SafeDefault, CancellationToken.None);

        Assert.False(latest.Known);
        Assert.Contains("no release repository", latest.Failure!, StringComparison.Ordinal);
        // No address to probe, so nothing left the machine.
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task A_repository_value_that_is_not_an_address_is_refused_before_it_is_sent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = new GitHubReleaseProbe(new HttpClient(handler));

        // A name pasted from a settings file may carry a query, a fragment or a slash of its own; building an
        // address from it would address something other than the repository it claims.
        var latest = await probe.ReadAsync(
            new ReleaseSettings("jrfrigat", "Aiko?tab=readme"),
            CancellationToken.None);

        Assert.False(latest.Known);
        Assert.Contains("is not an owner/repository address", latest.Failure!, StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
    }

    /// <summary>A handler that answers without a network, and remembers what it was asked.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public void A_ref_that_points_outside_the_git_directory_is_refused()
    {
        using var tree = new GitTree();
        var outside = Path.Combine(Path.GetTempPath(), $"aiko-outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "this file's first line is not a commit");
        try
        {
            // A HEAD naming a rooted path is a ref by shape only; following it would read a file of the
            // repository's choosing and hand its first line back as a commit.
            tree.WriteHead($"ref: {outside}");

            var head = new GitRefReader().Read(tree.Root);

            Assert.False(head.Known);
            Assert.Null(head.Commit);
            Assert.False(string.IsNullOrWhiteSpace(head.Failure));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    /// <summary>A temporary working tree holding a <c>.git</c> directory this spec writes to.</summary>
    private sealed class GitTree : IDisposable
    {
        public GitTree()
        {
            Root = Path.Combine(Path.GetTempPath(), "Aiko.Specs", Guid.NewGuid().ToString("N"));
            DotGit = Path.Combine(Root, ".git");
            Directory.CreateDirectory(DotGit);
        }

        public string Root { get; }

        public string DotGit { get; }

        public void WriteHead(string value) => WriteFile("HEAD", value);

        public void WriteFile(string relativePath, params string[] lines)
        {
            var path = Path.Combine(DotGit, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
