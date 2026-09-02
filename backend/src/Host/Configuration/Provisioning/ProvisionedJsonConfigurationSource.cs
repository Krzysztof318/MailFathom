// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>One deployment-provisioned JSON file, as a source that says which layer put it there.</summary>
/// <remarks>
/// It reads exactly as the framework's own JSON source does and adds nothing to it but its own type. That type is the
/// point: <see cref="OperatorOverrideBoundary" /> tells .NET User Secrets apart from an ordinary JSON source by the
/// file name the framework layers it in under, and a provisioned file resolves to a bare file name too — so a
/// ConfigMap key or a <c>ConfigurationSources:File</c> named <c>secrets.json</c> would otherwise be read as a source
/// the operator supplied, and the layers MailFathom inserts below that boundary would land below a provisioned file
/// instead of above it. A deployment can choose any file name it likes; it cannot choose which type this host
/// constructs.
/// </remarks>
internal sealed class ProvisionedJsonConfigurationSource : JsonConfigurationSource;
