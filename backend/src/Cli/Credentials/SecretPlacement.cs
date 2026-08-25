// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Credentials;

/// <summary>Which of the two places ended up holding a profile's secrets.</summary>
/// <param name="Store">The platform store that took them, or <see langword="null" /> when none did.</param>
/// <param name="Refusal">Why no store took them, or <see langword="null" /> when one did or when there was nothing to keep.</param>
/// <param name="Uncleared">Why an entry this sign-in meant to remove is still in the platform store, or <see langword="null" /> when nothing was left behind.</param>
/// <remarks>
/// <para>
/// Returned rather than printed, because <see cref="CredentialStore" /> has no terminal and must not acquire one: the
/// command that signed in is what says which storage the operator got. Saying it at all is the point — a machine with
/// no keyring gets the weaker arrangement, and a fallback nobody was told about is a fallback nobody can act on.
/// </para>
/// <para>
/// Three states rather than two, because a key-pair profile keeps no secret in either place and the sentence about
/// storage would be false either way it was worded.
/// </para>
/// <para>
/// <see cref="Uncleared" /> is beside all three rather than one of them, because what a sign-in leaves behind and where
/// it put this profile's secrets are separate facts: a profile can be sealed into the file because the keyring locked
/// halfway through and have a live credential still in that keyring for the same reason.
/// </para>
/// </remarks>
internal sealed record SecretPlacement(string? Store, string? Refusal, string? Uncleared = null)
{
    /// <summary>Gets the answer for a profile that stores no secret anywhere.</summary>
    internal static SecretPlacement NothingToKeep { get; } = new(Store: null, Refusal: null);

    /// <summary>Reports that the platform's own store took them.</summary>
    /// <param name="store">What that store is called.</param>
    /// <param name="uncleared">Why an entry this sign-in meant to remove is still there, or <see langword="null" /> when nothing was left behind.</param>
    /// <returns>The placement.</returns>
    internal static SecretPlacement Held(string store, string? uncleared = null) =>
        new(store, Refusal: null, uncleared);

    /// <summary>Reports that they were sealed into the credentials file instead, and why.</summary>
    /// <param name="refusal">Why this machine has no secret store to hold them.</param>
    /// <param name="uncleared">Why an entry the fallback meant to withdraw is still there, or <see langword="null" /> when nothing was left behind.</param>
    /// <returns>The placement.</returns>
    internal static SecretPlacement Sealed(string refusal, string? uncleared = null) =>
        new(Store: null, refusal, uncleared);

    /// <summary>Says what is still in the platform's store that nothing here could take out.</summary>
    /// <param name="uncleared">Why the entry would not go.</param>
    /// <returns>The sentence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="uncleared" /> is <see langword="null" />.</exception>
    /// <remarks>Stated once because three places print it — a sign-in that had to withdraw or replace something, a sign-out that could not clear both halves, and the first command that moves an older profile into the store — and an operator meeting the same leftover twice should read the same sentence about it.</remarks>
    internal static string DescribeUncleared(string uncleared)
    {
        ArgumentNullException.ThrowIfNull(uncleared);

        return $"An entry for this profile is still in the platform's secret store: {uncleared}. Remove it from your keyring once it is reachable.";
    }

    /// <summary>Says which of the two holds the credential.</summary>
    /// <returns>The sentence, or <see langword="null" /> when the profile keeps no secret to say it about.</returns>
    internal string? Describe() => this switch
    {
        { Store: { } store } => $"The credential is held by {store}.",
        { Refusal: { } refusal } =>
            $"The credential is sealed in the credentials file under a key beside it, because {refusal}.",
        _ => null,
    };
}

/// <summary>What forgetting one profile came to.</summary>
/// <param name="Removed"><see langword="true" /> when a profile carried that name, <see langword="false" /> when none did.</param>
/// <param name="Uncleared">Why the platform store's entries for it are still there, or <see langword="null" /> when nothing was left behind.</param>
/// <remarks>
/// The file entry and the store entries are two removals, and only the first of them is certain: a keyring that has
/// gone away between signing in and signing out leaves items nothing here can reach. That is reported rather than
/// swallowed, because the operator is the only one who can then open the keyring and remove them — and rather than
/// failing the command, because the profile is genuinely forgotten and refusing to say so would be worse.
/// </remarks>
internal sealed record ProfileRemoval(bool Removed, string? Uncleared)
{
    /// <summary>Gets the answer for a name no profile carried.</summary>
    internal static ProfileRemoval NothingToForget { get; } = new(Removed: false, Uncleared: null);
}
