// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads what each user asked to happen to their own junk, out of the record that is the whole of their posture.</summary>
/// <remarks>
/// <para>
/// The same source <see cref="ConfiguredSpamClassificationSettingsReader" /> reads, because the two halves of one
/// user's posture are written in one place: their own record.
/// </para>
/// <para>
/// The record is read per request rather than captured, so switching filing on reaches that user's next verdict and
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public SpamActionSettings ActionsFor(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A junk posture is read for a named user.", nameof(user));
        }

        var served = (synchronizationOptions.ServedUsers ?? [])
            .FirstOrDefault(candidate => candidate.User == user);

        if (served is null)
        {
            return SpamActionSettings.None;
        }

        var record = served.SpamClassification ?? new UserSpamClassificationOptions();

        return record.Enabled ? record.Actions.ToSettings() : SpamActionSettings.None;
    }
}
