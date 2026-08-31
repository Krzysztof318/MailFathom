// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.TestSupport;

/// <summary>Builds the screening the outbox and the draft book are exercised against.</summary>
/// <remarks>
/// <para>
/// Two shapes, because every consumer of it asserts both: a deployment that screens nothing must reach the write with
/// nothing parsed and nothing scanned, and a deployment that screens something must refuse before anything is written
/// down. The inactive one is a plain argument rather than something a test holds, for the reason the inactive guard is:
/// it owns no redactor and therefore has nothing to release.
/// </para>
/// <para>
/// The reader behind both reads the bytes as the message's plain-text body rather than parsing MIME. What these tests
/// are about is where the screen sits and what a refusal costs, so the parse has to be uninteresting — a test asserting
/// that a draft was not written must not be able to fail because a MIME structure did or did not round-trip. The real
/// reader is covered where it lives, against messages the composer actually produced.
/// </para>
/// </remarks>
internal static class OutgoingMailScreenings
{
    /// <summary>Builds the screening of a deployment that screens nothing at all.</summary>
    /// <returns>A screening that answers without parsing the message or constructing a detector.</returns>
    internal static OutgoingMailScreening Inactive() =>
        new(
            new PlainTextOutgoingMailTextReader(),
            new SensitiveContentEgressScreen(
                FixedSensitiveContentPostures.ScanningNothing(),
                new RecordingSensitiveContentEgressTelemetry(),
                TimeProvider.System));

    /// <summary>Builds the screening a switched-on deployment's screen answers for.</summary>
    /// <param name="screen">The screen, which <see cref="ScanningSensitiveContentEgress" /> holds the redaction behind.</param>
    /// <returns>A screening that reads the bytes as one plain-text body and judges it through that screen.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen" /> is <see langword="null" />.</exception>
    internal static OutgoingMailScreening Through(SensitiveContentEgressScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        return new OutgoingMailScreening(new PlainTextOutgoingMailTextReader(), screen);
    }

    /// <summary>Reads a test's bytes back as the one body representation the message carries.</summary>
    private sealed class PlainTextOutgoingMailTextReader : IOutgoingMailTextReader
    {
        public Task<OutgoingMailText> ReadAsync(
            ReadOnlyMemory<byte> rawMime,
            CancellationToken cancellationToken)
        {
            if (rawMime.IsEmpty)
            {
                throw new ArgumentException("A screened message carries MIME.", nameof(rawMime));
            }

            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                new OutgoingMailText(
                    Subject: string.Empty,
                    Encoding.UTF8.GetString(rawMime.Span),
                    HtmlBody: null));
        }
    }
}
