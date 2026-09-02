// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.Observability;
using Xunit;

namespace MailFathom.Common.UnitTests.Observability;

/// <summary>Covers the name MailFathom publishes telemetry under, and that both registries carry it.</summary>
/// <remarks>
/// The name reaches an operator's dashboard filter and an alert rule, so changing it silently stops collecting what
/// somebody is watching. A source or meter constructed under some other name is the same failure arriving from the
/// other side: the host subscribes <see cref="Telemetry.Name" />, so an instance not carrying it publishes into a
/// stream nothing reads while the code still looks instrumented.
/// </remarks>
public sealed class TelemetryTests
{
    [Fact]
    public void Name_IsTheNameTheHostIsExpectedToSubscribe()
    {
        // Act
        var declared = Telemetry.Name;

        // Assert
        Assert.Equal("MailFathom", declared);
    }

    [Fact]
    public void ActivitySource_CarriesTheDeclaredName()
    {
        // Act
        var published = Telemetry.ActivitySource.Name;

        // Assert
        Assert.Equal(Telemetry.Name, published);
    }

    [Fact]
    public void Meter_CarriesTheDeclaredName()
    {
        // Act
        var published = Telemetry.Meter.Name;

        // Assert
        Assert.Equal(Telemetry.Name, published);
    }
}
