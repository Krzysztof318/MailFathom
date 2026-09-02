// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the screening the outbox and the draft book are constructed with in every suite that builds one.</summary>
public sealed class OutgoingMailScreeningsTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Most tests of a send are about something else, so the default shape has to let every message through.</summary>
    [Fact]
    public async Task Inactive_AMessageCarryingScreenedMaterial_StopsNothing()
    {
        // Arrange
        var screening = OutgoingMailScreenings.Inactive();

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.Owner,
            MimeOf($"the key is {Marker}"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>
    /// A double that let a screened message through would report every refusal test as the outbox having failed to
    /// refuse, which is the assertion those tests exist to make.
    /// </summary>
    [Fact]
    public async Task Through_AMessageCarryingScreenedMaterial_StopsTheAct()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);
        var screening = OutgoingMailScreenings.Through(egress.Screen);

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.Owner,
            MimeOf($"the key is {Marker}"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.ContentFound, refusal.Reason);
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
    }

    /// <summary>What the reader hands the screen is the message's own words, so an ordinary one reaches the write.</summary>
    [Fact]
    public async Task Through_AnOrdinaryMessage_StopsNothingAndScansWhatItSaid()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);
        var screening = OutgoingMailScreenings.Through(egress.Screen);

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.Owner,
            MimeOf("an ordinary message"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.Equal(["an ordinary message"], egress.Scanner.ScannedTexts);
    }

    [Fact]
    public void Through_NoScreen_IsRefusedAsAnArgument() =>

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => OutgoingMailScreenings.Through(null!));

    private static ReadOnlyMemory<byte> MimeOf(string body) => Encoding.UTF8.GetBytes(body).AsMemory();
}
