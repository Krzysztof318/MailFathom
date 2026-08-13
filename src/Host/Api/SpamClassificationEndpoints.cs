// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.History;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the two things an operator does about spam classification over a mailbox that is already stored.</summary>
/// <remarks>
/// <para>
/// Asking for a whole mailbox to be classified, and reading back what was concluded and what it asked for. Nothing here
/// switches classification on, moves a threshold, or names a junk folder: those are configuration, which
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md">ADR 0002</see>
/// keeps read-only so that what an instance will do to a mailbox is reviewable in a diff before it runs.
/// </para>
/// <para>
/// They are here rather than on the MCP surface because neither is anything a model reasons over, and because what bounds
/// administrative access is what should bound the ability to start a pass over a whole mailbox — and, with acting asked
/// for, to move a thousand messages. <strong>Every authenticated caller may perform every administrative
/// operation</strong>, which <see cref="MailboxRefreshTokenEndpoint" /> states in full.
/// </para>
/// <para>
/// Nothing either of them answers with is mail. Counts, verdicts, scores, signal names, folder aliases, mutation names,
/// instants, and identifiers are the whole of it — a signal's observed value never leaves the database, because it is
/// text a mail server wrote and can carry a sending domain.
/// </para>
/// </remarks>
internal static class SpamClassificationEndpoints
{
    /// <summary>The route a whole-mailbox run is asked for and read from, relative to the administrative prefix.</summary>
    /// <remarks>
    /// One path and two verbs, because they are one operation performed and then watched: what the reading reports is the
    /// run the write asked for, and an operator who started a pass over their mailbox comes back to the same place to
    /// find out where it got to.
    /// </remarks>
    internal const string RunsRoute = "/spam/runs";

    /// <summary>The route the recorded classifications are read from, relative to the administrative prefix.</summary>
    internal const string ClassificationsRoute = "/spam/classifications";

    /// <summary>The greatest request body the run route reads before refusing it.</summary>
    /// <remarks>
    /// The body names one account, a handful of folder aliases, and two switches, so a few kilobytes is the whole of
    /// anything it could mean. Stated because the server's own default is measured in tens of megabytes, which for this
    /// route would let an authenticated client make the process buffer a body four orders of magnitude larger than the
    /// request it is sending.
    /// </remarks>
    internal const int MaxRunRequestBytes = 8 * 1024;

    /// <summary>Maps the classification routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapSpamClassification(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the rule run route does: it
        // implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body feature, so a body
        // over the bound is answered 413 before the handler is reached.
        api.MapPost(RunsRoute, StartRunAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRunRequestBytes));

        api.MapGet(RunsRoute, ReadRunAsync);

        api.MapGet(ClassificationsRoute, ReadClassificationsAsync);
    }

    /// <summary>Asks for every message stored for one account to be classified on the terms the caller named.</summary>
    /// <param name="request">The account, the scope, and the two switches.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="settings">Answers what scope classification is configured over, which is the default and the bound.</param>
    /// <param name="requests">Records the request, or reports the run already in front of the account.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the run, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// It records that the run is wanted and classifies nothing. The pass is a step of the account's synchronization run,
    /// so the request neither performs the work nor keeps it alive — which is what stops an operator's terminal closing
    /// from cancelling a walk of their mailbox, and what makes this answer immediately however large the mailbox is.
    /// <para>
    /// A second request while one is outstanding is answered with the run already under way rather than refused, and the
    /// terms this request carried are not applied to it: a walk that has scored half a mailbox as a dry run cannot become
    /// one that acts halfway through. The answer says which of the two happened and reports the terms the outstanding run
    /// is actually walking under.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<SpamClassificationRunStartResponse>, ProblemHttpResult>> StartRunAsync(
        [FromBody] SpamClassificationRunRequestBody? request,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] ISpamClassificationSettingsReader settings,
        [FromServices] SpamClassificationRunRequests requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(requests);

        if (ResolveAccount(request?.Account, accounts) is not { } accountId)
        {
            return UnknownAccount(request?.Account);
        }

        var configuredScope = settings.Settings.ScannedFolderAliases;
        var scope = ResolveScope(request?.Folders, configuredScope);

        if (scope.Refusal is { } refusal)
        {
            return TypedResults.Problem(refusal, statusCode: StatusCodes.Status400BadRequest);
        }

        var terms = SpamClassificationRunTerms.Create(
            scope.FolderAliases,
            request?.Apply is true ? SpamActionPosture.Acting : SpamActionPosture.DryRun,
            request?.Rescore is true);

        var submitted = await requests.SubmitAsync(accountId, terms, cancellationToken);

        return TypedResults.Ok(new SpamClassificationRunStartResponse(
            submitted.Accepted,
            SpamClassificationRunResponse.For(submitted.Run)));
    }

    /// <summary>Reports where one account's whole-mailbox classification run has got to, or how the last one ended.</summary>
    /// <param name="account">The configured identifier of the account whose run is read.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="runs">Holds the one run an account may have outstanding, and the ending of the last one.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the run, <c>200</c> with none where the account has never been asked for one, or <c>400</c>.</returns>
    /// <remarks>
    /// An account that has never been asked for a run is an outcome rather than a refusal, so the answer carries no run
    /// instead of a <c>404</c>: the caller asked a question this deployment can answer, and the answer is that nothing
    /// has been asked for.
    /// </remarks>
    internal static async Task<Results<Ok<SpamClassificationRunStateResponse>, ProblemHttpResult>> ReadRunAsync(
        [FromQuery] string? account,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] ISpamClassificationRunStore runs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(runs);

        if (ResolveAccount(account, accounts) is not { } accountId)
        {
            return UnknownAccount(account);
        }

        var run = await runs.FindLatestAsync(accountId, cancellationToken);

        return TypedResults.Ok(new SpamClassificationRunStateResponse(
            accountId.Value,
            run is null ? null : SpamClassificationRunResponse.For(run)));
    }

    /// <summary>Serves one page of what classification concluded about an account's mail.</summary>
    /// <param name="account">The configured identifier of the account whose classifications are read.</param>
    /// <param name="email">The local identity of the occurrence to narrow to, or <see langword="null" /> for every one.</param>
    /// <param name="verdict">The verdict to narrow to, or <see langword="null" /> for every verdict.</param>
    /// <param name="from">The earliest evaluation instant served, inclusive, or <see langword="null" /> for none.</param>
    /// <param name="before">The evaluation instant to stop before, exclusive, or <see langword="null" /> for none.</param>
    /// <param name="pageSize">How many classifications the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="classifications">Reads the page.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// Every refusal is <c>400</c>, including an account this deployment does not configure, which mirrors the rule
    /// history and the two audit trails beside it: an unknown account is a mistake in the request the caller wrote rather
    /// than a missing resource, and <c>404</c> is already what a client reads as "this port serves no administrative
    /// endpoint".
    /// </remarks>
    internal static async Task<Results<Ok<SpamClassificationPageResponse>, ProblemHttpResult>> ReadClassificationsAsync(
        [FromQuery] string? account,
        [FromQuery] Guid? email,
        [FromQuery] string? verdict,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] IMailAccountCatalog accounts,
        [FromServices] ISpamClassificationHistoryReader classifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(classifications);

        if (ResolveAccount(account, accounts) is not { } accountId)
        {
            return UnknownAccount(account);
        }

        SpamClassificationHistoryCursor? decodedCursor = null;

        if (cursor is not null)
        {
            if (!SpamClassificationHistoryCursor.TryDecode(cursor, out var presentedCursor))
            {
                return TypedResults.Problem(
                    "The continuation cursor is not one this deployment issued.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            decodedCursor = presentedCursor;
        }

        if (!TryReadVerdict(verdict, out var namedVerdict))
        {
            return TypedResults.Problem(
                $"The verdict filter names no verdict this deployment reaches. It is one of {DeclaredVerdicts()}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var queryResult = SpamClassificationHistoryQuery.Create(
            accountId,
            email is { } emailId ? StoredEmailId.Create(emailId) : null,
            namedVerdict,
            from,
            before,
            pageSize,
            decodedCursor);

        if (queryResult.Query is not { } query)
        {
            return TypedResults.Problem(
                DescribeRefusal(queryResult.Outcome),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await classifications.ReadPageAsync(query, cancellationToken);

        return TypedResults.Ok(SpamClassificationPageResponse.For(page));
    }

    /// <summary>Reads the account a request named, or nothing when this deployment does not serve it.</summary>
    private static MailAccountId? ResolveAccount(string? account, IMailAccountCatalog accounts)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var accountId = MailAccountId.Create(account);

        return accounts.ServedAccounts.Any(served => served.Id == accountId) ? accountId : null;
    }

    /// <summary>Resolves the folders a run walks, defaulting to and bounded by the scope classification is configured over.</summary>
    /// <remarks>
    /// A folder outside the configured scope is refused rather than walked, because the classifier declines an occurrence
    /// outside that scope message by message: walking one would produce a run that read a whole folder and recorded
    /// nothing, and reported it as mail it could reach no verdict about. Refusing names the remedy, which is an edit to
    /// the configured scope rather than a different argument.
    /// </remarks>
    private static (IReadOnlyList<MailFolderAlias> FolderAliases, string? Refusal) ResolveScope(
        IReadOnlyList<string>? requested,
        IReadOnlyList<MailFolderAlias> configuredScope)
    {
        if (requested is null)
        {
            return configuredScope.Count > 0
                ? (configuredScope, null)
                : ([], "This deployment classifies no folder, so a run over one would read nothing. Name the folders in the SpamClassification section first.");
        }

        var named = requested.Where(static alias => !string.IsNullOrWhiteSpace(alias)).ToArray();

        if (named.Length == 0)
        {
            return ([], "The request named no folder to classify. Leave the folders out to walk the configured scope.");
        }

        var unusable = named.FirstOrDefault(static alias => !IsUsableAlias(alias));

        if (unusable is not null)
        {
            return ([], $"'{unusable}' is not a usable folder alias.");
        }

        MailFolderAlias[] aliases = [.. named.Select(MailFolderAlias.Create)];
        var outsideScope = aliases.FirstOrDefault(alias => !configuredScope.Contains(alias));

        return outsideScope == default
            ? (aliases, null)
            : ([], $"This deployment does not classify folder '{outsideScope.Value}', so a run over it would record nothing. Add it to the SpamClassification scope first.");
    }

    /// <summary>Reports whether text a caller sent is a value this system could have issued as an alias.</summary>
    /// <remarks>
    /// Asked before the value object is built rather than by catching what it throws, because a caller's text is
    /// untrusted input at the boundary and a refusal is what a <c>400</c> is made of.
    /// </remarks>
    private static bool IsUsableAlias(string alias) => !alias.Any(char.IsControl);

    /// <summary>Reads the verdict a filter named, keeping "no filter" apart from "a name nothing answers to".</summary>
    private static bool TryReadVerdict(string? verdict, out SpamVerdict? namedVerdict)
    {
        namedVerdict = null;

        if (verdict is null)
        {
            return true;
        }

        if (!Enum.TryParse<SpamVerdict>(verdict, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            return false;
        }

        namedVerdict = parsed;

        return true;
    }

    private static string DeclaredVerdicts() => string.Join(", ", Enum.GetNames<SpamVerdict>());

    /// <summary>States that the request named no account this deployment serves, without echoing an empty one.</summary>
    private static ProblemHttpResult UnknownAccount(string? account) => TypedResults.Problem(
        string.IsNullOrWhiteSpace(account)
            ? "The request named no mail account."
            : $"This deployment configures no mail account named '{account}'.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States what a caller has to change, without restating the filters they already sent.</summary>
    private static string DescribeRefusal(SpamClassificationHistoryQueryOutcome outcome) => outcome switch
    {
        SpamClassificationHistoryQueryOutcome.PageSizeOutOfRange =>
            $"A page of classifications holds between 1 and {SpamClassificationHistoryQuery.MaximumPageSize} records.",
        SpamClassificationHistoryQueryOutcome.TimeRangeEmpty =>
            "The time range ends at or before it begins, so it names no classifications.",
        SpamClassificationHistoryQueryOutcome.VerdictUnknown =>
            $"The verdict filter names no verdict this deployment reaches. It is one of {DeclaredVerdicts()}.",
        _ => "The continuation cursor was issued for a different set of classification filters.",
    };
}
