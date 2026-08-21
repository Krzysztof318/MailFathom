// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AllocationBudgets.UnitTests;

/// <summary>What opening one attachment and writing its octets out may allocate.</summary>
/// <remarks>
/// A download is where a whole file crosses the process, and it is the one path a reader waits on directly. The
/// reader's own contract is that nothing is buffered — the measuring pass discards what it decodes and the write
/// decodes straight into the destination — so a budget below the attachment's size is what states that contract as a
/// number.
/// </remarks>
public sealed class AttachmentDownloadAllocationBudgetTests
{
    /// <summary>The attachment this suite's message carries, which is the one a download opens.</summary>
    private const int FirstAttachmentPosition = 0;

    /// <summary>What one download may allocate, as a share of the message the attachment is parsed out of.</summary>
    /// <remarks>
    /// A read that materialized the file before writing it would allocate at least the attachment's decoded length,
    /// which on this message is most of the message; any share below one therefore fails that regression. A quarter is
    /// where it is set for the reason the extraction budget is: the parse and the copy buffer do not grow with the
    /// payload, so a message of several megabytes leaves the honest cost far below this.
    /// </remarks>
    private const double MaximumAllocatedShareOfMessage = 0.25;

    /// <summary>A download decodes a multi-megabyte attachment into the destination without holding it.</summary>
    [Fact]
    public async Task OpenAsync_LargeAttachment_StaysWithinItsAllocationBudget()
    {
        // Arrange
        var reader = new MimeKitEmailAttachmentContentReader(new EmailMimeExtractionOptions());
        var content = LargeSyntheticMessage.AsStored();
        var cancellationToken = TestContext.Current.CancellationToken;
        var budgetBytes = (long)(content.RawMime.Length * MaximumAllocatedShareOfMessage);

        // Establishing that the run really opens and writes the whole attachment is a step of its own, so the measured
        // run can assert nothing and be charged for nothing but the work. The description's own measurement is what the
        // write is held to, because it is the length the download states before its first octet.
        var download = await DownloadAsync(reader, content, cancellationToken);
        Assert.Equal(download.DescribedOctets, download.WrittenOctets);
        Assert.True(download.WrittenOctets > 0, "The measured message's attachment decoded to nothing.");

        // Act, Assert
        await AllocationBudget.AssertWithinAsync(
            "Opening and writing out a large attachment",
            budgetBytes,
            () => DownloadAsync(reader, content, cancellationToken));
    }

    /// <summary>Opens the attachment and writes it to a destination that keeps nothing, reporting both lengths.</summary>
    /// <remarks>
    /// A counting destination rather than a buffer, because a buffer would be the caller's own copy of the file and
    /// would be charged to the path the budget is about.
    /// </remarks>
    private static async Task<(long DescribedOctets, long WrittenOctets)> DownloadAsync(
        MimeKitEmailAttachmentContentReader reader,
        StoredEmailContent content,
        CancellationToken cancellationToken)
    {
        var opened = await reader.OpenAsync(content, FirstAttachmentPosition, cancellationToken);

        await using var attachment = opened.Attachment
                                     ?? throw new InvalidOperationException(
                                         "The measured message carries no attachment at the position a download opens.");

        using var destination = new CountingStream();
        await attachment.WriteContentToAsync(destination, cancellationToken);

        return (attachment.Description.DecodedSizeOctets, destination.WrittenOctets);
    }

    /// <summary>A destination that counts what it is given and keeps none of it.</summary>
    private sealed class CountingStream : Stream
    {
        /// <summary>Gets how many octets have been written to this destination.</summary>
        public long WrittenOctets { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => this.WrittenOctets;

        /// <inheritdoc />
        public override long Position
        {
            get => this.WrittenOctets;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => this.WrittenOctets += count;

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer) => this.WrittenOctets += buffer.Length;

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.WrittenOctets += count;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.WrittenOctets += buffer.Length;

            return ValueTask.CompletedTask;
        }
    }
}
