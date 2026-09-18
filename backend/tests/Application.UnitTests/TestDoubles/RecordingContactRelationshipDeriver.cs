// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Relationship;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the provider call an opened contact's card is derived by, recording what it was scoped to.</summary>
internal sealed class RecordingContactRelationshipDeriver : IContactRelationshipDeriver
{
    private readonly ContactRelationship answer;

    /// <summary>Creates a deriver answering with one card, or with nothing where none is given.</summary>
    /// <param name="answer">What every derivation answers with.</param>
    internal RecordingContactRelationshipDeriver(ContactRelationship? answer = null) =>
        this.answer = answer ?? ContactRelationship.Nothing;

    /// <summary>Gets every brief the use case put to this deriver, in the order it did.</summary>
    internal List<ContactRelationshipBrief> Briefs { get; } = [];

    /// <inheritdoc />
    public Task<ContactRelationship> DeriveAsync(
        ContactRelationshipBrief brief,
        CancellationToken cancellationToken)
    {
        this.Briefs.Add(brief);

        return Task.FromResult(this.answer);
    }
}
