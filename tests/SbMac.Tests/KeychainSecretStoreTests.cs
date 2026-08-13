using SbMac.Core.Connections;

using Xunit;

namespace SbMac.Tests;

/// <summary>
/// Exercises the real macOS keychain rather than a fake.
/// </summary>
/// <remarks>
/// A fake would not have caught the bug these tests exist for: the original implementation
/// shelled out to <c>security add-generic-password -w</c> with the secret on stdin, which
/// prompts on the terminal instead of reading stdin. It wrote an <em>empty</em> password,
/// exited 0, and every connection string silently vanished. Only a real round-trip shows
/// that.
///
/// Each test uses a unique account name and removes it afterwards, so runs leave nothing
/// behind in the developer's keychain.
/// </remarks>
public class KeychainSecretStoreTests
{
    static string UniqueKey() => $"sbmac-test-{Guid.NewGuid():N}";

    [Fact]
    public async Task StoreIsAvailableOnMacOS()
    {
        Assert.Equal(OperatingSystem.IsMacOS(), new KeychainSecretStore().IsAvailable);
    }

    [Fact]
    public async Task SecretsRoundTrip()
    {
        var store = new KeychainSecretStore();
        var key = UniqueKey();

        const string secret =
            "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc123def456==";

        try
        {
            await store.SetSecretAsync(key, secret, TestContext.Current.CancellationToken);

            Assert.Equal(secret, await store.GetSecretAsync(key, TestContext.Current.CancellationToken));
        }
        finally
        {
            await store.DeleteSecretAsync(key, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// The keychain has no upsert primitive — adding over an existing item fails with
    /// errSecDuplicateItem — so writing twice has to update rather than throw or duplicate.
    /// </summary>
    [Fact]
    public async Task WritingTwiceReplacesTheValue()
    {
        var store = new KeychainSecretStore();
        var key = UniqueKey();

        try
        {
            await store.SetSecretAsync(key, "first", TestContext.Current.CancellationToken);
            await store.SetSecretAsync(key, "second", TestContext.Current.CancellationToken);

            Assert.Equal("second", await store.GetSecretAsync(key, TestContext.Current.CancellationToken));
        }
        finally
        {
            await store.DeleteSecretAsync(key, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Connection strings are base64 and can contain non-ASCII in the namespace name.</summary>
    [Fact]
    public async Task NonAsciiSecretsSurvive()
    {
        var store = new KeychainSecretStore();
        var key = UniqueKey();

        const string secret = "sb://ünïcode-ñämespace/;Key=🔑+/==";

        try
        {
            await store.SetSecretAsync(key, secret, TestContext.Current.CancellationToken);

            Assert.Equal(secret, await store.GetSecretAsync(key, TestContext.Current.CancellationToken));
        }
        finally
        {
            await store.DeleteSecretAsync(key, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task MissingSecretReadsAsNull()
    {
        Assert.Null(await new KeychainSecretStore()
            .GetSecretAsync(UniqueKey(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An empty item is what the broken CLI implementation left behind. Reporting it as
    /// absent rather than as an empty connection string means the app asks for the value
    /// again instead of trying to connect with nothing.
    /// </summary>
    [Fact]
    public async Task EmptySecretIsReportedAsAbsent()
    {
        var store = new KeychainSecretStore();
        var key = UniqueKey();

        try
        {
            await store.SetSecretAsync(key, string.Empty, TestContext.Current.CancellationToken);

            Assert.Null(await store.GetSecretAsync(key, TestContext.Current.CancellationToken));
        }
        finally
        {
            await store.DeleteSecretAsync(key, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DeletingRemovesTheSecret()
    {
        var store = new KeychainSecretStore();
        var key = UniqueKey();

        await store.SetSecretAsync(key, "value", TestContext.Current.CancellationToken);
        await store.DeleteSecretAsync(key, TestContext.Current.CancellationToken);

        Assert.Null(await store.GetSecretAsync(key, TestContext.Current.CancellationToken));
    }

    /// <summary>Removing something that was never there is the desired end state, not a failure.</summary>
    [Fact]
    public async Task DeletingAMissingSecretDoesNotThrow()
    {
        await new KeychainSecretStore().DeleteSecretAsync(UniqueKey(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SecretsAreKeyedIndependently()
    {
        var store = new KeychainSecretStore();
        var first = UniqueKey();
        var second = UniqueKey();

        try
        {
            await store.SetSecretAsync(first, "AAA", TestContext.Current.CancellationToken);
            await store.SetSecretAsync(second, "BBB", TestContext.Current.CancellationToken);

            Assert.Equal("AAA", await store.GetSecretAsync(first, TestContext.Current.CancellationToken));
            Assert.Equal("BBB", await store.GetSecretAsync(second, TestContext.Current.CancellationToken));

            await store.DeleteSecretAsync(first, TestContext.Current.CancellationToken);

            // Deleting one must not disturb the other.
            Assert.Null(await store.GetSecretAsync(first, TestContext.Current.CancellationToken));
            Assert.Equal("BBB", await store.GetSecretAsync(second, TestContext.Current.CancellationToken));
        }
        finally
        {
            await store.DeleteSecretAsync(first, TestContext.Current.CancellationToken);
            await store.DeleteSecretAsync(second, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>
/// The whole save/restart/reload cycle against the real keychain — the thing the user
/// actually does.
/// </summary>
public class ConnectionStoreKeychainTests
{
    [Fact]
    public async Task ConnectionStringSurvivesAnAppRestart()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-keychain-test");
        var path = Path.Combine(directory.FullName, "connections.json");

        const string connectionString =
            "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=SECRET==";

        var connection = new NamespaceConnection
        {
            Name = "contoso-prod",
            AuthenticationMode = AuthenticationMode.ConnectionString,
            ConnectionString = connectionString
        };

        try
        {
            await new ConnectionStore(path, new KeychainSecretStore())
                .SaveAsync([connection], TestContext.Current.CancellationToken);

            // The secret must not be in the file.
            var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("SECRET==", json);

            // A fresh store is what the next app launch builds.
            var reloaded = await new ConnectionStore(path, new KeychainSecretStore())
                .LoadAsync(TestContext.Current.CancellationToken);

            var restored = Assert.Single(reloaded);
            Assert.Equal("contoso-prod", restored.Name);
            Assert.Equal(connectionString, restored.ConnectionString);
        }
        finally
        {
            await new ConnectionStore(path, new KeychainSecretStore())
                .DeleteAsync(connection, TestContext.Current.CancellationToken);

            directory.Delete(recursive: true);
        }
    }
}
