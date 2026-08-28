// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>What one username resolves to when a request is being authenticated.</summary>
/// <param name="Id">The credential's stable identifier, which is what a successful request is recorded under.</param>
/// <param name="Owner">The owner the request will act for once the password matches.</param>
/// <param name="Enabled">Whether the credential still authenticates anything.</param>
/// <param name="PasswordHash">The stored record the presented password is judged against.</param>
/// <remarks>
/// <para>
/// This is the one shape that carries a stored hash out of persistence, and it exists for exactly one caller: the
/// authenticator judging a presented credential. Nothing else reads it and nothing composed for an operator is derived
/// from it — <see cref="OwnerPasswordCredential" /> is what an administrative answer is written from, and the split is
/// what keeps a hash from reaching a response by somebody projecting one record onto another.
/// </para>
/// <para>
/// <see cref="Enabled" /> is carried rather than filtered out by the lookup, because the authenticator must spend the
/// same work on a disabled credential as on an enabled one. A read that returned nothing for a disabled row would let a
/// caller time the difference between a username that exists and one that has been turned off, which is the
/// distinction one indistinguishable failure path exists to hide.
/// </para>
/// <para>
/// <see cref="ToString" /> is redacted, so no diagnostic, log template, or exception message can print the stored hash
/// by rendering the record it arrived in.
/// </para>
/// </remarks>
public sealed record ResolvedOwnerPasswordCredential(
    Guid Id,
    MailOwnerId Owner,
    bool Enabled,
    string PasswordHash)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(ResolvedOwnerPasswordCredential)} {{ {this.Id} }}";
}
