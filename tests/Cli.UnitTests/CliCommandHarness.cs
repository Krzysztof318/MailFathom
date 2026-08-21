// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Cli.UnitTests;

/// <summary>Runs one mfctl invocation against a fake deployment, over a signed-in credential store of its own.</summary>
/// <remarks>
/// Every command suite arranges the same three things — a temporary store holding one saved profile, a console that
/// records what was written, and a clock that does not move — and then runs the parser over them. Holding those here
/// rather than per suite keeps the arrangement one decision: the console the assertions read stays the suite's own
/// property rather than being hidden behind a base class, and the store directory is cleaned up by whoever owns it.
/// </remarks>
internal sealed class CliCommandHarness : IDisposable
{
    /// <summary>The deployment address every command suite points at, which is also what a saved profile carries.</summary>
    internal const string Endpoint = "https://mail.example.test:8443";

    private static readonly Uri EndpointAddress = new(Endpoint);

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-tests-{Guid.NewGuid():N}");

    /// <summary>Initializes the harness with the instant its clock stands at.</summary>
    /// <param name="now">What the clock reads for the whole invocation.</param>
    internal CliCommandHarness(DateTimeOffset now) => this.Clock = new FakeTimeProvider(now);

    /// <summary>Gets the terminal the invocation wrote to, which is what a suite's assertions read.</summary>
    internal RecordingCliConsole Console { get; } = new();

    /// <summary>Gets the clock the invocation runs against, which stands still unless a test advances it.</summary>
    internal FakeTimeProvider Clock { get; }

    /// <summary>Runs one invocation against a deployment, with the credential a signed-in operator would hold.</summary>
    /// <param name="deployment">The deployment the command meets instead of a server.</param>
    /// <param name="args">The command line as an operator would type it.</param>
    /// <returns>The exit code the invocation ended with.</returns>
    internal Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args)
    {
        var store = new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");

        var context = new CliContext(
            this.Console,
            store,
            (endpoint, trust) => FakeDeploymentTransport.Over(deployment, endpoint, trust),
            FakeMailboxRedirect.Silent(),
            _ => false,
            this.Clock);

        return CliRunner.RunAsync(context, args);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
