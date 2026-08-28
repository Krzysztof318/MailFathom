// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>The transport pipeline registered for every request the client sends to its deployment.</summary>
public sealed class DeploymentRegistrationTests
{
    /// <summary>A query is mail metadata and never becomes the URI in the factory's default request logs.</summary>
    [Fact]
    public async Task AddMailFathomDeployment_ARequestCarryingAQuery_EmitsNoHttpClientLog()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new StubTransport(_ => response);
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddMailFathomDeployment(new DeploymentOptions());
        services
            .AddHttpClient(DeploymentHttpClients.Deployment)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var transport = factory.CreateClient(DeploymentHttpClients.Deployment);

        // Act
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://mail.example/api/client/emails/search?query=private%20words");
        _ = await transport.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient.", StringComparison.Ordinal));
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        internal List<(string Category, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this.Entries);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(string category, List<(string Category, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add((category, formatter(state, exception)));
    }
}
