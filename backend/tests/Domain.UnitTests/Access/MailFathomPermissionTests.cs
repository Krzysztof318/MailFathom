// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the published permission vocabulary a grant is written from and a token carries as scopes.</summary>
/// <remarks>
/// The names travel outside this process — into an operator's configuration file, into a protected resource metadata
/// document, and into an authorization server as scopes — so what is asserted here is the identity rather than the
/// membership: the exact spelling, the surface each name belongs to, and the parsing that refuses everything else.
/// </remarks>
public sealed class MailFathomPermissionTests
{
    /// <summary>Two permissions sharing a name would be one grant an operator could write two ways and nothing could tell apart.</summary>
    [Fact]
    public void All_NamesAreUnique()
    {
        // Act
        var distinctNames = MailFathomPermission.All.Select(permission => permission.Name).Distinct(StringComparer.Ordinal).Count();

        // Assert
        Assert.Equal(MailFathomPermission.All.Count, distinctNames);
    }

    /// <summary>
    /// A declared permission left out of the registry is invisible to every other assertion here, and it is silently
    /// unwritable: parsing, validation, and the startup record all resolve a name through the registry alone, so
    /// startup would refuse the very permission this repository declares.
    /// </summary>
    [Fact]
    public void All_ListsEveryDeclaredPermission()
    {
        // Arrange
        var declaredPermissions = typeof(MailFathomPermission)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(MailFathomPermission))
            .Select(property => (MailFathomPermission)property.GetValue(null)!);

        // Act
        var unregistered = declaredPermissions.Where(permission => !MailFathomPermission.All.Contains(permission)).ToArray();

        // Assert
        Assert.Empty(unregistered);
    }

    /// <summary>The published set, asserted by name because a name is what an operator writes and an authorization server mints.</summary>
    /// <remarks>
    /// ADR 0012 fixed the first eight and the rule the set grows under: a name is published when the capability it names
    /// exists. The two contact permissions were allocated under that rule when the contact tools arrived, the flag
    /// permission when the tool that writes a mailbox did, the send permission when the outbox began requiring one, the
    /// drafting permission when the tools that write a draft arrived beside it, and the configuration permission when
    /// the commands that change a persisted setting did.
    /// </remarks>
    [Fact]
    public void All_CarriesThePublishedNames() =>
        Assert.Equal(
            [
                "mailfathom.mail.read",
                "mailfathom.mail.ask",
                "mailfathom.mail.contacts.read",
                "mailfathom.mail.contacts.write",
                "mailfathom.mail.flags.write",
                "mailfathom.mail.drafts.write",
                "mailfathom.mail.send",
                "mailfathom.admin.read",
                "mailfathom.admin.audit.read",
                "mailfathom.admin.operate",
                "mailfathom.admin.credentials.write",
                "mailfathom.admin.spend",
                "mailfathom.admin.erase",
                "mailfathom.admin.configuration.write",
            ],
            MailFathomPermission.All.Select(permission => permission.Name));

    /// <summary>The same string travels in a space-delimited <c>scope</c> claim, where a space or a quotation mark would split one permission into two.</summary>
    [Fact]
    public void All_NamesAreValidScopeTokens()
    {
        // Act
        var unusableAsScopes = MailFathomPermission.All
            .Where(permission => !permission.Name.All(character => character is > (char)0x20 and < (char)0x7F and not '"' and not '\\'))
            .ToArray();

        // Assert
        Assert.Empty(unusableAsScopes);
    }

    /// <summary>The prefix is what a startup refusal reads to tell a grant written on the wrong endpoint from one that belongs there.</summary>
    [Fact]
    public void Surface_FollowsThePrefixOfTheName()
    {
        // Act
        var mismatched = MailFathomPermission.All
            .Where(permission => permission.Surface != ExpectedSurfaceOf(permission.Name))
            .ToArray();

        // Assert
        Assert.Empty(mismatched);
    }

    /// <summary>The two halves are disjoint, which is what makes one vocabulary safe to publish for two surfaces.</summary>
    [Fact]
    public void PublishedFor_TheTwoSurfaces_PartitionTheWholeSet()
    {
        // Act
        var mail = MailFathomPermission.PublishedFor(ProtectedSurface.Mail);
        var administration = MailFathomPermission.PublishedFor(ProtectedSurface.Administration);

        // Assert
        Assert.Equal(
            [
                MailFathomPermission.MailRead,
                MailFathomPermission.MailAsk,
                MailFathomPermission.MailContactsRead,
                MailFathomPermission.MailContactsWrite,
                MailFathomPermission.MailFlagsWrite,
                MailFathomPermission.MailDraftsWrite,
                MailFathomPermission.MailSend,
            ],
            mail);
        Assert.Equal(MailFathomPermission.All.Count, mail.Count + administration.Count);
        Assert.Empty(mail.Intersect(administration));
    }

    [Fact]
    public void TryParse_APublishedName_ReturnsThatPermission()
    {
        // Act
        var parsed = MailFathomPermission.TryParse("mailfathom.admin.spend", out var permission);

        // Assert
        Assert.True(parsed);
        Assert.Equal(MailFathomPermission.AdminSpend, permission);
    }

    /// <summary>A misspelling is unknown rather than reconstructed, which is what lets startup refuse it instead of granting something nothing enforces.</summary>
    [Theory]
    [InlineData("mailfathom.mail.write")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameNothingPublishes_ReportsUnspecified(string? written)
    {
        // Act
        var parsed = MailFathomPermission.TryParse(written, out var permission);

        // Assert
        Assert.False(parsed);
        Assert.False(permission.IsSpecified);
    }

    /// <summary>An authorization server compares a scope byte for byte, so a spelling accepted here that it would treat as another scope is a grant that means two things.</summary>
    [Theory]
    [InlineData("MailFathom.Mail.Read")]
    [InlineData(" mailfathom.mail.read")]
    [InlineData("mailfathom.mail.read ")]
    public void TryParse_ASpellingAnAuthorizationServerWouldTreatAsAnotherScope_IsRefused(string written)
    {
        // Act
        var parsed = MailFathomPermission.TryParse(written, out _);

        // Assert
        Assert.False(parsed);
    }

    [Fact]
    public void Default_NamesNoPermission()
    {
        // Arrange
        var permission = default(MailFathomPermission);

        // Assert
        Assert.False(permission.IsSpecified);
        Assert.Equal("(unspecified)", permission.ToString());
        Assert.Throws<InvalidOperationException>(() => permission.Name);
    }

    /// <summary>A log line and a startup record name the permission an operator wrote, not the structure carrying it.</summary>
    [Fact]
    public void ToString_IsThePublishedName() =>
        Assert.Equal("mailfathom.mail.ask", MailFathomPermission.MailAsk.ToString());

    [Fact]
    public void JsonRoundTrip_PreservesThePermission()
    {
        // Act
        var json = JsonSerializer.Serialize(MailFathomPermission.AdminErase);
        var restored = JsonSerializer.Deserialize<MailFathomPermission>(json);

        // Assert
        Assert.Equal("\"mailfathom.admin.erase\"", json);
        Assert.Equal(MailFathomPermission.AdminErase, restored);
    }

    [Fact]
    public void JsonRoundTrip_AsAPropertyName_PreservesThePermission()
    {
        // Arrange
        var granted = new Dictionary<MailFathomPermission, bool> { [MailFathomPermission.MailRead] = true };

        // Act
        var json = JsonSerializer.Serialize(granted);
        var restored = JsonSerializer.Deserialize<Dictionary<MailFathomPermission, bool>>(json);

        // Assert
        Assert.Equal("{\"mailfathom.mail.read\":true}", json);
        Assert.True(Assert.Single(restored!).Value);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"mailfathom.mail.write\"")]
    public void JsonRead_TokenThatNamesNoPublishedPermission_IsRejected(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailFathomPermission>(json));

    [Fact]
    public void JsonRead_PropertyNameThatNamesNoPublishedPermission_IsRejected() =>
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<MailFathomPermission, bool>>("{\"mailfathom.mail.write\":true}"));

    [Fact]
    public void JsonWrite_UnspecifiedPermission_IsRejected() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(MailFathomPermission)));

    private static ProtectedSurface ExpectedSurfaceOf(string name) => name.StartsWith("mailfathom.mail.", StringComparison.Ordinal)
        ? ProtectedSurface.Mail
        : ProtectedSurface.Administration;
}
