// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Signals;

/// <summary>Reaches a verdict from what a message already carried, with no scanner, no network, and no model.</summary>
/// <remarks>
/// <para>
/// This is the whole of the working feature without a sidecar deployed. Mail that reaches an IMAP mailbox has usually
/// been scored already: the receiving server recorded SPF, DKIM, and DMARC outcomes in <c>Authentication-Results</c>,
/// ARC preserved them across the forwarding hops where the first two legitimately break, the provider wrote its own
/// verdict into an <c>X-Spam-*</c> header, and where it decided the message was junk it filed it in the folder it
/// advertises for that. Every one of those is a fact from the moment it mattered, with network context nothing after
/// delivery has.
/// </para>
/// <para>
/// What decides the verdict is deliberately narrower than what is recorded. A provider's own verdict decides, and a
/// junk-folder placement outranks it because that is a decision somebody already acted on. An authentication failure
/// does not: a DMARC failure is something the receiving server saw and chose to deliver anyway, and turning it into a
/// spam verdict here would file mail the operator's own provider decided to accept. It is recorded as its own signal so
/// a reader can see it, which is what the record is for.
/// </para>
/// </remarks>
public sealed class DeterministicSpamClassifier
{
    /// <summary>The header the receiving server writes its own outcomes into.</summary>
    private const string AuthenticationResultsField = "Authentication-Results";

    /// <summary>The header an ARC set preserves a previous hop's outcomes in.</summary>
    private const string ForwardedAuthenticationResultsField = "ARC-Authentication-Results";

    /// <summary>The word <see cref="ProviderSpamHeaderFields.SpamFlag" /> carries for a spam verdict.</summary>
    private const string AffirmativeFlag = "YES";

    /// <summary>The word <see cref="ProviderSpamHeaderFields.SpamStatus" /> opens with for a spam verdict.</summary>
    private const string AffirmativeStatus = "Yes";

    /// <summary>The word <see cref="ProviderSpamHeaderFields.SpamStatus" /> opens with for a clean verdict.</summary>
    private const string NegativeStatus = "No";

    private const string ScoreProperty = "score=";

    private const string ThresholdProperty = "required=";

    /// <summary>Reads a verdict out of one message's headers and where the mailbox filed it.</summary>
    /// <param name="facts">What the message's headers carried.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder the occurrence was stored from.</param>
    /// <param name="isJunkFolder">Whether that folder is the one the account advertises as its junk folder.</param>
    /// <returns>The verdict, the assessment when a source carried both numbers, and every observed fact.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="facts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing here reads the body, and nothing here writes. The same headers produce the same reading whenever it runs,
    /// which is what lets a classification be recomputed for an occurrence without the message being fetched again.
    /// </remarks>
    public DeterministicSpamReading Read(
        SpamHeaderFacts facts,
        MailFolderAlias folderAlias,
        bool isJunkFolder)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var providerVerdict = ProviderVerdict(facts.ProviderHeaders);

        IReadOnlyList<SpamSignal> signals =
        [
            .. PlacementSignals(folderAlias, isJunkFolder),
            .. AuthenticationSignals(facts.AuthenticationResults),
            .. ProviderSignals(facts.ProviderHeaders),
        ];

        // The placement outranks the header because it is a decision already acted on: the message is in the folder the
        // operator's own provider files junk into, whatever any header of it says.
        var verdict = isJunkFolder ? SpamVerdict.Spam : providerVerdict.Verdict;

        return new DeterministicSpamReading(verdict, providerVerdict.Assessment, signals);
    }

    private static IEnumerable<SpamSignal> PlacementSignals(MailFolderAlias folderAlias, bool isJunkFolder)
    {
        if (!isJunkFolder)
        {
            return [];
        }

        return
        [
            SpamSignal.Create(
                SpamSignalKind.JunkFolderPlacement,
                folderAlias.Value,
                observation: null,
                SpamSignalProvenance.FromFolderPlacement(folderAlias.Value)),
        ];
    }

    private static IEnumerable<SpamSignal> AuthenticationSignals(
        IReadOnlyList<MessageAuthenticationResult> results) => results.Select(static result => SpamSignal.Create(
            result.IsForwarded
                ? SpamSignalKind.ForwardedSenderAuthentication
                : SpamSignalKind.SenderAuthentication,
            result.Method,
            result.Detail is { Length: > 0 }
                ? string.Concat(result.Result, " ", result.Detail)
                : result.Result,
            SpamSignalProvenance.FromMessageHeader(result.IsForwarded
                ? ForwardedAuthenticationResultsField
                : AuthenticationResultsField)));

    private static IEnumerable<SpamSignal> ProviderSignals(IReadOnlyList<ProviderSpamHeaderValue> headers) =>
        headers.Select(static header => SpamSignal.Create(
            SpamSignalKind.ProviderSpamVerdict,
            header.FieldName,
            header.Value,
            SpamSignalProvenance.FromMessageHeader(header.FieldName)));

    /// <summary>Reads the verdict and, where one source carried both numbers, the assessment the provider reached.</summary>
    /// <remarks>
    /// <para>
    /// The flag is read before the status because it is the unambiguous one: a single word with two accepted values,
    /// where the status field's grammar is a verdict followed by properties whose order and presence vary by server.
    /// A message carrying both and disagreeing with itself is answered by the flag.
    /// </para>
    /// <para>
    /// An assessment is recorded only when <see cref="ProviderSpamHeaderFields.SpamStatus" /> carries the score and the
    /// threshold together. A score without a threshold is a number in an unknown scale — a value of 6 is ordinary mail
    /// under one server's configuration and spam under another's — and pairing it with a threshold configured here for
    /// a different scanner would produce a comparison that reads as a measurement and is not one. The score is still
    /// recorded, as the signal the header itself is.
    /// </para>
    /// </remarks>
    private static (SpamVerdict Verdict, SpamAssessment? Assessment) ProviderVerdict(
        IReadOnlyList<ProviderSpamHeaderValue> headers)
    {
        var flagVerdict = FlagVerdict(Value(headers, ProviderSpamHeaderFields.SpamFlag));
        var status = Value(headers, ProviderSpamHeaderFields.SpamStatus);
        var statusVerdict = StatusVerdict(status);

        var verdict = flagVerdict is SpamVerdict.Undetermined ? statusVerdict : flagVerdict;

        return (verdict, StatusAssessment(status));
    }

    private static string? Value(IReadOnlyList<ProviderSpamHeaderValue> headers, string fieldName) => headers
        .FirstOrDefault(header => StringComparer.OrdinalIgnoreCase.Equals(header.FieldName, fieldName))
        ?.Value;

    private static SpamVerdict FlagVerdict(string? flag) => flag?.Trim() switch
    {
        null or "" => SpamVerdict.Undetermined,
        var value when StringComparer.OrdinalIgnoreCase.Equals(value, AffirmativeFlag) => SpamVerdict.Spam,
        _ => SpamVerdict.NotSpam,
    };

    /// <summary>Reads the verdict word the status field opens with, before its first comma or space.</summary>
    /// <remarks>
    /// Anything the field opens with that is neither word leaves the verdict undetermined rather than defaulting to
    /// either: an unrecognized grammar is a server this reading does not understand, and guessing would either file
    /// ordinary mail or publish a clean verdict nobody reached.
    /// </remarks>
    private static SpamVerdict StatusVerdict(string? status)
    {
        var verdictWord = status?.Trim().Split([',', ' '], 2)[0];

        return verdictWord switch
        {
            null or "" => SpamVerdict.Undetermined,
            var word when StringComparer.OrdinalIgnoreCase.Equals(word, AffirmativeStatus) => SpamVerdict.Spam,
            var word when StringComparer.OrdinalIgnoreCase.Equals(word, NegativeStatus) => SpamVerdict.NotSpam,
            _ => SpamVerdict.Undetermined,
        };
    }

    private static SpamAssessment? StatusAssessment(string? status)
    {
        if (status is null)
        {
            return null;
        }

        return PropertyNumber(status, ScoreProperty) is { } score
            && PropertyNumber(status, ThresholdProperty) is { } threshold
            ? SpamAssessment.Create(score, threshold)
            : null;
    }

    /// <summary>Reads one <c>name=value</c> number out of a status field, where the properties are space separated.</summary>
    private static double? PropertyNumber(string status, string propertyName)
    {
        var property = status
            .Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(propertyName, StringComparison.OrdinalIgnoreCase));

        return property is not null
            && double.TryParse(
                property.AsSpan(propertyName.Length),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
            && double.IsFinite(number)
                ? number
                : null;
    }
}
