// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ContactRelationships;

/// <summary>What a relationship derivation reports about itself, carrying no part of the card and no part of the correspondence.</summary>
/// <remarks>
/// A card is composed out of somebody's mail and about a named person, so it inherits the classification of both and
/// nothing here names a word of either: how much correspondence the run read, how many observations survived, and the
/// endpoint that answered are the whole of what is safe to report. No contact, no address, no subject, and no file name
/// is named.
/// </remarks>
internal static partial class ContactRelationshipEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The contact-relationship agent at {EndpointAlias} derived a card from {ConversationCount} conversations and {DocumentCount} documents, with {ObservationCount} observations beside the note.")]
    internal static partial void LogDerived(
        ILogger logger,
        string endpointAlias,
        int conversationCount,
        int documentCount,
        int observationCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "The contact-relationship agent at {EndpointAlias} answered with nothing this build could read as a card, so the contact is drawn without one.")]
    internal static partial void LogAnswerUnreadable(ILogger logger, string endpointAlias);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "The contact-relationship agent at {EndpointAlias} derived no card: this deployment has spent what it allows a provider for the period, so the contact is drawn without one.")]
    internal static partial void LogAllowanceExhausted(ILogger logger, string endpointAlias);
}
