// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A clock that reads whatever a test set it to, so a freshness gap is arithmetic rather than a race.</summary>
/// <remarks>
/// Written here rather than taken from <c>Microsoft.Extensions.TimeProvider.Testing</c>, which this stack does not
/// restore: what these tests need of a clock is one instant it reads back, and a package for that would be a
/// dependency and a licence review for a single overridden member.
/// </remarks>
internal sealed class StubClock : TimeProvider
{
    /// <summary>Initializes the clock at an instant.</summary>
    /// <param name="now">What the clock reads.</param>
    internal StubClock(DateTimeOffset now) => this.Now = now;

    /// <summary>Gets or sets what the clock reads.</summary>
    internal DateTimeOffset Now { get; set; }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => this.Now;
}
