// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Puts the owner roster onto every mail snapshot the process materializes.</summary>
/// <remarks>
/// <para>
/// A mail account is declared in one of three places — the deployment's own section, an owner's declared section, and
/// an owner's document — and only the first of them is a section this snapshot binds. The other two are reconciled
/// against the database while the host starts, which is after the first snapshot exists, so what is handed over here
/// is the holder rather than its contents: a lookup reads it when it runs, by which time the gate has filled it.
/// </para>
/// <para>
/// Registered by the composition root rather than beside the bound sections, so a candidate configuration is judged
/// without it. A write is judged against configuration alone, and a candidate that inherited this process's roster
/// would be judged against a deployment rather than against itself.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this post-configuration step.")]
internal sealed class ServedOwnerMailAccounts(ServedMailOwners servedOwners)
    : IPostConfigureOptions<MailSynchronizationOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, MailSynchronizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ServedOwners = servedOwners;
    }
}
