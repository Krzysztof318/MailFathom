// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the switch that decides whether a mail account's source server is still the truth about its mailbox.</summary>
/// <remarks>
/// <para>
/// It is an administrative command rather than a configuration key, which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>'s
/// option J2 and the one decision here that could not be a setting: the switch on is a point of no return for
/// somebody's mail, so what makes it happen has to be an act with an author, a moment, and an audit line, not a value
/// that arrives with a file.
/// </para>
/// <para>
/// The read is published under <c>mailfathom.admin.read</c>, and both write routes — the switch, and the settlement of
/// an append whose answer never came back — under the same <c>mailfathom.admin.custody.write</c>. A grant of its own
/// rather than the configuration writer's, because these are the administrative acts that end with a mail server no
/// longer holding a copy of the mailbox, or with a second copy in somebody's folder: a credential that may rewrite
/// settings must not thereby be able to do either.
/// </para>
/// <para>
/// Nothing on any of the three routes is derived from a message. The standing figures are counts, an append is named
/// by a record identity, a folder alias and an instant, and a refusal names an account, a folder alias, or a replica —
/// MailFathom's own words for things.
/// </para>
/// </remarks>
internal static class MailAccountCustodyEndpoints
{
    /// <summary>The route one account's custody is read from, relative to the administrative prefix.</summary>
    internal const string CustodyRoute = "/accounts/custody";

    /// <summary>The route a switch is asked for on, relative to the administrative prefix.</summary>
    internal const string CustodySwitchRoute = "/accounts/custody/switch";

    /// <summary>The route an unanswered restore append is settled on, relative to the administrative prefix.</summary>
    internal const string CustodyAppendSettlementRoute = "/accounts/custody/restore/settle";

    /// <summary>The greatest request body either write route reads before refusing it.</summary>
    /// <remarks>
    /// One body names an account and a custody and the other an account, a record and a verdict, so a few hundred
    /// bytes is the whole of anything either could mean. Stated for the reason every administrative body states it:
    /// the server's own default is measured in tens of megabytes.
    /// </remarks>
    internal const int MaxCustodyRequestBytes = 4 * 1024;

    /// <summary>Maps the three custody routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailAccountCustody(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(CustodyRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminRead);
        api.MapPost(CustodySwitchRoute, SwitchAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxCustodyRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCustodyWrite);
        api.MapPost(CustodyAppendSettlementRoute, SettleRestoreAppendAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxCustodyRequestBytes))
            .RequirePermission(MailFathomPermission.AdminCustodyWrite);
    }

    /// <summary>Reports which copy of one account's mailbox is the truth, and how far a switch under way has got.</summary>
    /// <param name="account">The account, as the deployment's configuration names it.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="custody">Reads the account's custody.</param>
    /// <param name="drain">Reads how much of the source is still to be emptied.</param>
    /// <param name="restore">Reads how much of the mailbox is still to be put back.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the state and the standing figures, <c>400</c> naming what was wrong with the request, or <c>409</c> where the account is served but has bound no folder yet.</returns>
    internal static async Task<Results<Ok<MailAccountCustodyResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailAccountCustodySwitch custody,
        [FromServices] MailboxDrainPass drain,
        [FromServices] MailboxRestorePass restore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(drain);
        ArgumentNullException.ThrowIfNull(restore);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        if (await custody.ReadAsync(servedAccount, cancellationToken) is not { } state)
        {
            return NoAccountRecordYet(servedAccount);
        }

        var standing = await drain.ReadStandingAsync(servedAccount, cancellationToken);

        // Only a restoring account is asked what it still owes its source, because the question is meaningless of any
        // other and expensive to answer: every message a mirrored account holds carries an occurrence whose state has
        // never been written down, so the same reading over one would report the whole mailbox as outstanding work.
        var restoreStanding = state.Phase is MailAccountCustodyPhase.Restoring
            ? await ReadRestoreStandingAsync(servedAccount, restore, cancellationToken)
            : null;

        return TypedResults.Ok(new MailAccountCustodyResponse(
            servedAccount.Value,
            state.Requested.ToString(),
            state.Phase.ToString(),
            state.IsSwitchPending,
            new MailAccountDrainStandingResponse(
                standing.AwaitingDrain,
                standing.HeldBackAboveSizeLimit,
                standing.HeldBackAwaitingHeadroom,
                standing.AwaitingSourceRemoval),
            restoreStanding));
    }

    /// <summary>Reads what one restoring account still owes its source, counted and with each append named.</summary>
    private static async Task<MailAccountRestoreStandingResponse> ReadRestoreStandingAsync(
        MailAccountId account,
        MailboxRestorePass restore,
        CancellationToken cancellationToken)
    {
        var standing = await restore.ReadStandingAsync(account, cancellationToken);
        var unanswered = await restore.ReadUnansweredAppendsAsync(account, cancellationToken);

        return new MailAccountRestoreStandingResponse(
            standing.AwaitingAppend,
            standing.AwaitingStateWrite,
            standing.UnansweredAppends,
            standing.AwaitingConfirmation,
            [
                .. unanswered.Select(static append => new MailAccountUnansweredAppendResponse(
                    append.Id.Value,
                    append.SourceFolderAlias.Value,
                    append.IssuedAt)),
            ]);
    }

    /// <summary>Asks for one account's custody to become what the request names.</summary>
    /// <param name="request">The account and the custody asked for.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="custody">Performs the switch.</param>
    /// <param name="cancellationToken">Cancels the request when the client disconnects.</param>
    /// <returns><c>200</c> with what the request did, <c>400</c> naming what was wrong with it, <c>409</c> where the account is served but has bound no folder yet, or <c>503</c> where the source could not be read.</returns>
    /// <remarks>
    /// <para>
    /// A refused switch answers <c>200</c> with the refusals rather than an error status, because every one of them is
    /// a true statement about the deployment rather than a fault in the request: a replica running a build that does
    /// not know the mode, a folder mapping that names nothing the source advertises, a folder playing a virtual role.
    /// The operator acts on what the answer names and asks again.
    /// </para>
    /// <para>
    /// A switch off reads what the source advertises before it accepts anything, so a source that cannot be reached is
    /// the ordinary condition rather than a fault: it answers <c>503</c> saying nothing was started, because a
    /// generic failure there would read as the deployment being broken rather than as the mail server being away.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<MailAccountCustodySwitchResponse>, ProblemHttpResult>> SwitchAsync(
        [FromBody] MailAccountCustodySwitchRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailAccountCustodySwitch custody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(custody);

        if (AdminAccountRequest.Resolve(request?.Account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        if (ResolveCustody(request?.Custody) is not { } requested)
        {
            return TypedResults.Problem(
                $"The request named no custody. Name one of {string.Join(", ", Enum.GetNames<MailAccountCustody>())}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        MailAccountCustodySwitchOutcome? outcome;

        try
        {
            outcome = await custody.SwitchAsync(servedAccount, requested, cancellationToken);
        }
        catch (MailboxUnavailableException)
        {
            return TypedResults.Problem(
                "The account's source server could not be reached, so the folder mappings the restore needs could not be checked. Nothing was started; ask again once the source answers.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (outcome is null)
        {
            return NoAccountRecordYet(servedAccount);
        }

        return TypedResults.Ok(new MailAccountCustodySwitchResponse(
            servedAccount.Value,
            outcome.WasAccepted,
            outcome.State.Requested.ToString(),
            outcome.State.Phase.ToString(),
            [.. outcome.Refusals.Select(refusal => refusal.Describe())]));
    }

    /// <summary>Records what an operator found in the folder one unanswered restore append was issued against.</summary>
    /// <param name="request">The account, the record, and what the folder holds.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="settlement">Writes the verdict.</param>
    /// <param name="cancellationToken">Cancels the request when the client disconnects.</param>
    /// <returns><c>200</c> with whether the record was still standing, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// A record nothing was standing for answers <c>200</c> with <c>false</c> rather than an error, because the
    /// operator's question is whether the append is still outstanding and "somebody already settled it" is an answer
    /// to it. That also makes the command safe to repeat.
    /// </remarks>
    internal static async Task<Results<Ok<MailAccountRestoreSettlementResponse>, ProblemHttpResult>> SettleRestoreAppendAsync(
        [FromBody] MailAccountRestoreSettlementRequest? request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxRestoreSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(settlement);

        if (AdminAccountRequest.Resolve(request?.Account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(request?.Account);
        }

        if (request?.Record is not { } record || record == Guid.Empty)
        {
            return TypedResults.Problem(
                "The request named no append record. Name the record the account's custody reading reports.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var settled = await settlement.SettleAsync(
            servedAccount,
            new MailboxRestoreAppendId(record),
            request.SourceHoldsTheCopy,
            cancellationToken);

        return TypedResults.Ok(new MailAccountRestoreSettlementResponse(servedAccount.Value, record, settled));
    }

    /// <summary>States that the deployment serves the account but holds no record of it yet.</summary>
    /// <param name="account">The served account.</param>
    /// <returns>The refusal, which is a statement about the deployment rather than about the request.</returns>
    /// <remarks>
    /// Told apart from an account this deployment does not serve, because the operator's next act differs: that one is
    /// a name to correct, and this one is a wait. The account row is written by whichever synchronization run first
    /// binds one of the account's folders, and custody is a property of that row — so an account configured a minute
    /// ago has none to report and none to change, and answering with the unknown-account sentence would tell an
    /// operator their configuration never took.
    /// </remarks>
    private static ProblemHttpResult NoAccountRecordYet(MailAccountId account) => TypedResults.Problem(
        $"The account '{account.Value}' is served by this deployment but has bound no folder yet, so it holds no custody to report or change. Its record is written by the first synchronization run that binds one of its folders; ask again once that run has happened.",
        statusCode: StatusCodes.Status409Conflict);

    /// <summary>Resolves the custody a request names, against the member names this API publishes.</summary>
    /// <param name="written">The custody as the request wrote it.</param>
    /// <returns>The custody named, or <see langword="null" /> where the text names none this API publishes.</returns>
    /// <remarks>
    /// Compared against the published names rather than parsed, the way an account's configured reading language is:
    /// parsing accepts a bare number and combines a comma-separated list by bitwise OR even for an enum that is not a
    /// flags set, so <c>1</c> and <c>MirrorSource,HoldMailbox</c> would both reach the one administrative act that
    /// ends with a source server no longer holding a copy of the mailbox, while naming no custody this API publishes.
    /// </remarks>
    private static MailAccountCustody? ResolveCustody(string? written) => Enum.GetValues<MailAccountCustody>()
        .Where(custody => string.Equals(custody.ToString(), written, StringComparison.OrdinalIgnoreCase))
        .Select(custody => (MailAccountCustody?)custody)
        .FirstOrDefault();
}

/// <summary>What a deployment is asked when one account's custody is to change.</summary>
/// <param name="Account">The account, as the deployment's configuration names it.</param>
/// <param name="Custody">The custody asked for, named as one of the declared values.</param>
internal sealed record MailAccountCustodySwitchRequest(string? Account, string? Custody);

/// <summary>Which copy of one account's mailbox is the truth, and how far a switch under way has got.</summary>
/// <param name="Account">The account.</param>
/// <param name="Requested">The custody an administrator last asked for.</param>
/// <param name="Phase">Which copy is the truth at this moment.</param>
/// <param name="IsSwitchPending">Whether the account is still moving towards what was asked for.</param>
/// <param name="Drain">What the source still holds, counted.</param>
/// <param name="Restore">What the mailbox still owes the source, or <see langword="null" /> for an account putting nothing back.</param>
/// <remarks>
/// The restore block is absent rather than zeroed off a restoring account, because the question is meaningless of any
/// other and expensive to answer: every message a mirrored account holds carries an occurrence whose state has never
/// been written down, so the same reading over one would report the whole mailbox as outstanding work. A reader takes
/// its absence as "nothing is being put back", which is what a row of zeroes would otherwise have to be read as.
/// </remarks>
internal sealed record MailAccountCustodyResponse(
    string Account,
    string Requested,
    string Phase,
    bool IsSwitchPending,
    MailAccountDrainStandingResponse Drain,
    MailAccountRestoreStandingResponse? Restore);

/// <summary>What one held account's source still holds, counted rather than listed.</summary>
/// <param name="AwaitingDrain">Messages whose occurrence still stands on the source.</param>
/// <param name="HeldBackAboveSizeLimit">Messages the source keeps because their content is above the size MailFathom stores.</param>
/// <param name="HeldBackAwaitingHeadroom">Messages the source keeps until the storage ceiling has headroom for them.</param>
/// <param name="AwaitingSourceRemoval">Messages erased locally whose source copy has still to be removed.</param>
internal sealed record MailAccountDrainStandingResponse(
    int AwaitingDrain,
    int HeldBackAboveSizeLimit,
    int HeldBackAwaitingHeadroom,
    int AwaitingSourceRemoval);

/// <summary>What one restoring account's mailbox still owes its source, counted rather than listed.</summary>
/// <param name="AwaitingAppend">Messages the source no longer holds that have still to be put back.</param>
/// <param name="AwaitingStateWrite">Messages whose stored state has still to be written onto the occurrence they keep.</param>
/// <param name="UnansweredAppends">Appends whose answer never came back, each of which holds the account in <c>Restoring</c>.</param>
/// <param name="AwaitingConfirmation">Appends the source answered in full whose occurrence the next pass has still to write, which need nobody.</param>
/// <param name="Unanswered">The unanswered appends, named so an operator can settle them one at a time.</param>
/// <remarks>
/// The records are the one part of this answer that is a list rather than a count, and they carry a record identity, a
/// folder alias, and an instant — MailFathom's own words for things. What an operator needs is which of their folders
/// to look in and which record to settle, and neither is derived from the message.
/// </remarks>
internal sealed record MailAccountRestoreStandingResponse(
    int AwaitingAppend,
    int AwaitingStateWrite,
    int UnansweredAppends,
    int AwaitingConfirmation,
    IReadOnlyList<MailAccountUnansweredAppendResponse> Unanswered);

/// <summary>One append the restore issued whose answer never came back.</summary>
/// <param name="Record">What the settling command names the record by.</param>
/// <param name="Folder">MailFathom's own name for the folder the copy was appended into.</param>
/// <param name="IssuedAt">When the command went out.</param>
internal sealed record MailAccountUnansweredAppendResponse(
    Guid Record,
    string Folder,
    DateTimeOffset IssuedAt);

/// <summary>What an operator found in the folder one unanswered restore append was issued against.</summary>
/// <param name="Account">The account, as the deployment's configuration names it.</param>
/// <param name="Record">The record being settled, as the custody reading names it.</param>
/// <param name="SourceHoldsTheCopy">Whether the folder holds the copy the append may have put there.</param>
internal sealed record MailAccountRestoreSettlementRequest(string? Account, Guid? Record, bool SourceHoldsTheCopy);

/// <summary>What settling one unanswered restore append did.</summary>
/// <param name="Account">The account.</param>
/// <param name="Record">The record named.</param>
/// <param name="WasSettled">Whether the record was still standing, which is false where somebody had already settled it.</param>
internal sealed record MailAccountRestoreSettlementResponse(string Account, Guid Record, bool WasSettled);

/// <summary>What asking for a custody did.</summary>
/// <param name="Account">The account.</param>
/// <param name="WasAccepted">Whether the request was recorded, which is false exactly when refusals are named.</param>
/// <param name="Requested">The custody the account is now asked to have, unchanged by a refused request.</param>
/// <param name="Phase">Which copy of the mailbox is the truth at this moment.</param>
/// <param name="Refusals">One operator-facing sentence per reason the switch was refused, empty when it was accepted.</param>
internal sealed record MailAccountCustodySwitchResponse(
    string Account,
    bool WasAccepted,
    string Requested,
    string Phase,
    IReadOnlyList<string> Refusals);
