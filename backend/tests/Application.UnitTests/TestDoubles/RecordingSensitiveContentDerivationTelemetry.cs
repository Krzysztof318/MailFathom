// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Records what the derived-write guard reported, so a test can assert it without a metric listener.</summary>
/// <remarks>
/// Hand-written rather than substituted because what these tests assert is a sequence — which texts were redacted, in
/// which order, and which writes were refused — and a recorded list reports that without a matcher.
/// </remarks>
internal sealed class RecordingSensitiveContentDerivationTelemetry : ISensitiveContentDerivationTelemetry
{
    private readonly List<DerivedText> derived = [];
    private readonly List<SensitiveContentScannerKind> refused = [];

    /// <summary>Gets every text that was redacted on its way into the derived store, in order.</summary>
    public IReadOnlyList<DerivedText> Derived => this.derived;

    /// <summary>Gets the scanner behind every refused derived write, in the order the refusals happened.</summary>
    public IReadOnlyList<SensitiveContentScannerKind> Refused => this.refused;

    /// <inheritdoc />
    public void RecordDerived(RedactedText redacted, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(redacted);

        this.derived.Add(new DerivedText(redacted, elapsed));
    }

    /// <inheritdoc />
    public void RecordRefused(SensitiveContentScannerKind scanner) => this.refused.Add(scanner);

    /// <summary>One text redacted on its way into the derived store.</summary>
    /// <param name="Redacted">What the redaction produced.</param>
    /// <param name="Elapsed">What the scan added to the derivation.</param>
    internal sealed record DerivedText(RedactedText Redacted, TimeSpan Elapsed);
}
