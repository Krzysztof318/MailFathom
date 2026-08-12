// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Domain.Failures;

/// <summary>Identifies a failure MailFathom raised deliberately, as a five-digit code stable enough to publish.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the number is the
/// identity: it is what a log records, what an alert matches, and what a support conversation names. An enum member's
/// ordinal would carry no meaning outside this assembly, and its name would change with every rename of the failure it
/// belongs to.
/// </para>
/// <para>
/// The code reads as <c>C S NNN</c>: the first digit is the <see cref="Category" />, the second is the
/// <see cref="Subcategory" /> within it, and the last three number the failure inside that subcategory. A reader who
/// sees <c>21001</c> knows it is a mail-protocol failure about authentication before looking anything up.
/// </para>
/// <para>
/// Numbers are allocated once and never reused or renumbered, for the same reason an enum member's value is never
/// reordered: a code that changes meaning silently invalidates every runbook, alert, and log search written against it.
/// Being a struct, <see langword="default" /> is reachable and names no failure; <see cref="IsSpecified" /> reports
/// that, and every failure reaches its code through a declared member, so the default cannot arrive from a raised
/// exception.
/// </para>
/// <para>
/// The cost against an enum is that the members are not compile-time constants, so a boundary translates them through a
/// lookup rather than through a <see langword="switch" /> over constants.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailFathomErrorCodeJsonConverter))]
public readonly record struct MailFathomErrorCode
{
    private MailFathomErrorCode(int value) => this.Value = value;

    #region Category 1 — Configuration and transport security

    /// <summary>Gets subcategory 1, transport security policy: a configured combination would weaken protection in a way no opt-in allows.</summary>
    public static MailFathomErrorCode MailTransportSecurityPolicyViolated { get; } = new(11001);

    /// <summary>Gets subcategory 2, configuration sources: the deployment's configuration-source settings name a path that is absent or a setting that does not exist.</summary>
    public static MailFathomErrorCode ProvisionedConfigurationSourceInvalid { get; } = new(12001);

    /// <summary>Gets subcategory 2, configuration sources: a setting only the process environment can deliver carries a value that came from somewhere else.</summary>
    public static MailFathomErrorCode EnvironmentOnlySettingMisplaced { get; } = new(12002);

    /// <summary>Gets subcategory 3, mailbox access tokens: an account's authorization server did not issue an access token its OAuth mechanisms require.</summary>
    public static MailFathomErrorCode MailAccessTokenUnavailable { get; } = new(13001);

    /// <summary>Gets subcategory 3, mailbox access tokens: an operator-driven authorization run did not produce a refresh token to provision.</summary>
    public static MailFathomErrorCode MailboxAuthorizationFailed { get; } = new(13002);

    #endregion

    #region Category 2 — Mail protocol

    /// <summary>Gets subcategory 1, authentication: a mail server advertises no authentication mechanism the account's policy permits.</summary>
    public static MailFathomErrorCode MailAuthenticationMechanismUnavailable { get; } = new(21001);

    /// <summary>Gets subcategory 2, session availability: a mail server did not serve an operation within the resilience budget configured for it.</summary>
    public static MailFathomErrorCode MailboxUnavailable { get; } = new(22001);

    /// <summary>Gets subcategory 3, folder identity: a folder was reselected with a UIDVALIDITY that makes the session's identities name different emails.</summary>
    public static MailFathomErrorCode MailboxFolderRecreated { get; } = new(23001);

    /// <summary>Gets subcategory 4, answer completeness: a mail server answered for an email without the data items the command requested.</summary>
    public static MailFathomErrorCode MailboxAnswerIncomplete { get; } = new(24001);

    /// <summary>Gets subcategory 5, mutation support: a mail server advertises no extension able to carry a requested change safely.</summary>
    /// <remarks>
    /// It is a subcategory of its own rather than one more availability failure, because the two say opposite things
    /// about repeating the work. An unavailable mailbox is expected to serve the same operation on a later run; a server
    /// that advertises no way to remove one message without removing others will still advertise none tomorrow, so the
    /// operation is refused rather than deferred.
    /// </remarks>
    public static MailFathomErrorCode MailboxMutationUnsupported { get; } = new(25001);

    /// <summary>Gets subcategory 5, mutation support: a command that must never be issued twice went out and its answer never came back.</summary>
    /// <remarks>
    /// It is its own code because it is the one mutation failure that must not be retried. <c>UID COPY</c> issued twice
    /// puts two messages in the destination folder, and nothing in the mailbox afterwards distinguishes a copy
    /// MailFathom made from one a person made, so the mutation is left in its recorded stage for a person or for
    /// convergence to resolve rather than attempted again.
    /// </remarks>
    public static MailFathomErrorCode MailboxMutationOutcomeUnknown { get; } = new(25002);

    /// <summary>Gets subcategory 5, mutation support: a mutation spent its bounded attempts without completing.</summary>
    /// <remarks>
    /// The code names the bound rather than whatever failed on the way, which stays on the record as the last failure.
    /// A mutation reaching this is visible as stuck instead of being retried forever.
    /// </remarks>
    public static MailFathomErrorCode MailboxMutationAttemptsExhausted { get; } = new(25003);

    /// <summary>Gets subcategory 5, mutation support: a mutation ended in a failure this system does not classify.</summary>
    /// <remarks>
    /// A mutation record needs a code for every failure it can end in, because that field is what an operator reads. A
    /// failure MailFathom did not raise itself has none of its own, and one generic code is the honest answer rather
    /// than borrowing the nearest classified one.
    /// </remarks>
    public static MailFathomErrorCode MailboxMutationFailedUnexpectedly { get; } = new(25004);

    /// <summary>Gets subcategory 5, mutation support: the folder a relocation or a copy names as its destination does not exist on the server.</summary>
    /// <remarks>
    /// It sits beside <see cref="MailboxMutationUnsupported" /> rather than among the availability failures for the same
    /// reason that one does: a folder the server does not have is not a round trip that went badly, and asking again
    /// every interval would spend a login apiece to be told the same thing. The remedy is an operator's — recreate the
    /// folder, or correct whatever asked for that path — so the mutation is given up on visibly at the first refusal
    /// instead of after its attempt bound.
    /// </remarks>
    public static MailFathomErrorCode MailboxMutationDestinationMissing { get; } = new(25005);

    /// <summary>Gets subcategory 6, folder creation: a mail server refused to create the folder a mapping asked for.</summary>
    /// <remarks>
    /// It is a subcategory of its own rather than one more mutation failure, because creating a folder is not one of the
    /// four mutations at all: it changes the shape of a mailbox rather than a message in one, and it is reached through a
    /// port no path that moves mail can obtain. Keeping it apart is also what makes it readable beside
    /// <see cref="MailboxMutationDestinationMissing" />, which is the code an alias that resolves to nothing produces —
    /// a quota, a namespace that forbids the name, or a name the server will not accept says something different from a
    /// folder nobody has.
    /// </remarks>
    public static MailFathomErrorCode RemoteFolderCreationRefused { get; } = new(26001);

    #endregion

    #region Category 3 — Persistence

    /// <summary>Gets subcategory 1, concurrent writes: a local write did not commit because another writer changed the same durable state.</summary>
    public static MailFathomErrorCode PersistenceConcurrencyConflict { get; } = new(31001);

    /// <summary>Gets subcategory 2, schema state: the database does not carry every migration the running build was compiled against.</summary>
    public static MailFathomErrorCode DatabaseSchemaOutOfDate { get; } = new(32001);

    /// <summary>Gets subcategory 2, schema state: the migration history could not be read, so the schema is of unknown shape.</summary>
    public static MailFathomErrorCode DatabaseSchemaStateUnreadable { get; } = new(32002);

    /// <summary>Gets subcategory 2, schema state: the lexical index was built with a different text search configuration than the one configured.</summary>
    public static MailFathomErrorCode DatabaseSchemaTextSearchConfigurationMismatch { get; } = new(32003);

    /// <summary>Gets subcategory 3, vector indexes: the approximate index one embedding profile's vectors are searched through is not in the state its lifecycle asked for.</summary>
    /// <remarks>
    /// <para>
    /// One code covers a build that did not happen and a removal that did not, because both leave the same finding for
    /// an operator to act on: the index a profile's lifecycle calls for is not the index the database holds. Which of
    /// the two it was is in the message, which names the profile.
    /// </para>
    /// <para>
    /// It is a subcategory of its own rather than one more schema-state failure, because the state it describes is not
    /// the migration history. This index belongs to no migration at all — it is tied to one profile's dimension, so it
    /// is built when that profile is activated — and a database missing it is serving correct results slowly rather
    /// than running against a schema the build does not recognize.
    /// </para>
    /// </remarks>
    public static MailFathomErrorCode EmbeddingVectorIndexUnavailable { get; } = new(33001);

    #endregion

    #region Category 4 — Outbound resilience

    /// <summary>Gets subcategory 1, pipeline rejection: a resilience pipeline declined to serve an operation against an outbound dependency any further.</summary>
    public static MailFathomErrorCode OutboundDependencyUnavailable { get; } = new(41001);

    #endregion

    #region Category 5 — The MCP boundary

    /// <summary>Gets subcategory 1, request validation: a mailbox query asked for a page size outside the range the query serves.</summary>
    public static MailFathomErrorCode MailboxQueryPageSizeOutOfRange { get; } = new(51001);

    /// <summary>Gets subcategory 1, request validation: one filter of a mailbox query carries a value, a count, or a length the query does not accept.</summary>
    public static MailFathomErrorCode MailboxQueryFilterInvalid { get; } = new(51002);

    /// <summary>Gets subcategory 1, request validation: an email search asked for more ranked results than the search serves.</summary>
    public static MailFathomErrorCode EmailSearchResultLimitOutOfRange { get; } = new(51003);

    /// <summary>Gets subcategory 1, request validation: a request named an email by text that is not an identifier this system issues.</summary>
    /// <remarks>
    /// It is separate from <see cref="StoredEmailNotFound" /> because the two answer different questions: this one says
    /// the request never named an email at all, while that one says an email was named and is not held here. Reporting
    /// a malformed identifier as an absent email would tell a caller that a typo is a message someone deleted.
    /// </remarks>
    public static MailFathomErrorCode StoredEmailIdentifierMalformed { get; } = new(51004);

    /// <summary>Gets subcategory 1, request validation: a content read named no emails, or more emails than one call serves.</summary>
    /// <remarks>
    /// One code covers both ends of the range, as <see cref="MailboxQueryPageSizeOutOfRange" /> does for a page size: a
    /// call naming nothing and a call naming too much are the same finding about the count the caller chose, and neither
    /// is served by a truncated answer that would hide which emails were dropped.
    /// </remarks>
    public static MailFathomErrorCode EmailContentReadCountOutOfRange { get; } = new(51005);

    /// <summary>Gets subcategory 1, request validation: a content read named the same email more than once.</summary>
    /// <remarks>
    /// Serving it twice would spend the read's character budget on content the caller already has, and silently
    /// collapsing it would return fewer entries than were named, which a caller reading results positionally cannot
    /// detect. Refusing says which of the two the caller meant is theirs to decide.
    /// </remarks>
    public static MailFathomErrorCode EmailContentReadDuplicateEmail { get; } = new(51006);

    /// <summary>Gets subcategory 2, pagination: a continuation cursor is not one this system issued.</summary>
    public static MailFathomErrorCode MailboxQueryCursorMalformed { get; } = new(52001);

    /// <summary>Gets subcategory 2, pagination: a continuation cursor was issued for a different set of filters than the request carries.</summary>
    public static MailFathomErrorCode MailboxQueryCursorFilterMismatch { get; } = new(52002);

    /// <summary>Gets subcategory 3, access: a request named a mail account this deployment does not serve.</summary>
    public static MailFathomErrorCode MailAccountNotAccessible { get; } = new(53001);

    /// <summary>Gets subcategory 3, access: a request named an email the local mailbox copy holds no row for.</summary>
    public static MailFathomErrorCode StoredEmailNotFound { get; } = new(53002);

    /// <summary>Gets subcategory 4, undiagnosed failure: a tool call failed for a reason the boundary deliberately does not describe.</summary>
    /// <remarks>
    /// This is the one code every failure that is not already an allocated one collapses into, so a client learns that
    /// the call failed and nothing about why. The detail stays in the server log, correlated by the trace the request
    /// already carries. It is the only code in this category a tool boundary raises itself rather than reports on behalf
    /// of a use case.
    /// </remarks>
    public static MailFathomErrorCode McpToolFailedUnexpectedly { get; } = new(54001);

    /// <summary>Gets subcategory 5, local consistency: an email exists locally, but the content stored for it is missing, damaged, or unreadable.</summary>
    /// <remarks>
    /// It is separate from <see cref="StoredEmailNotFound" /> because the two say different things about the same
    /// request: one names an email that was never stored here, the other an email that is stored and whose body this
    /// deployment cannot currently serve. Only the second one schedules repair, and a caller that could not tell them
    /// apart would retry the wrong one. It is a subcategory of its own rather than one more access failure, because a
    /// caller can act on it: the local copy is being repaired, so the request is worth repeating.
    /// </remarks>
    public static MailFathomErrorCode EmailContentUnavailable { get; } = new(55001);

    /// <summary>Gets subcategory 6, capability: a request asked for something this deployment does not currently serve.</summary>
    /// <remarks>
    /// A subcategory of its own because it is about the deployment rather than about the request: nothing the caller
    /// wrote caused it, and no rewriting of the request reaches an answer. It is separate from the access failures for
    /// the same reason — an account this deployment does not serve is a refusal about that caller's request, while this
    /// says the capability is absent for everyone. One code covers a capability that was never configured and one that
    /// is momentarily unable to run, because the message says which and neither is something a client can act on beyond
    /// deciding whether to ask again.
    /// </remarks>
    public static MailFathomErrorCode MailAnsweringUnavailable { get; } = new(56001);

    /// <summary>Gets subcategory 7, spend ceilings: answering a question would exceed a ceiling this deployment configured on what it spends.</summary>
    /// <remarks>
    /// Separate from the capability failure above because the deployment is working and nothing is degraded: the
    /// operator declared how much answering may cost and that much has been spent. It is the one refusal on this
    /// surface a caller can act on by waiting, so it is not collapsed into a code whose whole meaning is that waiting
    /// changes nothing. One code covers the ceiling on a single run and the ceiling over a period, because the message
    /// says which and neither names a number the caller could have influenced.
    /// </remarks>
    public static MailFathomErrorCode MailAnsweringBudgetExhausted { get; } = new(57001);

    #endregion

    #region Category 6 — Embedding providers

    /// <summary>Gets subcategory 1, credentials: an embedding provider refused the credential this deployment presented.</summary>
    /// <remarks>
    /// Separate from every availability failure because the two ask opposite things of the operator: an unreachable
    /// endpoint is waited out, while a refused credential stays refused until somebody rotates it. It is also the one
    /// provider failure that must never be repeated, since repeating a rejected key spends the account's request
    /// budget to receive the same answer.
    /// </remarks>
    public static MailFathomErrorCode EmbeddingProviderCredentialRejected { get; } = new(61001);

    /// <summary>Gets subcategory 2, availability: no endpoint of the declared chain served an embedding request within the budget configured for it.</summary>
    /// <remarks>A rate limit, a timeout, and an unreachable endpoint collapse into this one code, because each says the same thing to the work that asked: the vectors belong to a later run.</remarks>
    public static MailFathomErrorCode EmbeddingProviderUnavailable { get; } = new(62001);

    /// <summary>Gets subcategory 3, answer shape: a provider returned a vector the declared geometry does not describe.</summary>
    /// <remarks>
    /// Raised at the adapter rather than left to the database's dimension check, so a width the model was never asked
    /// for is named where the model was called instead of surfacing later as a rejected row with no provider in sight.
    /// </remarks>
    public static MailFathomErrorCode EmbeddingVectorShapeUnexpected { get; } = new(63001);

    #endregion

    #region Category 7 — Chat providers

    /// <summary>Gets subcategory 1, credentials: a chat provider refused the credential this deployment presented.</summary>
    /// <remarks>
    /// Separate from the embedding category rather than shared with it, because the two providers are configured
    /// independently and fail independently: an instance may hold a working embedding credential and a rejected chat
    /// one, and a single code would leave an operator rotating the key that was never refused.
    /// </remarks>
    public static MailFathomErrorCode ChatProviderCredentialRejected { get; } = new(71001);

    /// <summary>Gets subcategory 2, availability: the declared chat endpoint did not answer within the budget configured for it.</summary>
    /// <remarks>A rate limit, a timeout, an unreachable endpoint, and a request the provider rejected outright collapse into this one code, because each says the same thing to the work that asked: no answer exists to present.</remarks>
    public static MailFathomErrorCode ChatProviderUnavailable { get; } = new(72001);

    /// <summary>Gets subcategory 3, answer shape: a chat provider ended the call with no text to present.</summary>
    /// <remarks>
    /// Raised at the adapter rather than passed on as an empty answer, because an empty string reaching a caller reads
    /// as a model that had nothing to say rather than as a call that produced nothing, and the two lead an operator to
    /// different places.
    /// </remarks>
    public static MailFathomErrorCode ChatAnswerEmpty { get; } = new(73001);

    #endregion

    #region Category 8 — Sensitive-content scanning

    /// <summary>Gets subcategory 1, availability: a scanner that guards content leaving the process could not produce findings.</summary>
    /// <remarks>
    /// One code covers a detector that is unreachable, one that did not answer inside the configured scan timeout, and
    /// one that failed outright, because each says the same thing to the operation it guards: nothing established that
    /// this text is safe to hand on, so the operation fails rather than passing the text through. An operator switched
    /// the scanner on, and a scan that could not run must never be the same outcome as a scan that found nothing.
    /// </remarks>
    public static MailFathomErrorCode SensitiveContentScannerUnavailable { get; } = new(81001);

    #endregion

    /// <summary>Gets every allocated code.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailFathomErrorCode> All { get; } =
    [
        MailTransportSecurityPolicyViolated,
        ProvisionedConfigurationSourceInvalid,
        EnvironmentOnlySettingMisplaced,
        MailAccessTokenUnavailable,
        MailboxAuthorizationFailed,
        MailAuthenticationMechanismUnavailable,
        MailboxUnavailable,
        MailboxFolderRecreated,
        MailboxAnswerIncomplete,
        MailboxMutationUnsupported,
        MailboxMutationOutcomeUnknown,
        MailboxMutationAttemptsExhausted,
        MailboxMutationFailedUnexpectedly,
        MailboxMutationDestinationMissing,
        RemoteFolderCreationRefused,
        PersistenceConcurrencyConflict,
        DatabaseSchemaOutOfDate,
        DatabaseSchemaStateUnreadable,
        DatabaseSchemaTextSearchConfigurationMismatch,
        EmbeddingVectorIndexUnavailable,
        OutboundDependencyUnavailable,
        MailboxQueryPageSizeOutOfRange,
        MailboxQueryFilterInvalid,
        EmailSearchResultLimitOutOfRange,
        StoredEmailIdentifierMalformed,
        EmailContentReadCountOutOfRange,
        EmailContentReadDuplicateEmail,
        MailboxQueryCursorMalformed,
        MailboxQueryCursorFilterMismatch,
        MailAccountNotAccessible,
        StoredEmailNotFound,
        McpToolFailedUnexpectedly,
        EmailContentUnavailable,
        MailAnsweringUnavailable,
        MailAnsweringBudgetExhausted,
        EmbeddingProviderCredentialRejected,
        EmbeddingProviderUnavailable,
        EmbeddingVectorShapeUnexpected,
        ChatProviderCredentialRejected,
        ChatProviderUnavailable,
        ChatAnswerEmpty,
        SensitiveContentScannerUnavailable,
    ];

    /// <summary>Gets the five-digit code.</summary>
    public int Value { get; }

    /// <summary>Gets whether this value names an allocated code rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Value is not 0;

    /// <summary>Gets the subsystem the failure belongs to, which is the code's first digit.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than an allocated code.</exception>
    public int Category => this.IsSpecified
        ? this.Value / 10000
        : throw new InvalidOperationException("The value is the default of the struct and belongs to no category.");

    /// <summary>Gets the concern within the category, which is the code's second digit.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than an allocated code.</exception>
    public int Subcategory => this.IsSpecified
        ? this.Value / 1000 % 10
        : throw new InvalidOperationException("The value is the default of the struct and belongs to no subcategory.");

    /// <summary>Parses a recorded five-digit code back into the value it names.</summary>
    /// <param name="value">The number read from a log, an alert, or a serialized error.</param>
    /// <param name="errorCode">The parsed code when the number is allocated; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the number is an allocated code; otherwise <see langword="false" />.</returns>
    /// <remarks>An unallocated number is not accepted, so a code retired or mistyped is recognized as unknown rather than reconstructed as a value nothing raises.</remarks>
    public static bool TryParse(int value, out MailFathomErrorCode errorCode)
    {
        // No allocated code is the struct default, so an unmatched number yields the unspecified value the caller
        // already receives when parsing fails.
        errorCode = All.FirstOrDefault(candidate => candidate.Value == value);

        return errorCode.IsSpecified;
    }

    /// <summary>Returns the five-digit code, so a log or an error response records the number rather than the structure.</summary>
    /// <returns>The code formatted as five digits, or a marker when the value is the struct default.</returns>
    public override string ToString() => this.IsSpecified
        ? this.Value.ToString("D5", CultureInfo.InvariantCulture)
        : "(unspecified)";
}

/// <summary>Serializes <see cref="MailFathomErrorCode" /> as its five-digit number.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the number for the same reason the value object
/// exists: the number is the published identity, and a member name would change with a rename that the code is meant
/// to survive.
/// </remarks>
public sealed class MailFathomErrorCodeJsonConverter : JsonConverter<MailFathomErrorCode>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a number or does not name an allocated code.</exception>
    public override MailFathomErrorCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException($"An error code must be a JSON number, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetInt32());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailFathomErrorCode value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteNumberValue(SpecifiedValueOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name is not a number or does not name an allocated code.</exception>
    public override MailFathomErrorCode ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var propertyName = reader.GetString();

        // Parsing the number first would reduce "022001" to 22001 and accept a spelling this converter never writes,
        // so two keys could name one code and a round trip would not return the document it read.
        if (propertyName is not { Length: 5 } || !propertyName.All(char.IsAsciiDigit))
        {
            throw new JsonException($"'{propertyName}' is not a five-digit error code.");
        }

        return ParseOrThrow(int.Parse(propertyName, NumberStyles.None, CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailFathomErrorCode value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SpecifiedValueOrThrow(value).ToString("D5", CultureInfo.InvariantCulture));
    }

    private static MailFathomErrorCode ParseOrThrow(int value)
    {
        if (!MailFathomErrorCode.TryParse(value, out var errorCode))
        {
            throw new JsonException($"'{value}' is not an allocated MailFathom error code.");
        }

        return errorCode;
    }

    private static int SpecifiedValueOrThrow(MailFathomErrorCode errorCode) => errorCode.IsSpecified
        ? errorCode.Value
        : throw new JsonException("An unspecified error code cannot be serialized.");
}
