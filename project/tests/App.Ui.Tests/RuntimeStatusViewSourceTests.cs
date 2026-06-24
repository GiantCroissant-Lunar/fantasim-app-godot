using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Command;
using FantaSim.App.Command.Orchestration;
using FantaSim.App.Ui;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class RuntimeStatusViewSourceTests
{
    [Fact]
    public void ViewId_IsRuntimeStatus()
    {
        var src = new RuntimeStatusViewSource(new FakeOrchestration(healthy: true), NullLogger.Instance);
        Assert.Equal("runtime-status", src.ViewId);
    }

    [Fact]
    public void BuildDocument_HealthyOrchestration_ProducesHealthyRuntimeText()
    {
        var src = new RuntimeStatusViewSource(new FakeOrchestration(healthy: true), NullLogger.Instance);
        var doc = src.BuildDocument();
        Assert.Equal("runtime-status", doc.SurfaceId);
        Assert.Equal("basic", doc.CatalogId);
        var runtime = (string)doc.DataModel["agentRuntime"]!["runtime"]!;
        Assert.Contains("healthy", runtime);
    }

    [Fact]
    public void BuildDocument_DegradedOrchestration_ProducesDegradedRuntimeText()
    {
        var src = new RuntimeStatusViewSource(new FakeOrchestration(healthy: false), NullLogger.Instance);
        var doc = src.BuildDocument();
        var runtime = (string)doc.DataModel["agentRuntime"]!["runtime"]!;
        Assert.Contains("degraded", runtime);
    }

    [Fact]
    public void BuildDocument_ThrowingOrchestration_ProducesUnknownRuntimeText()
    {
        var src = new RuntimeStatusViewSource(new ThrowingOrchestration(), NullLogger.Instance);
        var doc = src.BuildDocument();
        var runtime = (string)doc.DataModel["agentRuntime"]!["runtime"]!;
        Assert.Contains("unknown", runtime);
    }

    [Fact]
    public void Dispatch_LogsTheActionAndComponentId()
    {
        var logger = new CapturingLogger();
        var src = new RuntimeStatusViewSource(new FakeOrchestration(healthy: true), logger);

        src.Dispatch("refresh", "panel-1");

        var message = Assert.Single(logger.Messages);
        Assert.Contains("refresh", message);
        Assert.Contains("panel-1", message);
    }

    [Fact]
    public void Refresh_RaisesChangedEventOnce()
    {
        var src = new RuntimeStatusViewSource(new FakeOrchestration(healthy: true), NullLogger.Instance);
        var fires = 0;
        src.Changed += () => fires++;
        src.Refresh();
        Assert.Equal(1, fires);
    }

    private sealed class FakeOrchestration : IWorldOrchestration
    {
        private readonly bool _healthy;
        public FakeOrchestration(bool healthy) => _healthy = healthy;
        public Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult(Id: "fake", Ok: _healthy, ResultJson: "{}", Error: null));
        public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandHealth(Ok: _healthy, Commands: 1));
    }

    private sealed class ThrowingOrchestration : IWorldOrchestration
    {
        public Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => throw new System.NotImplementedException();
        public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
            => throw new System.NotImplementedException();
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}