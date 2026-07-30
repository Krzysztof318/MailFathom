// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Host.Configuration;

/// <summary>Configures what one read of a message body may return.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class EmailContentOptions
{
    /// <summary>Gets or sets the maximum number of characters one body representation returns.</summary>
    /// <remarks>
    /// <para>
    /// The range is about what a response can usefully carry rather than about what can be stored. The lower bound
    /// keeps a configured value from making every message look truncated, and the upper bound is where a single body
    /// stops fitting in the context of anything that would read it: a million characters is several times the largest
    /// context an MCP client can be expected to hold, so a body above it would be discarded by the caller rather than
    /// read.
    /// </para>
    /// <para>
    /// It is not the bound that protects this process. A body can only be as large as the raw MIME it was stored
    /// under, which <c>MailSynchronization:MaxRawMimeBytes</c> already limits; this decides how much of it a caller is
    /// handed.
    /// </para>
    /// </remarks>
    [Range(1_000, 1_000_000)]
    public int MaxBodyCharacters { get; set; } = 100_000;
}
