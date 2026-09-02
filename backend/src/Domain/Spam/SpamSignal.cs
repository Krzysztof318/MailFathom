// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>One fact a classification rests on, with where it came from.</summary>
/// <remarks>
/// <para>
/// A signal is a fact rather than a contribution to a score: it states what was observed, not how much that observation
/// moved a number. Two deployments that weigh the same observations differently therefore record the same signals, and a
/// verdict can be re-derived from them under different settings without the message being read again.
/// </para>
/// <para>
/// The observation is text a mail server or a scanner wrote, so it is treated as untrusted input and as personal data:
/// it can name a sending domain, and it is never written to a log or to telemetry. Only the count of signals, their
/// kinds, and their names are safe to report.
/// </para>
/// </remarks>
public sealed record SpamSignal
{
    /// <summary>The greatest length a signal name may carry.</summary>
    /// <remarks>
    /// Names are method names from RFC 8601 (<c>spf</c>, <c>dkim</c>, <c>dmarc</c>), header field names, folder aliases,
    /// and scanner rule names. All are short by construction, so an over-long one is a malformed source rather than a
    /// long name, and it is refused instead of shortened.
    /// </remarks>
    public const int MaximumNameLength = 128;

    /// <summary>The greatest length an observation may carry before it is shortened to it.</summary>
    /// <remarks>
    /// This one truncates rather than refusing, because the value is whatever a mail server chose to write and refusing
    /// it would discard a whole classification over a verbose header. Enough is kept for the result and its reason to be
    /// readable, and what a longer header holds beyond that is repetition of the properties already recorded as their
    /// own signals.
    /// </remarks>
    public const int MaximumObservationLength = 512;

    private SpamSignal(SpamSignalKind kind, string name, string? observation, SpamSignalProvenance provenance)
    {
        this.Kind = kind;
        this.Name = name;
        this.Observation = observation;
        this.Provenance = provenance;
    }

    /// <summary>Gets what kind of fact the signal states.</summary>
    public SpamSignalKind Kind { get; }

    /// <summary>Gets the name of the observed property: an authentication method, a header field, a folder alias, or a rule.</summary>
    public string Name { get; }

    /// <summary>Gets what was observed, or <see langword="null" /> when the signal is the observation.</summary>
    /// <remarks>
    /// A junk-folder placement and a scanner rule that fired carry nothing here: that they were observed at all is the
    /// whole fact. An authentication result and a provider header carry the value the source wrote.
    /// </remarks>
    public string? Observation { get; }

    /// <summary>Gets where the signal was read from.</summary>
    public SpamSignalProvenance Provenance { get; }

    /// <summary>Records one observed fact.</summary>
    /// <param name="kind">What kind of fact it is.</param>
    /// <param name="name">The name of the observed property.</param>
    /// <param name="observation">What was observed, or <see langword="null" /> when the fact is the observation itself.</param>
    /// <param name="provenance">Where it was read from.</param>
    /// <returns>The signal, with the observation shortened to <see cref="MaximumObservationLength" /> when the source wrote a longer one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provenance" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the name is blank, over-long, or carries a control character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> is not a defined member.</exception>
    public static SpamSignal Create(
        SpamSignalKind kind,
        string name,
        string? observation,
        SpamSignalProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A signal states one of the kinds of fact this system reads.");
        }

        return new SpamSignal(kind, CheckedName(name), ShortenedObservation(observation), provenance);
    }

    private static string CheckedName(string name)
    {
        var trimmed = name.Trim();

        if (trimmed.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"A signal name carries at most {MaximumNameLength} characters.",
                nameof(name));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A signal name cannot contain control characters.", nameof(name));
        }

        return trimmed;
    }

    /// <summary>Puts an observation into the one form a record keeps it in.</summary>
    /// <remarks>
    /// Header values are folded across lines, so the whitespace a source wrote says nothing and collapsing it is what
    /// makes two records of the same observation one value. Control characters are removed with it rather than refused,
    /// because a folded header legitimately carries them and the value is not MailFathom's own.
    /// </remarks>
    private static string? ShortenedObservation(string? observation)
    {
        if (string.IsNullOrWhiteSpace(observation))
        {
            return null;
        }

        var collapsed = string.Join(
            ' ',
            observation.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => new string([.. part.Where(static character => !char.IsControl(character))]))
                .Where(static part => part.Length > 0));

        return collapsed.Length switch
        {
            0 => null,
            > MaximumObservationLength => collapsed[..MaximumObservationLength],
            _ => collapsed,
        };
    }
}
