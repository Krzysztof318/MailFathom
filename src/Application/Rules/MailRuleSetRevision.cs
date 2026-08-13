// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Application.Rules;

/// <summary>Identifies the rule set a pass ran under, as a digest of the rules themselves in the order they were declared.</summary>
/// <remarks>
/// <para>
/// Derived rather than declared, which is what makes it trustworthy: an authored version key can be forgotten, and an
/// edit that changes the rules while leaving the key alone is exactly the case a history has to be able to tell apart.
/// A publication counter was the other candidate and is worse for the opposite reason — it is process-local, so it
/// restarts at zero and means something different on every replica, while two instances reading the same file must name
/// the same revision.
/// </para>
/// <para>
/// The digest is taken over the bound rules, so it does not move for the wrong reasons: a change to an unrelated
/// configuration key leaves it alone, and so does reformatting the file or reordering the keys within one rule.
/// Reordering the rules themselves does move it, because declared order is part of what a rule set means. So is the
/// scope: narrowing a rule to one account changes which mail it reaches, which is a different rule set rather than the
/// same one applied differently. So are the triggers, for the same reason — a rule withdrawn from firing on arrival
/// reaches different mail from the one that fires on it — and so is the schedule beside them, because moving when a rule
/// walks a mailbox is moving which mail it reaches at any moment. So are the actions, for the reason the revision is part of a
/// request's identity at all — a rule now filing into a different folder must ask afresh rather than be answered by the
/// record of the filing it asked for before the edit.
/// </para>
/// <para>
/// It carries none of the authored text and no ordering. A record naming a revision therefore holds nothing personal
/// that the condition contributed, and a record that has to say which of two revisions came first carries its own
/// timestamp rather than inferring one from the value.
/// </para>
/// </remarks>
public readonly record struct MailRuleSetRevision
{
    /// <summary>How many hexadecimal characters of the digest the identity keeps.</summary>
    /// <remarks>
    /// Twelve characters are forty-eight bits, which is far more than enough to tell apart the handful of revisions one
    /// deployment's configuration passes through, and short enough to read in a log line without being elided.
    /// </remarks>
    private const int LengthInCharacters = 12;

    /// <summary>Separates the parts of one rule inside the digest input, and one rule from the next.</summary>
    /// <remarks>
    /// Unit and record separators rather than punctuation, because neither can occur in a rule name or a condition. A
    /// separator a value could contain would let two different rule sets render identically, which is the one way a
    /// derived identity can quietly stop distinguishing what it exists to distinguish.
    /// </remarks>
    private const char FieldSeparator = '\u001F';
    private const char RuleSeparator = '\u001E';

    /// <summary>Separates the values inside a field holding several: the scope, the actions, and the triggers.</summary>
    /// <remarks>
    /// A third separator rather than a reused one, so that a scope of two accounts cannot render as the same text as a
    /// rule whose next field begins where the second account would have. Every multi-valued field uses this one, which
    /// stays unambiguous because a field separator ends each of them.
    /// </remarks>
    private const char ListSeparator = '\u001D';

    private readonly string? value;

    private MailRuleSetRevision(string value) => this.value = value;

    /// <summary>Gets whether this value names a revision rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the identity, as lowercase hexadecimal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a revision.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a revision.");

    /// <summary>Reports whether a value carries a character this identity separates its own fields with.</summary>
    /// <param name="value">A rule name, a condition, or an account identifier about to become part of a rule set.</param>
    /// <returns><see langword="true" /> when the value would blur the boundary between two fields of the digest input.</returns>
    /// <remarks>
    /// The remarks above say a separator cannot occur in what is hashed, and this is what lets that be true rather than
    /// hoped for: whatever binds a rule set asks first and refuses a value that answers yes. Without the check the claim
    /// rests on the expression language's grammar, which is somebody else's decision and can change under this one — and
    /// a rule set that renders identically to a different rule set is exactly the collision this identity exists to make
    /// impossible.
    /// </remarks>
    public static bool ContainsSeparator(string? value) =>
        value?.AsSpan().IndexOfAny(FieldSeparator, RuleSeparator, ListSeparator) >= 0;

    /// <summary>Derives the identity of a rule set from the rules it declares, in the order it declares them.</summary>
    /// <param name="declarations">Every rule of the bound set, in declared order.</param>
    /// <returns>The revision the set is known by.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A set that declares no rules has an identity too, and it is the same one on every instance that declares none.
    /// That is deliberate: a deployment which has removed its last rule is in a state a history should be able to name,
    /// rather than one with no revision at all.
    /// </remarks>
    public static MailRuleSetRevision Create(IReadOnlyList<MailRuleDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var canonicalForm = new StringBuilder();

        foreach (var declaration in declarations)
        {
            canonicalForm
                .Append(declaration.Name)
                .Append(FieldSeparator)
                .Append(declaration.ConditionText)
                .Append(FieldSeparator)
                .AppendJoin(ListSeparator, declaration.Actions.Select(action => action.CanonicalForm))
                .Append(FieldSeparator)
                .Append(declaration.StopWhenMatched ? "stop" : "continue")
                .Append(FieldSeparator)
                .AppendJoin(ListSeparator, declaration.Accounts)
                .Append(FieldSeparator)
                .AppendJoin(ListSeparator, declaration.Triggers.Select(trigger => trigger.Name))
                .Append(FieldSeparator)
                .Append(declaration.Schedule?.CanonicalForm ?? string.Empty)
                .Append(RuleSeparator);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalForm.ToString()));

        return new MailRuleSetRevision(Convert.ToHexStringLower(digest)[..LengthInCharacters]);
    }

    /// <summary>Reads back an identity this system derived earlier and recorded.</summary>
    /// <param name="value">The recorded identity.</param>
    /// <returns>The revision, which compares equal to a freshly derived one of the same rule set.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not an identity this type could have produced.</exception>
    /// <remarks>
    /// A durable record of a run has to say which rule set the run was bound to, and it survives the process that
    /// derived it — so the identity has to come back from storage rather than only out of <see cref="Create" />. The
    /// shape is checked rather than trusted, because a value that is not one this type produces would compare unequal
    /// to every rule set and silently make every run look superseded.
    /// </remarks>
    public static MailRuleSetRevision Restore(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != LengthInCharacters
            || !value.All(static character => char.IsAsciiDigit(character) || char.IsAsciiLetterLower(character) && character <= 'f'))
        {
            throw new ArgumentException(
                $"A rule set revision is exactly {LengthInCharacters} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new MailRuleSetRevision(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";
}
