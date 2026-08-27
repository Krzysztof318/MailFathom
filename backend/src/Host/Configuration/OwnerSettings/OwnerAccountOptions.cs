// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>One owner's configurable record, as the document their <c>settings_accounts</c> row holds.</summary>
/// <remarks>
/// <para>
/// This is the typed shape of a single element of the top-level <c>Accounts</c> collection, which is the collection of
/// owner accounts and deliberately not <c>MailSynchronization:Accounts</c>. One owner is one document and one row,
/// whichever mailboxes they own, so declaring a second mailbox for somebody adds an entry here rather than a row to
/// the table.
/// </para>
/// <para>
/// What belongs in it is whatever is that owner's own rather than the deployment's. Today that is their mail-account
/// declarations; the mail rules, the trusted senders, and the scanning and spam postures join them as each moves out
/// of the deployment's section, and each arrives as a property here rather than as a second document. What never
/// belongs in it is a value the deployment set: there is no owner configuration layer, so nothing here shadows a
/// deployment setting, and a property that would need to is a deployment setting somebody put in the wrong document.
/// </para>
/// <para>
/// The envelope is not repeated here. The owner's identifier, the label they are told apart by, the version, and the
/// marker saying whether this document has ever been written are relational columns, because authenticating a request
/// and joining an owner's mail must never depend on reading a document.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when an owner's document is read.")]
internal sealed class OwnerAccountOptions : IValidatableObject
{
    /// <summary>Gets or sets the mail accounts this owner owns, which may be none.</summary>
    /// <remarks>
    /// Zero is an ordinary state rather than an unfinished one: an owner is provisioned before their first mailbox is
    /// declared, and one whose last mailbox is withdrawn is still an owner. It is named for what it holds rather than
    /// repeating the word the collection above already carries, so a path reads <c>Accounts:…:MailAccounts:0</c> and
    /// says which of the two collections each segment is.
    /// </remarks>
    public List<MailSynchronizationAccountOptions> MailAccounts { get; set; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.FindRefusals();

    /// <summary>Judges this record by every rule a mail account is declared under.</summary>
    /// <returns>One result per refusal, empty when the record could be this owner's.</returns>
    /// <remarks>
    /// Each account is judged as one that will be synchronized, which is the strict reading, because a declaration in
    /// an owner's record is an account they own rather than one a deployment-wide switch left unread. The naming space
    /// is judged within this owner alone, which is what makes two owners each declaring <c>work</c> an ordinary pair
    /// of records rather than a collision.
    /// </remarks>
    internal IEnumerable<ValidationResult> FindRefusals()
    {
        if (this.MailAccounts is null)
        {
            return [new ValidationResult("An owner's mail accounts must be a list.", [nameof(this.MailAccounts)])];
        }

        return
        [
            .. MailAccountNamingSpace.FindCollisions(this.MailAccounts, nameof(this.MailAccounts)),
            .. this.MailAccounts.SelectMany(account => account.ValidateForSynchronization(synchronizationEnabled: true)),
        ];
    }
}
