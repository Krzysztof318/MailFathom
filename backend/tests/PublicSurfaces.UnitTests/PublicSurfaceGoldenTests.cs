// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Administration;
using Xunit;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Holds the surfaces nothing else records against the files committed beside this suite.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md">ADR 0004</see>
/// permits a break on any of the four public surfaces below <c>1.0.0</c> and makes the record of the break the
/// obligation instead. The database schema already has a test that proves what a release ships, and the deployment
/// contract is read by the assets that render it; the tool contract and the configuration key set had nothing but the
/// author's memory between a rename and a release note that never mentioned it. The HTTP API is here for the same
/// reason and is not one of that ADR's four: an endpoint, a verb, a request shape, a status code, or a security
/// declaration changes a client exactly as a renamed tool does, and until the document existed there was nothing to
/// record it against.
/// </para>
/// <para>
/// So the whole of each surface is rendered into one ordered file, and a change to it is a diff in the pull request
/// rather than something a reviewer has to reconstruct from twenty tool classes. No test here judges whether a change
/// is right — a break is permitted here and the golden file is where it is written down.
/// </para>
/// </remarks>
public sealed class PublicSurfaceGoldenTests
{
    /// <summary>Every tool descriptor a client discovers, as the registration publishes it.</summary>
    [Fact]
    public void McpToolContract_AsTheRegistrationPublishesIt_MatchesTheCommittedRecord() =>
        PublicSurfaceGolden.AssertMatches(
            "mcp-tool-contract.json",
            "published MCP tool contract",
            McpToolContractSurface.Render());

    /// <summary>Every configuration key an operator may write, as the host binds it.</summary>
    [Fact]
    public void ConfigurationKeySet_AsTheHostBindsIt_MatchesTheCommittedRecord() =>
        PublicSurfaceGolden.AssertMatches(
            "configuration-keys.txt",
            "published configuration key set",
            ConfigurationKeySurface.Render());

    /// <summary>
    /// Every section the key set names is one the administrative reading recognizes as this deployment's.
    /// </summary>
    /// <remarks>
    /// <see cref="MailFathomConfigurationSections" /> is what stops <c>GET /api/admin/configuration</c> handing back a
    /// value the unprefixed environment provider supplied under a name MailFathom never chose, and it is a written list
    /// rather than a discovery because the reading runs per request. This is the mechanism that keeps the list honest:
    /// the section set is discovered here, from the <c>SectionName</c> constant of every bound options class, so a
    /// section added without a line there fails this rather than silently withholding its own settings from the one
    /// surface an operator reads them through. The reverse is deliberately not asserted — the list names
    /// <c>Logging</c>, <c>ConnectionStrings</c>, and <c>Accounts</c>, which the key set leaves out for reasons of its
    /// own.
    /// </remarks>
    [Fact]
    public void ConfigurationKeySet_EverySectionItNames_IsOneTheAdministrativeReadingRecognizes()
    {
        // Arrange
        var rendered = ConfigurationKeySurface.Render()
            .Split('\n')
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split([':', ' '], 2)[0])
            .Where(section => section.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // Act
        var unrecognized = rendered.Where(section => !MailFathomConfigurationSections.Name(section)).ToArray();

        // Assert
        Assert.Empty(unrecognized);
    }

    /// <summary>Every HTTP operation both API surfaces publish, as the host's own OpenAPI document describes it.</summary>
    [Fact]
    public async Task HttpApiContract_AsTheHostDocumentsIt_MatchesTheCommittedRecord() =>
        PublicSurfaceGolden.AssertMatches(
            "http-api-contract.json",
            "published HTTP API contract",
            await HttpApiContractSurface.RenderAsync(TestContext.Current.CancellationToken));

    /// <summary>The presentation plan a Discover run produces, as the contract's own serializer writes it.</summary>
    [Fact]
    public void PresentationPlanContract_AsItsSerializerWritesIt_MatchesTheCommittedRecord() =>
        PublicSurfaceGolden.AssertMatches(
            "presentation-plan-contract.json",
            "published presentation plan contract",
            PresentationPlanContractSurface.Render());

    /// <summary>Two renderings of one surface are the same text, which is what makes a diff mean a change.</summary>
    /// <remarks>
    /// The control for the tests above. Reflection promises no ordering, a schema generator none either, and a
    /// rendering that varied between runs would fail on a tree nobody touched and pass on one somebody broke — the
    /// failure a golden file is least able to report about itself.
    /// </remarks>
    [Fact]
    public async Task PublicSurfaces_RenderedTwice_ProduceTheSameText()
    {
        // Arrange, Act
        var tools = McpToolContractSurface.Render();
        var keys = ConfigurationKeySurface.Render();
        var presentationPlan = PresentationPlanContractSurface.Render();
        var httpApi = await HttpApiContractSurface.RenderAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(tools, McpToolContractSurface.Render(), StringComparer.Ordinal);
        Assert.Equal(keys, ConfigurationKeySurface.Render(), StringComparer.Ordinal);
        Assert.Equal(presentationPlan, PresentationPlanContractSurface.Render(), StringComparer.Ordinal);
        Assert.Equal(
            httpApi,
            await HttpApiContractSurface.RenderAsync(TestContext.Current.CancellationToken),
            StringComparer.Ordinal);
    }
}
