// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AllocationBudgets.UnitTests;

/// <summary>What a content-store read may allocate once the provider has handed over the row.</summary>
/// <remarks>
/// <para>
/// The query itself needs a real database and is proved by the integration suite. What is unit-testable is everything
/// the read then does with the payload, which is where a copy would be taken: the projected columns become the
/// application's own value, and the caller verifies them against what the writer recorded. Both are supposed to work
/// over the provider's array in place — read-only views rather than copies, and a digest computed straight over the
/// span — so the budget is a small fixed number rather than a share of the message.
/// </para>
/// <para>
/// A share would be the wrong shape here. This path is meant to cost the same whether the message is a kilobyte or ten
/// megabytes, and stating that as a constant is what makes a copy of the payload fail the test at any size.
/// </para>
/// </remarks>
public sealed class StoredContentReadAllocationBudgetTests
{
    /// <summary>What turning one read row into verified stored content may allocate, whatever the message weighs.</summary>
    /// <remarks>
    /// A few value objects and the digest buffer are the honest cost, which is tens of bytes rather than thousands. It
    /// is set at a quarter of a mebibyte because a process-wide measurement carries whatever the test host allocated on
    /// its own threads during the run, and hashing a multi-megabyte payload leaves that window milliseconds wide — noise
    /// worth far more headroom than this path's own cost is. It still fails the regression it exists for by a factor of
    /// twenty: the measured message is several megabytes, so one copy of the payload is nowhere near this.
    /// </remarks>
    private const long MaximumAllocatedBytes = 256 * 1024;

    /// <summary>Reading stored content back costs the same whatever the message weighs, because nothing is copied.</summary>
    [Fact]
    public async Task ToStoredContent_LargeMessage_StaysWithinItsAllocationBudget()
    {
        // Arrange
        var stored = LargeSyntheticMessage.AsStored();
        var rawMime = stored.RawMime.ToArray();
        var recordedHash = stored.RecordedSha256Hash.ToArray();
        var row = new StoredEmailContentRow(
            rawMime,
            rawMime.LongLength,
            recordedHash,
            ContentStorageBackend.Database,
            ObjectLocator: null,
            CarriesDatabasePayload: true);

        // Establishing that the run really verifies the payload is a step of its own, so the measured run can assert
        // nothing: an intact copy is what a read of ordinary mail meets, and a defect here would make the measurement
        // about a refusal rather than about a read.
        Assert.Null(row.ToStoredContent(rawMime).FindIntegrityDefect());

        // Act, Assert
        await AllocationBudget.AssertWithinAsync(
            "Reading a large message back out of the content store",
            MaximumAllocatedBytes,
            () =>
            {
                _ = row.ToStoredContent(rawMime).FindIntegrityDefect();

                return Task.CompletedTask;
            });
    }
}
