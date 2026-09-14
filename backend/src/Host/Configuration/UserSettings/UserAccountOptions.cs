// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;

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
/// What belongs in it is whatever is that person's own rather than the deployment's or a mailbox's. Today that is the
/// language this deployment writes for them in, the stored file they are drawn by, and which of the two mail-serving
/// endpoints they are served on; the mail rules and the trusted senders join them as each moves out of the deployment's
/// section, and each arrives as a property here rather than as a second document. What never belongs in it is a value
/// the deployment set: there is no user configuration layer, so nothing here shadows a deployment setting, and a
/// property that would need to is a deployment setting somebody put in the wrong document. What also never belongs in
/// it is a setting of a mailbox — how mail is classified as spam, what it is scanned for, which folders are mirrored —
/// because the mail is one copy and two people assigned one mailbox must not configure it apart; those are the account
/// record's, and a record naming one of them is refused as a property nothing binds.
/// </para>
/// <para>
/// The envelope is not repeated here. The user's identifier, the label they are told apart by, the version, and the
/// marker saying whether this document has ever been written are relational columns, because authenticating a request
/// and joining a user's mail must never depend on reading a document. The endpoint switches are the one value both
/// places hold: stated here so every record write reaches them, and copied onto the row by the commit that writes them.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class UserAccountOptions : IValidatableObject
{
    /// <summary>Every language this build writes in, as one phrase a refusal ends with.</summary>
    /// <remarks>Composed from the members rather than written out, so a third language added to the enumeration reaches both refusals below without either being edited.</remarks>
    private static readonly string PublishedLanguages =
        string.Join(" or ", Enum.GetValues<MailUserLanguage>().Select(language => $"'{language}'"));

    /// <summary>Gets or sets the language this deployment writes for this user in, named as it is spelled in English.</summary>
    /// <remarks>
    /// <para>
    /// The one property here a record being written must state. Everything else in this document is something a user
    /// asks for, and absence is the answer for somebody who asked for nothing; a language has no such absence, because
    /// a derivation written for them comes out in some language whether or not anybody chose it — and what it fell
    /// back to before this property existed was whatever language their mail happened to be in. A record already held
    /// is the one case the requirement is not asked of, for the reason <see cref="FindMissingLanguageError" /> gives.
    /// </para>
    /// <para>
    /// Carried as the written name rather than as the resolved value, because the binder that reads this document
    /// reports an unknown member as a refusal naming both values it takes, and a typed property would have made the
    /// same mistake a binding failure with the configuration binder's own sentence in place of that one.
    /// </para>
    /// </remarks>
    public string? Language { get; set; }

    /// <summary>Gets or sets the mail accounts this user owns, which may be none.</summary>
    /// <remarks>
    /// Zero is an ordinary state rather than an unfinished one: a user is provisioned before their first mailbox is
    /// recorded, and one whose last mailbox is withdrawn is still a user. It is named for what it holds rather than
    /// for the row it hangs off, so a path within the record reads <c>MailAccounts:0</c> and says what each segment is
    /// without borrowing the word the deployment's own mail section carries.
    /// </remarks>
    public List<MailSynchronizationAccountOptions> MailAccounts { get; set; } = [];

    /// <summary>Gets or sets the stored file this user is drawn by, or <see langword="null" /> for none.</summary>
    /// <remarks>
    /// A link rather than the picture: the octets are a stored file of this user's, and a write naming a file that is
    /// not theirs is refused where the record is committed, because only the database can say whose a file is.
    /// </remarks>
    public Guid? Portrait { get; set; }

    /// <summary>Gets which of the two mail-serving endpoints this user is served on.</summary>
    /// <remarks>
    /// Always present and on in both switches, so a record written before this block existed serves its user where it
    /// always did. The commit writes what it states onto the user's row as well, which is where a request reads it.
    /// </remarks>
    public UserEndpointAccessOptions EndpointAccess { get; } = new();

    /// <summary>Gets the language this record states, or <see langword="null" /> where it states none this build writes in.</summary>
    /// <remarks>
    /// Read only where the record has already been judged, which leaves exactly one way for this to answer
    /// <see langword="null" />: a record held from before the property existed, whose reader takes
    /// <see cref="MailUserLanguage.English" /> for it. The comparison is against the member names and is
    /// case-insensitive, so a record written by hand is read the way it was typed, and a number is not a language
    /// however well it would have parsed.
    /// </remarks>
    internal MailUserLanguage? ReadingLanguage => this.Language is { } written
        ? Enum.GetValues<MailUserLanguage>()
            .Where(language => string.Equals(language.ToString(), written, StringComparison.OrdinalIgnoreCase))
            .Select(language => (MailUserLanguage?)language)
            .FirstOrDefault()
        : null;

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
    /// </remarks>
    internal IEnumerable<ValidationResult> FindRefusals() =>
        this.FindUnwritableLanguageError()
            .Concat(UserMailAccountRules.FindRefusals(this.MailAccounts, nameof(this.MailAccounts)));

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

    /// <summary>Finds everything about this record's scanning blocks that this deployment could not serve.</summary>
    /// <param name="deployment">The deployment's own <c>SensitiveContent</c> section, which an account may only tighten.</param>
    /// <returns>One result per refusal, empty when every block is one this deployment can serve.</returns>
    /// <remarks>
    /// Asked by whoever supplies the deployment's section, for the reason the synchronization window is asked that way:
    /// the answer depends on something outside the record, and a record judged by every rule but this one would accept
    /// a posture the composition could not honour. One block per account rather than one for the record, because the
    /// mail each is about is the account's, and the refusal names the account's position so that whoever wrote it knows
    /// which mailbox the operator's switch was being asked about.
    /// </remarks>
    internal IEnumerable<ValidationResult> FindSensitiveContentErrors(SensitiveContentOptions deployment) =>
        this.MailAccounts.Index().SelectMany(entry => entry.Item.FindSensitiveContentErrors(
            deployment,
            $"{nameof(this.MailAccounts)}:{entry.Index.ToString(CultureInfo.InvariantCulture)}:{MailAccountSensitiveContentOptions.BlockName}"));

    /// <summary>Finds the refusal for a record stating a language this build does not write in.</summary>
    /// <returns>One result where <see cref="Language" /> names something no member does, empty where it names a member or nothing at all.</returns>
    /// <remarks>
    /// A value nobody writes in is wrong whichever direction the record arrived from, because no release ever accepted
    /// one: the property binds strictly, so a stored record naming <c>German</c> was never committed through this
    /// binder. The absence is the case that differs, and <see cref="FindMissingLanguageError" /> holds it.
    /// </remarks>
    private IEnumerable<ValidationResult> FindUnwritableLanguageError()
    {
        if (!string.IsNullOrWhiteSpace(this.Language) && this.ReadingLanguage is null)
        {
            yield return new ValidationResult(
                $"{nameof(this.Language)} states '{this.Language}', which is not a language MailFathom writes in. It takes {PublishedLanguages}.",
                [nameof(this.Language)]);
        }
    }

    /// <summary>Finds the refusal for a record that states no language at all.</summary>
    /// <returns>One result where <see cref="Language" /> is unstated, empty otherwise.</returns>
    /// <remarks>
    /// <para>
    /// Asked of a record being written and of no other, which is the second case <see cref="UserRecordArrival" />
    /// exists for and it is there rather than here. Every record committed before this property existed states no
    /// language, and this property binds strictly, so no administrator could have added one in advance — refusing
    /// those at the next start would refuse the start itself, for every user, through the surface they would have
    /// rewritten the record from. A held record stating nothing therefore reads as <see cref="MailUserLanguage.English" />,
    /// which is the answer every unresolved read already gives, and states a language the first time it is written.
    /// </para>
    /// <para>
    /// The sentence is worded apart from the one above because the administrator's next act differs: an absence is one
    /// line to add, and an unknown name is a value to correct. Both name every language this build writes in, so
    /// neither leaves anybody guessing at the spelling.
    /// </para>
    /// </remarks>
    internal IEnumerable<ValidationResult> FindMissingLanguageError()
    {
        if (string.IsNullOrWhiteSpace(this.Language))
        {
            yield return new ValidationResult(
                $"{nameof(this.Language)} is not stated, and every user record names the language MailFathom writes for that person in — the reading on a message row, the statement about a conversation. State {PublishedLanguages}.",
                [nameof(this.Language)]);
        }
    }
}
