using SbMac.Core.Connections;

using Xunit;

namespace SbMac.Tests;

public class ConnectionStringParserTests
{
    const string SampleConnectionString =
        "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=abc123def456==";

    [Fact]
    public void EndpointHostIsExtracted()
    {
        Assert.Equal("contoso.servicebus.windows.net", ConnectionStringParser.TryGetEndpointHost(SampleConnectionString));
    }

    [Fact]
    public void ValuesContainingEqualsAreNotTruncated()
    {
        // SAS keys are base64 and routinely end in '=' padding, so splitting on every
        // '=' rather than the first would silently corrupt the key.
        Assert.Equal("abc123def456==", ConnectionStringParser.TryGetValue(SampleConnectionString, "SharedAccessKey"));
    }

    [Fact]
    public void KeyLookupIsCaseInsensitive()
    {
        Assert.Equal("RootManageSharedAccessKey",
            ConnectionStringParser.TryGetValue(SampleConnectionString, "sharedaccesskeyname"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("Endpoint=sb://contoso.servicebus.windows.net/")]   // no credential
    [InlineData("SharedAccessKey=abc")]                              // no endpoint
    public void InvalidConnectionStringsAreRejected(string? value)
    {
        Assert.False(ConnectionStringParser.LooksValid(value));
    }

    [Fact]
    public void ConnectionStringWithSasSignatureIsAccepted()
    {
        Assert.True(ConnectionStringParser.LooksValid(
            "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessSignature=SharedAccessSignature sr=..."));
    }

    [Fact]
    public void RedactHidesTheKeyButKeepsTheRest()
    {
        var redacted = ConnectionStringParser.Redact(SampleConnectionString);

        Assert.DoesNotContain("abc123def456", redacted);
        Assert.Contains("SharedAccessKey=****", redacted);
        Assert.Contains("Endpoint=sb://contoso.servicebus.windows.net/", redacted);
    }
}

public class NamespaceConnectionTests
{
    [Fact]
    public void ResolvedNamespaceComesFromTheConnectionStringForSasAuth()
    {
        var connection = new NamespaceConnection
        {
            AuthenticationMode = AuthenticationMode.ConnectionString,
            ConnectionString = "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKey=k"
        };

        Assert.Equal("contoso.servicebus.windows.net", connection.ResolvedNamespace);
    }

    [Fact]
    public void ResolvedNamespaceComesFromTheHostForEntraAuth()
    {
        var connection = new NamespaceConnection
        {
            AuthenticationMode = AuthenticationMode.EntraId,
            FullyQualifiedNamespace = "contoso.servicebus.windows.net"
        };

        Assert.Equal("contoso.servicebus.windows.net", connection.ResolvedNamespace);
    }

    [Fact]
    public void CreatingASessionWithoutCredentialsFailsWithAClearMessage()
    {
        var connection = new NamespaceConnection
        {
            AuthenticationMode = AuthenticationMode.ConnectionString,
            ConnectionString = null
        };

        var exception = Assert.Throws<InvalidOperationException>(() => Core.ServiceBusSession.Create(connection));
        Assert.Contains("connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntraSessionWithoutANamespaceFailsWithAClearMessage()
    {
        var connection = new NamespaceConnection
        {
            AuthenticationMode = AuthenticationMode.EntraId,
            FullyQualifiedNamespace = "   "
        };

        var exception = Assert.Throws<InvalidOperationException>(() => Core.ServiceBusSession.Create(connection));
        Assert.Contains("fully qualified namespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class ConnectionStoreTests
{
    /// <summary>
    /// The JSON file must never contain the SAS key — that's the whole reason secrets are
    /// pushed out to a separate store.
    /// </summary>
    [Fact]
    public async Task SecretsAreKeptOutOfTheConnectionsFile()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-test");
        try
        {
            var connectionsFile = Path.Combine(directory.FullName, "connections.json");
            var store = new ConnectionStore(
                connectionsFile,
                new FileSecretStore(Path.Combine(directory.FullName, "secrets.json")));

            var connection = new NamespaceConnection
            {
                Name = "contoso",
                AuthenticationMode = AuthenticationMode.ConnectionString,
                ConnectionString = "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKey=TOPSECRETKEY"
            };

            await store.SaveAsync([connection]);

            var fileContents = await File.ReadAllTextAsync(connectionsFile);
            Assert.DoesNotContain("TOPSECRETKEY", fileContents);
            Assert.Contains("contoso", fileContents);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SavedConnectionsRoundTripIncludingTheirSecrets()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-test");
        try
        {
            var store = new ConnectionStore(
                Path.Combine(directory.FullName, "connections.json"),
                new FileSecretStore(Path.Combine(directory.FullName, "secrets.json")));

            var original = new NamespaceConnection
            {
                Name = "contoso",
                AuthenticationMode = AuthenticationMode.ConnectionString,
                ConnectionString = "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKey=TOPSECRETKEY",
                Transport = ServiceBusTransport.AmqpWebSockets
            };

            await store.SaveAsync([original]);
            var loaded = await store.LoadAsync();

            var restored = Assert.Single(loaded);
            Assert.Equal(original.Id, restored.Id);
            Assert.Equal("contoso", restored.Name);
            Assert.Equal(original.ConnectionString, restored.ConnectionString);
            Assert.Equal(ServiceBusTransport.AmqpWebSockets, restored.Transport);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DeletingAConnectionRemovesItAndItsSecret()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-test");
        try
        {
            var secretsFile = Path.Combine(directory.FullName, "secrets.json");
            var secretStore = new FileSecretStore(secretsFile);
            var store = new ConnectionStore(Path.Combine(directory.FullName, "connections.json"), secretStore);

            var connection = new NamespaceConnection
            {
                Name = "contoso",
                ConnectionString = "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKey=TOPSECRETKEY"
            };

            await store.SaveAsync([connection]);
            await store.DeleteAsync(connection);

            Assert.Empty(await store.LoadAsync());
            Assert.DoesNotContain("TOPSECRETKEY", await File.ReadAllTextAsync(secretsFile));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LoadingWithNoSavedFileReturnsAnEmptyList()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-test");
        try
        {
            var store = new ConnectionStore(
                Path.Combine(directory.FullName, "does-not-exist.json"),
                new FileSecretStore(Path.Combine(directory.FullName, "secrets.json")));

            Assert.Empty(await store.LoadAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
