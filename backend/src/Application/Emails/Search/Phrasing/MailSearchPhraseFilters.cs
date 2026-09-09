// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search.Phrasing;

/// <summary>The constraints a sentence was read as: what a search may return, rather than what it ranks by.</summary>
/// <remarks>
/// <para>
/// Every member here is a filter the search screen already draws as an object somebody can see and take off, and that
/// is the whole of why the set is this one. A reading that produced a constraint the screen has no chip for would be a
/// constraint in force that nobody can see or remove, which is exactly the failure the screen was built to prevent.
/// </para>
/// <para>
/// The account and the folder are deliberately absent. Those two come from the mail somebody was looking at when they
/// searched, which the screen copies onto the search already; a model choosing between mailboxes would be guessing at
/// names it was never shown, and a wrong guess there hides mail rather than ranking it lower.
/// </para>
/// <para>
/// The three state members are <see langword="bool" /> rather than nullable, because a search asks for unread, flagged
/// or attachment-carrying mail and never for the opposite: <em>only read</em> is not a search anybody types, and the
/// screen has no chip for one.
/// </para>
/// </remarks>
public sealed record MailSearchPhraseFilters
{
    /// <summary>The reading that constrains nothing, which is what a sentence carrying no constraint produces.</summary>
    public static MailSearchPhraseFilters None { get; } = new();

    /// <summary>Gets the address the sender must carry, or <see langword="null" /> for any sender.</summary>
    /// <remarks>A whole address rather than a name, because that is what the search filters by. A sentence naming a person rather than an address produces nothing here and leaves the name among the criteria, where it ranks instead of excluding.</remarks>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address a recipient must carry, or <see langword="null" /> for any recipient.</summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the first calendar day the search reaches back to, or <see langword="null" /> for no start.</summary>
    public DateOnly? ReceivedFrom { get; init; }

    /// <summary>Gets the last calendar day the search reaches, inclusive, or <see langword="null" /> for no end.</summary>
    public DateOnly? ReceivedTo { get; init; }

    /// <summary>Gets whether only unread mail may come back.</summary>
    public bool Unread { get; init; }

    /// <summary>Gets whether only flagged mail may come back.</summary>
    public bool Flagged { get; init; }

    /// <summary>Gets whether only mail carrying attachments may come back.</summary>
    public bool HasAttachments { get; init; }
}
