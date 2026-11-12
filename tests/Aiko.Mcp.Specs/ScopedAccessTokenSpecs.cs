using Aiko.Server.Security;
using Xunit;

namespace Aiko.Mcp.Specs;

public class ScopedAccessTokenSpecs
{
    [Fact]
    public void A_token_opens_only_its_project()
    {
        var store = new ScopedAccessTokenStore();
        var token = store.Issue("project", "DLV-1", TimeSpan.FromMinutes(5));

        Assert.True(store.IsValid(token, "project"));
        Assert.True(store.IsValid(token, "PROJECT"));
        Assert.False(store.IsValid(token, "other"));
        Assert.False(store.IsValid(token, null));
        Assert.False(store.IsValid("not-a-token", "project"));
    }

    [Fact]
    public void Revoked_and_expired_tokens_are_refused()
    {
        var store = new ScopedAccessTokenStore();
        var revoked = store.Issue("project", "DLV-1", TimeSpan.FromMinutes(5));
        store.Revoke(revoked);
        Assert.False(store.IsValid(revoked, "project"));

        var expired = store.Issue("project", "DLV-2", TimeSpan.FromSeconds(-1));
        Assert.False(store.IsValid(expired, "project"));
    }

    [Fact]
    public void Revoking_a_delivery_drops_every_token_it_issued()
    {
        var store = new ScopedAccessTokenStore();
        var first = store.Issue("project", "DLV-1", TimeSpan.FromMinutes(5));
        var second = store.Issue("project", "DLV-1", TimeSpan.FromMinutes(5));

        store.RevokeDelivery("DLV-1");

        Assert.False(store.IsValid(first, "project"));
        Assert.False(store.IsValid(second, "project"));
    }
}
