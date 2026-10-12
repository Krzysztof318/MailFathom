// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Policies;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Reads and writes the settings policy the deployment holds, and the one each organization holds.</summary>
/// <remarks>
/// <para>
/// A policy is read and written whole, against the version it was read at. Nothing here patches one: the caller saves
/// a document, the document is judged on its own by <see cref="SettingsPolicyCandidate" />, and what passes is
/// committed as one statement or not at all.
/// </para>
/// <para>
/// A stored policy governs the users and the mail accounts of its scope: each is read through the policies above
/// it, so a write here changes what they are served. It is therefore judged twice. First as a document, and then —
/// for each section whose defaults or forced values it changes — against every record of that kind it governs,
/// inside the commit: a write that would leave one of them a record this deployment could not serve is refused
/// naming the records in the way, and one that governs more records than
/// <see cref="DeploymentOptions.MaximumRecordsJudgedPerPolicyWrite" />, users and mail accounts together, is refused
/// before any is read. The same commit rewrites the endpoint switches on the row of every user whose effective
/// switches it moves, and the queryable settings of every mail account whose effective settings it moves, which is
/// what makes a session stop on its holder's next request and a query over every account choose by the effective
/// settings on every replica. A write that changes nothing but an editing restriction judges no record, because a
/// restriction cannot make a record invalid.
/// </para>
/// <para>
/// A grant held below the deployment is bound in that pass as it is in a write of one account. Forcing a host, a
/// port, or a token endpoint decides where a mailbox's credential is presented, and such a grant is trusted with its
/// organization's mailboxes and not with a credential the deployment holds for somebody else. So a write under one
/// is refused, naming the accounts in the way, where it moves the effective settings of an account naming a secret
/// that was not provisioned for a user the account is assigned to. Which settings decide where a credential goes is
/// not enumerated, because a list is what a setting added later would be missing from. A policy in force that this
/// build cannot judge whole — one a newer build stored while both were serving — is judged as one stating nothing
/// an account could have been served with, so such a grant is not given the benefit of what nobody here can read.
/// </para>
/// <para>
/// The deployment's policy is the deployment's to write, so it takes the grant over the whole deployment. An
/// organization's accepts the same grant held at that organization, which is what lets an organization's own
/// administrator write its policy and no other; an organization the caller's scope does not cover is answered as one
/// this deployment does not hold, as every other route naming one answers.
/// </para>
/// <para>
/// A policy is never logged, and a refusal repeats of it only what <see cref="SettingsPolicyCandidate" /> states: a
/// language, a zone, or a recording level somebody misspelled, the alias a folder was given and the special-use role
/// it names, and a key shaped like a setting's name. By the rules it is judged under it carries no name, no address, and no credential of anybody it
/// governs, but a forced list of trusted senders is still a list of somebody's correspondents, so it is handled as
/// the records it governs are.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class SettingsPolicyAdministration(
    AccessAuthorization authorization,
    ISettingsPolicyStore store,
    IOptionsMonitor<DeploymentOptions> deployment,
    ConfigurationChangeAnnouncements announcements)
{
    /// <summary>Reads the policy one scope holds.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The policy, stating nothing where the scope stores none; or <see langword="null" /> when this deployment holds no such organization or the caller's scope does not cover it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />, or holds it below the deployment where the deployment's own policy is asked for.</exception>
    /// <exception cref="SettingsPolicyUnreadableException">Thrown when the scope stores a policy past what this build reads one from, which no route maps: the request fails and the failure is the operator's to find in the log.</exception>
    internal async Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken) =>
        await this.CoversAsync(MailFathomPermission.AdminRead, organizationId, cancellationToken)
            ? await store.ReadAsync(organizationId, cancellationToken)
            : null;

    /// <summary>Replaces the policy one scope holds with the one the caller saved.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="documentJson">The whole policy as the caller saved it.</param>
    /// <param name="expectedVersion">The version the saved policy was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such organization or the caller's scope does not cover it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />, or holds it below the deployment where the deployment's own policy is written.</exception>
    /// <exception cref="SettingsPolicyUnreadableException">Thrown when the policy in force is past what this build reads one from, as <see cref="ReadAsync" /> throws it.</exception>
    /// <remarks>
    /// The version is checked before the candidate is judged as well as in the statement, so a policy composed over
    /// one somebody else has replaced is told so rather than told about faults in a document it is about to compose
    /// again. Two administrators writing at once are settled by the statement: the second to commit matches no row,
    /// whichever replica each reached, and is answered with the version the first produced.
    /// </remarks>
    internal async Task<SettingsPolicyWriteOutcome?> ApplyAsync(
        Guid? organizationId,
        string documentJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentJson);

        if (!await this.CoversAsync(MailFathomPermission.AdminConfigurationWrite, organizationId, cancellationToken)
            || await store.ReadAsync(organizationId, cancellationToken) is not { } inForce)
        {
            return null;
        }

        if (inForce.Version != expectedVersion)
        {
            return Superseded(expectedVersion, inForce.Version);
        }

        var candidate = SettingsPolicyCandidate.Judge(documentJson);

        if (candidate.Json is not { } judged)
        {
            return SettingsPolicyWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                candidate.Refusals);
        }

        if (JsonNode.DeepEquals(JsonNode.Parse(inForce.Json), JsonNode.Parse(judged)))
        {
            return SettingsPolicyWriteOutcome.NothingToChange(
                inForce.Version,
                $"The saved policy states what the policy in force already states, so nothing was written and version {inForce.Version} stays in force.");
        }

        var maximum = deployment.CurrentValue.MaximumRecordsJudgedPerPolicyWrite;

        var commit = await store.CommitAsync(
            organizationId,
            judged,
            expectedVersion,
            new GovernedRecordsToJudge(
                MovesWhatARecordIsServed(SettingsPolicySection.Users, inForce.Json, judged),
                MovesWhatARecordIsServed(SettingsPolicySection.MailAccounts, inForce.Json, judged),
                maximum,
                UnderANarrowerGrant: !authorization.Permits(MailFathomPermission.AdminConfigurationWrite)),
            cancellationToken);

        switch (commit.Result)
        {
            case SettingsPolicyCommitResult.Committed:
                // The fast path only, as it is for a committed record: every replica converges on a version on its
                // own interval whether or not this reaches it.
                await announcements.AnnounceAsync();

                return SettingsPolicyWriteOutcome.Committed(commit.Version!.Value);

            case SettingsPolicyCommitResult.TooManyRecords:
                return SettingsPolicyWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationCandidateInvalid,
                    inForce.Version,
                    [
                        $"The saved policy changes a default or a forced value for {commit.GovernedRecords} records, counting the users and the mail accounts it changes one for, and one policy write judges at most {maximum}, so none was read and nothing was written. {DeploymentOptions.SectionName}:{nameof(DeploymentOptions.MaximumRecordsJudgedPerPolicyWrite)} raises that bound; the judgement runs inside the write, and saves of the records it judges wait for it.",
                    ]);

            case SettingsPolicyCommitResult.RecordsLeftInvalid:
                return SettingsPolicyWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationCandidateInvalid,
                    inForce.Version,
                    DescribeRecordsInTheWay(commit));

            default:
                // The policy moved while this candidate was being judged, or the organization was removed under it.
                // Which of the two is settled by reading rather than assumed, because the statement distinguishes
                // neither.
                return await store.ReadAsync(organizationId, cancellationToken) is { } current
                    ? Superseded(expectedVersion, current.Version)
                    : null;
        }
    }

    /// <summary>Reports whether a save changes a default or a forced value of one section, which is all of a policy that can break a record of that kind or move what it is served with.</summary>
    /// <remarks>
    /// A policy in force that this build does not judge whole is answered as one the save moves: what it states is
    /// unknown here, so whether the save leaves it standing is as well.
    /// </remarks>
    private static bool MovesWhatARecordIsServed(SettingsPolicySection section, string inForceJson, string judgedJson) =>
        SettingsPolicyStatements.Read(inForceJson, section) is not { } standing
        || SettingsPolicyStatements.Read(judgedJson, section) is not { } saved
        || !JsonNode.DeepEquals(standing.Defaults, saved.Defaults)
        || !JsonNode.DeepEquals(standing.Forced, saved.Forced);

    /// <summary>Names the records a policy write was refused over, and what each was refused for.</summary>
    /// <remarks>An account is named by the label this deployment tells it apart by and never by its address, which is somebody's.</remarks>
    private static IReadOnlyList<string> DescribeRecordsInTheWay(SettingsPolicyCommit commit)
    {
        var named = commit.RecordsInTheWay;

        return
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"The saved policy is refused over {commit.RecordsLeftInvalid} of the {commit.GovernedRecords} records it governs — a user record or a mail account it would leave one this deployment could not serve, or a mail account whose effective settings the grant it was written under may not move — so nothing was written. Correct those records first, or keep what they rely on in the policy. {(named.Count < commit.RecordsLeftInvalid ? $"The first {named.Count} are named below." : "Each is named below.")}"),
            .. named.Select(record =>
                $"{(record.Kind == SettingsPolicyRecordKind.MailAccount ? "Mail account" : "User")} '{record.DisplayName}' ({record.Identity:D}): {string.Join(" ", record.Corrections)}"),
        ];
    }

    /// <summary>Requires the capability an act on a policy is published under, and reports whether the caller holds it over the scope named.</summary>
    /// <remarks>
    /// The deployment's policy names no target, so a grant held below the deployment is refused rather than answered
    /// as nothing found: there is exactly one such policy, and whether it exists is not a fact to withhold.
    /// </remarks>
    private async Task<bool> CoversAsync(
        MailFathomPermission permission,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (organizationId is not { } organization)
        {
            authorization.RequirePermission(permission);

            return true;
        }

        authorization.RequirePermissionAtAnyScope(permission);

        return await authorization.PermitsOverScopeAsync(
            permission,
            AssignmentScope.Organization(organization),
            cancellationToken);
    }

    private static SettingsPolicyWriteOutcome Superseded(long composedOver, long inForce) =>
        SettingsPolicyWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationVersionSuperseded,
            inForce,
            [
                $"The change was composed over settings policy version {composedOver}, and version {inForce} is in force. Read the policy as it now stands and decide again against it.",
            ]);
}
