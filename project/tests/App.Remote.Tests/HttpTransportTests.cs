using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FantaSim.App.Command;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

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
        fixture.Service.NextResult = new CommandResult("result-1", true, "{\"done\":true}");

        var response = await fixture.Http.PostAsync(
            "/command",
            JsonContent(request));
        var result = await ReadJsonAsync<CommandResult>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fixture.Service.NextResult, result);
        Assert.NotNull(fixture.Service.LastRequest);
        Assert.Equal("world.refresh", fixture.Service.LastRequest.Command);
        Assert.Equal("{\"force\":true}", fixture.Service.LastRequest.PayloadJson);
        Assert.Equal("correlation-1", fixture.Service.LastRequest.CorrelationId);
        Assert.Equal("http", fixture.Service.LastRequest.ActorKind);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Service.LastRequest.ActorId));
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
        Assert.Equal("fantasim-test", status.App);
        Assert.Collection(
            status.Commands,
            c => Assert.Equal("world.refresh", c.Id),
            c => Assert.Equal("world.orchestrate", c.Id));
    }

    [Fact]
    public async Task TokenAuth_RejectsMissingOrWrongBearerAndAllowsCorrectBearer()
    {
        using var fixture = await TransportFixture.StartAsync(token: "secret-token");
        var request = new CommandRequest("world.refresh");

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
    }

    [Fact]
    public async Task CommandResultFailure_IsSurfacedUnchanged()
    {
        using var fixture = await TransportFixture.StartAsync();
        fixture.Service.NextResult = new CommandResult(
            "missing-1",
            false,
            Error: new CommandError("unknown-command", "Unknown command: missing.command"));

        var response = await fixture.Http.PostAsync(
            "/command",
            JsonContent(new CommandRequest("missing.command")));
        var result = await ReadJsonAsync<CommandResult>(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fixture.Service.NextResult, result);
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
        private TransportFixture(HttpTransport transport, Uri baseUri, FakeCommandService service)
        {
            Transport = transport;
            BaseUri = baseUri;
            Service = service;
            Http = CreateClient();
        }

        public HttpTransport Transport { get; }

        public Uri BaseUri { get; }

        public FakeCommandService Service { get; }

        public HttpClient Http { get; }

        public static async Task<TransportFixture> StartAsync(string? token = null)
        {
            var port = GetFreeLoopbackPort();
            var bind = $"127.0.0.1:{port}";
            var service = new FakeCommandService();
            var client = new FakeCommandClient(service);
            var transport = new HttpTransport(
                new RemoteOptions(true, bind, token),
                client,
                new InlineMainThreadDispatcher(),
                NullLoggerFactory.Instance);

            await transport.StartAsync(CancellationToken.None);
            return new TransportFixture(transport, new Uri($"http://{bind}/"), service);
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

    private sealed class FakeCommandService : IService
    {
        private readonly List<CommandDescriptor> _commands =
        [
            new CommandDescriptor("world.refresh", "Refresh world", "Refreshes world state.", "world"),
            new CommandDescriptor("world.orchestrate", "Orchestrate world", "Runs world orchestration.", "world"),
        ];

        public CommandRequest? LastRequest { get; private set; }

        public CommandResult NextResult { get; set; } = new("ok-1", true, "{\"ok\":true}");

        public IReadOnlyList<CommandDescriptor> Commands => _commands;

        public void Register(CommandDescriptor descriptor, CommandHandler handler)
        {
            _commands.Add(descriptor);
        }

        public void Unregister(string commandId)
        {
            _commands.RemoveAll(command => string.Equals(command.Id, commandId, StringComparison.Ordinal));
        }

        public Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeCommandClient : IClient
    {
        private readonly FakeCommandService _service;

        public FakeCommandClient(FakeCommandService service)
        {
            _service = service;
        }

        public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandHealth(true, _service.Commands.Count));

        public Task<CommandStatus> StatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandStatus("fantasim-test", _service.Commands));

        public Task<CommandResult> CommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => _service.ExecuteAsync(request, cancellationToken);
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
