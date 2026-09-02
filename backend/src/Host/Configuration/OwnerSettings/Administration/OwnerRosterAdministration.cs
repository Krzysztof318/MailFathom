// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Owners;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What an administrator does to the roster itself: who this deployment holds, who joins it, and who leaves.</summary>
/// <remarks>
/// <para>
/// The roster is the relational envelope rather than anybody's record, and this is the whole of what an administrator
/// does to it. What one owner has configured is a separate service over the document beside the envelope, because the
/// two are granted apart and reached apart: an owner maintains their own record and never the roster, and the roster is
/// deployment-wide and therefore administrative and nothing else.
/// </para>
/// <para>
/// Provisioning writes the envelope and then commits the empty record, which is two statements and one act. The second
/// is what makes the owner's mail accounts their own from the start, and it is not the adoption this deployment refuses
/// to perform behind an operator's back: nothing declares an owner nobody had until this call, so there is no
/// configuration section for the record to be quietly replacing. An owner a file <em>does</em> declare stays served
/// from it until <c>mfctl owner adopt</c> moves them, which is the refusal the record administration carries.
/// </para>
/// <para>
/// Every operation asks for its own permission with the transport absent, as every other permission-bearing use case in
/// this system does, so an entrypoint added later cannot widen this surface by forgetting a route filter. Which
/// permission is which follows what the act costs: reading the roster is
/// <see cref="MailFathomPermission.AdminRead" />, adding somebody is
/// <see cref="MailFathomPermission.AdminConfigurationWrite" /> because it changes who this deployment serves rather
/// than what it does next, and removing somebody is <see cref="MailFathomPermission.AdminErase" /> because it disposes
/// of every message this deployment holds for them.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class OwnerRosterAdministration(
    AccessAuthorization authorization,
    IMailOwnerDirectory directory,
    IMailOwnerProvisioning provisioning,
    IMailOwnerErasure erasure,
    IOwnerSettingsDocumentWriter documents,
    ServedMailOwners servedOwners,
    SeveralOwnerAdmission admission,
    ConfiguredOwnerSettings configured,
    ILogger<OwnerRosterAdministration> logger)
{
    /// <summary>The record an owner is provisioned with, which is the empty one until they declare something.</summary>
    private const string EmptyRecord = "{}";

    /// <summary>The version a freshly provisioned row stands at, which the record's first commit is composed over.</summary>
    private const long ProvisionedVersion = 1;

    /// <summary>Reads the owners this deployment holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owners, in the order they were recorded in, each annotated with what this process is doing about them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>
    /// One more than a deployment may declare is read, so a roster past the bound is observable rather than silently
    /// truncated into a listing an administrator would then act on as though it were complete.
    /// </remarks>
    internal async Task<IReadOnlyList<OwnerRosterEntry>> ReadRosterAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminRead);

        var held = await directory.ReadOwnersAsync(DeclaredOwners.MaximumDeclaredOwners + 1, cancellationToken);

        // Read once rather than per entry: the declarations are a reflection bind of the whole collection, and this
        // route is read unconditionally by six of the commands `mfctl owner` publishes.
        var declaredInConfiguration = configured.OwnersAConfigurationSourceDeclares();

        return
        [
            .. held.Select(record => new OwnerRosterEntry(
                record.Owner,
                record.DisplayName,
                record.DocumentWrittenAtRuntime,
                Served: servedOwners.Owners.Any(served => served.Owner == record.Owner),
                DeclaredInConfiguration: declaredInConfiguration.Contains(record.Owner))),
        ];
    }

    /// <summary>Records an owner this deployment did not hold, under an identifier it mints.</summary>
    /// <param name="displayName">The label the owner is told apart by, which is unique across the deployment.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The identifier the owner was minted under, or the sentence naming what has to change first.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <exception cref="OwnerSettingsUnwritableException">Thrown when the record's first commit did not complete, which leaves the envelope written and the marker unset.</exception>
    /// <remarks>
    /// The identifier is minted here rather than supplied, and it is a version 4 value for the reason the column is:
    /// an owner identifier reaches administrative APIs, audit records, and logs, and a time-ordered one would publish
    /// when each owner was provisioned and in what order relative to every other.
    /// </remarks>
    internal async Task<OwnerProvisioningOutcome> ProvisionAsync(
        string? displayName,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (FindLabelRefusal(displayName) is { } unusable)
        {
            return OwnerProvisioningOutcome.Refused(unusable);
        }

        var label = displayName!.Trim();
        var held = await directory.ReadOwnersAsync(DeclaredOwners.MaximumDeclaredOwners + 1, cancellationToken);

        if (held.Count > 0 && admission.AdmitsACallerNamingNoOwner)
        {
            return OwnerProvisioningOutcome.Refused(admission.Refusal);
        }

        if (held.Count + 1 > DeclaredOwners.MaximumDeclaredOwners)
        {
            return OwnerProvisioningOutcome.Refused(
                $"This deployment already holds the {DeclaredOwners.MaximumDeclaredOwners} owners one deployment may serve. Remove an owner it no longer serves before recording another.");
        }

        if (held.Any(record => StringComparer.Ordinal.Equals(record.DisplayName, label)))
        {
            return OwnerProvisioningOutcome.Refused(LabelTaken(label));
        }

        var owner = MailOwnerId.Create(Guid.NewGuid());
        await servedOwners.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            if (!await provisioning.ProvisionAsync(owner, label, cancellationToken))
            {
                // The label was taken between the roster being read and the insert reaching the table, which no reading of
                // a snapshot could have refused earlier.
                return OwnerProvisioningOutcome.Refused(LabelTaken(label));
            }

            // The record rather than only the envelope, because an owner nothing declares is served from their own record
            // or from nothing at all. It is the empty object the envelope already carries, so what the commit changes is
            // the marker beside it — which is what the next start reads to decide that this owner is not waiting on a
            // configuration section that does not exist.
            if (await documents.CommitAsync(owner, EmptyRecord, ProvisionedVersion, cancellationToken) is not { } committed)
            {
                // The envelope was written and the row is gone again, which is another administrator erasing this owner
                // between the two statements. Reporting the owner as recorded would hand back an identifier nothing holds;
                // reporting it as provisioned without the marker would leave the next start reading their mail accounts
                // out of a configuration section that was never written for them, and refusing to start over the second
                // such row it met.
                return OwnerProvisioningOutcome.Refused(
                    "The owner was recorded and then removed before their record could be written, so this deployment holds nobody under that label. Record them again.");
            }

            // An adoption commits an empty record, so the owner it publishes declares no mailbox, classifies nothing,
            // and reads the deployment's own scanning posture until they write one.
            servedOwners.OwnerDocumentPublished(owner, label, new OwnerAccountOptions(), committed);

            this.LogOwnerProvisioned(label);

            return OwnerProvisioningOutcome.Provisioned(owner);
        }
        finally
        {
            servedOwners.ReleaseRosterPublication();
        }
    }

    /// <summary>Puts a new label on an owner this deployment already holds.</summary>
    /// <param name="owner">The owner to relabel.</param>
    /// <param name="displayName">The label the owner is told apart by from now on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the relabel did: whether the deployment holds this owner at all, and the sentence naming what has to change first where it holds them and refused.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <remarks>
    /// The label is what an administrator selects an owner by and is keyed by nothing, so changing it moves no mail and
    /// invalidates no identifier — which is why this is the configuration grant rather than the erasing one. An owner a
    /// file declares is relabelled by that file at every start, so a rename written here for one of them lasts until
    /// the next; the label to change is the declaration's, and this reaches an owner nothing declares.
    /// </remarks>
    internal async Task<OwnerRelabelOutcome> RelabelAsync(
        MailOwnerId owner,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An owner is relabelled for a named owner.", nameof(owner));
        }

        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (FindLabelRefusal(displayName) is { } unusable)
        {
            return OwnerRelabelOutcome.Refused(unusable);
        }

        var label = displayName!.Trim();
        var held = await directory.ReadOwnersAsync(DeclaredOwners.MaximumDeclaredOwners + 1, cancellationToken);

        if (held.All(record => record.Owner != owner))
        {
            return OwnerRelabelOutcome.NoSuchOwner;
        }

        if (held.Any(record => record.Owner != owner && StringComparer.Ordinal.Equals(record.DisplayName, label)))
        {
            return OwnerRelabelOutcome.Refused(LabelTaken(label));
        }

        if (!await provisioning.RelabelAsync(owner, label, cancellationToken))
        {
            // The label was taken between the roster being read and the statement reaching the table, which no reading
            // of a snapshot could have refused earlier.
            return OwnerRelabelOutcome.Refused(LabelTaken(label));
        }

        this.LogOwnerRelabelled();

        return OwnerRelabelOutcome.Relabelled;
    }

    /// <summary>Erases one owner and everything this deployment recorded for them.</summary>
    /// <param name="owner">The owner to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns>What was removed, and whether this process was serving the person it removed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// Whether the owner was served is read before the erasure rather than after, because the answer must describe the
    /// deployment the caller asked about rather than the roster the erasure left.
    /// </para>
    /// <para>
    /// An owner a configuration source names is refused rather than erased. The next start reconciles the declarations
    /// against the roster and writes back every declared owner the roster no longer holds, under the identifier the
    /// declaration carries and with the mail accounts it supplies — so the erasure would run, the mail would go, and
    /// the person would be recreated and their mailboxes downloaded again. A deletion request answered that way is
    /// worse than one refused, so what comes back names the declaration to remove first.
    /// </para>
    /// </remarks>
    internal async Task<OwnerErasureOutcome> EraseAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An owner is erased for a named owner.", nameof(owner));
        }

        authorization.RequirePermission(MailFathomPermission.AdminErase);

        if (configured.DeclaredByAConfigurationSource(owner))
        {
            var served = servedOwners.Owners.Any(candidate => candidate.Owner == owner);
            return new OwnerErasureOutcome(OwnerErased: false, served, DeclaredElsewhere);
        }

        await servedOwners.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            var served = servedOwners.Owners.Any(candidate => candidate.Owner == owner);
            var erased = await erasure.EraseAsync(owner, cancellationToken);

            if (erased)
            {
                servedOwners.OwnerErased(owner);
                this.LogOwnerErased(served);
            }

            return new OwnerErasureOutcome(erased, served);
        }
        finally
        {
            servedOwners.ReleaseRosterPublication();
        }
    }

    /// <summary>Says why a label cannot be an owner's, or nothing when it can.</summary>
    /// <remarks>The rules the column and the declared collection are held to, asked here so a label refused in a file is refused over a route.</remarks>
    private static string? FindLabelRefusal(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "An owner is recorded with the label an administrator tells them apart by. Write one, unique across this deployment.";
        }

        var label = displayName.Trim();

        return label.Length > MailOwnerRecord.MaximumDisplayNameLength
            ? $"The label is {label.Length} characters, past the {MailOwnerRecord.MaximumDisplayNameLength} an owner's label is stored as. Shorten it."
            : null;
    }

    /// <summary>The sentence an erasure a start would undo is refused with.</summary>
    /// <remarks>
    /// It names both shapes a declaration takes, because which one an operator has is decided by their own file rather
    /// than by anything this deployment could report without publishing that file back to them.
    /// </remarks>
    private const string DeclaredElsewhere =
        "A configuration source declares this owner, and a start writes every declared owner it no longer holds back into the roster — so erasing them here would destroy their mail and then recreate the person and download it again. Remove their entry from the top-level Accounts collection, or the mail accounts of MailSynchronization:Accounts where this deployment declares no owners, and erase them once no source names them.";

    private static string LabelTaken(string label) =>
        $"Another owner of this deployment is already recorded as '{label}'. A label is what an administrator selects an owner by, so two owners carrying one would leave nothing to select on: choose another.";

    /// <remarks>The label rather than the identifier, because it is the operator's own text and the identifier is a generated handle for a person.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An owner labelled {OwnerDisplayName} was recorded. Their mail accounts are read from their own record; no configuration source reaches them.")]
    private partial void LogOwnerProvisioned(string ownerDisplayName);

    /// <remarks>
    /// Neither label is written down, which is the opposite of the line above and deliberate: recording somebody is a
    /// deployment gaining a person, and the label is how an operator then finds the owner the line is about, while
    /// renaming one would put two of that person's names in a record outliving the reason either was chosen.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An owner this deployment holds was relabelled. Which owner, and under what label, is read from the roster rather than from here.")]
    private partial void LogOwnerRelabelled();

    /// <remarks>The record names no owner at all: a person's whole record was disposed of, and a log line naming them would outlive the erasure it reports.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An owner and everything recorded for them were erased. This process was serving them: {WasServed}. The runtime roster now excludes them.")]
    private partial void LogOwnerErased(bool wasServed);
}
