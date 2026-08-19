// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Carries one mutation's report from the call that starts it to the outcome that ends it.</summary>
/// <remarks>
/// <para>
/// The scope reports a failure unless <see cref="Completed" /> was called, so an exception thrown anywhere inside the
/// mutation is counted rather than dropping the operation out of the record entirely.
/// </para>
/// <para>
/// The outcome is the only thing the counter is broken down by beyond the mutation itself. Which protocol path ran is
/// deliberately not a dimension, because a dimension is exactly the thing that would let a dashboard tell a native
/// relocation from a fallback one; it is written to the debug log instead, where somebody diagnosing a broken fallback
/// is already looking.
/// </para>
/// </remarks>
internal sealed class MailboxMutationScope : IDisposable
{
    private readonly MailboxMutationTelemetry telemetry;
    private readonly string operationName;
    private readonly MailAccountId accountId;
    private readonly MailFolderAlias folderAlias;
    private readonly Activity? activity;
    private readonly long startingTimestamp;

    private bool succeeded;
    private bool reported;

    internal MailboxMutationScope(
        MailboxMutationTelemetry telemetry,
        string operationName,
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        Activity? activity,
        long startingTimestamp)
    {
        this.telemetry = telemetry;
        this.operationName = operationName;
        this.accountId = accountId;
        this.folderAlias = folderAlias;
        this.activity = activity;
        this.startingTimestamp = startingTimestamp;
    }

    /// <summary>Records which protocol path is carrying the mutation, as debug detail and nowhere else.</summary>
    /// <param name="protocolPath">A short name for the path, such as <c>native</c> or <c>fallback</c>.</param>
    internal void ProtocolPathChosen(string protocolPath) =>
        this.telemetry.RecordProtocolPath(this.operationName, this.accountId, this.folderAlias, protocolPath);

    /// <summary>Records one IMAP command the mutation issued, as debug detail and nowhere else.</summary>
    /// <param name="imapCommand">The command name, such as <c>UID COPY</c>.</param>
    /// <remarks>
    /// The command is named without its arguments. A UID set would identify individual emails, and the question this
    /// record answers — which step a broken fallback reached — is answered by the command alone.
    /// </remarks>
    internal void CommandIssued(string imapCommand) =>
        this.telemetry.RecordCommand(this.operationName, this.accountId, this.folderAlias, imapCommand);

    /// <summary>Marks the mutation as having done what it was asked to do.</summary>
    internal void Completed()
    {
        this.succeeded = true;
        this.activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.reported)
        {
            return;
        }

        this.reported = true;

        if (!this.succeeded)
        {
            this.activity?.SetStatus(ActivityStatusCode.Error);
        }

        this.telemetry.RecordOutcome(
            this.operationName,
            this.accountId,
            this.folderAlias,
            this.succeeded,
            this.telemetry.ElapsedSince(this.startingTimestamp));

        this.activity?.Dispose();
    }
}
