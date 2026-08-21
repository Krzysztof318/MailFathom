// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Domain.Spam;

/// <summary>Identifies the terms a verdict was reached under, as a digest of the operator's own classification settings.</summary>
/// <remarks>
/// <para>
/// Derived rather than declared, for the reason a rule set's revision is: an authored version key can be forgotten, and
/// an edit that moves a threshold while leaving the key alone is exactly the case a record has to be able to tell apart.
/// It is what makes "this message was already decided under the terms in force now" a question somebody can ask of an
/// existing record, which is what a run over a whole mailbox skips mail on.
/// </para>
/// <para>
/// The digest covers only what changes a verdict: whether a scanner is consulted, and the threshold its score is judged
/// by. The scanned folder scope is deliberately outside it — adding a folder changes which mail is classified and not
/// what any classification would have concluded, so including it would force a rescan of mail nothing about the decision
/// has moved for. The scanner's own rule corpus is outside it too, because a classification records that separately and
/// under the name the scanner gives it.
/// </para>
/// <para>
/// It carries nothing derived from a message, so a record naming a profile and a log line reporting one hold nothing
/// personal.
/// </para>
/// </remarks>
public readonly record struct SpamClassificationProfile
{
    /// <summary>How many hexadecimal characters of the digest the identity keeps.</summary>
    /// <remarks>
    /// Twelve characters are forty-eight bits, far more than enough to tell apart the handful of settings one deployment
    /// passes through and short enough to read in a log line without being elided.
    /// </remarks>
    public const int LengthInCharacters = 12;

    /// <summary>Separates the digested fields, chosen because neither of them can contain it.</summary>
    private const char FieldSeparator = '\u001F';

    private readonly string? value;

    private SpamClassificationProfile(string value) => this.value = value;

    /// <summary>Gets whether this value names a profile rather than the unusable struct default.</summary>
    /// <remarks>
    /// A classification recorded before the profile became part of the record carries none, which is what an unspecified
    /// value means when it is read back. A run treats such a record as decided under terms it cannot compare, and
    /// therefore as mail to classify again rather than as mail to skip.
    /// </remarks>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the identity, as lowercase hexadecimal.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a profile.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a profile.");

    /// <summary>Derives the identity of the settings a classification is reached under.</summary>
    /// <param name="usesScanner">Whether a configured scanner is consulted after the deterministic stage.</param>
    /// <param name="scannerThreshold">The score a scanner's verdict is judged by, or <see langword="null" /> to keep the scanner's own.</param>
    /// <returns>The profile those settings are known by.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scannerThreshold" /> is not a finite number.</exception>
    /// <remarks>
    /// A threshold left to the scanner and a threshold configured to whatever the scanner already uses are deliberately
    /// different profiles. What the scanner's own threshold is, is not something MailFathom knows before it has asked,
    /// so treating the two as one would mean claiming an equality that cannot be established.
    /// </remarks>
    public static SpamClassificationProfile Create(bool usesScanner, double? scannerThreshold)
    {
        if (scannerThreshold is { } threshold && !double.IsFinite(threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scannerThreshold),
                threshold,
                "A configured scanner threshold is a finite number.");
        }

        var canonicalForm = string.Join(
            FieldSeparator,
            usesScanner ? "scanner" : "deterministic",
            scannerThreshold?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalForm));

        return new SpamClassificationProfile(Convert.ToHexStringLower(digest)[..LengthInCharacters]);
    }

    /// <summary>Reads back a profile this system derived earlier and recorded.</summary>
    /// <param name="value">The recorded identity.</param>
    /// <returns>The profile, which compares equal to a freshly derived one of the same settings.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not an identity this type could have produced.</exception>
    /// <remarks>
    /// The shape is checked rather than trusted, because a value that is not one this type produces would compare unequal
    /// to every profile and silently make every classified message look like mail to score again.
    /// </remarks>
    public static SpamClassificationProfile Restore(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != LengthInCharacters
            || !value.All(static character =>
                char.IsAsciiDigit(character) || (char.IsAsciiLetterLower(character) && character <= 'f')))
        {
            throw new ArgumentException(
                $"A spam classification profile is exactly {LengthInCharacters} lowercase hexadecimal characters.",
                nameof(value));
        }

        return new SpamClassificationProfile(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";
}
