// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Infrastructure.Mail;

/// <summary>One account's resolved secret material, owned by the operation that resolved it.</summary>
/// <param name="Password">The mailbox password or app password.</param>
/// <remarks>
/// <para>
/// The instance is owned by the operation that resolved it — one connection attempt, or one startup validation pass —
/// and must be disposed when that operation ends, which bounds the window in which a process dump could contain the
/// material to an operation rather than to process uptime. Because every operation owns its own instance, publishing a
/// new configuration snapshot never erases material an in-flight operation is still reading.
/// </para>
/// <para>
/// It wraps a single secret today and could look collapsible into <see cref="ResolvedSecret" />. It is kept because the
/// resolved trust anchor joins it beside the password, and the disposal rule has to cover both without changing the
/// signature every caller of the settings provider already uses.
/// </para>
/// </remarks>
public sealed record MailAccountSecrets(ResolvedSecret Password) : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        this.Password.Dispose();
    }
}
