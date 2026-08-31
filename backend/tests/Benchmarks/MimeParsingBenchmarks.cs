// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using BenchmarkDotNet.Attributes;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.TestSupport;

namespace MailFathom.Benchmarks;

/// <summary>How long extracting one large message's metadata takes, and what it allocates.</summary>
/// <remarks>
/// The path every synchronized message takes. What the report is watched for is the shape of the number across
/// releases: a mailbox of twenty thousand messages is re-read whenever extraction changes, so a parse that becomes
/// half as fast is hours of a backfill rather than milliseconds of a request.
/// </remarks>
public class MimeParsingBenchmarks
{
    private readonly MimeKitEmailMimeReader reader = new(
        new EmailMimeExtractionOptions(),
        new NoTrustedAuthentication(),
        localSenderVerifier: null);

    private RemoteEmailContent content = null!;

    /// <summary>Composes the message every iteration reads, outside what is measured.</summary>
    [GlobalSetup]
    public void ComposeMessage() => this.content = LargeSyntheticMessage.AsFetched();

    /// <summary>Extracts the metadata of one large multipart message.</summary>
    /// <returns>What the extraction produced, returned so nothing about it can be optimized away.</returns>
    [Benchmark]
    public Task<EmailMimeExtractionResult> ReadMetadata() =>
        this.reader.ReadMetadataAsync(this.content, SyntheticMailOwner.Deployment, CancellationToken.None);
}
