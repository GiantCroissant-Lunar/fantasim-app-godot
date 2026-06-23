using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Services;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class DeterminismFixesTests
{
    // ---- Fix 7: reproducible UpdatedUtc ----

    [Fact]
    public void BuildFamily_DefaultUpdatedUtc_IsUnixEpochAndReproducible()
    {
        var first = WorldGenerationGraphDefaults.BuildFamily();
        var second = WorldGenerationGraphDefaults.BuildFamily();

        Assert.Equal(DateTimeOffset.UnixEpoch, first.UpdatedUtc);
        Assert.Equal(first.UpdatedUtc, second.UpdatedUtc);
    }

    [Fact]
    public void BuildFamily_ExplicitUpdatedUtc_IsStampedExactly()
    {
        var stamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var family = WorldGenerationGraphDefaults.BuildFamily(updatedUtc: stamp);

        Assert.Equal(stamp, family.UpdatedUtc);
    }

    // ---- Fix 13: subscriber isolation with logging ----

    [Fact]
    public void EmitGenerationChanged_ThrowingSubscriber_DoesNotBlockSecondSubscriber()
    {
        using var service = new Service(new ServiceRegistry());
        var secondReceived = false;

        service.SubscribeGenerationChanged(_ => throw new InvalidOperationException("boom"));
        service.SubscribeGenerationChanged(_ => secondReceived = true);

        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "isolation-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));

        Assert.True(secondReceived);
        Assert.NotNull(service.LastSubscriberError);
        Assert.IsType<InvalidOperationException>(service.LastSubscriberError);
    }

    [Fact]
    public void EmitGenerationChanged_ThrowingSubscriber_LoggerRecordsFault()
    {
        var capture = new CapturingLoggerFactory();
        var registry = new ServiceRegistry();
        registry.Register<ILoggerFactory>(capture);

        using var service = new Service(registry);
        var secondReceived = false;

        service.SubscribeGenerationChanged(_ => throw new InvalidOperationException("logged-boom"));
        service.SubscribeGenerationChanged(_ => secondReceived = true);

        service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "logged-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));

        Assert.True(secondReceived);
        Assert.NotEmpty(capture.Errors);
        Assert.Contains(capture.Errors, entry =>
            entry.Exception is not null &&
            entry.Exception.Message.Contains("logged-boom", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(string Message, Exception? Exception)> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerFactory _owner;

        public CapturingLogger(CapturingLoggerFactory owner) => _owner = owner;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                _owner.Errors.Add((formatter(state, exception), exception));
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}