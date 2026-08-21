// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration;

/// <summary>Indicates that a setting only the process environment can deliver was given its value somewhere else.</summary>
/// <remarks>
/// <para>
/// The failure exists because the mistake it names is otherwise invisible. A value written into an <c>appsettings.json</c>
/// file, a provisioned configuration file, or a command-line argument is accepted by the configuration pipeline and can
/// be read back out of it, so nothing about the deployment looks wrong; the reader that needed it had already taken its
/// value from the environment, or reads only the environment and never consults configuration at all. The result is a
/// setting an operator can point at in their own file while the process behaves as though it were unset.
/// </para>
/// <para>
/// The message names the variables and never their values. A variable name is MailFathom's or the platform's own name
/// for a setting, in the same class as an account alias; a value is whatever the deployment put there, which for
/// <c>OTEL_EXPORTER_OTLP_HEADERS</c> is a collector's credential and for <c>OPENSSL_CONF</c> is a path into the
/// deployment's own filesystem. Naming what to do with each is what the reader needs, and the value is already in front
/// of them.
/// </para>
/// </remarks>
public sealed class EnvironmentOnlySettingMisplacedException : MailFathomException
{
    /// <summary>Initializes a new failure naming the settings whose configured value reaches no reader.</summary>
    /// <param name="operatorSafeMessage">A message naming the variables and the correction, and no configured value.</param>
    public EnvironmentOnlySettingMisplacedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EnvironmentOnlySettingMisplaced;
}
