// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>What one presented lookup resolves to while a request is being authenticated.</summary>
/// <param name="Id">The credential's stable identifier, which is what a successful request is recorded under.</param>
/// <param name="Owner">The owner the request will act for once the credential is judged.</param>
/// <param name="Method">How the credential is presented, which decides what <paramref name="Material" /> holds.</param>
/// <param name="Permissions">What a request this credential admits may do, in the published order.</param>
/// <param name="Enabled">Whether the credential still authenticates anything.</param>
/// <param name="Material">The stored record the presented credential is judged against, or <see langword="null" /> for a method whose lookup is the whole of what is kept.</param>
/// <remarks>
/// <para>
/// This is the one shape that carries stored material out of persistence, and it exists for exactly one caller: the
/// authenticator judging a presented credential. Nothing else reads it and nothing composed for an operator is derived
/// from it — <see cref="OwnerCredential" /> is what an administrative answer is written from, and the split is what
/// keeps a stored record from reaching a response by somebody projecting one shape onto another.
/// </para>
/// <para>
/// <see cref="Enabled" /> is carried rather than filtered out by the lookup, because the authenticator must spend the
/// same work on a disabled credential as on an enabled one. A read that returned nothing for a disabled row would let a
/// caller time the difference between a credential that exists and one that has been turned off, which is the
/// distinction one indistinguishable failure path exists to hide.
/// </para>
/// <para>
/// <see cref="ToString" /> is redacted, so no diagnostic, log template, or exception message can print the stored
/// material by rendering the record it arrived in.
/// </para>
/// </remarks>
public sealed record ResolvedOwnerCredential(
    Guid Id,
    MailOwnerId Owner,
    OwnerCredentialMethod Method,
    IReadOnlyList<MailFathomPermission> Permissions,
    bool Enabled,
    string? Material)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(ResolvedOwnerCredential)} {{ {this.Id} }}";
}
