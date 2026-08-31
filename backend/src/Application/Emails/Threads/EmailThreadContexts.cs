// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>Assembles the conversations one content read has to publish, once each.</summary>
/// <remarks>
/// <para>
/// Built for a single read and thrown away with it. A read names up to ten emails and they are routinely the same
/// exchange, so assembling per email would read one conversation ten times, order it ten times, and — where a scanner is
/// switched on — scan every subject in it ten times. The instance is what makes each of those happen once.
/// </para>
/// <para>
/// Three things happen here in one place because they have to happen in this order. Folder visibility is applied first,
/// and it is applied in the query rather than to its answer: the read is bounded, so a withheld message left for this
/// class to drop would have spent one of those rows and pushed a readable message out of the conversation. The order is
/// produced from what comes back, so a message whose parent is withheld becomes a root of what the caller is shown; and
/// the subjects are scanned last, so what leaves this read has been through the same scanner the listing and the search
/// send theirs through.
/// </para>
/// </remarks>
public sealed class EmailThreadContexts
{
    private readonly IEmailThreadReader threadReader;
    private readonly MailboxScope readableScope;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly Dictionary<EmailThreadId, AssembledThread> assembled = [];

    /// <summary>Initializes the per-read assembly.</summary>
    /// <param name="threadReader">Reads the messages one conversation holds.</param>
    /// <param name="scopeResolver">Names the accounts and folders a tool may read, which every read here runs under.</param>
    /// <param name="egressGuard">Scans the subjects before any of them becomes a caller's.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The scope is resolved once, here, rather than per conversation: it is configuration rather than a caller's
    /// filter, and one read of several emails must not be able to see two answers to the same question. Junk mail is
    /// included, because a conversation is threaded across the folders it reached and a reply that landed in junk is
    /// part of the exchange the caller is reading rather than a listing they asked for.
    /// </remarks>
    public EmailThreadContexts(
        IEmailThreadReader threadReader,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(threadReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.threadReader = threadReader;
        this.readableScope = scopeResolver.ReadableScope([], [], JunkMailInclusion.Included);
        this.egressGuard = egressGuard;
    }

    /// <summary>Reads one conversation in its own order, out of the mail the caller may see.</summary>
    /// <param name="threadId">The conversation to assemble.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The conversation's visible messages in its order, and whether more of it exists than was assembled.</returns>
    public async Task<AssembledThread> AssembleAsync(EmailThreadId threadId, CancellationToken cancellationToken)
    {
        if (this.assembled.TryGetValue(threadId, out var already))
        {
            return already;
        }

        using var actingFor = this.egressGuard.ActingFor(this.readableScope.Owner);

        var read = await this.threadReader.ReadEmailsAsync(threadId, this.readableScope, cancellationToken);
        var wasCutShort = read.Count > IEmailThreadReader.MaximumAssembledEmails;
        var visible = read.Take(IEmailThreadReader.MaximumAssembledEmails).ToArray();

        var placed = EmailThreadOrder.Of(await this.GuardedAsync(visible, cancellationToken));
        var thread = new AssembledThread(placed, wasCutShort);

        this.assembled[threadId] = thread;

        return thread;
    }

    /// <summary>Builds what one read email publishes about the conversation it belongs to.</summary>
    /// <param name="threadId">The conversation the email's row names, or <see langword="null" /> when it names none.</param>
    /// <param name="storedEmailId">The email the read is answering for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The conversation as this email publishes it, or <see langword="null" /> when the email is in none.</returns>
    public async Task<ReadEmailThread?> ContextForAsync(
        EmailThreadId? threadId,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        if (threadId is not { } conversation)
        {
            return null;
        }

        var thread = await this.AssembleAsync(conversation, cancellationToken);
        var placed = thread.Emails.SingleOrDefault(email => email.Email.StoredEmailId == storedEmailId);
        var others = thread.Emails
            .Where(email => email.Email.StoredEmailId != storedEmailId)
            .Take(ReadEmailThread.MaximumNamedEmails)
            .ToArray();

        return new ReadEmailThread
        {
            ThreadId = conversation,
            Position = placed?.Position,
            AnsweredStoredEmailId = placed?.AnsweredStoredEmailId,
            EmailCount = thread.Emails.Count,
            OtherEmails = others,
            MoreEmailsNotNamed = thread.WasCutShort || others.Length < thread.Emails.Count - 1,
        };
    }

    /// <summary>Scans every subject the conversation would publish, before any of it becomes a caller's.</summary>
    /// <remarks>
    /// <para>
    /// The subject is the one thing a message's author wrote that a conversation reproduces, so it is scanned exactly as
    /// the listing, the search, and the message's own headers scan theirs. A tool that named a conversation with the
    /// unredacted subject the listing had redacted would leave the two disagreeing about what the same message says,
    /// which is what a caller cannot resolve on its own.
    /// </para>
    /// <para>
    /// The sender's address is left alone, on the same line every other read draws: it is a routing identity a caller
    /// acts on rather than free text somebody wrote, and withholding it would remove the use while protecting nothing
    /// the subject beside it did not already carry. No display name is published here at all, so none is scanned.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ThreadedEmailSummary>> GuardedAsync(
        ThreadedEmailSummary[] emails,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return emails;
        }

        // One report for the conversation, which is the unit its subjects are scanned as: a call naming ten messages
        // of one exchange guards these once rather than once per message.
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            cancellationToken);

        var guarded = new List<ThreadedEmailSummary>(emails.Length);

        foreach (var email in emails)
        {
            guarded.Add(email with
            {
                Subject = await this.egressGuard.GuardOptionalAsync(
                    SensitiveContentEgressPoint.McpEmailContent,
                    email.Subject,
                    cancellationToken),
            });
        }

        scan.Completed();

        return guarded;
    }

    /// <summary>One conversation as a read sees it: its visible messages in order, and whether more of it exists.</summary>
    /// <param name="Emails">The visible messages in the conversation's own order.</param>
    /// <param name="WasCutShort">Whether the conversation holds more messages than one read assembles.</param>
    public sealed record AssembledThread(IReadOnlyList<PlacedThreadedEmail> Emails, bool WasCutShort);
}
