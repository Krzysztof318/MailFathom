// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>What a write against an account's own folder declarations did.</summary>
/// <param name="Refusal">The rule the write broke, or <see langword="null" /> where it committed.</param>
/// <remarks>
/// A result type rather than an exception, because the immediate caller acts on the refusal and carries on: it is the
/// answer a person gets for the act they asked for, and every value of it is one the client draws a message from.
/// </remarks>
public sealed record MailFolderDeclarationOutcome(MailFolderActRefusal? Refusal)
{
    /// <summary>Gets the outcome of a write that committed.</summary>
    public static MailFolderDeclarationOutcome Committed { get; } = new(Refusal: null);

    /// <summary>Describes a write the account's declarations would not take.</summary>
    /// <param name="refusal">The rule it broke.</param>
    /// <returns>The refused outcome.</returns>
    public static MailFolderDeclarationOutcome Refused(MailFolderActRefusal refusal) => new(refusal);
}
