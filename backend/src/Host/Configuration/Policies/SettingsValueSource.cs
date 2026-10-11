// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Which step of the resolution order supplied a governed property's effective value.</summary>
/// <remarks>
/// Declared in the order a value resolves in, first hit winning. The two halves run in opposite directions on
/// purpose: a force is the wider scope overruling the narrower one, so the deployment's is read first, and a default
/// is the narrower scope knowing its own people better, so the organization's is.
/// </remarks>
internal enum SettingsValueSource
{
    /// <summary>The deployment's policy forces the value.</summary>
    DeploymentForced = 0,

    /// <summary>The organization's policy forces the value.</summary>
    OrganizationForced = 1,

    /// <summary>The record states the value itself.</summary>
    Record = 2,

    /// <summary>The record states nothing and the organization's policy supplies a default.</summary>
    OrganizationDefault = 3,

    /// <summary>The record and the organization state nothing and the deployment's policy supplies a default.</summary>
    DeploymentDefault = 4,

    /// <summary>Nothing states the value, so it is whatever the build takes for an unstated one.</summary>
    BuiltIn = 5,
}
