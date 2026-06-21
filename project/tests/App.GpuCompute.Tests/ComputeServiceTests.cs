using FantaSim.App.GpuCompute;
using FantaSim.App.GpuCompute.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.GpuCompute.Tests;

public sealed class ComputeServiceTests
{
    [Fact]
    public async Task DispatchAsync_DelegatesValidRequestAndRaisesCompletion()
    {
        var expected = new ComputeDispatchResult("shader.test", true, new Dictionary<string, byte[]>());
        using var backend = new FakeBackend(expected);
        using var service = new FantaSim.App.GpuCompute.Services.Service(backend, NullLoggerFactory.Instance);
        var completed = new List<ComputeDispatchResult>();
        service.DispatchCompleted += completed.Add;

        var result = await service.DispatchAsync(ValidRequest());

        Assert.Same(expected, result);
        Assert.Single(completed, expected);
        Assert.Equal(1, backend.DispatchCount);
    }

    [Fact]
    public async Task DispatchAsync_ReturnsFailedResultWhenBackendThrows()
    {
        using var backend = new FakeBackend(new InvalidOperationException("backend failed"));
        using var service = new FantaSim.App.GpuCompute.Services.Service(backend, NullLoggerFactory.Instance);

        var result = await service.DispatchAsync(ValidRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("shader.test", result.ShaderId);
        Assert.Contains("backend failed", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatchAsync_RejectsDuplicateBindings()
    {
        using var backend = new FakeBackend(new ComputeDispatchResult("shader.test", true, new Dictionary<string, byte[]>()));
        using var service = new FantaSim.App.GpuCompute.Services.Service(backend, NullLoggerFactory.Instance);
        var request = ValidRequest() with
        {
            Buffers = new[]
            {
                new ComputeBufferBinding(0, 0, new byte[4]),
                new ComputeBufferBinding(0, 0, new byte[4]),
            },
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.DispatchAsync(request));
        Assert.Equal(0, backend.DispatchCount);
    }

    private static ComputeDispatchRequest ValidRequest()
        => new(
            new ComputeShaderReference("shader.test", "res://shaders/test.glsl"),
            new ComputeDispatchSize(1, 1, 1),
            new[] { new ComputeBufferBinding(0, 0, new byte[] { 1, 2, 3, 4 }, 4, "out") });

    private sealed class FakeBackend : IComputeBackend
    {
        private readonly ComputeDispatchResult? _result;
        private readonly Exception? _exception;

        public FakeBackend(ComputeDispatchResult result)
        {
            _result = result;
        }

        public FakeBackend(Exception exception)
        {
            _exception = exception;
        }

        public int DispatchCount { get; private set; }

        public ComputeCapabilities Capabilities { get; } = new(true, "fake");

        public Task<ComputeDispatchResult> DispatchAsync(
            ComputeDispatchRequest request,
            CancellationToken cancellationToken = default)
        {
            DispatchCount++;
            if (_exception is not null)
                throw _exception;
            return Task.FromResult(_result!);
        }

        public void Dispose()
        {
        }
    }
}
