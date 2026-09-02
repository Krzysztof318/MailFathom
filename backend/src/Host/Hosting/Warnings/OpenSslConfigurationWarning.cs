// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>States at startup that this process reads its TLS parameters from a configured OpenSSL file rather than from the platform default.</summary>
/// <remarks>
/// <para>
/// Every TLS connection this process makes or terminates is handshaked by the system OpenSSL, and
/// <c>OPENSSL_CONF</c> replaces the configuration that library initializes from. A deployment sets it for one reason:
/// a mail server whose cipher suite, key size, or protocol version the platform's own policy refuses, which is a
/// legitimate posture where the alternative is not synchronizing that mailbox at all. It is therefore reported rather
/// than refused, the same way <see cref="McpTransportEncryptionWarning" /> reports a clear-text endpoint.
/// </para>
/// <para>
/// That statement is unconditional because every environment MailFathom targets is one where it holds: the deployment
/// shapes are a container, Kubernetes, and a systemd service, and the image is built for <c>linux/amd64</c> and
/// <c>linux/arm64</c> alone. Nothing here therefore asks which operating system it is running on. A platform where
/// .NET hands the handshake to something other than OpenSSL is a platform where much else in this repository — the
/// systemd credential provisioning above all — does not apply either, so a guard here would answer that question in
/// one place and leave it unasked everywhere it matters equally.
/// </para>
/// <para>
/// What it refuses to let happen is that the relaxation outlives the server that needed it. The variable is read while
/// OpenSSL initializes, before configuration binding exists, so nothing about it appears in any settings file an
/// operator reviews later; and its scope is the whole process, so a policy loosened for an IMAP handshake governs the
/// PostgreSQL connection and every other TLS session this process takes part in. Naming it at startup is the one place
/// those two facts are visible together.
/// </para>
/// <para>
/// The path is reported and the file's contents never are. A reader who can act on this message can read the file
/// themselves, and a log that transcribed it would put a deployment's whole TLS posture into whatever collects the
/// logs. What is reported is therefore that the variable was set, not that OpenSSL could read what it names: a path
/// that does not resolve leaves the platform default in force and says nothing, which is why the message states that
/// the posture <em>may</em> be weaker rather than that it is.
/// </para>
/// <para>
/// It runs as a hosted service so it reports during startup, next to the other startup diagnostics an operator reads.
/// It is registered whether or not the variable is set, because it is the warning that decides whether it has anything
/// to say.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class OpenSslConfigurationWarning : IHostedService
{
    /// <summary>The environment variable OpenSSL reads the path of its configuration file from.</summary>
    /// <remarks>
    /// OpenSSL's own name, not MailFathom's, because the library reads it directly. It is consequently unprefixed and
    /// governs every program started with it in the environment, which is why the app model passes an operator's value
    /// through to the host process rather than setting one of its own.
    /// </remarks>
    private const string EnvironmentVariableName = "OPENSSL_CONF";

    private readonly string? configurationFilePath;
    private readonly ILogger<OpenSslConfigurationWarning> logger;

    /// <summary>Initializes a new startup warning.</summary>
    /// <param name="configurationFilePath">The configured OpenSSL configuration file path, or <see langword="null" /> when the process was started without one.</param>
    /// <param name="logger">The startup logger.</param>
    public OpenSslConfigurationWarning(string? configurationFilePath, ILogger<OpenSslConfigurationWarning> logger)
    {
        this.configurationFilePath = configurationFilePath;
        this.logger = logger;
    }

    /// <summary>Composes the warning from the process environment.</summary>
    /// <param name="logger">The startup logger.</param>
    /// <returns>A warning that reports the configured path, or stays silent when the variable is unset.</returns>
    /// <remarks>
    /// Read from the environment rather than from <see cref="IConfiguration" />, even though the environment-variable
    /// provider would supply the same key. Configuration would also accept the name from an <c>appsettings.json</c>
    /// file or a mounted source, and a value written there reaches OpenSSL no earlier than the rest of that file — long
    /// after the library initialized. Reporting such a value would announce a policy that is not in force.
    /// </remarks>
    public static OpenSslConfigurationWarning FromEnvironment(ILogger<OpenSslConfigurationWarning> logger) =>
        new(Environment.GetEnvironmentVariable(EnvironmentVariableName), logger);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(this.configurationFilePath))
        {
            this.LogProcessTlsParametersTakenFromConfiguredOpenSslFile(this.configurationFilePath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This process was started with OPENSSL_CONF set, so its TLS parameters come from "
            + "{OpenSslConfigurationPath} rather than from the platform default, and its TLS posture may be weaker than "
            + "that default. The scope is the whole process: whatever that file relaxes applies to the mail connection "
            + "it was most likely set for, and equally to the database connection and to every other TLS session this "
            + "process takes part in. Unset it once the server that needed it no longer does.")]
    private partial void LogProcessTlsParametersTakenFromConfiguredOpenSslFile(string openSslConfigurationPath);
}
