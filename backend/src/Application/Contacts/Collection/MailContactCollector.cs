// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Contacts.Collection;

/// <summary>Records the people an account corresponds with, out of the mail synchronization has just committed.</summary>
/// <remarks>
/// <para>
/// This runs inside the pass that stored the message and owns no worker, timer, or queue: the headers are already read
/// and the transaction has already committed, so what is left is a bounded number of indexed reads and, rarely, one
/// insert. It reaches the mail server for nothing at all, which is what keeps it out of the fetch it runs behind.
/// </para>
/// <para>
/// <b>Which addresses are considered is decided by the folder the message arrived in.</b> A message in an ordinary
/// folder is somebody writing to the owner, so its author — the <c>From</c> header — is the candidate. A message in the
/// folder mapped as <see cref="MailFolderSpecialUse.Sent" /> is the owner writing to somebody, so its primary
/// recipients — the <c>To</c> header — are. Drafts are unsent and say nothing about correspondence; junk and trash say
/// the opposite of what a book is for. No other header is ever read: <c>Cc</c> and <c>Bcc</c> are the copied recipients
/// of somebody else's thread, and <c>Sender</c> and <c>Reply-To</c> name where a message was submitted from and where a
/// reply is to go rather than who the correspondent is.
/// </para>
/// <para>
/// <b>The two directions are held to different evidence, deliberately.</b> An address that wrote to the owner is
/// recorded once it has written <see cref="ContactCollectionSettings.MinimumMessagesFromSender" /> times, because one
/// message from a stranger is not correspondence. An address the owner wrote to is recorded at once, because the owner
/// having addressed somebody is exactly the evidence a count of their messages is standing in for.
/// </para>
/// <para>
/// <b>An address the book already holds is left alone</b> — under either origin and without collection ever reading the
/// record. That is the collision rule the issue behind this feature asked for, and it is a refusal rather than a merge:
/// an address that turns out to belong to somebody the owner asserted is already answered for by that record, and
/// adding it there would be collection editing what an owner wrote down. An owner who wants the address on that person
/// puts it there themselves, which is one amendment rather than a rule guessing on their behalf.
/// </para>
/// <para>
/// Nothing here logs, and nothing it hands to an instrument names a person. What it reports is which of six conclusions
/// it reached, and the identities involved stay between the message and the book.
/// </para>
/// </remarks>
public sealed class MailContactCollector
{
    /// <summary>How many primary recipients a message the owner sent may name and still be read as correspondence.</summary>
    /// <remarks>
    /// A letter is addressed to the few people it concerns and an announcement to everybody, and the count is what tells
    /// them apart without reading a word of either. The bound is also what keeps one message from deciding how much work
    /// this pass does: past it the message contributes nothing rather than its first few recipients, because a
    /// truncation would record whoever the sender happened to write first.
    /// </remarks>
    public const int MaximumRecipientsCollected = 16;

    private readonly ContactBook book;
    private readonly IContactCollectionSettingsReader settingsReader;
    private readonly IAuthoredMailTally authoredMail;
    private readonly IContactCollectionTelemetry telemetry;

    /// <summary>Initializes collection over the book it writes to and the evidence it reads.</summary>
    /// <param name="book">Answers whether an address is already held, and records the ones that are not.</param>
    /// <param name="settingsReader">Answers what the account being synchronized collects.</param>
    /// <param name="authoredMail">Counts how much of the account's mail one address wrote.</param>
    /// <param name="telemetry">Reports what was concluded, without reporting about whom.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailContactCollector(
        ContactBook book,
        IContactCollectionSettingsReader settingsReader,
        IAuthoredMailTally authoredMail,
        IContactCollectionTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(authoredMail);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.book = book;
        this.settingsReader = settingsReader;
        this.authoredMail = authoredMail;
        this.telemetry = telemetry;
    }

    /// <summary>Opens what one synchronization run of one folder collects under.</summary>
    /// <param name="account">The account being synchronized, whose settings size the run's bound.</param>
    /// <param name="folderRole">The role the folder plays, or <see langword="null" /> when configuration gave it none.</param>
    /// <returns>The run, which the pass then hands back with every message it commits.</returns>
    /// <remarks>
    /// The bound is read once per run rather than per message, so an operator raising it reaches the run after the one
    /// in flight. An account that collects nothing opens a run whose bound is zero, which costs nothing: the settings
    /// are read again per message and stop the work before the bound is ever asked.
    /// </remarks>
    public ContactCollectionRun OpenRun(MailAccountIdentity account, MailFolderSpecialUse? folderRole) =>
        new(
            account,
            folderRole,
            new ContactCollectionBudget(
                this.settingsReader.GetContactCollectionSettings(account.Id).MaxContactsPerRun));

    /// <summary>Records whoever one committed message says the account corresponds with.</summary>
    /// <param name="metadata">What was read out of the message that was just stored.</param>
    /// <param name="run">
    /// The account, the folder role, and the remaining bound of the run the message reached this pass in. The account
    /// is the run's rather than the message's, so it is what selects the collection settings and whose correspondents
    /// the addresses are recorded as — handing a message a run opened for another account attributes them to that one.
    /// </param>
    /// <param name="cancellationToken">Cancels the collection.</param>
    /// <returns>A task that completes once every candidate has been decided, or at once where the account collects nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <remarks>
    /// An account with collection switched off performs one property read and reaches neither the book nor the tally,
    /// which is the shape every switched-off feature in this system has.
    /// </remarks>
    public async Task CollectFromAsync(
        ExtractedEmailMetadata metadata,
        ContactCollectionRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(run);

        var account = run.Account;
        var settings = this.settingsReader.GetContactCollectionSettings(account.Id);

        if (!settings.IsEnabled || CollectedRoleIn(run.FolderRole) is not { } role)
        {
            return;
        }

        if (!settings.Policy.Admits(metadata.Automation))
        {
            this.telemetry.RecordOutcome(ContactCollectionOutcome.NotCorrespondence);

            return;
        }

        foreach (var address in CandidatesIn(metadata, role))
        {
            if (!await this.RecordAsync(account, address, role, settings, run.Budget, cancellationToken))
            {
                return;
            }
        }
    }

    /// <summary>Decides one candidate address, answering whether the message's remaining candidates are worth deciding.</summary>
    /// <remarks>
    /// The order the questions are asked in is their cost. The policy is held in memory, being already held is one
    /// indexed lookup, and the threshold is the only question that counts rows — so a correspondent the book already
    /// holds, which is the ordinary case once a book has filled, costs one lookup and nothing else.
    /// </remarks>
    private async Task<bool> RecordAsync(
        MailAccountIdentity account,
        EmailAddress address,
        EmailAddressRole role,
        ContactCollectionSettings settings,
        ContactCollectionBudget budget,
        CancellationToken cancellationToken)
    {
        if (!settings.Policy.Admits(address) || !TryReadName(address, out var displayName))
        {
            this.telemetry.RecordOutcome(ContactCollectionOutcome.Excluded);

            return true;
        }

        if (await this.book.HoldsAddressAsync(address, cancellationToken))
        {
            this.telemetry.RecordOutcome(ContactCollectionOutcome.AlreadyHeld);

            return true;
        }

        if (role == EmailAddressRole.From && !await this.HasWrittenOftenEnoughAsync(
            account,
            address,
            settings.MinimumMessagesFromSender,
            cancellationToken))
        {
            this.telemetry.RecordOutcome(ContactCollectionOutcome.BelowThreshold);

            return true;
        }

        if (!budget.TryClaim())
        {
            this.telemetry.RecordOutcome(ContactCollectionOutcome.RunBoundReached);

            return false;
        }

        var written = await this.book.CollectAsync(
            new NewContact
            {
                DisplayName = displayName,
                Addresses = [address],
                PreferredAddress = address,
                Origin = ContactOrigin.Collected,
            },
            cancellationToken);

        this.telemetry.RecordOutcome(written.Outcome == ContactWriteOutcome.Written
            ? ContactCollectionOutcome.Recorded
            : ContactCollectionOutcome.AlreadyHeld);

        return true;
    }

    /// <summary>Answers whether an address has written enough of this account's mail to be a correspondent.</summary>
    /// <remarks>
    /// A threshold of one is answered without reading anything, because the message that reached this pass is itself the
    /// first one and counting it would be asking the database to confirm what the caller is holding.
    /// </remarks>
    private async Task<bool> HasWrittenOftenEnoughAsync(
        MailAccountIdentity account,
        EmailAddress address,
        int minimumMessages,
        CancellationToken cancellationToken) =>
        minimumMessages <= 1
        || await this.authoredMail.CountMessagesAuthoredByAsync(
            account,
            address,
            minimumMessages,
            cancellationToken) >= minimumMessages;

    /// <summary>Names which header a message in this folder contributes, or nothing when the folder contributes none.</summary>
    private static EmailAddressRole? CollectedRoleIn(MailFolderSpecialUse? folderRole) => folderRole switch
    {
        MailFolderSpecialUse.Sent => EmailAddressRole.To,
        MailFolderSpecialUse.Drafts or MailFolderSpecialUse.Junk or MailFolderSpecialUse.Trash => null,
        _ => EmailAddressRole.From,
    };

    /// <summary>Reads the distinct addresses one message wrote in the header this folder contributes.</summary>
    /// <remarks>
    /// Two spellings of one mailbox are one candidate, and the first spelling is the one kept, because that is the rule
    /// the book itself compares addresses by. A message naming more recipients than
    /// <see cref="MaximumRecipientsCollected" /> contributes none of them.
    /// </remarks>
    private static EmailAddress[] CandidatesIn(ExtractedEmailMetadata metadata, EmailAddressRole role)
    {
        var candidates = metadata.Participants
            .Where(participant => participant.Role == role)
            .Select(static participant => participant.Address)
            .Distinct()
            .ToArray();

        return role == EmailAddressRole.To && candidates.Length > MaximumRecipientsCollected ? [] : candidates;
    }

    /// <summary>Reads the name a collected contact carries, which is what the message wrote or else the address itself.</summary>
    /// <remarks>
    /// A collected record is named by the one thing the message offered, and a sender's spelling of somebody's name is
    /// exactly what a collected claim is: weaker than a name the owner wrote down, and replaced by one the moment they
    /// promote the record. Where the message wrote nothing usable the address stands in, so a contact always carries a
    /// name a reader can tell people apart by. An address too long to be a name and carrying none is not collected at
    /// all, because a record the book cannot name is one nobody could read.
    /// </remarks>
    private static bool TryReadName(EmailAddress address, out ContactDisplayName displayName)
    {
        foreach (var candidate in new[] { address.DisplayName, address.Address })
        {
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Trim().Length > ContactDisplayName.MaximumLength)
            {
                continue;
            }

            try
            {
                displayName = ContactDisplayName.Create(candidate);

                return true;
            }
            catch (ArgumentException)
            {
                // What one sender wrote as a name is not a value an owner typed, so a name this deployment refuses is
                // stepped over rather than reported: the address below it names the person just as well.
            }
        }

        displayName = default;

        return false;
    }
}
