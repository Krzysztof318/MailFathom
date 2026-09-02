// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration;

/// <summary>Where a deployment answers, relative to the address the operator gave.</summary>
/// <remarks>
/// The command is configured with a host and a port and appends the rest, so these paths are the whole of what it
/// assumes about the other side. Stated together rather than beside the code that calls each, because they are one
/// agreement with the service: the deployment publishes its administrative routes beneath the prefix and refuses to
/// start unless its resource identifier names that same prefix, which is what puts the metadata document exactly where
/// RFC 9728 says to look for it.
/// </remarks>
internal static class AdminEndpointRoutes
{
    /// <summary>The prefix every administrative route is served beneath.</summary>
    internal const string Prefix = "/api/admin";

    /// <summary>Where a deployment reports who a presented credential makes the caller.</summary>
    internal const string SessionPath = $"{Prefix}/session";

    /// <summary>Where a deployment accepts the refresh token it should keep for one of its mail accounts.</summary>
    internal const string MailboxRefreshTokenPath = $"{Prefix}/mailbox/refresh-token";

    /// <summary>Where a deployment reports what its mail synchronization is doing, account by account and folder by folder.</summary>
    internal const string MailboxSynchronizationPath = $"{Prefix}/mailbox/synchronization";

    /// <summary>Where the cost of discarding an account's synchronization progress is read, and where it is discarded.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and performed with <c>POST</c>, which is what keeps the figure an operator
    /// confirms and the figure the deployment acts on the same figure rather than two counts that happen to agree.
    /// </remarks>
    internal const string MailboxRewindPath = $"{Prefix}/mailbox/rewind";

    /// <summary>Where a deployment is asked to re-read one bounded pass of the raw MIME it already stores.</summary>
    /// <remarks>
    /// One pass per request, so the command repeats it until nothing is left. That is what makes a re-derivation the
    /// operator interrupted resumable: what a batch committed stays committed, and the next invocation continues from
    /// there rather than starting the scope over.
    /// </remarks>
    internal const string MailboxRederivationPath = $"{Prefix}/mailbox/rederivation";

    /// <summary>Where the move of already-stored content into the object backend is asked for, and where it is read.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and asked for with <c>POST</c>, for the reason the rewind's is: what the reading
    /// reports is the move the write asked for, and an operator who started one comes back to the same place to find out
    /// where it has got to. The reading also answers on a deployment that moves nothing, because how much content its
    /// database holds is the figure it weighs before selecting the other backend at all.
    /// </remarks>
    internal const string ContentMovePath = $"{Prefix}/content/move";

    /// <summary>Where a move under way is stopped.</summary>
    internal const string ContentMovePausePath = $"{ContentMovePath}/pause";

    /// <summary>Where a stopped move is set going again.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the request, because pausing and resuming are opposite decisions and a
    /// body carrying which one was meant would make a mistyped value the difference between the two.
    /// </remarks>
    internal const string ContentMoveResumePath = $"{ContentMovePath}/resume";

    /// <summary>Where the database copies a move left beside its objects are read, and where they are freed.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the move's, because the two are different acts under different grants:
    /// copying mail into a bucket is work, and removing the last copy of it outside one is disposal. One path read with
    /// <c>GET</c> and performed with <c>POST</c>, so the figure an operator confirms is the figure the deployment acts
    /// on. One request frees one bounded batch and the command repeats it, which is what makes an interrupted release
    /// resumable.
    /// </remarks>
    internal const string ContentReleasePath = $"{Prefix}/content/release";

    /// <summary>Where a deployment reports whether semantic search is working and how far behind it is.</summary>
    internal const string EmbeddingStatusPath = $"{Prefix}/embeddings";

    /// <summary>Where a deployment reports what activating its declaration would cost, and where that activation is performed.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and performed with <c>POST</c>, which is what keeps the figure an operator confirms
    /// and the figure the deployment weighs the same figure rather than two counts that happen to agree.
    /// </remarks>
    internal const string EmbeddingActivationPath = $"{Prefix}/embeddings/activation";

    /// <summary>Where a deployment is asked to stop the reindex it has under way.</summary>
    internal const string EmbeddingReindexCancellationPath = $"{Prefix}/embeddings/reindex/cancellation";

    /// <summary>Where a deployment reports the mail rules it has loaded, and whether its rule file was accepted.</summary>
    internal const string RulesPath = $"{Prefix}/rules";

    /// <summary>Where a whole-mailbox rule run is asked for, and where the one an account has is read.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and asked for with <c>POST</c>, which is what keeps the run an operator started and
    /// the run they come back to watch the same run rather than two answers that happen to agree.
    /// </remarks>
    internal const string RuleRunsPath = $"{Prefix}/rules/runs";

    /// <summary>Where a deployment reports what its rules concluded about the mail they were run over.</summary>
    internal const string RuleHistoryPath = $"{Prefix}/rules/history";

    /// <summary>Where a whole-mailbox classification run is asked for, and where the one an account has is read.</summary>
    /// <remarks>One path read with <c>GET</c> and asked for with <c>POST</c>, for the reason the rule runs path is.</remarks>
    internal const string SpamClassificationRunsPath = $"{Prefix}/spam/runs";

    /// <summary>Where a deployment reports what classification concluded about an account's mail.</summary>
    internal const string SpamClassificationsPath = $"{Prefix}/spam/classifications";

    /// <summary>Where a deployment reports the background work it will not attempt again.</summary>
    internal const string JobDeadLettersPath = $"{Prefix}/jobs/dead-letters";

    /// <summary>Where one dead letter is asked to be run again, under the identity it already carries.</summary>
    internal const string JobRetryPath = $"{JobDeadLettersPath}/retry";

    /// <summary>Where one dead letter is recorded as work that will never be run.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the retry request, because the two are opposite decisions and a body
    /// that carried which one was meant would make a mistyped value the difference between running somebody's work
    /// again and writing it off.
    /// </remarks>
    internal const string JobDropPath = $"{JobDeadLettersPath}/drop";

    /// <summary>Where one page of what a deployment has been asked to send is read.</summary>
    internal const string OutboxPath = $"{Prefix}/outbox";

    /// <summary>Where the counts by stage are read.</summary>
    /// <remarks>
    /// A literal segment where the single-send path takes an identifier, which a deployment's routing prefers over a
    /// parameter, so the two cannot be confused. It answers one figure per stage rather than a page, which is why it is
    /// a path of its own rather than a filter on the listing.
    /// </remarks>
    internal const string OutboxSummaryPath = $"{OutboxPath}/summary";

    /// <summary>Where one send is withdrawn before it leaves.</summary>
    internal const string OutboxCancellationPath = $"{OutboxPath}/cancellation";

    /// <summary>Where one send is put back for another attempt.</summary>
    /// <remarks>
    /// A path of its own rather than a field on the cancellation request, because the two are opposite decisions and a
    /// body carrying which one was meant would make a mistyped value the difference between withdrawing a message and
    /// sending it a second time.
    /// </remarks>
    internal const string OutboxRequeuePath = $"{OutboxPath}/requeue";

    /// <summary>Where a deployment is asked to erase one bounded pass of a folder's stored mail.</summary>
    /// <remarks>
    /// One pass per request, so the command repeats it until the folder is empty. That is what makes an erasure the
    /// operator interrupted resumable: what a pass committed stays committed, and the next invocation continues from
    /// there rather than starting a folder over.
    /// </remarks>
    internal const string FolderErasurePath = $"{Prefix}/folders/erasure";

    /// <summary>Where a deployment's contact book is listed and where a person is recorded in it.</summary>
    internal const string ContactsPath = $"{Prefix}/contacts";

    /// <summary>Where the person behind one address is read.</summary>
    /// <remarks>
    /// A path of its own rather than a filter on the listing, because it answers with one person rather than a page. The
    /// segment is not a UUID, so nothing can confuse it with the path one contact is read at.
    /// </remarks>
    internal const string ContactByAddressPath = $"{ContactsPath}/by-address";

    /// <summary>Where the whole collected half of a deployment's book is erased.</summary>
    /// <remarks>
    /// A literal segment where the single-contact path takes an identifier, which a deployment's routing prefers over a
    /// parameter, so the two cannot be confused. It names the origin rather than an action because what is being
    /// disposed of is the half of the book the owner did not write.
    /// </remarks>
    internal const string CollectedContactsPath = $"{ContactsPath}/collected";

    /// <summary>Where one recorded send is read, with what each of its recipients was told.</summary>
    /// <param name="outgoingEmailId">The send the path names.</param>
    /// <returns>The path, with the identity written the way a deployment's route constraint reads one.</returns>
    internal static string OutboxSendPath(Guid outgoingEmailId) => $"{OutboxPath}/{outgoingEmailId:D}";

    /// <summary>Where one contact is read, amended, and erased.</summary>
    /// <param name="contactId">The contact the path names.</param>
    /// <returns>The path, with the identity written the way a deployment's route constraint reads one.</returns>
    internal static string ContactPath(Guid contactId) => $"{ContactsPath}/{contactId:D}";

    /// <summary>Where a collected contact is promoted to one the owner has taken responsibility for.</summary>
    /// <param name="contactId">The contact the path names.</param>
    /// <returns>The path.</returns>
    /// <remarks>
    /// A path of its own rather than a field on the amendment, because promotion is the one act that changes an origin
    /// and a body carrying which act was meant would make a mistyped value the difference between correcting a record
    /// and taking it on.
    /// </remarks>
    internal static string ContactPromotionPath(Guid contactId) => $"{ContactPath(contactId)}/promotion";

    /// <summary>Where everything the deployment holds about one person is exported from.</summary>
    /// <param name="contactId">The contact the path names.</param>
    /// <returns>The path.</returns>
    internal static string ContactExportPath(Guid contactId) => $"{ContactPath(contactId)}/export";

    /// <summary>Where a deployment's settings are read with the layer each value came from, and where a keyed change is written.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and written with <c>POST</c>, which is what keeps the value an operator was shown
    /// and the value a write is judged against the same reading rather than two that happen to agree.
    /// </remarks>
    internal const string ConfigurationPath = $"{Prefix}/configuration";

    /// <summary>Where the persisted configuration document is read whole, and where an edited one is saved back.</summary>
    /// <remarks>
    /// A path of its own rather than a shape on the one above, because the two are different transactions: that one
    /// names the settings it changes, and this one carries the whole document and is judged against the version it was
    /// opened over.
    /// </remarks>
    internal const string ConfigurationDocumentPath = $"{ConfigurationPath}/document";

    /// <summary>Where an adoption of the deployment's file-decided settings is previewed, and where it is performed.</summary>
    internal const string ConfigurationAdoptionPath = $"{ConfigurationPath}/adoption";

    /// <summary>Where the owners a deployment holds records for are listed, and where one is recorded.</summary>
    /// <remarks>
    /// The listing every owner-scoped path below is composed from: an administrator selects an owner before doing
    /// anything else, and a generated identifier is the only handle either side has for one. A deployment serving one
    /// person answers with one entry, which is what lets a command act without asking which owner was meant.
    /// </remarks>
    internal const string OwnersPath = $"{Prefix}/owners";

    /// <summary>Where one owner and everything the deployment recorded for them are erased.</summary>
    /// <param name="ownerId">The owner the path names.</param>
    /// <returns>The path, with the identity written the way a deployment's route constraint reads one.</returns>
    internal static string OwnerPath(Guid ownerId) => $"{OwnersPath}/{ownerId:D}";

    /// <summary>Where the label one owner is told apart by is replaced.</summary>
    /// <param name="ownerId">The owner the path names.</param>
    /// <returns>The path.</returns>
    internal static string OwnerDisplayNamePath(Guid ownerId) => $"{OwnerPath(ownerId)}/display-name";

    /// <summary>Where one owner's record is read whole, and where an edited one is saved back.</summary>
    /// <param name="ownerId">The owner the path names.</param>
    /// <returns>The path.</returns>
    internal static string OwnerRecordPath(Guid ownerId) => $"{OwnerPath(ownerId)}/record";

    /// <summary>Where one mail account is declared into an owner's record.</summary>
    /// <param name="ownerId">The owner the mailbox belongs to.</param>
    /// <returns>The path.</returns>
    internal static string OwnerMailAccountsPath(Guid ownerId) => $"{OwnerRecordPath(ownerId)}/mail-accounts";

    /// <summary>Where one mail account is withdrawn from an owner's record.</summary>
    /// <param name="ownerId">The owner the mailbox belongs to.</param>
    /// <returns>The path.</returns>
    /// <remarks>A path of its own carrying the identifier in the body, because an account is named by something its owner chose: a dot or a space in one would decide whether the route matched at all.</remarks>
    internal static string OwnerMailAccountRemovalPath(Guid ownerId) =>
        $"{OwnerMailAccountsPath(ownerId)}/removal";

    /// <summary>Where one owner's adoption is previewed, and where it is performed.</summary>
    /// <param name="ownerId">The owner the path names.</param>
    /// <returns>The path.</returns>
    internal static string OwnerAdoptionPath(Guid ownerId) => $"{OwnerRecordPath(ownerId)}/adoption";

    /// <summary>Where one owner's credentials are listed and provisioned, whichever method each is presented by.</summary>
    /// <param name="ownerId">The owner the path names.</param>
    /// <returns>The path, with the identity written the way a deployment's route constraint reads one.</returns>
    internal static string OwnerCredentialsPath(Guid ownerId) => $"{OwnerPath(ownerId)}/credentials";

    /// <summary>Where one credential is removed.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential the path names.</param>
    /// <returns>The path.</returns>
    /// <remarks>The owner is in the path as well as the credential, because that is what the deployment's own contract asks for: an identifier copied out of the wrong listing is refused rather than acted on.</remarks>
    internal static string OwnerCredentialPath(Guid ownerId, Guid credentialId) =>
        $"{OwnerCredentialsPath(ownerId)}/{credentialId:D}";

    /// <summary>Where what one credential is presented as is replaced.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential the path names.</param>
    /// <returns>The path.</returns>
    /// <remarks>A path of its own rather than a field on the credential, because replacing what a credential is presented as and suspending it are opposite decisions and a body carrying which was meant would make a mistyped value the difference between them.</remarks>
    internal static string OwnerCredentialMaterialPath(Guid ownerId, Guid credentialId) =>
        $"{OwnerCredentialPath(ownerId, credentialId)}/material";

    /// <summary>Where one credential is turned on or off.</summary>
    /// <param name="ownerId">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential the path names.</param>
    /// <returns>The path.</returns>
    internal static string OwnerCredentialEnablementPath(Guid ownerId, Guid credentialId) =>
        $"{OwnerCredentialPath(ownerId, credentialId)}/enablement";

    /// <summary>Where a deployment publishes the document naming its authorization servers, resource, and required scopes.</summary>
    /// <remarks>
    /// Composed rather than discovered from a challenge, because a client that knows which routes it is about to call
    /// already knows enough: RFC 9728 places the document under a well-known segment with the resource's path appended,
    /// and the deployment refuses to start unless its resource path is <see cref="Prefix" />. One request rather than
    /// two, and no dependence on the wording of a refusal.
    /// </remarks>
    internal const string ProtectedResourceMetadataPath = $"/.well-known/oauth-protected-resource{Prefix}";
}
