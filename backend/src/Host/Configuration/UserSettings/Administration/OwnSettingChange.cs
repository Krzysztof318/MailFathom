// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What a signed-in person changing one setting of their own record did: nothing this deployment holds, the change, or a refusal.</summary>
/// <param name="UserHeld">Whether this deployment holds a record for them.</param>
/// <param name="RefusalMessage">The sentence naming the setting and who holds it where a settings policy does not leave it to them, or <see langword="null" /> where the record is as they asked.</param>
/// <remarks>
/// The refusal is the one a person can do nothing about by correcting what they sent, which is why it travels apart
/// from a value the route itself would refuse: the setting is the organization's or the deployment's, and the
/// sentence says which.
/// </remarks>
internal readonly record struct OwnSettingChange(bool UserHeld, string? RefusalMessage)
{
    /// <summary>Gets the outcome of a record that now states what was asked, whether or not a write was needed.</summary>
    internal static OwnSettingChange Settled { get; } = new(UserHeld: true, RefusalMessage: null);

    /// <summary>Gets the outcome for a caller this deployment holds no record for.</summary>
    internal static OwnSettingChange NoSuchUser { get; } = new(UserHeld: false, RefusalMessage: null);

    /// <summary>Reports that the record is held and the setting was not theirs to change.</summary>
    /// <param name="refusalMessage">The sentence naming the setting and who holds it.</param>
    /// <returns>The refused outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    internal static OwnSettingChange Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new OwnSettingChange(UserHeld: true, refusalMessage);
    }
}
