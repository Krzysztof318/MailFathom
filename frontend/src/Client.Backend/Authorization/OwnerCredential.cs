// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Authorization;

/// <summary>The username and password an owner signs in to their deployment with.</summary>
/// <remarks>
/// <para>
/// The whole of what HTTP Basic carries, held as the two halves RFC 7617 defines rather than as the encoded field they
/// travel in. The client needs the username on its own — to say who it is signed in as — and a single stored value
/// that has to be split back apart would be worse than two that are already named.
/// </para>
/// <para>
/// Public because the port that keeps one is implemented outside this assembly, by the head whose operating system
/// holds the secret. Nothing else outside <c>Client.Backend</c> ever receives one: a screen hands a typed pair in and
/// is never given one back, and <see cref="SignedInOwner" /> exposes the username alone.
/// </para>
/// <para>
/// The record deliberately declares no <see cref="object.ToString" /> of its own beyond the one suppressed below.
/// A positional record prints every member, so an interpolated string, a log line, or a debugger's own rendering would
/// carry the password — which is exactly what this type may never let happen.
/// </para>
/// </remarks>
public sealed record OwnerCredential
{
    /// <summary>The character RFC 7617 separates the two halves at, which a username may therefore not contain.</summary>
    private const char UserIdSeparator = ':';

    /// <summary>Initializes the credential from what somebody typed.</summary>
    /// <param name="username">The owner's username, which carries no colon.</param>
    /// <param name="password">The owner's password, which may carry anything including a colon.</param>
    /// <exception cref="ArgumentException">Thrown when either half is blank, or when the username carries a colon.</exception>
    /// <remarks>
    /// The colon is refused here rather than encoded around, because RFC 7617 splits the decoded field at the first one
    /// and a username containing one would authenticate a shorter name with a longer password. The deployment refuses
    /// such a credential too; refusing it at the point it is typed is what turns silently authenticating a different
    /// name into a sentence on the screen.
    /// </remarks>
    public OwnerCredential(string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (username.Contains(UserIdSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A username may not contain a colon: HTTP Basic separates the username from the password at the "
                + "first one, so a name carrying one would be presented as a different name.",
                nameof(username));
        }

        this.Username = username;
        this.Password = password;
    }

    /// <summary>Gets the owner's username, which is the half that is not a secret.</summary>
    public string Username { get; }

    /// <summary>Gets the owner's password.</summary>
    public string Password { get; }

    /// <summary>Reports the credential without either half of it.</summary>
    /// <returns>The type's name and nothing more.</returns>
    /// <remarks>
    /// The one override this type needs. A record's generated printing walks every member, so anything that renders a
    /// credential — an interpolated message, a structured log's fallback formatter, a debugger watch — would otherwise
    /// carry the password. Naming the username here would be no better: it is what a rate-limiter and an audit record
    /// key on, and a diagnostic naming it says who is being signed in as.
    /// </remarks>
    public override string ToString() => nameof(OwnerCredential);
}
