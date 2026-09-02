// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;
using MailFathom.Domain.Access;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes every authorization refusal on the two channels an operator has, and on no third one.</summary>
/// <remarks>
/// <para>
/// The counter is what an alert is written against, because the reading worth acting on is a rate rather than an
/// event: one refusal is a client that was narrowed, and a credential that starts producing them is one being used for
/// something it was never provisioned for. The warning beside it is what turns that rate back into a repair, since a
/// counter cannot say which credential to widen.
/// </para>
/// <para>
/// The dimensions are three closed sets — the surface, MailFathom's own name for the tool or route that was refused,
/// and the permission that would have sufficed — so a client looping over names it invented cannot mint a time series
/// apiece. The identity is deliberately on the log alone: it is the one value here whose cardinality follows the
/// credentials an operator wrote and, where a token admitted the caller, it is an issuer and a remote party's
/// identifier for a person, which belongs in a record a deployment can set a level on rather than in an exported
/// series that never goes away.
/// </para>
/// <para>
/// Nothing else about the refused request reaches either channel. Not an argument, not a route value, not a header, not
/// the credential material the caller presented, and nothing about the mail the request was for — a refused call is
/// still a call a stranger composed, and none of what it carried belongs in the record written to prove it was stopped.
/// </para>
/// </remarks>
internal sealed partial class AuthorizationRefusalTelemetry : IAuthorizationRefusalTelemetry
{
    internal const string SurfaceTagName = "mailfathom.authorization.surface";
    internal const string OperationTagName = "mailfathom.authorization.operation";
    internal const string PermissionTagName = "mailfathom.authorization.permission";

    internal const string MailSurfaceName = "mail";
    internal const string AdministrationSurfaceName = "administration";

    /// <summary>The one value a refusal that no grant would have satisfied is counted under.</summary>
    /// <remarks>
    /// A refusal names no permission where the operation published none for a caller to hold: a route that declared
    /// nothing, or a use case refusing over the kind of principal that reached it rather than over a grant. Counting
    /// those under a permission name would tell an operator to grant something that would not have helped, and leaving
    /// them out would drop the one refusal whose remedy is a defect report.
    /// </remarks>
    internal const string UnnamedPermissionValue = "(none)";

    /// <summary>What the log names a refusal whose work was reached under no principal at all.</summary>
    internal const string UnidentifiedCallerValue = "(none)";

    private readonly ILogger<AuthorizationRefusalTelemetry> logger;
    private readonly Counter<long> refusalCount;

    /// <summary>Initializes the instrument every refusal is counted on.</summary>
    /// <param name="logger">Records the credential and the permission it lacked.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public AuthorizationRefusalTelemetry(ILogger<AuthorizationRefusalTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.refusalCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.authorization.refusals",
            unit: "{refusal}",
            description: "Calls and routes refused for want of a permission, by surface, operation, and the permission that was missing.");
    }

    /// <inheritdoc />
    public void RecordRefusal(
        ProtectedSurface surface,
        string operation,
        MailFathomPermission requiredPermission,
        string? refusedIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var surfaceName = NameOf(surface);

        this.refusalCount.Add(
            1,
            new KeyValuePair<string, object?>(SurfaceTagName, surfaceName),
            new KeyValuePair<string, object?>(OperationTagName, operation),
            new KeyValuePair<string, object?>(
                PermissionTagName,
                requiredPermission.IsSpecified ? requiredPermission.Name : UnnamedPermissionValue));

        var identity = refusedIdentity ?? UnidentifiedCallerValue;

        if (requiredPermission.IsSpecified)
        {
            this.LogRefusalOfAPermission(identity, operation, surfaceName, requiredPermission.Name);
        }
        else
        {
            this.LogRefusalNamingNoPermission(identity, operation, surfaceName);
        }
    }

    /// <summary>Names a surface as a dimension value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the surface is not one this adapter publishes.</exception>
    /// <remarks>
    /// A closed mapping rather than the member's own name, for the reason every published mapping here is closed: the
    /// value is what a dashboard and an alert are written against, so a surface added without one has to fail rather
    /// than silently rename a series.
    /// </remarks>
    private static string NameOf(ProtectedSurface surface) => surface switch
    {
        ProtectedSurface.Mail => MailSurfaceName,
        ProtectedSurface.Administration => AdministrationSurfaceName,
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "The surface has no published dimension value."),
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The credential '{RefusedIdentity}' was refused '{Operation}' on the {Surface} surface, because its grant does not carry '{RequiredPermission}'.")]
    private partial void LogRefusalOfAPermission(
        string refusedIdentity,
        string operation,
        string surface,
        string requiredPermission);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The credential '{RefusedIdentity}' was refused '{Operation}' on the {Surface} surface, which publishes no permission any grant could carry.")]
    private partial void LogRefusalNamingNoPermission(string refusedIdentity, string operation, string surface);
}
