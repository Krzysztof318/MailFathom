// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads what each owner asked to happen to their own junk, from whichever source their record is read from.</summary>
/// <remarks>
/// <para>
/// The same two sources <see cref="ConfiguredSpamClassificationSettingsReader" /> reads and the same marker deciding
/// between them, because the two halves of one owner's posture are written in one place: an owner whose document has
/// been written has their switches read from it, and an owner still served from a configuration source has them read
/// from the deployment's section.
/// </para>
/// <para>
/// The source is read per request rather than captured, so switching filing on reaches that owner's next verdict and
/// switching it off stops it, neither needing a restart.
/// </para>
/// <para>
/// Classification being switched off answers for the actions too, although validation already refuses that combination
/// in both sources. The two are read from one posture here, so a candidate that somehow reached this reader cannot
/// leave a mailbox being written to on the strength of verdicts nothing is producing.
/// </para>
/// </remarks>
internal sealed class ConfiguredSpamActionSettingsReader(
    IOptionsMonitor<SpamClassificationOptions> deploymentOptions,
    MailSynchronizationOptions synchronizationOptions)
    : ISpamActionSettingsReader
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    public SpamActionSettings ActionsFor(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A junk posture is read for a named owner.", nameof(owner));
        }

        var served = (synchronizationOptions.ServedOwners ?? [])
            .FirstOrDefault(candidate => candidate.Owner == owner);

        if (served is null)
        {
            return SpamActionSettings.None;
        }

        if (served.ReadFromConfiguration)
        {
            var deployment = deploymentOptions.CurrentValue;

            return deployment.Enabled ? deployment.Actions.ToSettings() : SpamActionSettings.None;
        }

        var record = served.SpamClassification ?? new OwnerSpamClassificationOptions();

        return record.Enabled ? record.Actions.ToSettings() : SpamActionSettings.None;
    }
}
