// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An in-memory stand-in for the preview reader, holding the text a test arranged for each email.</summary>
/// <remarks>
/// It answers only for the emails it was given, exactly as the port promises: a message nothing has extracted is absent
/// from the answer rather than present with nothing in it. What each call asked for is recorded, because a page that
/// fetched previews for rows it did not return would be reading a message the caller never sees.
/// </remarks>
internal sealed class InMemoryStoredEmailPreviews : IStoredEmailPreviewReader
{
    private readonly Dictionary<StoredEmailId, string> previews = [];

    private readonly List<IReadOnlyList<StoredEmailId>> calls = [];

    /// <summary>Gets which emails each call to the port asked about, in order.</summary>
    public IReadOnlyList<IReadOnlyList<StoredEmailId>> Calls => this.calls;

    /// <summary>Records the text one email's preview is cut from.</summary>
    /// <param name="storedEmailId">The email the text belongs to.</param>
    /// <param name="preview">The text storage would answer with.</param>
    /// <returns>This reader, so arrangement reads as one statement.</returns>
    public InMemoryStoredEmailPreviews With(StoredEmailId storedEmailId, string preview)
    {
        this.previews[storedEmailId] = preview;

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<StoredEmailId, string>> ReadPreviewsAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add([.. storedEmailIds]);

        IReadOnlyDictionary<StoredEmailId, string> found = storedEmailIds
            .Where(this.previews.ContainsKey)
            .ToDictionary(storedEmailId => storedEmailId, storedEmailId => this.previews[storedEmailId]);

        return Task.FromResult(found);
    }
}
