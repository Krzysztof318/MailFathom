// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads what each account asks to happen to its own junk, out of the record that is the whole of its posture.</summary>
/// <remarks>
/// <para>
/// The same source <see cref="ConfiguredSpamClassificationSettingsReader" /> reads, because the two halves of one
/// account's posture are written in one place: that account's own record.
/// </para>
/// <para>
/// The record is read per request rather than captured, so switching filing on reaches that account's next verdict and
/// switching it off stops it, neither needing a restart.
/// </para>
/// <para>
/// Classification being switched off answers for the actions too, although validation already refuses that combination
/// in a record. The two are read from one posture here, so a candidate that somehow reached this reader cannot leave a
/// mailbox being written to on the strength of verdicts nothing is producing.
/// </para>
/// </remarks>
internal sealed class ConfiguredSpamActionSettingsReader(MailSynchronizationOptions synchronizationOptions)
    : ISpamActionSettingsReader
{
    /// <inheritdoc />
    public SpamActionSettings ActionsFor(MailAccountId account)
    {
        if (synchronizationOptions.FindConfiguredAccount(account) is not { } declared)
        {
            return SpamActionSettings.None;
        }

        var record = declared.SpamClassification ?? new MailAccountSpamClassificationOptions();

        return record.Enabled ? record.Actions.ToSettings() : SpamActionSettings.None;
    }
}
