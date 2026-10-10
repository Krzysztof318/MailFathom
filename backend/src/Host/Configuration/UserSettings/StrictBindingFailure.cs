// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>What the strict configuration binder refused, reduced to the two fragments that are safe to repeat back.</summary>
/// <param name="UnknownProperties">The quoted names of the properties nothing binds, or <see langword="null" /> where the failure names none that may be repeated.</param>
/// <param name="UnconvertiblePath">The path of the setting whose value would not convert, or <see langword="null" /> where the failure names none that may be repeated.</param>
/// <remarks>
/// <para>
/// Neither sentence the framework raises can be handed on. The one naming unknown properties names MailFathom's own
/// type and the binder option that was set, neither of which is a thing whoever wrote the document can act on; the
/// one about a value that will not convert names the setting's path in a sentence that quotes <em>the value beside
/// it</em> — which for a mailbox password is the material the binder refuses everywhere else. So the two shapes are
/// recognized here and re-stated by whoever reads this, in a sentence about the document that was bound, and the
/// framework's own text is carried in neither.
/// </para>
/// <para>
/// The path is taken from the last marker rather than the first, because a value quoted before it may contain
/// anything, including the marker itself. Neither fragment goes back unexamined: the path has to be a configuration
/// path — segments separated by colons and nothing else — and the property names have to be the quoted list the
/// framework writes, carrying no control character. What fails either test is reported as nothing, which is also
/// what a message shaped differently by a later runtime gets, and the reader answers with its general sentence.
/// </para>
/// <para>
/// Both sentences are looked for on the failure and on the one inside it, because where the fault sits decides which
/// carries it: one written at the top of the document arrives on the failure itself, while one written inside a
/// nested element is met while that element is bound and arrives as an inner failure under a sentence saying only
/// that binding failed.
/// </para>
/// </remarks>
internal readonly record struct StrictBindingFailure(string? UnknownProperties, string? UnconvertiblePath)
{
    /// <summary>Reads what a strict binding refused.</summary>
    /// <param name="refusal">The failure the configuration binder raised.</param>
    /// <returns>The fragment that may be repeated back, or neither where the failure carries none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    internal static StrictBindingFailure Read(InvalidOperationException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        const string unknownProperties = "were not found on the instance of";
        const string pathOpening = " at '";
        const string pathClosing = "' to type '";

        var unknown = new[] { refusal.Message, refusal.InnerException?.Message }
            .OfType<string>()
            .FirstOrDefault(message => message.Contains(unknownProperties, StringComparison.Ordinal));

        if (unknown is not null
            && unknown.LastIndexOf(": ", StringComparison.Ordinal) is var named and > 0
            && QuotedNamesIn(unknown[(named + 2)..]) is { } names)
        {
            return new StrictBindingFailure(names, UnconvertiblePath: null);
        }

        var conversion = new[] { refusal.Message, refusal.InnerException?.Message }
            .OfType<string>()
            .FirstOrDefault(message => message.Contains(pathClosing, StringComparison.Ordinal)) ?? string.Empty;
        var closing = conversion.LastIndexOf(pathClosing, StringComparison.Ordinal);
        var opening = closing > 0 ? conversion.LastIndexOf(pathOpening, closing, StringComparison.Ordinal) : -1;

        return opening > 0 && conversion[(opening + pathOpening.Length)..closing] is var path && IsSettingPath(path)
            ? new StrictBindingFailure(UnknownProperties: null, path)
            : default;
    }

    /// <summary>Gets whether a fragment is a configuration path and therefore safe to repeat back.</summary>
    /// <param name="candidate">The fragment, which may be a key somebody wrote.</param>
    /// <returns><see langword="true" /> when every segment of it is a setting's name.</returns>
    internal static bool IsSettingPath(string candidate) =>
        candidate.Length > 0
        && candidate.Split(':').All(segment => segment.Length > 0 && segment.All(char.IsLetterOrDigit));

    /// <summary>Gets the named properties back when they are safe to repeat, and nothing when they are not.</summary>
    /// <remarks>
    /// What sits in this fragment is the property names of a stored document, which is text whoever wrote it chose. A
    /// name carrying a newline would put a line of its own choosing into the refusal an administrator reads and into
    /// any log of it, and a name carrying the marker this fragment was cut at would leave the cut inside the name, so
    /// what came back would be a fragment the document does not hold. The framework quotes each name, which is what
    /// makes the second detectable: a fragment cut inside a name no longer opens with the quotation mark. Anything
    /// that fails either test goes back as nothing, and the bound is on the whole fragment rather than on each name
    /// because a page of names is as unreadable as one long one.
    /// </remarks>
    private static string? QuotedNamesIn(string candidate) =>
        candidate.Length is > 1 and <= 512
        && candidate.StartsWith('\'')
        && candidate.EndsWith('\'')
        && !candidate.Any(char.IsControl)
            ? candidate
            : null;
}
