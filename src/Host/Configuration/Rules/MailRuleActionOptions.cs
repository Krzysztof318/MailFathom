// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;

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
    /// <summary>Gets or sets the alias of the folder a matching email is moved into.</summary>
    public string? MoveTo { get; set; }

    /// <summary>Gets or sets the alias of the folder a second occurrence of a matching email is put into.</summary>
    public string? CopyTo { get; set; }

    /// <summary>Gets or sets whether a matching email is removed from the folder it is in.</summary>
    /// <remarks>Deletion is the one action an account must permit explicitly, whatever a rule declares.</remarks>
    public bool? Delete { get; set; }

    /// <summary>Gets or sets whether a matching email's remote <c>\Seen</c> flag is set or cleared.</summary>
    public bool? MarkAsRead { get; set; }

    /// <summary>Gets whether the block asks for anything at all.</summary>
    internal bool IsEmpty => this.ToActions().Count == 0;

    /// <summary>Reads the declared keys as the actions they name, in the order they are written above.</summary>
    /// <returns>The actions, empty for a rule that selects mail and changes nothing.</returns>
    /// <remarks>
    /// An alias that is not one this system issues is left out rather than thrown over, because a blank or unusable
    /// alias is reported by validation against the key an operator edits and reading it here would raise instead.
    /// </remarks>
    internal IReadOnlyList<MailRuleAction> ToActions() =>
    [
        .. new[]
        {
            TryReadDestination(this.MoveTo, MailRuleAction.Relocate),
            TryReadDestination(this.CopyTo, MailRuleAction.Copy),
            this.Delete == true ? MailRuleAction.Delete() : null,
            this.MarkAsRead is { } isRead ? MailRuleAction.SetSeen(isRead) : null,
        }.OfType<MailRuleAction>(),
    ];

    /// <summary>Reports the aliases the block names, whether or not they are values this system could issue.</summary>
    /// <returns>The destination aliases as they were written, in declared order.</returns>
    internal IReadOnlyList<string> DeclaredDestinations() =>
        [.. new[] { this.MoveTo, this.CopyTo }.Where(alias => alias is not null).OfType<string>()];

    private static MailRuleAction? TryReadDestination(
        string? alias,
        Func<MailFolderAlias, MailRuleAction> toAction) =>
        TryReadAlias(alias, out var readAlias) ? toAction(readAlias) : null;

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
