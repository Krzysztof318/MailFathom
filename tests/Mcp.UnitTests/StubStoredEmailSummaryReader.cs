// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Domain.Emails;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Answers a lookup with one fixed summary and records that it was asked.</summary>
/// <remarks>
/// The identity it was asked for is kept, because the tool owns the conversion from a caller's text into that identity
/// and the value storage received is the observable result of it. The count is kept so a test can prove that a refusal
/// at the boundary never reached storage at all.
/// </remarks>
internal sealed class StubStoredEmailSummaryReader(EmailSummary? summary = null) : IStoredEmailSummaryReader
{
    /// <summary>Gets the identity the last lookup named, or <see langword="null" /> when nothing was looked up.</summary>
    public StoredEmailId? LastStoredEmailId { get; private set; }

    /// <summary>Gets how many lookups were issued.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc />
    public Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.LastStoredEmailId = storedEmailId;
        this.ReadCount++;

        return Task.FromResult(summary);
    }
}
