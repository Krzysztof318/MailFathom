// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Reads where one conversation stands, as the block a client draws beside the conversation itself.</summary>
/// <remarks>
/// <para>
/// It exists so the two questions a thread screen asks — what the exchange says, and where it stands — are scoped by
/// one decision rather than by two that could drift. The scope is
/// <see cref="BrowseThread.MailThreadBrowser" />'s own: every account this user holds, no folder narrowing, and
/// junk included, because a conversation is read by membership and a reply that landed in junk is still part of the
/// exchange. A state published under a wider scope than the conversation it describes would report a withheld folder's
/// contents one derived sentence at a time.
/// </para>
/// <para>
/// It reaches no provider and starts no derivation. A conversation nothing has been derived about answers with
/// <see langword="null" />, which is a state a client draws rather than a failure it reports.
/// </para>
/// </remarks>
public sealed class MailThreadStateBrowser
{
    private readonly IStoredThreadStateReader stateReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the read.</summary>
    /// <param name="stateReader">Reads the stored state of one conversation.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the conversation is read across.</param>
    /// <param name="egressGuard">Scans what the block is about to publish, where this deployment scans anything.</param>
    /// <param name="authorization">Enforces the permission this read is behind.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is <see langword="null" />.</exception>
    public MailThreadStateBrowser(
        IStoredThreadStateReader stateReader,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(stateReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(authorization);

        this.stateReader = stateReader;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.authorization = authorization;
    }

    /// <summary>Reads where one of the acting user's conversations stands.</summary>
    /// <param name="threadId">The conversation, as a message row published it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The state, or <see langword="null" /> where this deployment has none for that conversation.</returns>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the state carries, which refuses it rather than serving it unscanned.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    public async Task<EmailThreadState?> ReadStateAsync(EmailThreadId threadId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.User);

        var scope = this.scopeResolver.ReadableScope([], [], JunkMailInclusion.Included);

        if (scope.AccountIds.Count is 0)
        {
            return null;
        }

        var state = await this.stateReader.ReadStateAsync(threadId, scope, cancellationToken);

        return state is null ? null : await this.GuardedAsync(state, cancellationToken);
    }

    /// <summary>Scans everything the block would publish, under the point this surface is read on.</summary>
    /// <remarks>
    /// One report for the block rather than one per statement, because the block is what a screen waits for. The dates
    /// and the aspects are this deployment's own values rather than text anybody wrote, so nothing but the statement
    /// and the name a commitment is owed by is offered to a scanner.
    /// </remarks>
    private async Task<EmailThreadState> GuardedAsync(EmailThreadState state, CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive || state.Entries.Count is 0)
        {
            return state;
        }

        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientThreadState,
            cancellationToken);

        var guarded = new List<ThreadStateEntry>(state.Entries.Count);

        foreach (var entry in state.Entries)
        {
            guarded.Add(ThreadStateEntry.Create(
                entry.Aspect,
                await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ClientThreadState,
                    entry.Text,
                    cancellationToken),
                entry.Sources,
                await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.ClientThreadState,
                    entry.OwedBy,
                    cancellationToken),
                entry.DueAt));
        }

        scan.Completed();

        return state with { Entries = guarded };
    }
}
