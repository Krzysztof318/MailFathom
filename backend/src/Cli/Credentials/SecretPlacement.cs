// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Credentials;

/// <summary>Which of the two places ended up holding a profile's secrets.</summary>
/// <param name="Store">The platform store that took them, or <see langword="null" /> when none did.</param>
/// <param name="Refusal">Why no store took them, or <see langword="null" /> when one did or when there was nothing to keep.</param>
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
/// </remarks>
internal sealed record SecretPlacement(string? Store, string? Refusal)
{
    /// <summary>Gets the answer for a profile that stores no secret anywhere.</summary>
    internal static SecretPlacement NothingToKeep { get; } = new(Store: null, Refusal: null);

    /// <summary>Reports that the platform's own store took them.</summary>
    /// <param name="store">What that store is called.</param>
    /// <returns>The placement.</returns>
    internal static SecretPlacement Held(string store) => new(store, Refusal: null);

    /// <summary>Reports that they were sealed into the credentials file instead, and why.</summary>
    /// <param name="refusal">Why this machine has no secret store to hold them.</param>
    /// <returns>The placement.</returns>
    internal static SecretPlacement Sealed(string refusal) => new(Store: null, refusal);

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
