// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using DnsClient;
using DnsClient.Protocol;
using MailFathom.Application.Mail;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Resolves a published DKIM key record from DNS, behind one bounded lookup and one cache.</summary>
/// <remarks>
/// <para>
/// This is the only place MailFathom asks a stranger's nameserver anything, so what it discloses is worth stating
/// exactly: the name <c>&lt;selector&gt;._domainkey.&lt;domain&gt;</c>, which the signing domain published in order to
/// be asked for, and which carries nothing about the message, the mailbox, or the recipient. What that domain can learn
/// from being asked is that somebody here received mail they sent, which they already know.
/// </para>
/// <para>
/// The name is assembled and then validated as a domain before it is queried, because both halves of it come from a
/// header an attacker writes. A selector carrying whitespace, an over-long label, or a name past what a resolver
/// accepts is refused here rather than handed to the resolver, and the refusal reads as an unresolvable key — which
/// leaves the verdict not established, exactly as an absent record does.
/// </para>
/// <para>
/// Nothing raises. A record that does not exist, a nameserver that will not answer, a response that is not a usable
/// key, and a lookup that outlives its deadline are all ordinary, and each answers with no record; extraction runs over
/// whatever arrives in a mailbox and must not stop on somebody else's DNS trouble. The caller's own cancellation is the
/// one thing that still propagates, because that is this process giving up rather than the network failing.
/// </para>
/// </remarks>
internal sealed class DnsDkimPublicKeyRecordResolver : IDkimPublicKeyRecordResolver
{
    /// <summary>The label every DKIM key record is published beneath, which RFC 6376 fixes.</summary>
    private const string KeyRecordLabel = "_domainkey";

    /// <summary>The longest record this reads, past which nothing is handed to a key parser.</summary>
    /// <remarks>
    /// A published key record holds an algorithm, a flag or two, and one base64 key; the largest key anybody publishes
    /// leaves it far below this. The bound exists because the answer arrives from a server this deployment does not
    /// control, and a parser is the wrong place to meet an unbounded string.
    /// </remarks>
    private const int MaximumRecordLength = 4096;

    /// <summary>How long one name is waited for before the lookup answers that nothing was resolved.</summary>
    /// <remarks>
    /// It bounds the whole resolution rather than one attempt, so a nameserver that accepts a query and never answers
    /// costs the extraction this and no more. Extraction is on the path a folder run advances along, so the budget is
    /// deliberately closer to a mail server's patience than to a resolver's default.
    /// </remarks>
    private static readonly TimeSpan LookupDeadline = TimeSpan.FromSeconds(5);

    private readonly IDnsQuery lookup;
    private readonly DkimPublicKeyRecordCache cache;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan lookupDeadline;

    /// <summary>Initializes a resolver over one DNS client and the cache its answers are held in.</summary>
    /// <param name="lookup">Queries the resolver the operating system configured.</param>
    /// <param name="cache">Holds what was resolved for as long as it stays good.</param>
    /// <param name="timeProvider">Measures the deadline one lookup is given.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DnsDkimPublicKeyRecordResolver(IDnsQuery lookup, DkimPublicKeyRecordCache cache, TimeProvider timeProvider)
        : this(lookup, cache, timeProvider, LookupDeadline)
    {
    }

    /// <summary>Initializes a resolver whose deadline is supplied, so a test need not state the real one.</summary>
    /// <param name="lookup">Queries a resolver.</param>
    /// <param name="cache">Holds what was resolved for as long as it stays good.</param>
    /// <param name="timeProvider">Measures the deadline one lookup is given.</param>
    /// <param name="lookupDeadline">How long one name is waited for.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DnsDkimPublicKeyRecordResolver(
        IDnsQuery lookup,
        DkimPublicKeyRecordCache cache,
        TimeProvider timeProvider,
        TimeSpan lookupDeadline)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.lookup = lookup;
        this.cache = cache;
        this.timeProvider = timeProvider;
        this.lookupDeadline = lookupDeadline;
    }

    /// <summary>Builds the DNS client the local verification queries with.</summary>
    /// <returns>A client bound to the resolvers the operating system configured.</returns>
    /// <remarks>
    /// It names no nameserver of its own, so a deployment that routes DNS through its own resolver is followed rather
    /// than bypassed — which matters here more than usual, since this is the one path that leaves the process to judge
    /// mail. Its own response cache is off because <see cref="DkimPublicKeyRecordCache" /> is the cache this system
    /// bounds and reasons about, and two would make what is held a question with two answers.
    /// </remarks>
    public static LookupClient CreateLookupClient() =>
        new(new LookupClientOptions
        {
            Timeout = LookupDeadline,
            Retries = 1,
            UseCache = false,
            ThrowDnsErrors = false,
            UseTcpFallback = true,
        });

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(string selector, string signingDomain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(signingDomain);

        if (!TryBuildKeyRecordName(selector, signingDomain, out var keyRecordName))
        {
            return null;
        }

        if (this.cache.TryRead(keyRecordName.NormalizedValue, out var cachedRecord))
        {
            return cachedRecord;
        }

        var resolved = await this.QueryKeyRecordAsync(keyRecordName.Value, cancellationToken);

        if (resolved is null)
        {
            this.cache.StoreAbsence(keyRecordName.NormalizedValue);

            return null;
        }

        this.cache.Store(keyRecordName.NormalizedValue, resolved.Value.Record, resolved.Value.TimeToLive);

        return resolved.Value.Record;
    }

    /// <summary>Assembles the name a key is published at, and refuses one no resolver would accept.</summary>
    private static bool TryBuildKeyRecordName(string selector, string signingDomain, out SenderDomain keyRecordName) =>
        SenderDomain.TryCreate($"{selector.Trim()}.{KeyRecordLabel}.{signingDomain.Trim()}", out keyRecordName);

    /// <summary>Reads the first usable key record out of an answer, with the lifetime that record itself carries.</summary>
    /// <remarks>
    /// <para>
    /// A record longer than 255 octets is published as several strings and is one value, so they are concatenated with
    /// nothing between them; splitting a base64 key on a boundary the publisher chose would otherwise make a perfectly
    /// good key unparseable.
    /// </para>
    /// <para>
    /// The lifetime comes from the record that was taken rather than from the answer's first, because a name may hold
    /// several TXT records and the one usable here need not be the one at the front.
    /// </para>
    /// </remarks>
    private static (string Record, TimeSpan TimeToLive)? ReadKeyRecord(IDnsQueryResponse response)
    {
        foreach (var record in response.Answers.OfType<TxtRecord>())
        {
            var text = string.Concat(record.Text);

            if (text.Length is > 0 and <= MaximumRecordLength)
            {
                return (text, TimeSpan.FromSeconds(record.InitialTimeToLive));
            }
        }

        return null;
    }

    /// <summary>Asks for one name, answering with nothing wherever the network or the answer does not cooperate.</summary>
    private async Task<(string Record, TimeSpan TimeToLive)?> QueryKeyRecordAsync(
        string keyRecordName,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(this.lookupDeadline, this.timeProvider);
        using var query = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            var response = await this.lookup.QueryAsync(
                keyRecordName,
                QueryType.TXT,
                QueryClass.IN,
                query.Token);

            return response.HasError ? null : ReadKeyRecord(response);
        }
        catch (DnsResponseException)
        {
            // Every configured nameserver failed. It says nothing about the sender, so the verdict stays not
            // established rather than becoming a failure attributed to their mail.
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The lookup outlived its own deadline, which is this deployment's network rather than the caller giving
            // up. A caller that did give up is the one case this does not swallow.
            return null;
        }
    }
}
