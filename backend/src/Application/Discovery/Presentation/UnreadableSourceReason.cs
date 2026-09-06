// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Says why a source a run found could not be read, so that a silence is never mistaken for an absence.</summary>
/// <remarks>
/// <para>
/// A source that exists, is named on a message, and yielded nothing is a different situation from a mailbox that holds
/// nothing on the subject, and the difference decides what somebody does next. An encrypted contract is opened with a
/// password; a scan is read by a person; a mailbox that holds nothing is a question asked somewhere else. A plan that
/// reported all three as an empty answer would send every one of those readers to the wrong place — and would leave a
/// run answering from a covering note while the contract that contradicts it sits unread.
/// </para>
/// <para>
/// The members are the granularity a reader acts on rather than the granularity extraction records. Reading an
/// attachment's text and describing an image each publish a longer set of their own — nine reasons apiece, several of
/// them about a provider or a ceiling an operator sets — and a client drawing one of those beside a fact would be
/// drawing this deployment's configuration into somebody's answer. Each member below states which of those it stands
/// for, so the mapping is one decision recorded in one place.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<UnreadableSourceReason>))]
public enum UnreadableSourceReason
{
    /// <summary>The source is protected and this deployment holds no password for it.</summary>
    /// <remarks>What an encrypted document records.</remarks>
    Encrypted = 0,

    /// <summary>The source does not hold what it declares itself to be, which is ordinary of real mail rather than exceptional.</summary>
    /// <remarks>What malformed document bytes and an unreadable image header record.</remarks>
    Corrupt = 1,

    /// <summary>Nothing here reads the source's format.</summary>
    /// <remarks>
    /// What an unrecognized format, a recognized format nothing parses, an unsupported image format, and a format an
    /// operator excluded all record. They differ in whether a later release could read the source and in nothing a
    /// reader of an answer can act on.
    /// </remarks>
    FormatNotRead = 2,

    /// <summary>The source is larger than this deployment reads, so it was not read.</summary>
    /// <remarks>
    /// What an oversized input, an oversized yield, a container that passed a structural ceiling, an image past the
    /// request ceiling, and a declared pixel grid past the decoder ceiling all record.
    /// </remarks>
    TooLarge = 3,

    /// <summary>The source was read and carried no text, which is what a scan with no text layer is.</summary>
    /// <remarks>
    /// The one member that is not a failure. Extraction succeeded and every page yielded nothing, which is a fact about
    /// the document rather than about this deployment, and it is the case a reader most often resolves by opening it.
    /// </remarks>
    NoTextFound = 4,

    /// <summary>Nothing offered the source a reader, because this deployment does not read sources of its kind.</summary>
    /// <remarks>What a deployment with image description switched off records against every image it stored.</remarks>
    NotAttempted = 5,

    /// <summary>Reading the source was attempted and did not finish, and asking again later may succeed.</summary>
    /// <remarks>What a timeout, an unavailable provider, and a provider that refused all record.</remarks>
    ReadingFailed = 6,
}
