// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Observability;
using MailFathom.Common.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the span one invocation opens, which is the whole of what carries a trace to the deployment.</summary>
/// <remarks>
/// <para>
/// The command exports nothing, so what this asserts is not what a collector receives but what
/// <c>HttpClient</c> is given to propagate: an activity current for the duration of the invocation. Without it every
/// request the command issues starts a trace of its own on the deployment, and which command caused them is
/// unanswerable — so the assertion that matters most here is simply that a span exists at all.
/// </para>
/// <para>
/// It is named after the command the parser resolved rather than after the arguments, which is the part worth guarding:
/// an argument list is where a deployment address, an account alias, and a credential are, and none of them may reach a
/// span.
/// </para>
/// </remarks>
public sealed class CliTelemetryTests : IDisposable
{
    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public CliTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (StringComparer.Ordinal.Equals(activity.OperationName, CliTelemetry.CommandSpanName))
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>An invocation that succeeded publishes the command that ran and the code it reported.</summary>
    [Fact]
    public async Task RunAsync_AnInvocationThatSucceeded_PublishesTheCommandAndTheExitCode()
    {
        // Arrange
        var console = new RecordingCliConsole();

        // Act
        var exitCode = await CliRunner.RunAsync(ContextFor(console), ["--version"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains(
            this.published,
            span => Equals(span.GetTagItem(CliTelemetry.CommandTagName), "mfctl")
                && Equals(span.GetTagItem(CliTelemetry.ExitCodeTagName), exitCode)
                && span.Status == ActivityStatusCode.Ok);
    }

    /// <summary>An invocation the parser refused is an error on the span, under the command it got as far as.</summary>
    [Fact]
    public async Task RunAsync_AnInvocationTheParserRefused_PublishesItAsAnError()
    {
        // Arrange
        var console = new RecordingCliConsole();

        // Act
        var exitCode = await CliRunner.RunAsync(
            ContextFor(console),
            ["--no-such-option"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(0, exitCode);
        Assert.Contains(
            this.published,
            span => Equals(span.GetTagItem(CliTelemetry.ExitCodeTagName), exitCode)
                && span.Status == ActivityStatusCode.Error);
    }

    /// <summary>Nothing the operator typed beyond a declared command name reaches the span.</summary>
    /// <remarks>
    /// The arguments carry a deployment address and, for a sign-in, a credential, so the assertion is over every tag
    /// value of every span this listener saw rather than over the one the command name is on. A tag added later would
    /// otherwise carry them unasserted, and the address is this test's own string, so a span another class published
    /// can only make the claim stronger.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AnInvocationCarryingAnAddress_PublishesNoneOfWhatWasTyped()
    {
        // Arrange
        var console = new RecordingCliConsole();
        const string Address = "https://mail.example.invalid:8443";

        // Act
        _ = await CliRunner.RunAsync(
            ContextFor(console),
            ["--version", Address],
            TestContext.Current.CancellationToken);

        // Assert
        var values = this.published
            .SelectMany(span => span.TagObjects)
            .Select(tag => tag.Value?.ToString() ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(values);
        Assert.DoesNotContain(Address, values);
    }

    /// <summary>The command's own names obey the contract every other boundary's names are held to.</summary>
    /// <remarks>
    /// Asserted here because nothing else reaches it. The deployment-wide suite reads the assemblies the host
    /// references, and the command is deliberately not one of them — it is a separate binary with one project
    /// reference — so a name declared here would otherwise be the one name in the repository nothing judged.
    /// </remarks>
    [Fact]
    public void EveryDeclaredName_InTheAdministrationCommand_ObeysTheRedactionContract() =>
        TelemetryRedactionContract.AssertEveryDeclaredNameObeysTheContract(typeof(CliTelemetry).Assembly);

    /// <summary>The reader finds the command's names, so the assertion above is holding something against the contract.</summary>
    /// <remarks>The control for an assertion that is otherwise an absence: an assembly whose constants were not read reports no offending name in exactly the way a clean one does.</remarks>
    [Fact]
    public void DeclaredTelemetryNames_InTheAdministrationCommand_IncludeTheSpanItOpens()
    {
        // Arrange

        // Act
        var declared = TelemetryRedactionContract.DeclaredTelemetryNamesIn(typeof(CliTelemetry).Assembly);

        // Assert
        Assert.Contains((CliTelemetry.CommandSpanName, true), declared);
        Assert.Contains((CliTelemetry.CommandTagName, false), declared);
    }

    private static CliContext ContextFor(RecordingCliConsole console) => new(
        console,
        new CredentialStore("credentials.json", new TokenProtector("credentials.key")),
        static (_, _) => throw new InvalidOperationException("No command in this class opens a transport."),
        FakeMailboxRedirect.Silent(),
        static _ => false,
        TimeProvider.System);
}
