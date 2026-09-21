// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What erasing a user did, whether this process was serving them, or why nothing was erased at all.</summary>
/// <remarks>
/// <para>
/// The second says whether removing the user also changed the running process. A served user leaves the runtime roster
/// before the erasure commits, so callers and synchronization stop reaching them without a restart.
/// </para>
/// <para>
/// The third is a refusal rather than an exception for the reason provisioning's is: work the deployment could not
/// stop within its bound is a state an administrator acts on by asking again, not a failure of the machinery. A
/// refusal means nothing at all was erased, which is the whole point of reporting it — an erasure that went ahead over
/// a run still writing would answer that the deployment holds nothing while it was still being written to.
/// </para>
/// <para>
/// It is named for the roster rather than for the erasure alone because <see cref="Application.Access.UserErasureOutcome" />
/// is the other half of the same act and the two would otherwise read as one word meaning two things: that one is what
/// the store removed, and this is what the roster administration answered — including the refusal the store never sees.
/// </para>
/// </remarks>
/// <param name="UserErased">Whether a user record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="WasServed">Whether the runtime roster held the user that was erased.</param>
/// <param name="RefusalMessage">The sentence naming the work that is still running, or <see langword="null" /> where nothing refused the erasure.</param>
internal readonly record struct UserRosterErasureOutcome(bool UserErased, bool WasServed, string? RefusalMessage = null)
{
    /// <summary>Gets whether the deployment could stop the user's own work for long enough to erase them.</summary>
    public bool IsQuiesced => this.RefusalMessage is null;

    /// <summary>Reports that nothing was erased because work bound to the user's mail accounts would not stop.</summary>
    /// <param name="refusalMessage">The sentence naming what is still running.</param>
    /// <returns>The refused outcome.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="refusalMessage" /> is <see langword="null" />, empty, or white space.</exception>
    public static UserRosterErasureOutcome Refused(string refusalMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalMessage);

        return new UserRosterErasureOutcome(UserErased: false, WasServed: false, refusalMessage);
    }
}
