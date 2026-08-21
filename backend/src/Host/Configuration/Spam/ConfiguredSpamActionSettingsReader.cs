// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads what an operator asked to happen to junk out of the bound section.</summary>
/// <remarks>
/// <para>
/// The section is read per request rather than captured, so switching filing on reaches the next verdict and switching
/// it off stops the next one, neither needing a restart.
/// </para>
/// <para>
/// Classification being switched off answers for the actions too, although validation already refuses that combination.
/// The two are read from one snapshot here, so a candidate that somehow reached this reader cannot leave the mailbox
/// being written to on the strength of verdicts nothing is producing.
/// </para>
/// </remarks>
internal sealed class ConfiguredSpamActionSettingsReader(IOptionsMonitor<SpamClassificationOptions> options)
    : ISpamActionSettingsReader
{
    /// <inheritdoc />
    public SpamActionSettings Actions
    {
        get
        {
            var current = options.CurrentValue;

            return current.Enabled ? current.Actions.ToSettings() : SpamActionSettings.None;
        }
    }
}
