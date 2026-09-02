// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Api.Documentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Xunit;

namespace MailFathom.Host.UnitTests.Api.Documentation;

/// <summary>Covers whether the document says about each operation what the running host would actually enforce.</summary>
/// <remarks>
/// The inaccuracy this guards against is the one a reader cannot detect by reading. A document that described a
/// protected operation as open would have the explorer offer a call the deployment then refuses, leaving a developer
/// to decide whether their credential or the document was wrong — so the requirement is asserted against the same
/// endpoint metadata the authorization middleware reads.
/// </remarks>
public sealed class ApiDocumentSecurityTests
{
    /// <summary>The credential every surface challenges for is published once, as the header a client already knows how to write.</summary>
    [Fact]
    public void DeclareCredentialScheme_Always_PublishesTheBearerScheme()
    {
        // Arrange
        var document = new OpenApiDocument();

        // Act
        ApiDocumentSecurity.DeclareCredentialScheme(document);

        // Assert
        var published = Assert.IsType<OpenApiComponents>(document.Components).SecuritySchemes;
        Assert.NotNull(published);

        var scheme = Assert.IsType<OpenApiSecurityScheme>(
            Assert.Contains(ApiDocumentSecurity.SchemeName, published));

        Assert.Equal(SecuritySchemeType.Http, scheme.Type);
        Assert.Equal("bearer", scheme.Scheme);
    }

    /// <summary>An operation the authorization middleware would demand a credential for is described as demanding one.</summary>
    [Fact]
    public void RequireCredentialWhereProtected_ForAnOperationBehindAPolicy_RequiresTheScheme()
    {
        // Arrange
        var operation = new OpenApiOperation();

        // Act
        ApiDocumentSecurity.RequireCredentialWhereProtected(
            operation,
            OperationContext(new AuthorizeAttribute("mailfathom.admin")));

        // Assert
        var requirement = Assert.Single(operation.Security ?? []);
        Assert.Contains(
            requirement.Keys,
            scheme => string.Equals(scheme.Reference?.Id, ApiDocumentSecurity.SchemeName, StringComparison.Ordinal));
    }

    /// <summary>An anonymous exemption wins over a requirement in the pipeline, so it wins here too.</summary>
    [Fact]
    public void RequireCredentialWhereProtected_ForAnExemptedOperation_LeavesItOpen()
    {
        // Arrange
        var operation = new OpenApiOperation();

        // Act
        ApiDocumentSecurity.RequireCredentialWhereProtected(
            operation,
            OperationContext(new AuthorizeAttribute("mailfathom.admin"), new AllowAnonymousAttribute()));

        // Assert
        Assert.Empty(operation.Security ?? []);
    }

    /// <summary>
    /// A surface whose credentials an operator never configured maps its routes without a requirement, and the
    /// document then says so rather than describing a lock that is not on the door.
    /// </summary>
    [Fact]
    public void RequireCredentialWhereProtected_ForAnOperationBehindNoPolicy_LeavesItOpen()
    {
        // Arrange
        var operation = new OpenApiOperation();

        // Act
        ApiDocumentSecurity.RequireCredentialWhereProtected(operation, OperationContext());

        // Assert
        Assert.Empty(operation.Security ?? []);
    }

    private static OpenApiOperationTransformerContext OperationContext(params object[] endpointMetadata) => new()
    {
        DocumentName = ApiDocumentation.DocumentName,
        Description = new ApiDescription
        {
            RelativePath = "api/admin/session",
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata },
        },
        ApplicationServices = new ServiceCollection().BuildServiceProvider(),
    };
}
