// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Owns the pool a write-session test runs against, and releases it with the test.</summary>
/// <remarks>
/// The pool holds live connections and an expiry timer, and a test that let it outlive the test would leave a timer
/// running against a disposed connection, so the harness is what a test <c>await using</c>s rather than the factory.
/// The telemetry needs no release of its own: it reports through the application's one activity source and meter,
/// which live for the process rather than for a test.
/// </remarks>
internal sealed class MailboxWriteHarness : IAsyncDisposable
{
    internal MailboxWriteHarness(
        MailboxWriteConnectionPool pool,
        MailboxMutationTelemetry telemetry,
        RecordingLoggerProvider recordedLogs,
        MailKitImapWriteSessionTestContext.ScopeDisposalCounter scopeDisposals)
    {
        this.Pool = pool;
        this.RecordedLogs = recordedLogs;
        this.ScopeDisposals = scopeDisposals;
        this.Factory = new MailKitImapWriteSessionFactory(pool, telemetry);
    }

    /// <summary>Gets the factory under test, which produces sessions over the pool below.</summary>
    internal IMailboxWriteSessionFactory Factory { get; }

    /// <summary>Gets the pool the factory leases from, so a test can assert on connection reuse and expiry.</summary>
    internal MailboxWriteConnectionPool Pool { get; }

    /// <summary>Gets everything the telemetry wrote, so a test can read the level a fact was reported at.</summary>
    internal RecordingLoggerProvider RecordedLogs { get; }

    /// <summary>Gets how many per-connection dependency-injection scopes the pool has released.</summary>
    internal MailKitImapWriteSessionTestContext.ScopeDisposalCounter ScopeDisposals { get; }

    /// <summary>Opens a write session on the account's inbox.</summary>
    internal Task<IMailboxWriteSession> OpenSessionAsync() => this.OpenSessionAsync(InboxFolder);

    /// <summary>Opens a write session on one folder of the account.</summary>
    internal Task<IMailboxWriteSession> OpenSessionAsync(MailFolderResolution folder) =>
        this.Factory.OpenForWritingAsync(
            PrimaryAccount,
            folder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => this.Pool.DisposeAsync();
}
