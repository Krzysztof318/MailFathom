// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>Takes a message somebody wrote and a repetition they named, and writes both down as one declaration.</summary>
/// <remarks>
/// <para>
/// It is the authored submission's counterpart, and it stops one step earlier: nothing is queued, because nothing is
/// due. What it leaves is a declaration and the draft its occasions are composed from, and the recurring dispatch takes
/// it from there — one occurrence per occasion, each an ordinary send with an ordinary record.
/// </para>
/// <para>
/// The message is composed here rather than at each occasion, and then composed again at each occasion from what was
/// stored. That is not the same work done twice: composing now is what refuses a message this deployment will not send
/// while the person who wrote it is still present to be told, and composing again later is what gives every occasion an
/// identity and a date of its own — which is what keeps a year of Mondays a year of messages rather than one message a
/// recipient's client folds into itself.
/// </para>
/// <para>
/// The repetition is parsed before anything is written, by the same syntax every other recurring dispatch in this
/// deployment is declared in. Reusing it is deliberate: a second syntax for the same idea would be a second set of
/// rules about daylight saving, about how short an interval may be, and about what an operator has to learn.
/// </para>
/// <para>
/// Stopping a declaration is here as well, because it is the same use case read backwards and because the two have to
/// agree about what a declaration is. What it does not do is touch a message: an occurrence already written down is a
/// message the owner asked for at a moment that has come, and stopping that one is asked for against its own record.
/// </para>
/// </remarks>
public sealed class RecurringMailSubmission
{
    private readonly IDeploymentMailAccountCatalog accountCatalog;
    private readonly NamedRecipientResolver recipientResolver;
    private readonly IAuthoredEmailComposer composer;
    private readonly IRecurringSendStore recurringSends;
    private readonly IEmailContentStore contentStore;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case from the accounts it serves and the two writes it commits together.</summary>
    /// <param name="accountCatalog">Says which accounts this deployment serves, and therefore which one a caller may name.</param>
    /// <param name="recipientResolver">Turns the people the author named into the addresses every occurrence is offered to.</param>
    /// <param name="composer">Builds the draft, and decides every header this system owns rather than the author.</param>
    /// <param name="recurringSends">Holds the declaration and its idempotency identity.</param>
    /// <param name="contentStore">Holds the draft the declaration points at.</param>
    /// <param name="retryPolicy">Commits both writes together and resolves a lost race for the same identity.</param>
    /// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it send.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public RecurringMailSubmission(
        IDeploymentMailAccountCatalog accountCatalog,
        NamedRecipientResolver recipientResolver,
        IAuthoredEmailComposer composer,
        IRecurringSendStore recurringSends,
        IEmailContentStore contentStore,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(recipientResolver);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(recurringSends);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(authorization);

        this.accountCatalog = accountCatalog;
        this.recipientResolver = recipientResolver;
        this.composer = composer;
        this.recurringSends = recurringSends;
        this.contentStore = contentStore;
        this.retryPolicy = retryPolicy;
        this.authorization = authorization;
    }

    /// <summary>Declares one message to be sent again on every occasion a schedule names, or refuses it.</summary>
    /// <param name="request">The repetition that was asked for.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>The declaration, whether this call created it or an identical earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the repetition is unreadable, a recipient names nobody, a field cannot be composed, a bound is exceeded, or the account configures no address to send from.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write lost its race for the same identity on every allowed attempt.</exception>
    public async Task<RecurringSend> DeclareAsync(
        RecurringMailSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailSend);

        var account = this.accountCatalog.ServedAccounts.FirstOrDefault(served => served.IsNamedBy(request.Account))
            ?? throw new MailAccountNotAccessibleException(request.Account);

        // First of the three, because it is the only one that costs nothing: a repetition nobody can resolve is
        // refused before the contact book is read and before a message is assembled from what the caller wrote.
        if (!JobRecurrence.TryParse(request.Schedule, out _, out var scheduleError))
        {
            throw MailSubmissionRefusedException.ScheduleUnreadable(scheduleError!);
        }

        if (request.Recipients.Count > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw MailSubmissionRefusedException.TooManyRecipients();
        }

        var resolution = await this.recipientResolver.ResolveAsync(request.Recipients, cancellationToken);

        if (resolution.Refusal is { } recipientRefusal)
        {
            throw MailSubmissionRefusedException.From(recipientRefusal);
        }

        var authored = new AuthoredEmail
        {
            Recipients = resolution.Recipients,
            Subject = request.Subject,
            PlainTextBody = request.PlainTextBody,
            HtmlBody = request.HtmlBody,
        };

        var composition = this.composer.Compose(
            account.Id,
            request.Requester,
            authored,
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Email is not { } draft)
        {
            throw MailSubmissionRefusedException.From(composition.Refusal!);
        }

        var declaration = RecurringSendRequest.Create(
            account.Id,
            request.Requester,
            draft.Request.Recipients,
            request.Schedule);

        return await this.retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var declared = await this.recurringSends.DeclareAsync(
                    session,
                    declaration,
                    draft.RawMime.Length,
                    attemptCancellationToken);

                await this.contentStore.SaveRecurringSendDraftAsync(
                    session,
                    declared.Id,
                    draft.RawMime,
                    attemptCancellationToken);

                return declared;
            },
            cancellationToken);
    }

    /// <summary>Stops a declaration from producing any further occurrence.</summary>
    /// <param name="recurringSendId">The declaration to stop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the request, which is an answer rather than a failure in every case.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write lost its race on every allowed attempt.</exception>
    /// <remarks>
    /// The grant asked for is the one that lets a caller send, for the reason cancelling one message asks for it:
    /// whoever may write to this mailbox's correspondents is who may decide that it stops writing to them.
    /// </remarks>
    public Task<RecurringSendCancellation> CancelAsync(
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailSend);

        return this.retryPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.recurringSends.CancelAsync(
                session,
                recurringSendId,
                attemptCancellationToken),
            cancellationToken);
    }
}
