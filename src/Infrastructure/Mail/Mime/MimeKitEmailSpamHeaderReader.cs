// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Spam.Signals;
using MimeKit;
using MimeKit.Cryptography;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads the spam-relevant headers of a stored message with MimeKit, and interprets none of them.</summary>
/// <remarks>
/// <para>
/// Only the headers are parsed. <c>MimeParser.ParseHeadersAsync</c> stops at the blank line that ends them, so a
/// classification never pays for decoding a body, walking a MIME tree, or materializing an attachment — which is what
/// keeps the deterministic stage cheap enough to run on every message that arrives.
/// </para>
/// <para>
/// What crosses back is what the server wrote: an outcome, its properties, and a provider header's value. Deciding what
/// any of it means belongs above this adapter, where it is unit-testable and where the two sources can be weighed
/// against each other.
/// </para>
/// <para>
/// Both header values are personal data — an authentication result names a sending domain — so nothing here is logged,
/// including in the branch where the message does not parse.
/// </para>
/// </remarks>
internal sealed class MimeKitEmailSpamHeaderReader : IEmailSpamHeaderReader
{
    /// <inheritdoc />
    public async Task<SpamHeaderFacts> ReadAsync(StoredEmailContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var headers = await ParseHeadersAsync(content, cancellationToken);

        if (headers is null)
        {
            return SpamHeaderFacts.None;
        }

        // Each list stops at the bound while the header block is walked rather than being expanded and cut afterwards,
        // so a message repeating one header field pays for what is kept instead of for what it wrote.
        return SpamHeaderFacts.Create(
            [.. headers.SelectMany(ReadAuthenticationResults).Take(SpamHeaderFacts.MaximumAuthenticationResults)],
            [
                .. headers
                    .Where(static header => ProviderSpamHeaderFields.IsRecognized(header.Field))
                    .Select(static header => new ProviderSpamHeaderValue(header.Field, header.Value))
                    .Take(SpamHeaderFacts.MaximumProviderHeaders),
            ]);
    }

    /// <summary>Parses the header block, answering with nothing when the stored bytes are not a message.</summary>
    /// <remarks>
    /// Damage is an ordinary answer rather than a failure, for the reason the metadata extraction gives: one unreadable
    /// message must not stop a run over a mailbox. A message whose headers cannot be read is classified from its folder
    /// alone, which is a weaker classification and an honest one.
    /// </remarks>
    private static async Task<HeaderList?> ParseHeadersAsync(
        StoredEmailContent content,
        CancellationToken cancellationToken)
    {
        await using var rawMime = RawMimeStream.Open(content.RawMime);

        try
        {
            return await new MimeParser(rawMime, MimeFormat.Entity).ParseHeadersAsync(cancellationToken);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Reads every outcome one <c>Authentication-Results</c> or <c>ARC-Authentication-Results</c> header states.</summary>
    /// <remarks>
    /// A message carries the header once per authenticating hop and the ARC form once per chain instance, so the outcomes
    /// of every one of them are read rather than only the first. A header MimeKit cannot parse contributes nothing
    /// instead of failing the read: RFC 8601's grammar is widely written loosely, and a malformed one is a hop whose
    /// claim cannot be trusted rather than a message that cannot be classified.
    /// </remarks>
    private static IEnumerable<MessageAuthenticationResult> ReadAuthenticationResults(Header header)
    {
        var isForwarded = header.Id is HeaderId.ArcAuthenticationResults;

        if (header.Id is not (HeaderId.AuthenticationResults or HeaderId.ArcAuthenticationResults)
            || !AuthenticationResults.TryParse(Encoding.UTF8.GetBytes(header.Value), out var results))
        {
            return [];
        }

        return results.Results.Select(result => new MessageAuthenticationResult(
            result.Method,
            result.Result ?? string.Empty,
            Detail(result),
            isForwarded));
    }

    /// <summary>Renders the properties a hop wrote beside an outcome, which is what says whose domain it was about.</summary>
    /// <remarks>
    /// The properties are rendered rather than kept structured, because nothing decides on them: they exist so an
    /// operator reading a record can see that DKIM passed for one domain while the envelope named another. The signal
    /// they become shortens them if a hop wrote an unusually verbose set.
    /// </remarks>
    private static string? Detail(AuthenticationMethodResult result)
    {
        var properties = string.Join(
            ' ',
            result.Properties.Select(static property =>
                $"{property.PropertyType}.{property.Property}={property.Value}"));

        return properties.Length is 0 ? null : properties;
    }
}
