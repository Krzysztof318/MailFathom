// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.Spam;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>One user's configurable record, as the document their <c>settings_accounts</c> row holds.</summary>
/// <remarks>
/// <para>
/// This is the typed shape of what one user's row holds, and no configuration source states it: a user is recorded
/// rather than declared, so the document arrives from <c>settings_accounts</c> and from the commands that write it.
/// One user is one document and one row, whichever mailboxes they own, so recording a second mailbox for somebody adds
/// an entry here rather than a row to the table.
/// </para>
/// <para>
/// What belongs in it is whatever is that user's own rather than the deployment's. Today that is their mail-account
/// declarations, how their own mail is classified as spam, and what they ask to have it scanned for; the mail rules and
/// the trusted senders join them as each moves out of the deployment's section, and each arrives as a property here
/// rather than as a second document. What never belongs in it is a value the deployment set: there is no user
/// configuration layer, so nothing here shadows a deployment setting, and a property that would need to is a deployment
/// setting somebody put in the wrong document. The scanning block is the worked example of the difference — it can
/// switch a scanner on for this user's mail and can neither switch off what the deployment requires nor move the
/// analyzer the deployment stood up.
/// </para>
/// <para>
/// The envelope is not repeated here. The user's identifier, the label they are told apart by, the version, and the
/// marker saying whether this document has ever been written are relational columns, because authenticating a request
/// and joining a user's mail must never depend on reading a document.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class UserAccountOptions : IValidatableObject
{
    /// <summary>Gets or sets the mail accounts this user owns, which may be none.</summary>
    /// <remarks>
    /// Zero is an ordinary state rather than an unfinished one: a user is provisioned before their first mailbox is
    /// recorded, and one whose last mailbox is withdrawn is still a user. It is named for what it holds rather than
    /// for the row it hangs off, so a path within the record reads <c>MailAccounts:0</c> and says what each segment is
    /// without borrowing the word the deployment's own mail section carries.
    /// </remarks>
    public List<MailSynchronizationAccountOptions> MailAccounts { get; set; } = [];

    /// <summary>Gets or sets how this user's mail is classified as spam and what becomes of their junk.</summary>
    /// <remarks>
    /// Always present so that a record stating none of its keys still binds, and off in every switch, which is what
    /// makes classification something this user asked for rather than something a deployment did to their mailbox. It
    /// holds only the decisions that are about their own mail: the engine and what it costs stay the deployment's, and
    /// no key here shadows one of theirs.
    /// </remarks>
    public UserSpamClassificationOptions SpamClassification { get; set; } = new();

    /// <summary>Gets what this user asks to have their own mail scanned for, within what the deployment provides.</summary>
    /// <remarks>
    /// A record that says nothing here reads the deployment's own posture, which is what every user read before this
    /// block existed. What it may say is judged against the deployment rather than on its own, so the rule lives beside
    /// that section rather than in this type — <see cref="FindSensitiveContentErrors" /> is where it is asked.
    /// </remarks>
    public UserSensitiveContentOptions SensitiveContent { get; } = new();

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.FindRefusals();

    /// <summary>Judges this record by every rule it is written under that needs no clock.</summary>
    /// <returns>One result per refusal, empty when the record could be this user's.</returns>
    /// <remarks>
    /// <para>
    /// The mail-account rules are <see cref="UserMailAccountRules" />' rather than this type's, because the same
    /// declarations arrive here as a persisted record and arrive in the deployment's own file as a user's declared
    /// section, and a rule stated twice is a rule that comes to hold in one of the two places. The one rule that is not
    /// among them is the one that cannot be: <see cref="FindSynchronizationWindowErrors" /> asks a question about the
    /// current date, so it is supplied a clock by whoever runs it.
    /// </para>
    /// <para>
    /// The classification block is judged against this record's own mailboxes and against nothing else, which is what
    /// makes a scanned folder or a junk destination resolve within the user's accounts: a name only somebody else's
    /// account carries is refused here exactly as one nobody carries.
    /// </para>
    /// </remarks>
    internal IEnumerable<ValidationResult> FindRefusals() =>
        UserMailAccountRules.FindRefusals(this.MailAccounts, nameof(this.MailAccounts))
            .Concat(this.SpamClassification.FindRefusals(DeclaredMailAccounts.ReadFrom(this.MailAccounts)));

    /// <summary>Finds every declared earliest received date that could not mean anything on the supplied date.</summary>
    /// <param name="today">The current date the declared bounds are read against.</param>
    /// <returns>One result per account whose bound lies in the future, empty when every bound is usable.</returns>
    /// <remarks>
    /// The rule is the deployment's own, applied to a persisted record so that the two cannot drift apart: a future
    /// bound excludes every email the mailbox holds, which is indistinguishable from synchronization doing nothing,
    /// and a record that would be refused as configuration must not be accepted as a row.
    /// </remarks>
    internal IEnumerable<ValidationResult> FindSynchronizationWindowErrors(DateOnly today) =>
        UserMailAccountRules.FindSynchronizationWindowErrors(this.MailAccounts, today);

    /// <summary>Finds everything about the scanning block this deployment could not serve.</summary>
    /// <param name="deployment">The deployment's own <c>SensitiveContent</c> section, which a user may only tighten.</param>
    /// <returns>One result per refusal, empty when the block is one this deployment can serve.</returns>
    /// <remarks>
    /// Asked by whoever supplies the deployment's section, for the reason the synchronization window is asked that way:
    /// the answer depends on something outside the record, and a record judged by every rule but this one would accept
    /// a posture the composition could not honour.
    /// </remarks>
    internal IEnumerable<ValidationResult> FindSensitiveContentErrors(SensitiveContentOptions deployment) =>
        UserSensitiveContentRules
            .FindRefusals(this.SensitiveContent, deployment, UserSensitiveContentOptions.BlockName)
            .Select(refusal => new ValidationResult(refusal, [nameof(this.SensitiveContent)]));
}
