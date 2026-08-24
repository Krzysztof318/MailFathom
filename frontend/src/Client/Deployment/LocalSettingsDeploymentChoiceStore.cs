// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Deployment;

/// <summary>Keeps the chosen deployment in the settings store the platform already has.</summary>
/// <remarks>
/// <para>
/// <see cref="ApplicationData.LocalSettings" /> rather than a file this application invents, because every head already
/// has one and each maps it to what that platform actually uses: a per-user preferences store on a desktop and the
/// browser's own storage for the page's origin in the browser head. So "per user" and "survives a restart" are the
/// platform's guarantees rather than something written here, and the same two lines of code are correct on all of them.
/// </para>
/// <para>
/// The value is written as the text of the address rather than as anything serialized. A settings store holds simple
/// values, the browser head publishes trimmed, and a single origin has no shape worth a serializer — reading it back is
/// the same parse a person's typed address goes through, which is also why a value that is no longer readable is
/// treated as nothing having been chosen rather than as a failure to start.
/// </para>
/// </remarks>
internal sealed class LocalSettingsDeploymentChoiceStore : IDeploymentChoiceStore
{
    /// <summary>The name the chosen address is kept under.</summary>
    /// <remarks>Qualified, because the container is shared with every other setting this application and its framework keep — Uno's own theme choice is in there beside it.</remarks>
    internal const string SettingName = "MailFathom.Deployment.Address";

    /// <inheritdoc />
    public Uri? Read() =>
        ApplicationData.Current.LocalSettings.Values.TryGetValue(SettingName, out var kept)
        && kept is string written
        && Uri.TryCreate(written, UriKind.Absolute, out var address)
            ? address
            : null;

    /// <inheritdoc />
    public void Write(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        ApplicationData.Current.LocalSettings.Values[SettingName] = address.AbsoluteUri;
    }
}
