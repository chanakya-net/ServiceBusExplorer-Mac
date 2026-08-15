using System.Net;
using System.Text;

using SbMac.App.Services;

using Xunit;

namespace SbMac.Tests;

public sealed class UpdateCheckerTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewerReleaseIsReturnedAndAttemptIsRecordedFirst()
    {
        var events = new List<string>();
        var state = new RecordingStateStore(events);
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"tag_name":"v1.4.0","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"}""")), events);
        var checker = Build(new Version(1, 3, 2), state, handler);

        var update = await checker.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Version(1, 3, 2), update!.InstalledVersion);
        Assert.Equal(new Version(1, 4, 0), update.AvailableVersion);
        Assert.Equal("github.com", update.ReleaseUri.Host);
        Assert.Equal(Now, state.LastCheckUtc);
        Assert.Equal(["state", "request"], events);
        Assert.Contains("SB-Mac", handler.Request!.Headers.UserAgent.ToString());
        Assert.Equal(
            new Uri("https://api.github.com/repos/chanakya-net/ServiceBusExplorer-Mac/releases/latest"),
            handler.Request.RequestUri);
    }

    [Fact]
    public void ProductionRequestTimeoutIsFiveSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(5), UpdateChecker.DefaultRequestTimeout);

    [Theory]
    [InlineData("v1.3.2")]
    [InlineData("v1.2.9")]
    [InlineData("1.4")]
    [InlineData("preview")]
    [InlineData("v+2.0.0")]
    [InlineData("v1. 4.0")]
    [InlineData("v1.4.+0")]
    public async Task EqualOlderOrMalformedReleaseDoesNotNotify(string tag)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/{{tag}}"}""")));

        Assert.Null(await Build(new Version(1, 3, 2), new RecordingStateStore(), handler)
            .CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CheckInside24HoursMakesNoRequest()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("must not request"));
        var previous = Now.AddHours(-23).AddMinutes(-59);
        var state = new RecordingStateStore { LastCheckUtc = previous };

        Assert.Null(await Build(new Version(1, 3, 2), state, handler)
            .CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(handler.Request);
        Assert.Equal(previous, state.LastCheckUtc);
        Assert.Equal(0, state.SetCallCount);
    }

    [Fact]
    public async Task CheckAt24HourBoundaryMakesRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var state = new RecordingStateStore { LastCheckUtc = Now.AddHours(-24) };

        await Build(new Version(1, 3, 2), state, handler)
            .CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(handler.Request);
        Assert.Equal(Now, state.LastCheckUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.0.0")]
    public async Task UnbundledOrUnversionedBuildSkipsEverything(string? version)
    {
        var parsed = version is null ? null : Version.Parse(version);
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("must not request"));
        var state = new RecordingStateStore();
        var timeProvider = new RecordingTimeProvider(Now);

        Assert.Null(await Build(parsed, state, handler, timeProvider: timeProvider)
            .CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(handler.Request);
        Assert.Equal(0, timeProvider.GetUtcNowCallCount);
        Assert.Equal(0, state.GetCallCount);
        Assert.Equal(0, state.SetCallCount);
    }

    [Fact]
    public async Task BundleVersionFailureIsSilent()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("must not request"));
        var checker = new UpdateChecker(
            new HttpClient(handler),
            new ThrowingVersionProvider(),
            new RecordingStateStore(),
            new StubTimeProvider(Now),
            TimeSpan.FromSeconds(1));

        Assert.Null(await checker.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpFailureIsSilentAndStillRecordsAttempt(HttpStatusCode status)
    {
        var events = new List<string>();
        var state = new RecordingStateStore(events);
        var handler = new RecordingHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(status)), events);

        Assert.Null(await Build(new Version(1, 3, 2), state, handler)
            .CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Now, state.LastCheckUtc);
        Assert.Equal(["state", "request"], events);
    }

    [Fact]
    public async Task StateStoreExceptionsDoNotBlockTheNetworkCheck()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"tag_name":"v1.4.0","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"}""")));

        var update = await Build(new Version(1, 3, 2), new ThrowingStateStore(), handler)
            .CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Version(1, 4, 0), update!.AvailableVersion);
    }

    [Fact]
    public async Task ConcurrentChecksIssueOneRequest()
    {
        var handler = new BlockingHandler();
        var checker = new UpdateChecker(
            new HttpClient(handler),
            new StubVersionProvider(new Version(1, 3, 2)),
            new ThrowingStateStore(),
            new StubTimeProvider(Now),
            TimeSpan.FromSeconds(1));

        var first = checker.CheckAsync(TestContext.Current.CancellationToken);
        await handler.WaitForFirstRequestAsync(TestContext.Current.CancellationToken);
        var second = checker.CheckAsync(TestContext.Current.CancellationToken);
        handler.Complete();

        await Task.WhenAll(first, second);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WriteFailureStillAllowsOnlyOneRequestPerChecker()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"tag_name":"v1.4.0","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"}""")));
        var checker = Build(new Version(1, 3, 2), new ThrowingStateStore(), handler);

        await checker.CheckAsync(TestContext.Current.CancellationToken);
        await checker.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TimeoutIsSilent()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });

        Assert.Null(await Build(
                new Version(1, 3, 2), new RecordingStateStore(), handler, TimeSpan.FromMilliseconds(10))
            .CheckAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"tag_name\":\"v1.4.0\"}")]
    [InlineData("{\"tag_name\":\"v1.4.0\",\"html_url\":\"http://github.com/release\"}")]
    [InlineData("{\"tag_name\":\"v1.4.0\",\"html_url\":\"https://example.com/release\"}")]
    public async Task InvalidResponseIsSilent(string json)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(json)));

        Assert.Null(await Build(new Version(1, 3, 2), new RecordingStateStore(), handler)
            .CheckAsync(TestContext.Current.CancellationToken));
    }

    static UpdateChecker Build(
        Version? installed,
        IUpdateCheckStateStore state,
        RecordingHandler handler,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null) =>
        new(
            new HttpClient(handler),
            new StubVersionProvider(installed),
            state,
            timeProvider ?? new StubTimeProvider(Now),
            timeout ?? TimeSpan.FromSeconds(1));

    static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    sealed class StubVersionProvider(Version? version) : IAppVersionProvider
    {
        public Version? GetCurrentVersion() => version;
    }

    sealed class ThrowingVersionProvider : IAppVersionProvider
    {
        public Version? GetCurrentVersion() => throw new IOException("bundle unreadable");
    }

    sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    sealed class RecordingTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return value;
        }
    }

    sealed class RecordingStateStore(List<string>? events = null) : IUpdateCheckStateStore
    {
        public DateTimeOffset? LastCheckUtc { get; set; }
        public int GetCallCount { get; private set; }
        public int SetCallCount { get; private set; }

        public Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(LastCheckUtc);
        }

        public Task SetLastCheckUtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
        {
            SetCallCount++;
            LastCheckUtc = value;
            events?.Add("state");
            return Task.CompletedTask;
        }
    }

    sealed class ThrowingStateStore : IUpdateCheckStateStore
    {
        public Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("read failed");

        public Task SetLastCheckUtcAsync(
            DateTimeOffset value,
            CancellationToken cancellationToken = default) =>
            throw new IOException("write failed");
    }

    sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        List<string>? events = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestCount++;
            events?.Add("request");
            return send(request, cancellationToken);
        }
    }

    sealed class BlockingHandler : HttpMessageHandler
    {
        readonly TaskCompletionSource firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount { get; private set; }

        public Task WaitForFirstRequestAsync(CancellationToken cancellationToken) =>
            firstRequest.Task.WaitAsync(cancellationToken);

        public void Complete() => completion.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            firstRequest.TrySetResult();
            await completion.Task.WaitAsync(cancellationToken);
            return JsonResponse(
                """{"tag_name":"v1.4.0","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"}""");
        }
    }
}
