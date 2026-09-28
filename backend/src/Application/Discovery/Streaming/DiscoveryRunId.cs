// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Identifies one Discover run for as long as the deployment keeps it.</summary>
/// <remarks>
/// A run is addressed by this from the moment it is started: every event it publishes names it, and a client that lost
/// its connection reattaches by it rather than by asking the question again. It is a version 7 UUID, so the events it
/// leads the key of land together. A client holds it and presents it back, and what refuses a run somebody else started
/// is the user check beside it rather than the identifier being hard to know — ADR 0036 holds why.
/// </remarks>
public readonly record struct DiscoveryRunId
{
    private DiscoveryRunId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier for a run nothing has started yet.</summary>
    /// <returns>A new version 7 identifier.</returns>
    public static DiscoveryRunId New() => new(Guid.CreateVersion7());

    /// <summary>Creates a run identifier from a non-empty UUID, which is how one arrives back off the wire.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated run identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static DiscoveryRunId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A Discover run identifier cannot be empty.", nameof(value));
        }

        return new DiscoveryRunId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
