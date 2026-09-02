// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures how many connections this process accepts at once, across every listener it opens.</summary>
/// <remarks>
/// <para>
/// One section for the whole process rather than one per surface, and that is the substantive difference between this
/// bound and every other bound in the transport configuration. A connection is accepted by the server before any
/// routing has run, so nothing at that point knows which surface it will turn out to be for; the framework's own limit
/// is a single property of the server, and there is no per-listener form of it to bind even if the question were worth
/// asking per surface. Read this as the process's ceiling and never as the sum of what the endpoints permit — an
/// operator who narrows the MCP endpoint's <c>MaxConcurrentRequests</c> has changed what one surface serves at once,
/// while narrowing this changes what the machine accepts at all, including the probes.
/// </para>
/// <para>
/// It exists because the limits above it cannot see this far down. The rate limiter partitions a request that already
/// has an <see cref="HttpContext" />, and everything a flood spends before that point — the accept, the TLS handshake,
/// and on the MCP surface the client certificate's chain building — is the most expensive per-connection work this
/// process does and the part no endpoint's configuration reaches. Without a ceiling here the framework's default
/// applies, which is no ceiling at all.
/// </para>
/// <para>
/// Like every other limit in this system it is counted in this process alone, so a deployment running several instances
/// enforces it once per instance rather than once in total, and none of it is protection against a distributed flood.
/// What it buys is that one peer cannot exhaust the file descriptors, memory, and handshake capacity of the process it
/// is talking to.
/// </para>
/// <para>
/// The section is read once, while the host is being composed, because the server's limits are set as it is
/// constructed. A change takes effect on restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ConnectionLimitsOptions
{
    /// <summary>The configuration section the connection limits are bound from.</summary>
    public const string SectionName = "ConnectionLimits";

    private const int MaximumConcurrentConnections = 100_000;

    /// <summary>Gets or sets whether the process refuses a connection beyond <see cref="MaxConcurrentConnections" />.</summary>
    /// <remarks>
    /// On unless a deployment states otherwise, for the reason the endpoint rate limits are: an unbounded process is
    /// not something an operator decided by writing nothing. Turning it off restores the framework's own default, which
    /// accepts connections until the operating system stops supplying them, and it is the right setting only where an
    /// ingress or a firewall in front of this process already bounds them.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how many connections the process holds open at once, across every listener.</summary>
    /// <remarks>
    /// A thousand, which is far above what the endpoints' own limits could keep busy and far below what an unbounded
    /// flood opens. The gap between the two is deliberate: a connection is not a request, since a client holds one open
    /// across several and the keep-alive timeout decides how long an idle one survives, so a ceiling near the request
    /// limit would refuse ordinary clients long before it refused an attacker.
    /// </remarks>
    public int MaxConcurrentConnections { get; set; } = 1000;

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>Strict binding is part of the read, as it is for every other section that decides a security posture: a misspelled key would otherwise leave a deployment believing it had raised a ceiling that never moved.</remarks>
    public static ConnectionLimitsOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName)
            .Get<ConnectionLimitsOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new ConnectionLimitsOptions();
    }

    /// <summary>Finds everything an operator must fix before the ceiling can be applied.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (!this.Enabled)
        {
            return [];
        }

        if (this.MaxConcurrentConnections is < 1 or > MaximumConcurrentConnections)
        {
            return
            [
                $"{SectionName}:{nameof(this.MaxConcurrentConnections)} — '{this.MaxConcurrentConnections}' is outside 1 to {MaximumConcurrentConnections}; the process must accept at least one connection to serve anything, and a ceiling beyond that upper bound is past what the operating system would supply and reads as a limit while being none. Write '{SectionName}:{nameof(this.Enabled)}': false to accept connections without a ceiling.",
            ];
        }

        return [];
    }
}
