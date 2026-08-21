// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>The one set of port readers a snapshot of the mail section is read through.</summary>
/// <remarks>
/// <para>
/// One instance belongs to one <see cref="MailSynchronizationOptions" />, which is what a reload replaces, so a reader
/// here is built once per snapshot and shared by every scope that runs against it. That lifetime is the point: three
/// of the readers memoize a per-account map, and each of those maps walks every account and every account's own
/// addresses — work a per-scope reader would repeat on every work unit and every message.
/// </para>
/// <para>
/// The readers are constructed together and their maps are not, so resolving any one port stays as cheap as it was
/// while each map is still built by the first lookup that needs it.
/// </para>
/// </remarks>
/// <param name="settings">The snapshot every reader here reads.</param>
internal sealed class MailSynchronizationSettingsReaders(MailSynchronizationOptions settings)
{
    /// <summary>Gets the transport security an account connects and submits under.</summary>
    internal ConfiguredMailTransportSecurityPolicyReader TransportSecurityPolicies { get; } = new(settings);

    /// <summary>Gets the stretch of time an account is synchronized over.</summary>
    internal ConfiguredMailSynchronizationWindowReader SynchronizationWindows { get; } = new(settings);

    /// <summary>Gets what becomes of a stored email the server no longer holds.</summary>
    internal ConfiguredRemotelyDeletedEmailDispositionReader RemotelyDeletedEmailDispositions { get; } = new(settings);

    /// <summary>Gets what a delete this deployment itself authors does to the message.</summary>
    internal ConfiguredAuthoredDeleteEmailDispositionReader AuthoredDeleteEmailDispositions { get; } = new(settings);

    /// <summary>Gets which rule actions an operator admitted on an account.</summary>
    internal ConfiguredMailRuleActionPermissionReader RuleActionPermissions { get; } = new(settings);

    /// <summary>Gets whether a mailbox mutation is recorded, and for how long.</summary>
    internal ConfiguredMailboxMutationAuditSettingsReader MutationAuditSettings { get; } = new(settings);

    /// <summary>Gets whether an answered question is recorded, and for how long.</summary>
    internal ConfiguredMailAnsweringAuditSettingsReader AnsweringAuditSettings { get; } = new(settings);

    /// <summary>Gets the accounts this deployment serves.</summary>
    internal ConfiguredMailAccountCatalog AccountCatalog { get; } = new(settings);

    /// <summary>Gets which authentication results an account believes.</summary>
    internal ConfiguredTrustedAuthenticationAuthorityReader TrustedAuthenticationAuthorities { get; } = new(settings);

    /// <summary>Gets which senders an account recognizes.</summary>
    internal ConfiguredSenderTrustPolicyReader SenderTrustPolicies { get; } = new(settings);

    /// <summary>Gets the mailbox an account sends as.</summary>
    internal ConfiguredOutgoingSenderIdentityReader OutgoingSenderIdentities { get; } = new(settings);

    /// <summary>Gets whether a sent message is filed back into the account's own mailbox.</summary>
    internal ConfiguredOutgoingMailFilingPolicyReader OutgoingMailFilingPolicies { get; } = new(settings);

    /// <summary>Gets what each mapped folder takes part in.</summary>
    internal ConfiguredMailFolderParticipationReader FolderParticipation { get; } = new(settings);

    /// <summary>Gets which folders an operator mapped to the junk role.</summary>
    internal ConfiguredJunkMailFolderCatalog JunkFolderCatalog { get; } = new(settings);

    /// <summary>Gets one account's folder mappings.</summary>
    internal ConfiguredMailFolderMappingReader FolderMappings { get; } = new(settings);

    /// <summary>Gets what an account collects contacts from, and which correspondents it leaves out.</summary>
    internal ConfiguredContactCollectionSettingsReader ContactCollection { get; } = new(settings);
}
