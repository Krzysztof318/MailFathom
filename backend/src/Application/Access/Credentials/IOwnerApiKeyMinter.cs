// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Draws the keys an owner's clients present, and reduces a presented one to the value it is resolved by.</summary>
/// <remarks>
/// <para>
/// The deployment mints the key rather than accepting one an operator wrote, which is the difference between this
/// method and every credential MailFathom used to read out of a configuration file. A key drawn here carries the
/// entropy the deployment decided on, is reported exactly once, and is never stored: what the row holds is
/// <see cref="MintedOwnerApiKey.Lookup" />, and a key nobody wrote down is a key nobody can recover.
/// </para>
/// <para>
/// That is also why the lookup may be a plain digest rather than an adaptive hash. A password is a value a person
/// chose out of a space small enough to search, so its record has to be expensive to try; a key drawn here is
/// indistinguishable from random over a space nothing can enumerate, so a digest of it discloses nothing and can be
/// indexed — which is what lets one request resolve one row rather than compare itself against every key the
/// deployment holds.
/// </para>
/// <para>
/// Both members are synchronous. Digesting is bounded work over a value the caller already holds, and putting it
/// behind an await would say the material's lifetime extends past the call, which it does not.
/// </para>
/// </remarks>
public interface IOwnerApiKeyMinter
{
    /// <summary>Draws a new key and the value it will be resolved by.</summary>
    /// <returns>The key to report once, and the lookup to store.</returns>
    /// <remarks>Two calls never return one key. The plaintext is the caller's to hand back to whoever asked for it, and the caller is the one place it exists after this returns.</remarks>
    MintedOwnerApiKey Mint();

    /// <summary>Reduces a presented key to the value a stored credential is resolved by.</summary>
    /// <param name="presentedKey">The key as the request carried it, which is read within the call and never retained.</param>
    /// <param name="lookup">The value to resolve by when the presented key is one this deployment could have minted; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the presented key has a shape this deployment mints.</returns>
    /// <remarks>
    /// It answers rather than raising because its caller meets an unusable value as an ordinary event: a request
    /// presenting something that is not one of this deployment's keys is refused exactly as one presenting a key
    /// nobody holds, and neither may be told apart from the other.
    /// </remarks>
    bool TryDigest(ReadOnlySpan<char> presentedKey, out OwnerCredentialLookup lookup);
}

/// <summary>A key this deployment has just drawn, in the two forms that exist at that instant.</summary>
/// <param name="Key">The plaintext, which is reported to whoever asked for it and then exists nowhere.</param>
/// <param name="Lookup">The value the credential row is resolved by, which is what the deployment keeps.</param>
/// <remarks>
/// <see cref="ToString" /> renders neither half, so no diagnostic, log template, or exception message can print a
/// minted key or its digest by rendering the record it arrived in. The digest is withheld for the reason
/// <see cref="OwnerCredentialMethod.LookupIsDerivedFromTheSecret" /> gives — it verifies a presented key, so it is
/// material rather than a fact about the record, and the administrative listing answers it as absent. A record that
/// published it here would be giving away in a log what a reader holding <c>mailfathom.admin.credentials.read</c> is
/// refused.
/// </remarks>
public sealed record MintedOwnerApiKey(string Key, OwnerCredentialLookup Lookup)
{
    /// <inheritdoc />
    public override string ToString() => nameof(MintedOwnerApiKey);
}
