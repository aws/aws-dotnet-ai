// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Testing.Services;

/// <summary>
/// An ILoggerProvider that forwards all log messages to an Aspire ResourceLoggerService ILogger.
/// This bridges the embedded server's logging into the Aspire dashboard.
/// </summary>
public sealed class AspireLoggerProvider(ILogger aspireLogger) : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(aspireLogger, categoryName);

    /// <inheritdoc/>
    public void Dispose() { }

    private sealed class ForwardingLogger(ILogger target, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => target.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            target.Log(logLevel, eventId, $"[{category}] {message}", exception, (s, _) => s);
        }
    }
}
