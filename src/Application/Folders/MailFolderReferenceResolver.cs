// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Turns what a caller named a folder into the folder of an account it means.</summary>
/// <remarks>
/// <para>
/// This is the single place a reference becomes a mapping, and it exists so that naming a folder by role is one
/// behavior rather than one per caller. A rule's destination, a mailbox read's folder filter, and every later feature
/// that lets a folder be named come here, so the refusal a role nothing carries produces is the same refusal whichever
/// of them asked.
/// </para>
/// <para>
/// The two kinds of name fail differently, and deliberately. A role is a question only configuration answers, so a role
/// no folder of the account plays is refused: there is nothing the caller could have meant. An alias is already the
/// folder's name, so one configuration does not map resolves to no mapping without refusing anything — a mailbox read
/// naming a folder that is not there must answer with no mail rather than confirm which names exist.
/// </para>
/// </remarks>
public sealed class MailFolderReferenceResolver
{
    private readonly IMailFolderMappingReader mappings;

    /// <summary>Initializes the resolver.</summary>
    /// <param name="mappings">Answers which folder of an account an alias or a role names.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mappings" /> is <see langword="null" />.</exception>
    public MailFolderReferenceResolver(IMailFolderMappingReader mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        this.mappings = mappings;
    }

    /// <summary>Resolves what a caller named into the folder of one account it refers to.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="reference">The alias or role the caller named.</param>
    /// <returns>The mapping, or <see langword="null" /> when the reference names an alias configuration does not map.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reference" /> is the unspecified struct default.</exception>
    /// <exception cref="MailFolderRoleUnmappedException">Thrown when the reference names a role no folder of the account plays.</exception>
    public MailFolderMapping? Resolve(MailAccountId accountId, MailFolderReference reference)
    {
        var mapping = this.TryResolve(accountId, reference);

        return mapping is not null || reference.Role is not { } role
            ? mapping
            : throw new MailFolderRoleUnmappedException(accountId, role);
    }

    /// <summary>Resolves what a caller named without refusing a role the account plays no folder with.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="reference">The alias or role the caller named.</param>
    /// <returns>The mapping, or <see langword="null" /> when this account has no folder that reference means.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reference" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// It exists for the caller asking several accounts one question — a mailbox read narrowed to <em>the junk folder</em>
    /// across every account it serves — where one account without such a folder is an account contributing nothing
    /// rather than a request to refuse. A caller asking about a single account uses <see cref="Resolve" /> instead, so
    /// that a role nothing answers still fails rather than reading as a folder holding no mail.
    /// </remarks>
    public MailFolderMapping? TryResolve(MailAccountId accountId, MailFolderReference reference)
    {
        if (!reference.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct names no folder.", nameof(reference));
        }

        return reference.Role is { } role
            ? this.mappings.FindFolderPlayingRole(accountId, role)
            : this.mappings.FindFolderNamed(accountId, reference.Alias!.Value);
    }

    /// <summary>Resolves what a caller named into the alias every later read and write of the folder is expressed in.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="reference">The alias or role the caller named.</param>
    /// <returns>The alias of the folder the reference means.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reference" /> is the unspecified struct default.</exception>
    /// <exception cref="MailFolderRoleUnmappedException">Thrown when the reference names a role no folder of the account plays.</exception>
    /// <remarks>
    /// An alias configuration does not map is returned unchanged rather than refused, which keeps a folder filter naming
    /// an unknown folder answering with nothing. Every caller that only needs the name uses this, so nothing has to
    /// decide for itself what an unmapped alias means.
    /// </remarks>
    public MailFolderAlias ResolveAlias(MailAccountId accountId, MailFolderReference reference) =>
        this.Resolve(accountId, reference)?.Alias ?? reference.Alias!.Value;
}
