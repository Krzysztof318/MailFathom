// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Names the trusted-sender policy one stored verdict was reached under.</summary>
/// <remarks>
/// <para>
/// A verdict outlives the policy that produced it. An operator adds a domain, a reader trusts a correspondent, an
/// account is removed — and every message already judged keeps the answer it was judged with, because rewriting stored
/// verdicts would change what a reader was shown without anybody having reread the mail. The revision is what makes
/// that legible rather than silent: two verdicts carrying different revisions were reached under different lists, and
/// one carrying <see cref="None" /> was never put to a policy at all.
/// </para>
/// <para>
/// It is a digest of the effective policy rather than a counter, so it needs no stored state to allocate it and the
/// same list always names itself the same way — including after a restart, and including on a second deployment
/// configured identically. It is not a secret and protects nothing; what it has to do is differ whenever the list does.
/// </para>
/// </remarks>
public readonly record struct SenderTrustPolicyRevision
{
    /// <summary>The number of characters a revision is written as, which every column holding one is sized for.</summary>
    public const int Length = 32;

    /// <summary>How many bytes of the digest are kept, which is what <see cref="Length" /> hexadecimal characters carry.</summary>
    private const int RetainedDigestBytes = Length / 2;

    private SenderTrustPolicyRevision(string value) => this.Value = value;

    /// <summary>Gets the revision that names no policy, which is what a verdict no policy produced carries.</summary>
    public static SenderTrustPolicyRevision None => default;

    /// <summary>Gets the revision as the text a column stores, or the empty string for <see cref="None" />.</summary>
    public string Value { get; }

    /// <summary>Gets whether this revision names a policy, which is false exactly for <see cref="None" />.</summary>
    public bool NamesAPolicy => !string.IsNullOrEmpty(this.Value);

    /// <summary>Derives the revision of a policy from the statements it is made of.</summary>
    /// <param name="statements">Every statement the policy verifies by, each already in its comparison form.</param>
    /// <returns>The revision, which is <see cref="None" /> when the policy verifies nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="statements" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The statements are sorted and joined with a separator none of them may contain, so the revision follows what a
    /// policy says rather than the order an operator happened to write it in — reordering a list is not a change to it,
    /// and a deployment that reordered one must not have every later verdict read as reached under a different policy.
    /// A policy with nothing to say verifies nothing and is deliberately indistinguishable from no policy at all.
    /// </remarks>
    public static SenderTrustPolicyRevision Of(IEnumerable<string> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var canonical = string.Join('\n', statements.Order(StringComparer.Ordinal));

        if (canonical.Length == 0)
        {
            return None;
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return new SenderTrustPolicyRevision(Convert.ToHexStringLower(digest.AsSpan(0, RetainedDigestBytes)));
    }

    /// <summary>Reads back a revision a column stored.</summary>
    /// <param name="stored">The stored text, or <see langword="null" /> where the row carries none.</param>
    /// <returns>The revision, which is <see cref="None" /> when the row carries none.</returns>
    /// <remarks>
    /// Nothing re-derives a stored revision, so nothing here re-checks one either: the value is opaque to every reader
    /// and is only ever compared with another revision for equality.
    /// </remarks>
    public static SenderTrustPolicyRevision FromStoredValue(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? None : new SenderTrustPolicyRevision(stored.Trim());

    /// <inheritdoc />
    public override string ToString() => this.Value ?? string.Empty;
}
