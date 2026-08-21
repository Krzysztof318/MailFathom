// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what collection concluded, in the order it concluded it.</summary>
/// <remarks>
/// The outcomes are the only thing collection reports at all, so asserting on them is how a test proves that an address
/// was refused for the reason it was meant to be refused rather than merely absent from the book.
/// </remarks>
internal sealed class RecordingContactCollectionTelemetry : IContactCollectionTelemetry
{
    private readonly List<ContactCollectionOutcome> outcomes = [];

    /// <summary>Gets what was concluded, in order.</summary>
    internal IReadOnlyList<ContactCollectionOutcome> Outcomes => this.outcomes;

    /// <inheritdoc />
    public void RecordOutcome(ContactCollectionOutcome outcome) => this.outcomes.Add(outcome);
}
