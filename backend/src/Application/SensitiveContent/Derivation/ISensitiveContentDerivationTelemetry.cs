// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Redaction;

namespace MailFathom.Application.SensitiveContent.Derivation;

/// <summary>Reports what redacting one derived write found and what it cost, without reporting any of it.</summary>
/// <remarks>
/// <para>
/// The derived path is measured apart from the guarded egress points rather than as a fourth one of them, because the
/// two answer different operational questions. An egress figure says what a request is paying while somebody waits for
/// it; this one says what synchronization and the backfills are paying to fill a mailbox, which is where a scan budget
/// is actually spent on a deployment that has just switched a scanner on.
/// </para>
/// <para>
/// <b>Nothing recorded through this port is mail or derived from it.</b> A category name, a scanner name, a count, a
/// character count, and a duration are all it carries — never the detected value, the text it sat in, or the message it
/// came from, each of which would put the credential in the telemetry written to prove it was removed.
/// </para>
/// </remarks>
public interface ISensitiveContentDerivationTelemetry
{
    /// <summary>Records one text redacted on its way into the derived store.</summary>
    /// <param name="redacted">What the redaction produced, read for its findings and its dropped remainder.</param>
    /// <param name="elapsed">How long the scan added to the derivation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redacted" /> is <see langword="null" />.</exception>
    void RecordDerived(RedactedText redacted, TimeSpan elapsed);

    /// <summary>Records one derived write refused because the scanner guarding it could not answer.</summary>
    /// <param name="scanner">Which switched-on scanner could not answer.</param>
    void RecordRefused(SensitiveContentScannerKind scanner);
}
