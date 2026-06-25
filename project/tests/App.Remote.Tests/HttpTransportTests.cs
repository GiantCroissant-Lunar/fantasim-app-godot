using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FantaSim.App.Command;
using FantaSim.App.Command.Clients;
using FantaSim.App.Command.Providers;
using FantaSim.App.Command.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Core;
using Xunit;
using CommandMainThreadDispatcher = FantaSim.App.Command.Providers.ImmediateMainThreadDispatcher;
using RemoteMainThreadDispatcher = FantaSim.App.Remote.InlineMainThreadDispatcher;

namespace FantaSim.App.Remote.Tests;

public sealed class HttpTransportTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task PostCommand_ForwardsRequestAndReturnsResultJson()
    {
        using var fixture = await TransportFixture.StartAsync();
        var request = new CommandRequest(
            "world.refresh",
            "{\"force\":true}",
            "correlation-1",
            "caller",
            "agent");

        var response = await fixture.Http.PostAsync(
            "/command",
            JsonContent(request));
        var result = await ReadJsonAsync<CommandResult>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.Ok);
        Assert.Equal("{\"done\":true}", result.ResultJson);
        Assert.NotNull(fixture.CapturedRequest);
        Assert.Equal("world.refresh", fixture.CapturedRequest.Command);
        Assert.Equal("{\"force\":true}", fixture.CapturedRequest.PayloadJson);
        Assert.Equal("correlation-1", fixture.CapturedRequest.CorrelationId);
        Assert.Equal("http", fixture.CapturedRequest.ActorKind);
        Assert.False(string.IsNullOrWhiteSpace(fixture.CapturedRequest.ActorId));
    }

    [Fact]
    public async Task HealthAndStatus_ReturnClientShapes()
    {
        using var fixture = await TransportFixture.StartAsync();

        var healthResponse = await fixture.Http.GetAsync("/health");
        var health = await ReadJsonAsync<CommandHealth>(healthResponse);

        var statusResponse = await fixture.Http.GetAsync("/status");
        var status = await ReadJsonAsync<CommandStatus>(statusResponse);

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.True(health.Ok);
        Assert.Equal(2, health.Commands);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal("fantasim", status.App);
        Assert.Collection(
            status.Commands,
            c => Assert.Equal("world.orchestrate", c.Id),
            c => Assert.Equal("world.refresh", c.Id));
    }

    [Fact]
    public async Task TokenAuth_RejectsMissingOrWrongBearerAndAllowsCorrectBearer()
    {
        using var fixture = await TransportFixture.StartAsync(token: "secret-token");
        var request = new CommandRequest("world.refresh", "{\"force\":true}");

        var missingResponse = await fixture.Http.PostAsync("/command", JsonContent(request));
        Assert.Equal(HttpStatusCode.Unauthorized, missingResponse.StatusCode);

        using var wrongClient = fixture.CreateClient();
        wrongClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        var wrongResponse = await wrongClient.PostAsync("/command", JsonContent(request));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

        using var correctClient = fixture.CreateClient();
        correctClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "secret-token");
        var okResponse = await correctClient.PostAsync("/command", JsonContent(request));
        var result = await ReadJsonAsync<CommandResult>(okResponse);

        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        Assert.True(result.Ok);
        Assert.Equal("{\"done\":true}", result.ResultJson);
    }

    [Fact]
    public async Task CommandResultFailure_IsSurfacedUnchanged()
    {
        using var fixture = await TransportFixture.StartAsync();

        var response = await fixture.Http.PostAsync(
            "/command",
            JsonContent(new CommandRequest("missing.command")));
        var result = await ReadJsonAsync<CommandResult>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal("unknown-command", result.Error!.Type);
    }

    private static StringContent JsonContent<T>(T value)
        => new(
            JsonSerializer.Serialize(value, JsonOptions),
            Encoding.UTF8,
            "application/json");

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
        Assert.NotNull(value);
        return value;
    }

    private sealed class TransportFixture : IDisposable
    {
        private TransportFixture(
            HttpTransport transport,
            Uri baseUri,
            Service service,
            CapturingService capturingService)
        {
            Transport = transport;
            BaseUri = baseUri;
            Service = service;
            CapturingService = capturingService;
            Http = CreateClient();
        }

        public HttpTransport Transport { get; }

        public Uri BaseUri { get; }

        public Service Service { get; }

        public CapturingService CapturingService { get; }

        public HttpClient Http { get; }

        public CommandRequest? CapturedRequest => CapturingService.LastRequest;

        public static async Task<TransportFixture> StartAsync(string? token = null)
        {
            var port = GetFreeLoopbackPort();
            var bind = $"127.0.0.1:{port}";

            var registry = new ServiceRegistry();
            var service = new Service(
                new CommandMainThreadDispatcher(),
                registry,
                NullLoggerFactory.Instance);

            service.Register(
                new CommandDescriptor("world.refresh", "Refresh world", "test handler", "world"),
                (payloadJson, ct) => Task.FromResult<string?>("{\"done\":true}"));

            var capturingService = new CapturingService(service);
            var client = new InProcessClient(capturingService, NullLoggerFactory.Instance);
            var transport = new HttpTransport(
                new RemoteOptions(true, bind, token),
                client,
                new RemoteMainThreadDispatcher(),
                NullLoggerFactory.Instance);

            await transport.StartAsync(CancellationToken.None);

            return new TransportFixture(
                transport,
                new Uri($"http://{bind}/"),
                service,
                capturingService);
        }

        public HttpClient CreateClient()
            => new() { BaseAddress = BaseUri };

        public void Dispose()
        {
            Http.Dispose();
            Transport.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            Transport.Dispose();
        }
    }

    private sealed class CapturingService : IService, IDisposable
    {
        private readonly Service _inner;

        public CapturingService(Service inner)
        {
            _inner = inner;
        }

        public CommandRequest? LastRequest { get; private set; }

        public IReadOnlyList<CommandDescriptor> Commands => _inner.Commands;

        public void Register(CommandDescriptor descriptor, CommandHandler handler)
            => _inner.Register(descriptor, handler);

        public void Unregister(string commandId)
            => _inner.Unregister(commandId);

        public Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return _inner.ExecuteAsync(request, cancellationToken);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
