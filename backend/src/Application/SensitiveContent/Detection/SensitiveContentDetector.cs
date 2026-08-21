// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Application.SensitiveContent.Detection;

/// <summary>Identifies what produced a finding, and the revision of the corpus or profile it produced it under.</summary>
/// <remarks>
/// <para>
/// A finding is only reproducible against a stated detector and a stated revision. Two deployments running the same
/// switches and the same categories redact differently the moment one of them carries a newer rule corpus or a different
/// analyzer profile, so the revision travels with every finding rather than being read from whatever is installed when
/// somebody asks.
/// </para>
/// <para>
/// Both parts are MailFathom's own names for things it ships or deploys, never a remote party's, so both are safe to
/// record in a log, a metric label, and a stored attribution.
/// </para>
/// </remarks>
public sealed partial record SensitiveContentDetector
{
    private SensitiveContentDetector(string name, string revision)
    {
        this.Name = name;
        this.Revision = revision;
    }

    /// <summary>Gets the detector's name, which is stable across the revisions it runs under.</summary>
    public string Name { get; }

    /// <summary>Gets the revision of the rule corpus or the analyzer profile this detector ran under.</summary>
    public string Revision { get; }

    /// <summary>Creates a detector identity.</summary>
    /// <param name="name">The detector's stable name.</param>
    /// <param name="revision">The rule-corpus revision or analyzer-profile version it runs under.</param>
    /// <returns>The validated identity.</returns>
    /// <exception cref="ArgumentException">Thrown when either value is outside the grammar this type accepts.</exception>
    public static SensitiveContentDetector Create(string name, string revision)
    {
        if (name is null || !AcceptedValue.IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not an acceptable detector name. It may carry up to 64 letters, digits, dots, dashes, plus signs, and underscores, and must begin with a letter or a digit.",
                nameof(name));
        }

        if (revision is null || !AcceptedValue.IsMatch(revision))
        {
            throw new ArgumentException(
                $"'{revision}' is not an acceptable detector revision. It may carry up to 64 letters, digits, dots, dashes, plus signs, and underscores, and must begin with a letter or a digit.",
                nameof(revision));
        }

        return new SensitiveContentDetector(name, revision);
    }

    /// <inheritdoc />
    public override string ToString() => $"{this.Name}@{this.Revision}";

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedValue { get; }
}
