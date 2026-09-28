// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Reads an Agent's answer as the Markdown the client draws it as.</summary>
/// <remarks>
/// The instruction lets the Agent structure an answer with lists, bold, and a table, and refuses it headings, links,
/// images, and HTML. What it refuses is a structure a pattern settles, so it is checked without a judge; and what a
/// person reads is the drawn text, so evidence is looked for with the emphasis markers taken out — a quotation bolded
/// only in part is still read whole.
/// </remarks>
internal static partial class AnswerMarkdown
{
    /// <summary>The name the check that an answer writes only the Markdown it was asked to is recorded under.</summary>
    public const string MetricName = "Writes only the Markdown asked for";

    /// <summary>Names every kind of Markdown the answer carries that the instruction refuses.</summary>
    /// <param name="answer">What the Agent answered.</param>
    /// <returns>The refused kinds found, in a fixed order, and none where the answer keeps to what was asked.</returns>
    public static IReadOnlyList<string> Refused(string answer) =>
    [
        .. new (string Kind, Regex Pattern)[]
            {
                ("a heading", Heading()),
                ("an image", Image()),
                ("a link", Link()),
                ("HTML", Html()),
            }
            .Where(refused => refused.Pattern.IsMatch(answer))
            .Select(static refused => refused.Kind),
    ];

    /// <summary>Gets the answer's words as the client draws them, without the markers that only emphasize them.</summary>
    /// <param name="answer">What the Agent answered.</param>
    /// <returns>The answer with bold and inline-code markers removed.</returns>
    public static string AsRead(string answer) => answer.Replace("**", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);

    [GeneratedRegex(@"^ {0,3}#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"!\[[^\]]*\]\(")]
    private static partial Regex Image();

    // An inline link, an autolink to a URL or an address, and the definition a reference-style link needs before it
    // renders as one, which catches the full, collapsed, and shortcut forms alike.
    [GeneratedRegex(@"(?<!!)\[[^\]]+\]\([^)\s]+\)|<[A-Za-z][A-Za-z0-9+.-]{1,31}:[^<>\s]*>|<[^<>\s@]+@[^<>\s@]+>|^ {0,3}\[[^\]]+\]:[ \t]*\S", RegexOptions.Multiline)]
    private static partial Regex Link();

    [GeneratedRegex(@"</?[A-Za-z][A-Za-z0-9-]*(\s[^<>]*)?/?>")]
    private static partial Regex Html();
}
