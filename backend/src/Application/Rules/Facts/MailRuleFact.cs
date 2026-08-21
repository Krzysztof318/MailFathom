// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Rules.Facts;

/// <summary>Identifies one thing a rule condition may read about an email, together with the shape of value it carries.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration rather than a C# <see langword="enum" />, because a fact is inseparable from the
/// name an owner writes in a condition: that name is a published authoring surface, it has to survive any rename of the
/// member here, and the value shape the type checker judges a comparison against travels with it. A separate mapping
/// table from member to name and type is exactly the pair that drifts apart.
/// </para>
/// <para>
/// The set is closed on purpose, and closing it is what bounds the cost of a condition. An expression reaches nothing
/// but the facts declared here, every one of them is derived from a single email that has already been stored, and the
/// one fact that costs a read of stored content says so in <see cref="ReadsStoredContent" /> so that a resolver can
/// leave it alone until a condition names it.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a fact. It reports itself through
/// <see cref="IsSpecified" />, and the places that must reject it are <see cref="TryParseName" />, which never produces
/// it from an undeclared name, and the JSON converter, which refuses to write it.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailRuleFactJsonConverter))]
public readonly record struct MailRuleFact
{
    private readonly string? name;

    private MailRuleFact(string name, MailRuleFactType valueType, bool readsStoredContent = false)
    {
        this.name = name;
        this.ValueType = valueType;
        this.ReadsStoredContent = readsStoredContent;
    }

    #region Where the email is

    /// <summary>Gets the configured alias of the account the email belongs to.</summary>
    public static MailRuleFact Account { get; } = new("account", MailRuleFactType.Text);

    /// <summary>Gets the configured alias of the folder the email occurrence is in.</summary>
    public static MailRuleFact Folder { get; } = new("folder", MailRuleFactType.Text);

    /// <summary>Gets the special-use role the folder plays for its account, absent when configuration gave it none.</summary>
    /// <remarks>
    /// It is a fact of its own rather than a second spelling of <see cref="Folder" />, because a condition compares one
    /// value against one literal: a single fact answering to both an alias and a role would make
    /// <c>folder == 'Junk'</c> true for a folder actually called <c>Junk</c> and for whichever folder plays that role,
    /// and an operator could no longer write a condition meaning only one of them. Naming this one is how a rule reaches
    /// <em>the junk folder of whichever account this email came from</em> without knowing what each account called it.
    /// </remarks>
    public static MailRuleFact FolderRole { get; } = new("folderRole", MailRuleFactType.Text);

    #endregion

    #region Who wrote it and who it reached

    /// <summary>Gets the subject line, absent when the email carries none.</summary>
    public static MailRuleFact Subject { get; } = new("subject", MailRuleFactType.Text);

    /// <summary>Gets the sender's address in its comparison form, absent when the email names no sender.</summary>
    public static MailRuleFact SenderAddress { get; } = new("senderAddress", MailRuleFactType.Text);

    /// <summary>Gets the part of the sender's address after the at sign, absent when the email names no sender.</summary>
    public static MailRuleFact SenderDomain { get; } = new("senderDomain", MailRuleFactType.Text);

    /// <summary>Gets the addresses the email was sent to and copied to, in their comparison form.</summary>
    public static MailRuleFact RecipientAddresses { get; } = new("recipientAddresses", MailRuleFactType.TextSet);

    /// <summary>Gets the distinct domains of every recipient address.</summary>
    public static MailRuleFact RecipientDomains { get; } = new("recipientDomains", MailRuleFactType.TextSet);

    /// <summary>Gets what the receiving server established about the author the email displays.</summary>
    /// <remarks>
    /// The conclusion rather than the evidence behind it: <c>authenticated</c>, <c>failed</c>, or
    /// <c>notEstablished</c>. What a condition reads was stored when the email was extracted, so a rule judges what a
    /// receiving server reported at the time rather than re-evaluating a policy now.
    /// </remarks>
    public static MailRuleFact AuthorAuthentication { get; } = new("authorAuthentication", MailRuleFactType.Text);

    /// <summary>Gets whether this deployment recognizes the author the email authenticated as.</summary>
    /// <remarks>
    /// <c>trusted</c> or <c>unknown</c>, and a classification this deployment made rather than an authentication
    /// result. <c>unknown</c> is the ordinary state of legitimate mail from a correspondent nobody has named, which is
    /// why a rule acting on it usually names <see cref="AuthorAuthentication" /> beside it.
    /// </remarks>
    public static MailRuleFact SenderTrust { get; } = new("senderTrust", MailRuleFactType.Text);

    #endregion

    #region When it arrived and how large it is

    /// <summary>Gets when the last receiving hop recorded the email, absent when no hop recorded one.</summary>
    public static MailRuleFact ReceivedAt { get; } = new("receivedAt", MailRuleFactType.Timestamp);

    /// <summary>Gets when the sender's client stamped the email, absent when it carries no such header.</summary>
    public static MailRuleFact SentAt { get; } = new("sentAt", MailRuleFactType.Timestamp);

    /// <summary>Gets how many days have passed since the email was received, absent when nothing recorded that.</summary>
    public static MailRuleFact AgeInDays { get; } = new("ageInDays", MailRuleFactType.Number);

    /// <summary>Gets the size of the whole email as the server reported it.</summary>
    public static MailRuleFact SizeInBytes { get; } = new("sizeInBytes", MailRuleFactType.Number);

    #endregion

    #region What it carries

    /// <summary>Gets how many attachments the email carries.</summary>
    public static MailRuleFact AttachmentCount { get; } = new("attachmentCount", MailRuleFactType.Number);

    /// <summary>Gets the size of every attachment added together.</summary>
    public static MailRuleFact AttachmentTotalBytes { get; } = new("attachmentTotalBytes", MailRuleFactType.Number);

    /// <summary>Gets whether the email's body is encrypted.</summary>
    public static MailRuleFact IsEncrypted { get; } = new("isEncrypted", MailRuleFactType.Boolean);

    /// <summary>Gets whether the email carries a signature part. Nothing has verified it, and the name says so.</summary>
    public static MailRuleFact CarriesUnverifiedSignature { get; } = new("carriesUnverifiedSignature", MailRuleFactType.Boolean);

    #endregion

    #region What the server says about it

    /// <summary>Gets whether the server reports the email as read.</summary>
    public static MailRuleFact IsSeen { get; } = new("isSeen", MailRuleFactType.Boolean);

    /// <summary>Gets whether the server reports the email as answered.</summary>
    public static MailRuleFact IsAnswered { get; } = new("isAnswered", MailRuleFactType.Boolean);

    /// <summary>Gets whether the server reports the email as flagged.</summary>
    public static MailRuleFact IsFlagged { get; } = new("isFlagged", MailRuleFactType.Boolean);

    /// <summary>Gets whether the server reports the email as a draft.</summary>
    public static MailRuleFact IsDraft { get; } = new("isDraft", MailRuleFactType.Boolean);

    /// <summary>Gets the keywords the server reports the email as carrying.</summary>
    /// <remarks>
    /// The set a rule's own keyword actions write to, read back under the case-insensitive comparison those actions
    /// are declared under, so a rule adding <c>$Todo</c> is selected by a later rule naming <c>$todo</c>. An email
    /// carrying none, and one whose folder keeps no keywords at all, both read as the empty set.
    /// </remarks>
    public static MailRuleFact Keywords { get; } = new("keywords", MailRuleFactType.TextSet);

    #endregion

    #region What was extracted from it

    /// <summary>Gets whether text has been extracted from the email's body, which is what makes <see cref="BodyText" /> readable.</summary>
    public static MailRuleFact HasExtractedContent { get; } = new("hasExtractedContent", MailRuleFactType.Boolean);

    /// <summary>Gets the text extracted from the email's body, absent while no extraction has run for it.</summary>
    /// <remarks>
    /// The one fact that costs a read of stored content, which is why a condition naming none of it never pays for one.
    /// Guarding it with <see cref="HasExtractedContent" /> is not required — an email with nothing extracted reads as an
    /// absent value rather than as a failure.
    /// </remarks>
    public static MailRuleFact BodyText { get; } = new("bodyText", MailRuleFactType.Text, readsStoredContent: true);

    /// <summary>Gets how much the email's own text reads as machine written.</summary>
    /// <remarks>
    /// The band rather than the number behind it: <c>likely</c>, <c>possible</c>, <c>unlikely</c>, or
    /// <c>notAssessed</c>. The number is a heuristic comparable only within one weighting, so a rule written against a
    /// threshold would change meaning the next time that weighting moved, while a band survives it. <c>notAssessed</c>
    /// is what an email with no readable body carries, what a deployment assessing nothing records, and what mail
    /// stored before the assessment existed carries until it is re-read.
    /// </remarks>
    public static MailRuleFact MachineAuthorship { get; } = new("machineAuthorship", MailRuleFactType.Text);

    #endregion

    /// <summary>Gets every declared fact.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailRuleFact> All { get; } =
    [
        Account,
        Folder,
        FolderRole,
        Subject,
        SenderAddress,
        SenderDomain,
        RecipientAddresses,
        RecipientDomains,
        AuthorAuthentication,
        SenderTrust,
        ReceivedAt,
        SentAt,
        AgeInDays,
        SizeInBytes,
        AttachmentCount,
        AttachmentTotalBytes,
        IsEncrypted,
        CarriesUnverifiedSignature,
        IsSeen,
        IsAnswered,
        IsFlagged,
        IsDraft,
        Keywords,
        HasExtractedContent,
        BodyText,
        MachineAuthorship,
    ];

    /// <summary>Gets whether this value names a declared fact rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the shape of value the fact carries.</summary>
    public MailRuleFactType ValueType { get; }

    /// <summary>Gets whether resolving the fact reads the email's stored content rather than its already-loaded metadata.</summary>
    public bool ReadsStoredContent { get; }

    /// <summary>Gets the name a condition refers to the fact by.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a fact.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a fact.");

    /// <summary>Resolves the fact an expression identifier names, matching case exactly.</summary>
    /// <param name="name">The identifier written in a condition.</param>
    /// <param name="fact">The declared fact when the name is one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a declared fact; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Case is significant, unlike the SASL names elsewhere in this repository. A fact name is written in a condition
    /// beside operators and literals, so accepting <c>SENDERDOMAIN</c> as well as <c>senderDomain</c> would leave the
    /// documented surface and the accepted surface saying different things, and a rule set would stop reading uniformly
    /// the moment two people wrote it differently.
    /// </remarks>
    public static bool TryParseName(string? name, out MailRuleFact fact)
    {
        fact = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // No declared fact is the struct default, so an unmatched name yields the unspecified value the caller already
        // receives when parsing fails.
        fact = All.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        return fact.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="MailRuleFact" /> as the name a condition refers to it by.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the authoring name for the same reason the value
/// object exists: it is the identity the documentation, the configuration file, and the type checker already agree on,
/// while an ordinal position would silently change meaning if the declared set were ever reordered.
/// </remarks>
public sealed class MailRuleFactJsonConverter : JsonConverter<MailRuleFact>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a declared fact.</exception>
    public override MailRuleFact Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A mail rule fact must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, MailRuleFact value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a declared fact.</exception>
    public override MailRuleFact ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailRuleFact value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static MailRuleFact ParseOrThrow(string? name)
    {
        if (!MailRuleFact.TryParseName(name, out var fact))
        {
            throw new JsonException($"'{name}' is not a declared mail rule fact.");
        }

        return fact;
    }

    private static string NameOrThrow(MailRuleFact fact) => fact.IsSpecified
        ? fact.Name
        : throw new JsonException("An unspecified mail rule fact cannot be serialized.");
}
