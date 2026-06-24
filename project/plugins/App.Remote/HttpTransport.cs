using System.Net;
using System.Text;
using System.Text.Json;
using FantaSim.App.Command;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.Remote;

public sealed class HttpTransport : ITransport, IDisposable
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly RemoteOptions _options;
    private readonly IClient _client;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _stopCts;
    private Task? _acceptTask;
    private bool _started;
    private bool _disposed;

    public HttpTransport(
        RemoteOptions options,
        IClient client,
        IMainThreadDispatcher dispatcher,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<HttpTransport>();
    }

    public string Name => "http";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        lock (_gate)
        {
            if (_started)
                return Task.CompletedTask;

            try
            {
                var bind = ParseBindAddress(_options.BindAddress);
                if (!IPAddress.IsLoopback(bind.Address))
                {
                    _logger.LogWarning(
                        "Remote HTTP transport refused non-loopback bind address {BindAddress}.",
                        _options.BindAddress);
                    return Task.CompletedTask;
                }

                var listener = new HttpListener();
                listener.Prefixes.Add($"http://{bind.Host}:{bind.Port}/");
                listener.Start();

                _listener = listener;
                _stopCts = new CancellationTokenSource();
                _started = true;
                _acceptTask = Task.Run(() => AcceptLoopAsync(_stopCts.Token), CancellationToken.None);

                _logger.LogInformation("Remote HTTP transport listening on http://{BindAddress}/", _options.BindAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Remote HTTP transport failed to bind {BindAddress}.", _options.BindAddress);
                CleanupListener();
            }
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? acceptTask;
        lock (_gate)
        {
            if (!_started)
                return;

            _started = false;
            acceptTask = _acceptTask;
            _stopCts?.Cancel();
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
        }

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        lock (_gate)
            CleanupListener();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Remote HTTP accept loop failed.");
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsLoopbackRequest(context))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.Close();
                return;
            }

            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/";

            if (context.Request.HttpMethod == "GET" && path == "/health")
            {
                var health = await _client.HealthAsync(cancellationToken);
                await SendJsonAsync(context, health, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/status")
            {
                var status = await _client.StatusAsync(cancellationToken);
                await SendJsonAsync(context, status, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/command")
            {
                await HandleCommandAsync(context, cancellationToken);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.Close();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { context.Response.Close(); } catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote HTTP request failed.");
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private async Task HandleCommandAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        CommandRequest? request;
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);
            request = JsonSerializer.Deserialize<CommandRequest>(body, JsonReadOptions);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await SendJsonAsync(
                context,
                new CommandResult(
                    Guid.NewGuid().ToString("N"),
                    false,
                    Error: new CommandError("bad-request", "Invalid command request JSON.")),
                cancellationToken);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Command))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await SendJsonAsync(
                context,
                new CommandResult(
                    Guid.NewGuid().ToString("N"),
                    false,
                    Error: new CommandError("bad-request", "Missing command.")),
                cancellationToken);
            return;
        }

        var stampedRequest = request with
        {
            ActorKind = "http",
            ActorId = context.Request.RemoteEndPoint?.Address.ToString()
        };

        var result = await _dispatcher.InvokeAsync(
            () => _client.CommandAsync(stampedRequest, cancellationToken),
            cancellationToken);

        await SendJsonAsync(context, result, cancellationToken);
    }

    private bool IsAuthorized(HttpListenerContext context)
    {
        if (string.IsNullOrEmpty(_options.Token))
            return true;

        return string.Equals(
            context.Request.Headers["Authorization"],
            "Bearer " + _options.Token,
            StringComparison.Ordinal);
    }

    private static bool IsLoopbackRequest(HttpListenerContext context)
        => context.Request.RemoteEndPoint is null
            || IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address);

    private static async Task SendJsonAsync<T>(
        HttpListenerContext context,
        T value,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(value, JsonWriteOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private static BindAddress ParseBindAddress(string bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
            bindAddress = RemoteOptions.DefaultBindAddress;

        if (bindAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            bindAddress = bindAddress["http://".Length..].TrimEnd('/');

        var lastColon = bindAddress.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == bindAddress.Length - 1)
            throw new FormatException("Remote bind address must be host:port.");

        var host = bindAddress[..lastColon];
        if (!int.TryParse(bindAddress[(lastColon + 1)..], out var port) || port <= 0 || port > 65535)
            throw new FormatException("Remote bind port must be between 1 and 65535.");

        var address = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.Parse(host);

        return new BindAddress(host, address, port);
    }

    private void CleanupListener()
    {
        _listener = null;
        _acceptTask = null;
        _stopCts?.Dispose();
        _stopCts = null;
    }

    private sealed record BindAddress(string Host, IPAddress Address, int Port);
}
