// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability.ClientTelemetry;

/// <summary>Covers what the proxy does to an export request before it forwards one.</summary>
/// <remarks>
/// Two claims carry the privacy half of this feature: the owner this deployment authenticated is on every resource that
/// leaves, and a client's own claim under that key never is. Both are asserted against the attributes a reader takes
/// back out of the rewritten octets, because a batch is only attributed if a collector's own parse finds the entry
/// where a resource attribute belongs. Everything else the client sent is asserted to survive, because a proxy that
/// quietly dropped a field would be a processor.
/// </remarks>
public sealed class OtlpExportPayloadTests
{
    private const string OwnerKey = "mailfathom.owner";
    private const string AuthenticatedOwner = "9f2a1c64-0000-4000-8000-000000000001";

    /// <summary>The claim the whole attribution rests on: a client cannot export as somebody else.</summary>
    [Fact]
    public void Rewrite_ABatchClaimingAnOwnerOfItsOwn_ReplacesTheClaimWithTheAuthenticatedOne()
    {
        // Arrange
        var request = OtlpExportRequests.Batch([new KeyValuePair<string, string>(OwnerKey, "somebody-else")], 1);

        // Act
        var rewritten = OtlpExportPayload.Rewrite(request, OwnerKey, AuthenticatedOwner, maxRecords: 10);

        // Assert
        Assert.Equal(OtlpPayloadRefusal.None, rewritten.Refusal);
        Assert.Equal(
            [new KeyValuePair<string, string>(OwnerKey, AuthenticatedOwner)],
            OtlpExportRequests.ResourceAttributes(rewritten.Body).Where(attribute => attribute.Key == OwnerKey));
    }

    /// <summary>The control for the replacement: what the client says about itself is not this deployment's to drop.</summary>
    [Fact]
    public void Rewrite_ABatchCarryingOtherAttributes_LeavesThemAsTheClientSentThem()
    {
        // Arrange
        var request = OtlpExportRequests.Batch(
            [
                new KeyValuePair<string, string>("service.name", "mailfathom-client"),
                new KeyValuePair<string, string>(OwnerKey, "somebody-else"),
                new KeyValuePair<string, string>("browser.brands", "Chromium"),
            ],
            1);

        // Act
        var rewritten = OtlpExportPayload.Rewrite(request, OwnerKey, AuthenticatedOwner, maxRecords: 10);

        // Assert
        Assert.Equal(
            [
                new KeyValuePair<string, string>("service.name", "mailfathom-client"),
                new KeyValuePair<string, string>("browser.brands", "Chromium"),
                new KeyValuePair<string, string>(OwnerKey, AuthenticatedOwner),
            ],
            OtlpExportRequests.ResourceAttributes(rewritten.Body));
    }

    /// <summary>An envelope with no resource is the one path by which unattributed telemetry could have left.</summary>
    [Fact]
    public void Rewrite_AnEnvelopeCarryingNoResource_AddsOneNamingTheAuthenticatedOwner()
    {
        // Arrange
        var request = OtlpExportRequests.BatchWithoutResource(2);

        // Act
        var rewritten = OtlpExportPayload.Rewrite(request, OwnerKey, AuthenticatedOwner, maxRecords: 10);

        // Assert
        Assert.Equal(OtlpPayloadRefusal.None, rewritten.Refusal);
        Assert.Equal(
            [new KeyValuePair<string, string>(OwnerKey, AuthenticatedOwner)],
            OtlpExportRequests.ResourceAttributes(rewritten.Body));
    }

    /// <summary>The count is what the batch bound and the accepted-records instrument are both read from.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Rewrite_ABatchOfRecords_CountsEveryOne(int records)
    {
        // Arrange
        var request = OtlpExportRequests.Batch([], records);

        // Act
        var rewritten = OtlpExportPayload.Rewrite(request, OwnerKey, AuthenticatedOwner, maxRecords: 100);

        // Assert
        Assert.Equal(records, rewritten.RecordCount);
    }

    /// <summary>A receiver that forwarded whatever arrived would be a way to push arbitrary octets at somebody's collector.</summary>
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x0A, 0x7F })]
    [InlineData(new byte[] { 0x0B, 0x01 })]
    public void Rewrite_OctetsThatAreNotAnExportRequest_Refuses(byte[] request)
    {
        // Act
        var rewritten = OtlpExportPayload.Rewrite(request, OwnerKey, AuthenticatedOwner, maxRecords: 100);

        // Assert
        Assert.Equal(OtlpPayloadRefusal.Malformed, rewritten.Refusal);
        Assert.Empty(rewritten.Body);
    }

    /// <summary>The batch is refused whole rather than truncated, so a client is never told a rejection was a success.</summary>
    [Fact]
    public void Rewrite_ABatchPastTheRecordBound_RefusesItWholeRatherThanTruncatingIt()
    {
        // Arrange
        var request = OtlpExportRequests.Batch([], records: 5);

        // Act
        var rewritten = OtlpExportPayload.Rewrite(request, OwnerKey, AuthenticatedOwner, maxRecords: 4);

        // Assert
        Assert.Equal(OtlpPayloadRefusal.TooManyRecords, rewritten.Refusal);
        Assert.Empty(rewritten.Body);
    }

    /// <summary>An empty request is a valid one, and the resource it gains is what makes it attributed.</summary>
    [Fact]
    public void Rewrite_AnEmptyRequest_IsAccepted()
    {
        // Act
        var rewritten = OtlpExportPayload.Rewrite([], OwnerKey, AuthenticatedOwner, maxRecords: 10);

        // Assert
        Assert.Equal(OtlpPayloadRefusal.None, rewritten.Refusal);
        Assert.Equal(0, rewritten.RecordCount);
        Assert.Empty(rewritten.Body);
    }

    /// <summary>A refusal carries the specification's own document, which is what an exporter reads rather than a sentence.</summary>
    [Fact]
    public void Status_ACodeAndAMessage_EncodesBothFieldsInTheOrderTheSchemaDeclaresThem()
    {
        // Arrange
        var message = "refused";

        // Act
        var status = OtlpExportPayload.Status(3, message);

        // Assert
        Assert.Equal(
            [0x08, 0x03, 0x12, (byte)message.Length, .. Encoding.UTF8.GetBytes(message)],
            status);
    }
}
