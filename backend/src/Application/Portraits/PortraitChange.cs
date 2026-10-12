// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Portraits;

/// <summary>What a signed-in person replacing or removing their own portrait did.</summary>
/// <param name="UserHeld">Whether this deployment holds a record for them; nothing was written when it does not.</param>
/// <param name="Refusal">The sentence naming who holds the portrait where it is not theirs to change, or naming the settings policy that holds their whole record where this build does not read it; <see langword="null" /> where the change landed.</param>
/// <remarks>
/// A refusal is its own answer rather than a failure, because nothing the person sent was wrong: whoever administers
/// them decided the portrait is not theirs to change, or a policy governing them is one only an administrator or
/// another build can make readable, and the sentence says which.
/// </remarks>
public sealed record PortraitChange(bool UserHeld, string? Refusal)
{
    /// <summary>Gets the answer for a change that landed.</summary>
    public static PortraitChange Landed { get; } = new(UserHeld: true, Refusal: null);

    /// <summary>Gets the answer for a caller this deployment holds no record for.</summary>
    public static PortraitChange NoSuchUser { get; } = new(UserHeld: false, Refusal: null);
}
