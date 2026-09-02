// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Contacts.Collection;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures whether one account records the people it corresponds with, and under what bounds.</summary>
/// <remarks>
/// <para>
/// Off unless an owner switched it on, and switched on per account. Collection builds a record of who writes to its
/// owner — derived personal data about people who never dealt with MailFathom — so an instance nobody asked never
/// accumulates one, and a deployment that reads a work mailbox and a personal one decides separately for each.
/// </para>
/// <para>
/// Everything else here narrows what a switched-on account records. The two numbers bound who is written down and how
/// fast, and the exclusions are the owner's own list on top of the automated senders, role mailboxes, and mailing lists
/// every deployment excludes without being asked.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ContactCollectionOptions
{
    /// <summary>The fewest messages a threshold may ask for, which records a correspondent on first sight.</summary>
    internal const int MinimumMessageThreshold = 1;

    /// <summary>The most messages a threshold may ask for.</summary>
    /// <remarks>
    /// Far above any answer worth giving, and bounded because the threshold is what one indexed count per unknown
    /// address stops at: a deployment asking for a thousand would be asking every message about an address nobody will
    /// ever be recorded from.
    /// </remarks>
    internal const int MaximumMessageThreshold = 100;

    /// <summary>The most contacts one synchronization run may record.</summary>
    internal const int MaximumContactsPerRun = 1000;

    /// <summary>Gets or sets whether this account records anybody at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many messages an address must have written before the person behind it is recorded.</summary>
    /// <remarks>
    /// The default of two is the difference between a book of correspondents and a list of everyone who has ever mailed
    /// the account: one message from a stranger is not correspondence, and a second is. A value of one records every
    /// admitted sender on first sight, which is a deliberate choice rather than the absence of one. It bounds only the
    /// addresses that wrote to the owner — an address the owner themselves wrote to is recorded at once.
    /// </remarks>
    public int MinimumMessagesFromSender { get; set; } = 2;

    /// <summary>Gets or sets how many contacts one folder synchronization run may record.</summary>
    /// <remarks>
    /// What paces the first synchronization of a mailbox holding years of mail, where every message is new and the book
    /// would otherwise gain thousands of people in one pass. A run that reaches the bound leaves the rest for the next
    /// one, which meets the same senders again and finds the evidence they need still standing. Zero records nobody
    /// while leaving collection switched on, which is a way to see what a policy would do without writing anything.
    /// </remarks>
    public int MaxContactsPerRun { get; set; } = 50;

    /// <summary>Gets or sets the addresses and domains this account never records a contact from.</summary>
    /// <remarks>
    /// The owner's half of the bounds. The structural half — role mailboxes, no-reply names, list administration, and a
    /// message a mailing list or an automatic responder stamped as its own — needs no entry here and cannot be switched
    /// off, because it names what nobody corresponds with rather than what one owner would rather not keep.
    /// </remarks>
    public List<ContactCollectionExclusionOptions> Exclusions { get; set; } = [];

    /// <summary>Gets the configured entries as the values collection holds an address against, dropping the unusable ones.</summary>
    /// <remarks>
    /// An unusable entry is skipped here rather than raised over, because startup validation refuses that configuration
    /// and a reload being rejected must not make an arriving message throw. What that costs is one entry, and the cost
    /// is in the unsafe direction — an exclusion nobody could read excludes nobody — which is exactly why the same
    /// configuration fails startup rather than being left to this.
    /// </remarks>
    internal IReadOnlyList<ContactCollectionExclusion> ConfiguredExclusions =>
    [
        .. (this.Exclusions ?? [])
            .Select(static configured => configured.TryCreateExclusion(out var exclusion) ? exclusion : null)
            .OfType<ContactCollectionExclusion>(),
    ];
}
