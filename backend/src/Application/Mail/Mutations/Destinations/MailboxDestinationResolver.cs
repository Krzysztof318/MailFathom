// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Destinations;

/// <summary>Turns what an author named as a mutation's destination into the folder the command will be issued against.</summary>
/// <remarks>
/// <para>
/// This is the single place a destination becomes a remote folder, so every author of a relocation or a copy reaches
/// the same folder by the same name and meets the same refusal. A rule is one such author and has no resolution of its
/// own.
/// </para>
/// <para>
/// The two kinds of destination are answered differently, and the difference is the whole of what this type adds. A
/// folder the account mirrors already has a binding, because the run that synchronizes it resolved the alias before it
/// read anything, so asking the server again would repoint a binding a checkpoint hangs off. A folder the account only
/// maps is never scheduled by a run, so nothing would ever bind it: it is resolved here, on demand, through the same
/// resolver, the same binding record, and the same mapping-change audit a mirrored folder uses. A server that has since
/// renamed the folder is followed exactly as it is for a mirrored folder — the binding is replaced by the next
/// generation, with no checkpoint to invalidate, because a folder nothing mirrors has none.
/// </para>
/// <para>
/// Every answer is remembered for the life of this instance, which is one account run. A batch of two hundred emails
/// matching one filing rule therefore costs one listing rather than two hundred, and the folder a pass began with is
/// the folder it finishes with.
/// </para>
/// </remarks>
public sealed class MailboxDestinationResolver
{
    private readonly MailFolderReferenceResolver folderReferences;
    private readonly IMailFolderResolutionStore folderResolutions;
    private readonly MailFolderResolver folderResolver;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies;

    private readonly Dictionary<(MailAccountIdentity Account, MailFolderReference Destination), MailboxDestinationResolution> answers = [];

    /// <summary>Initializes the resolver from the two ways a destination is turned into a folder.</summary>
    /// <param name="folderReferences">Turns the alias or the role an author named into the mapping of the account it means.</param>
    /// <param name="folderResolutions">Reads the binding a mirrored folder's run has already recorded.</param>
    /// <param name="folderResolver">Resolves a mapped folder against what its server advertises, and records the binding.</param>
    /// <param name="transportSecurityPolicies">Supplies the connection and authentication policy an on-demand resolution obeys.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailboxDestinationResolver(
        MailFolderReferenceResolver folderReferences,
        IMailFolderResolutionStore folderResolutions,
        MailFolderResolver folderResolver,
        IMailTransportSecurityPolicyReader transportSecurityPolicies)
    {
        ArgumentNullException.ThrowIfNull(folderReferences);
        ArgumentNullException.ThrowIfNull(folderResolutions);
        ArgumentNullException.ThrowIfNull(folderResolver);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicies);

        this.folderReferences = folderReferences;
        this.folderResolutions = folderResolutions;
        this.folderResolver = folderResolver;
        this.transportSecurityPolicies = transportSecurityPolicies;
    }

    /// <summary>Resolves every destination one batch of authored changes names.</summary>
    /// <param name="account">The account the changes are authored for.</param>
    /// <param name="destinations">What the authors named, in any order and with repetitions.</param>
    /// <param name="cancellationToken">Cancels the listing and the write that records a new binding.</param>
    /// <returns>One answer per distinct destination named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destinations" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer recorded a binding for the same alias first.</exception>
    /// <remarks>
    /// It must be awaited before the transaction the changes are written in is opened, because resolving a folder the
    /// account only maps reaches the mail server and records a binding in a session of its own.
    /// </remarks>
    public async Task<MailboxDestinations> ResolveAsync(
        MailAccountIdentity account,
        IEnumerable<MailFolderReference> destinations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        var named = destinations.Where(destination => destination.IsSpecified).Distinct().ToArray();
        var resolutions = new Dictionary<MailFolderReference, MailboxDestinationResolution>(named.Length);

        foreach (var destination in named)
        {
            resolutions[destination] = await this.ResolveOneAsync(account, destination, cancellationToken);
        }

        return new MailboxDestinations(resolutions);
    }

    /// <summary>Answers one destination, remembering the answer for the rest of this instance's life.</summary>
    /// <remarks>
    /// Keyed by the account as well as the destination, because one name means a different folder on each account and
    /// nothing in this type's contract says a scope holds one account's work.
    /// </remarks>
    private async Task<MailboxDestinationResolution> ResolveOneAsync(
        MailAccountIdentity account,
        MailFolderReference destination,
        CancellationToken cancellationToken)
    {
        if (this.answers.TryGetValue((account, destination), out var remembered))
        {
            return remembered;
        }

        var resolution = await this.ReadCurrentAsync(account, destination, cancellationToken);

        this.answers[(account, destination)] = resolution;

        return resolution;
    }

    /// <summary>Turns a destination into the folder it currently names, in the two steps a name goes through.</summary>
    /// <remarks>
    /// A role no folder of the account plays is caught rather than propagated, because it says the same thing to the
    /// author as an alias no mapping declares: the change has nowhere to file, the action is refused, and the rest of
    /// the batch keeps moving.
    /// </remarks>
    private async Task<MailboxDestinationResolution> ReadCurrentAsync(
        MailAccountIdentity account,
        MailFolderReference destination,
        CancellationToken cancellationToken)
    {
        MailFolderMapping? mapping;

        try
        {
            mapping = this.folderReferences.Resolve(account.Id, destination);
        }
        catch (MailFolderRoleUnmappedException)
        {
            return MailboxDestinationResolution.Unmapped();
        }

        if (mapping is null)
        {
            return MailboxDestinationResolution.Unmapped();
        }

        return mapping.Participation.IsSynchronized
            ? await this.ReadMirroredBindingAsync(account, mapping, cancellationToken)
            : await this.ResolveOnDemandAsync(account, mapping, cancellationToken);
    }

    /// <summary>Reads the binding the folder's own synchronization run recorded.</summary>
    private async Task<MailboxDestinationResolution> ReadMirroredBindingAsync(
        MailAccountIdentity account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken)
    {
        var binding = await this.folderResolutions.GetCurrentResolutionAsync(
            account,
            mapping.Alias,
            cancellationToken);

        return binding is not null
            ? MailboxDestinationResolution.Resolved(new MailboxDestination(binding, IsMirrored: true))
            : MailboxDestinationResolution.Unbound();
    }

    /// <summary>Resolves a folder no run schedules, at the moment it is needed as a destination.</summary>
    /// <remarks>
    /// A creation the server refused is reported as an unadvertised folder rather than raised, for the reason every
    /// refusal here is a result: the folder is not there either way, and letting it out would end a pass over one
    /// destination and end the next pass the same way, forever.
    /// </remarks>
    private async Task<MailboxDestinationResolution> ResolveOnDemandAsync(
        MailAccountIdentity account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken)
    {
        var transportSecurityPolicy = this.transportSecurityPolicies.GetPolicy(account.Id);

        MailFolderResolutionResult result;

        try
        {
            result = await this.folderResolver.ResolveAsync(
                account,
                mapping,
                transportSecurityPolicy,
                cancellationToken);
        }
        catch (RemoteFolderCreationRefusedException)
        {
            return MailboxDestinationResolution.NotAdvertised();
        }

        return result switch
        {
            { Outcome: MailFolderResolutionOutcome.Resolved, Resolution: { } binding } =>
                MailboxDestinationResolution.Resolved(new MailboxDestination(binding, IsMirrored: false)),
            { Outcome: MailFolderResolutionOutcome.AdvertisedFoldersAreAmbiguous } =>
                MailboxDestinationResolution.Ambiguous(),
            _ => MailboxDestinationResolution.NotAdvertised(),
        };
    }
}
