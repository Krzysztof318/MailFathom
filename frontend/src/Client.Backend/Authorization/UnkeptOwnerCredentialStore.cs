// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>The store of a head that keeps no credential, which is the browser head and the default here.</summary>
/// <remarks>
/// <para>
/// An implementation that holds nothing rather than an absent registration, so the case has one shape everywhere
/// instead of a null every caller would have to remember to check. The session lasts as long as the process, exactly
/// as it did before anything was kept anywhere, and the sign-in screen says so rather than leaving somebody to
/// discover it by being asked again.
/// </para>
/// <para>
/// It is what the browser head registers, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md">ADR 0018</see>
/// is why: <c>localStorage</c>, <c>sessionStorage</c>, IndexedDB, and cookies are scoped to the page's origin rather
/// than to a person, so one injected script, one compromised dependency inside the bundle, or one extension with
/// access to the page would lift an owner's password. Encrypting it only moves the question to where the key lives,
/// and the answer is the same storage.
/// </para>
/// <para>
/// It is also the registration a head that has said nothing gets, which is the safe direction to default in: a head
/// composed without stating where it keeps a password keeps none.
/// </para>
/// </remarks>
public sealed class UnkeptOwnerCredentialStore : IOwnerCredentialStore
{
    /// <summary>Gets the one instance, which holds nothing and therefore needs no second.</summary>
    public static UnkeptOwnerCredentialStore Instance { get; } = new();

    /// <inheritdoc />
    public CredentialPersistence Persistence => CredentialPersistence.NotOfferedOnThisHead;

    /// <inheritdoc />
    public ValueTask<KeptOwnerCredential?> ReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<KeptOwnerCredential?>(null);

    /// <inheritdoc />
    public ValueTask<CredentialPersistence> WriteAsync(
        KeptOwnerCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return ValueTask.FromResult(CredentialPersistence.NotOfferedOnThisHead);
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
