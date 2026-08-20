// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Digests;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.SensitiveContent.Derivation;

/// <summary>Identifies the sensitive-content configuration one piece of derived data was written under.</summary>
/// <remarks>
/// <para>
/// Derived data outlives the configuration that produced it. A chunk cut before the secret scanner was switched on was
/// cut from unredacted text and stays that way, and a chunk cut before a category was added is under-redacted against
/// the set the deployment now runs. Neither fact is readable from the text itself, so the row records what produced it —
/// the same statement <see cref="Emails.Embeddings.EmbeddingProfileFingerprint" /> makes about a vector.
/// </para>
/// <para>
/// Everything that decides what a redaction leaves behind is in the digest: which scanners ran, the corpus revision or
/// analyzer profile each ran under, the categories each looked for, the rules suppressed inside them, and the analyzed
/// ceiling. The ceiling is there because on this path it is not a cost control at all: a redaction returns the text cut
/// at it, and what is returned is what is stored, chunked, and embedded — so lowering it truncates every message derived
/// afterwards, and raising it back has to leave those rows readably stale rather than silently short.
/// </para>
/// <para>
/// The per-call timeout and the concurrency limit are out, because neither changes one character of what a scan that
/// finished produced. Those are the tuning a deployment does against its own load, and folding them in would mark a
/// whole mailbox stale for a change that altered nothing it stored.
/// </para>
/// <para>
/// The digest names no mail. It is computed over a deployment's own configured names and MailFathom's own revisions, so
/// unlike a chunk's content hash it identifies nothing personal and is safe in a log, a metric, and a stored column.
/// </para>
/// </remarks>
public readonly record struct SensitiveContentDerivationStamp
{
    /// <summary>The number of characters a hexadecimal SHA-256 digest occupies.</summary>
    public const int Length = 64;

    /// <summary>Names the scheme in the digest itself, so a later encoding cannot collide with this one.</summary>
    private const string HashDomain = "mailfathom.sensitive-content-derivation.v1";

    private SensitiveContentDerivationStamp(string value) => this.Value = value;

    /// <summary>Gets the digest as sixty-four lowercase hexadecimal characters.</summary>
    public string Value { get; }

    /// <summary>Computes the stamp of a deployment with at least one scanner switched on.</summary>
    /// <param name="plan">What this deployment scans for.</param>
    /// <param name="scanners">Every registered detector, of which the planned ones are read.</param>
    /// <returns>The stamp every derived row written under this configuration carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when the plan switches on a scanner nothing registered.</exception>
    /// <remarks>
    /// Category and rule names are sorted before they are hashed, so two deployments that named the same categories in a
    /// different order in their configuration files write the same stamp. Scanners are already in plan order, which
    /// <see cref="SensitiveContentPlan" /> fixes for the same reason.
    /// </remarks>
    public static SensitiveContentDerivationStamp Compute(
        SensitiveContentPlan plan,
        IEnumerable<ISensitiveContentScanner> scanners)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scanners);

        var registered = scanners.ToArray();

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        CanonicalDigest.AppendText(digest, HashDomain);
        CanonicalDigest.AppendNumber(digest, plan.Bounds.MaximumAnalyzedCharacters);
        CanonicalDigest.AppendNumber(digest, plan.Scanners.Count);

        foreach (var scannerPlan in plan.Scanners)
        {
            var detector = registered.FirstOrDefault(candidate => candidate.Scanner == scannerPlan.Scanner)?.Detector
                ?? throw SensitiveContentScannerUnavailableException.NotRegistered(scannerPlan.Scanner);

            CanonicalDigest.AppendNumber(digest, (int)scannerPlan.Scanner);
            CanonicalDigest.AppendText(digest, detector.Name);
            CanonicalDigest.AppendText(digest, detector.Revision);
            AppendNames(digest, [.. scannerPlan.Categories.Select(category => category.Name)]);
            AppendNames(digest, [.. scannerPlan.SuppressedRules.Select(rule => rule.ToString())]);
        }

        return new SensitiveContentDerivationStamp(Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    /// <summary>Reads back a stamp that was written earlier.</summary>
    /// <param name="value">The sixty-four lowercase hexadecimal characters a derived row carries.</param>
    /// <returns>The stamp.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the value is not a lowercase hexadecimal SHA-256 digest.</exception>
    public static SensitiveContentDerivationStamp Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!CanonicalDigest.IsHexadecimalDigest(value, Length))
        {
            throw new ArgumentException(
                $"A sensitive-content derivation stamp is {Length} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new SensitiveContentDerivationStamp(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;

    /// <summary>Writes a set of names in a fixed order, so two deployments that listed them differently write one stamp.</summary>
    private static void AppendNames(IncrementalHash digest, string[] names)
    {
        CanonicalDigest.AppendNumber(digest, names.Length);

        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            CanonicalDigest.AppendText(digest, name);
        }
    }
}
