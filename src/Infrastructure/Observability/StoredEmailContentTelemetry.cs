// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes a read of stored raw MIME as a span of its own, with what it cost in bytes.</summary>
/// <remarks>
/// <para>
/// Every other query this deployment issues returns columns sized like a row. This one returns a whole message, so it
/// is the one place a read's duration is explained by how much it moved rather than by how much it searched — and the
/// database span beneath it reports a command duration without ever saying how large the payload was. A read of a
/// forty-megabyte message and a read of a two-kilobyte one are otherwise the same line in a trace.
/// </para>
/// <para>
/// The span carries a size and whether anything was there, and never the stored identity, the account, the folder, or
/// any part of the message. The identity alone would open a series per message, and the payload is mail.
/// </para>
/// </remarks>
internal sealed class StoredEmailContentTelemetry
{
    /// <summary>The name a read of one email's stored raw MIME opens its span under.</summary>
    internal const string ReadSpanName = "read_stored_email_content";

    internal const string ByteLengthTagName = "mailfathom.mail.content.bytes";
    internal const string FoundTagName = "mailfathom.mail.content.found";

    /// <summary>Opens the span one content read is reported as, and returns the scope that ends it.</summary>
    /// <returns>The scope, which the caller must dispose after recording what the read found.</returns>
    public ContentReadScope BeginRead() => new(Telemetry.ActivitySource.StartActivity(ReadSpanName));

    /// <summary>Carries one read of stored raw MIME from the span that opens it to what it turned out to hold.</summary>
    /// <remarks>
    /// A read that reported neither outcome is one that threw, and the span says so rather than publishing a size
    /// nobody measured.
    /// </remarks>
    internal sealed class ContentReadScope(Activity? activity) : IDisposable
    {
        private bool reported;

        /// <summary>Records the content that was read, and how many bytes of it there were.</summary>
        /// <param name="byteLength">The length of the raw MIME the read returned.</param>
        public void Found(long byteLength)
        {
            this.reported = true;

            activity?.SetTag(FoundTagName, true);
            activity?.SetTag(ByteLengthTagName, byteLength);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        /// <summary>Records an email whose content this deployment holds none of, which is not a failure.</summary>
        public void Absent()
        {
            this.reported = true;

            activity?.SetTag(FoundTagName, false);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!this.reported)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            activity?.Dispose();
        }
    }
}
