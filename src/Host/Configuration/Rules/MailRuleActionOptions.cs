// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>What one rule does to the mail it selects, as an operator declares it.</summary>
/// <remarks>
/// <para>
/// One named key per action rather than a list of action objects, for two reasons. A binder drops an element of a list
/// whose value it cannot convert, so a typo inside one entry would silently leave a rule doing less than it says; and a
/// rule declaring one action twice is unrepresentable in this shape rather than merely refused, which is the stronger
/// form of the same rule.
/// </para>
/// <para>
/// An absent key is an action the rule does not ask for, which is why every one of them is nullable. Writing
/// <c>MarkAsRead: false</c> is a rule asking for mail to be marked <em>unread</em>, and leaving the key out is a rule
/// that does not touch the flag at all; the two would be indistinguishable on a plain boolean.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRuleActionOptions
{
    /// <summary>Gets or sets the folder a matching email is moved into, named by its alias or as <c>role:&lt;name&gt;</c>.</summary>
    /// <remarks>
    /// Naming a role rather than an alias is what lets one rule reach several accounts whose junk folders are configured
    /// under different aliases. Which folder of an account the role means is settled where the change is written down,
    /// and a rule naming a role that no account it reaches maps fails startup.
    /// </remarks>
    public string? MoveTo { get; set; }

    /// <summary>Gets or sets the folder a second occurrence of a matching email is put into, named the same way as <see cref="MoveTo" />.</summary>
    public string? CopyTo { get; set; }

    /// <summary>Gets or sets whether a matching email is removed from the folder it is in.</summary>
    /// <remarks>Deletion is the one action an account must permit explicitly, whatever a rule declares.</remarks>
    public bool? Delete { get; set; }

    /// <summary>Gets or sets whether a matching email's remote <c>\Seen</c> flag is set or cleared.</summary>
    public bool? MarkAsRead { get; set; }

    /// <summary>Gets or sets whether a matching email's remote <c>\Flagged</c> flag is set or cleared.</summary>
    public bool? MarkAsFlagged { get; set; }

    /// <summary>Gets or sets the keywords put on a matching email, beside the ones it already carries.</summary>
    /// <remarks>
    /// An array rather than a list, because an operator writing <c>[]</c> binds only to one: a list property leaves the
    /// key unset and the rule reads as one that does not touch keywords at all. Here that distinction changes nothing,
    /// since an empty list is refused by validation either way, but <see cref="SetKeywords" /> depends on it and the
    /// three keys are written the same way so that one of them cannot quietly behave differently.
    /// </remarks>
    public string[]? AddKeywords { get; set; }

    /// <summary>Gets or sets the keywords taken off a matching email, leaving the ones it is not asked about.</summary>
    public string[]? RemoveKeywords { get; set; }

    /// <summary>Gets or sets the keywords a matching email ends up carrying, in place of whatever it carried before.</summary>
    /// <remarks>
    /// Writing <c>[]</c> is a rule asking for every keyword to be cleared, which is the one thing the other two keys
    /// cannot say however many keywords they name. Leaving the key out is a rule that does not touch keywords at all.
    /// </remarks>
    public string[]? SetKeywords { get; set; }

    /// <summary>Gets whether the block asks for anything at all.</summary>
    internal bool IsEmpty => this.ToActions().Count == 0;

    /// <summary>Reads the declared keys as the actions they name, in the order they are written above.</summary>
    /// <returns>The actions, empty for a rule that selects mail and changes nothing.</returns>
    /// <remarks>
    /// A destination or a keyword that is not a value this system reads is left out rather than thrown over, because an
    /// unusable one is reported by validation against the key an operator edits and reading it here would raise instead.
    /// </remarks>
    internal IReadOnlyList<MailRuleAction> ToActions() =>
    [
        .. new[]
        {
            TryReadDestination(this.MoveTo, MailRuleAction.Relocate),
            TryReadDestination(this.CopyTo, MailRuleAction.Copy),
            this.Delete == true ? MailRuleAction.Delete() : null,
            this.MarkAsRead is { } isRead ? MailRuleAction.SetSeen(isRead) : null,
            this.MarkAsFlagged is { } isFlagged ? MailRuleAction.SetFlagged(isFlagged) : null,
            TryReadKeywords(this.AddKeywords, permitsEmpty: false, MailRuleAction.AddKeywords),
            TryReadKeywords(this.RemoveKeywords, permitsEmpty: false, MailRuleAction.RemoveKeywords),
            TryReadKeywords(this.SetKeywords, permitsEmpty: true, MailRuleAction.SetKeywords),
        }.OfType<MailRuleAction>(),
    ];

    /// <summary>Reports the destinations the block names, whether or not they are values this system could read.</summary>
    /// <returns>The destinations as they were written, in declared order.</returns>
    internal IReadOnlyList<string> DeclaredDestinations() =>
        [.. new[] { this.MoveTo, this.CopyTo }.Where(destination => destination is not null).OfType<string>()];

    /// <summary>Reports each keyword key the block wrote, paired with the name an operator sees in a refusal.</summary>
    /// <returns>The written lists, in declared order, leaving out the keys the block did not write.</returns>
    /// <remarks>
    /// Validation reads the keys through this rather than each on its own, so a key added here is judged without a
    /// second edit somewhere else — which is the way the one that was forgotten would have shipped.
    /// </remarks>
    internal IReadOnlyList<(string Key, string[] Keywords)> DeclaredKeywordLists() =>
    [
        .. new (string Key, string[]? Keywords)[]
        {
            (nameof(this.AddKeywords), this.AddKeywords),
            (nameof(this.RemoveKeywords), this.RemoveKeywords),
            (nameof(this.SetKeywords), this.SetKeywords),
        }
            .Where(declared => declared.Keywords is not null)
            .Select(declared => (declared.Key, declared.Keywords!)),
    ];

    /// <summary>Reports whether a keyword list is one that must name at least one keyword to mean anything.</summary>
    /// <remarks>
    /// Only the replacement may be empty, and only because emptiness is what it says: clear every keyword. An empty
    /// addition or removal asks the server for nothing, which is a mistyped list far more often than an intent.
    /// </remarks>
    internal static bool RequiresAKeyword(string key) =>
        !string.Equals(key, nameof(SetKeywords), StringComparison.Ordinal);

    private static MailRuleAction? TryReadDestination(
        string? destination,
        Func<MailFolderReference, MailRuleAction> toAction) =>
        MailFolderReference.TryCreate(destination, out var reference) ? toAction(reference) : null;

    /// <summary>Reads a keyword list without raising, so an unusable one is reported by validation rather than here.</summary>
    private static MailRuleAction? TryReadKeywords(
        string[]? keywords,
        bool permitsEmpty,
        Func<AuthoredMailKeywords, MailRuleAction> toAction)
    {
        if (keywords is null || !AuthoredMailKeywords.TryCreate(keywords, out var authored))
        {
            return null;
        }

        return authored.IsEmpty && !permitsEmpty ? null : toAction(authored);
    }

    /// <summary>Reads an alias without raising, so an unusable one is reported by validation rather than here.</summary>
    internal static bool TryReadAlias(string? value, out MailFolderAlias alias)
    {
        alias = default;

        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        alias = MailFolderAlias.Create(value);

        return true;
    }
}
