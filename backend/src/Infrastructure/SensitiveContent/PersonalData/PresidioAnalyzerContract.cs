// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Infrastructure.SensitiveContent.PersonalData;

/// <summary>The body of an analyze request, which is the whole of what MailFathom asks the analyzer.</summary>
/// <param name="Text">The bounded text to analyze.</param>
/// <param name="Language">The language the analyzer selects a model by.</param>
/// <param name="Entities">The entities to look for, which are the ones this deployment's categories map onto.</param>
/// <param name="ScoreThreshold">The confidence a finding must reach to be reported at all.</param>
/// <remarks>
/// <para>
/// The entity list is stated on every request rather than left out. Omitting it asks the analyzer for everything it can
/// recognise, so a category an operator did not switch on would arrive as findings this scanner would then have to filter
/// — work paid for on a machine that had no reason to do it, and one filter away from redacting something nobody asked to
/// hide.
/// </para>
/// <para>
/// The threshold is stated for a related reason and a sharper one. Left out, the analyzer defaults to zero and reports
/// every recognizer that fired at all: measured against the shipped image, a payment card number comes back as a bank
/// account number at 0.05 and an arbitrary run of characters as a driving licence at 0.01, both inside categories that are
/// on by default. Redaction acts on a finding without weighing it, so a request with no threshold is a request to replace
/// text nothing was found in.
/// </para>
/// </remarks>
internal sealed record PresidioAnalyzeRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("entities")] IReadOnlyList<string> Entities,
    [property: JsonPropertyName("score_threshold")] double ScoreThreshold)
{
    /// <inheritdoc />
    /// <remarks>
    /// Redacted by construction, because <see cref="Text" /> is mail content. A record's generated formatting prints every
    /// member, so one of these reaching a log line — through an exception message, a diagnostic, or an interpolation
    /// written later — would put the message body in it.
    /// </remarks>
    public override string ToString() => "***";
}

/// <summary>One entity the analyzer reported, as much of it as MailFathom reads.</summary>
/// <param name="EntityType">The analyzer's own name for what it found, which is the rule name inside a category.</param>
/// <param name="Start">The offset of the first character covered, counted in Unicode code points.</param>
/// <param name="End">The offset just past the last character covered, counted in Unicode code points.</param>
/// <param name="Score">How sure the analyzer was, from 0 to 1.</param>
/// <remarks>
/// <para>
/// <b>The offsets are code points and not UTF-16 code units.</b> The analyzer indexes a Python string, where an emoji, an
/// ideograph beyond the basic plane, and a flag each count as one; .NET counts each as two. A response mapped straight
/// onto a <see cref="Application.SensitiveContent.Detection.SensitiveContentSpan" /> would therefore be correct for every
/// text made only of basic-plane characters and quietly wrong for the rest — redacting a region shifted left of the one
/// that was found, which leaves part of the value in the text and destroys part of what surrounded it.
/// <see cref="CodePointOffsets" /> is the translation.
/// </para>
/// <para>
/// <c>analysis_explanation</c> is deliberately unmapped. It is populated only when a request asks for the decision
/// process, which this one never does, and it quotes the matched text.
/// </para>
/// </remarks>
internal sealed record PresidioRecognizedEntity(
    [property: JsonPropertyName("entity_type")] string? EntityType,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End,
    [property: JsonPropertyName("score")] double Score);

/// <summary>Serializes the analyzer's request and answers without reflection.</summary>
/// <remarks>
/// Both answers are bare JSON arrays rather than objects, which is why the serializable types are arrays: the analyze
/// endpoint answers with the entities it found and the supported-entities endpoint with the names it knows, neither of
/// them wrapped in an envelope.
/// </remarks>
[JsonSerializable(typeof(PresidioAnalyzeRequest))]
[JsonSerializable(typeof(PresidioRecognizedEntity[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class PresidioJsonContext : JsonSerializerContext;
