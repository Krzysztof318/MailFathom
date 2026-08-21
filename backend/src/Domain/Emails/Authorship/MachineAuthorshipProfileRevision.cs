// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Domain.Emails.Authorship;

/// <summary>Names the weighting one stored authorship likelihood was reached under.</summary>
/// <remarks>
/// <para>
/// A likelihood outlives the profile that produced it. Weights are tuned, a signal is added, a threshold moves — and
/// every message already assessed keeps the number it was assessed with, because rewriting stored numbers would change
/// what a reader was shown without anybody having reread the mail. The revision is what makes that legible rather than
/// silent: two likelihoods carrying different revisions were reached under different weightings, and one carrying
/// <see cref="None" /> was assessed by nothing at all.
/// </para>
/// <para>
/// It is a digest of the profile's own weights and thresholds rather than a version number somebody remembers to
/// raise, for the reason <see cref="Authentication.SenderTrustPolicyRevision" /> is a digest of a list: a number
/// maintained by hand is a number that is wrong the first time a weight moves without it, and the whole value of this
/// column is that it cannot be.
/// </para>
/// </remarks>
public readonly record struct MachineAuthorshipProfileRevision
{
    /// <summary>The number of characters a revision is written as, which every column holding one is sized for.</summary>
    public const int Length = 32;

    /// <summary>How many bytes of the digest are kept, which is what <see cref="Length" /> hexadecimal characters carry.</summary>
    private const int RetainedDigestBytes = Length / 2;

    private readonly string? value;

    private MachineAuthorshipProfileRevision(string value) => this.value = value;

    /// <summary>Gets the revision that names no profile, which is what a message nothing assessed carries.</summary>
    public static MachineAuthorshipProfileRevision None => default;

    /// <summary>Gets the revision as the text a column stores, or the empty string for <see cref="None" />.</summary>
    /// <remarks>
    /// The field behind it is nullable and this is not, because <see cref="None" /> is the default instance and a
    /// default struct never runs a constructor — so the absence is answered here rather than left for every caller to
    /// meet as a <see langword="null" /> the annotation said could not happen.
    /// </remarks>
    public string Value => this.value ?? string.Empty;

    /// <summary>Gets whether this revision names a profile, which is false exactly for <see cref="None" />.</summary>
    public bool NamesAProfile => !string.IsNullOrEmpty(this.Value);

    /// <summary>Derives the revision of a profile from the weighted signals and thresholds it judges by.</summary>
    /// <param name="weights">Each signal the profile weighs, paired with the weight it carries.</param>
    /// <param name="thresholds">Each band boundary the profile reads the likelihood against.</param>
    /// <returns>The revision, which is <see cref="None" /> when the profile weighs nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The statements are sorted and joined with a separator none of them may contain, so the revision follows what a
    /// profile decides rather than the order its table happens to be written in. A profile weighing nothing assesses
    /// nothing and is deliberately indistinguishable from no profile at all, which is what a deployment that turned the
    /// assessment off has.
    /// </remarks>
    public static MachineAuthorshipProfileRevision Of(
        IReadOnlyDictionary<MachineAuthorshipSignals, double> weights,
        IReadOnlyList<double> thresholds)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (weights.Count == 0)
        {
            return None;
        }

        var statements = weights
            .Select(static weight => Statement(weight.Key.ToString(), weight.Value))
            .Concat(thresholds.Select(static (threshold, index) =>
                Statement(FormattableString.Invariant($"band{index}"), threshold)));

        var canonical = string.Join('\n', statements.Order(StringComparer.Ordinal));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return new MachineAuthorshipProfileRevision(Convert.ToHexStringLower(digest.AsSpan(0, RetainedDigestBytes)));
    }

    /// <summary>Reads back a revision a column stored.</summary>
    /// <param name="stored">The stored text, or <see langword="null" /> where the row carries none.</param>
    /// <returns>The revision, which is <see cref="None" /> when the row carries none.</returns>
    /// <remarks>
    /// Nothing re-derives a stored revision, so nothing here re-checks one either: the value is opaque to every reader
    /// and is only ever compared with another revision for equality.
    /// </remarks>
    public static MachineAuthorshipProfileRevision FromStoredValue(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? None : new MachineAuthorshipProfileRevision(stored.Trim());

    /// <inheritdoc />
    public override string ToString() => this.Value;

    /// <summary>Writes one weighting decision in the invariant form the digest is taken over.</summary>
    /// <remarks>
    /// The round-trip format is what keeps a weight from digesting the same as a neighbouring one that merely prints
    /// the same, and the invariant culture is what keeps the revision from following the host's decimal separator.
    /// </remarks>
    private static string Statement(string name, double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}={value:R}");
}
