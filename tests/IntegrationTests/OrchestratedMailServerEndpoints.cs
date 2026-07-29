// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.IntegrationTests;

/// <summary>Where the orchestrated mail server accepts the two protocols this suite speaks.</summary>
/// <param name="Imap">The endpoint the adapter under test connects to, and the one flag state is read back over.</param>
/// <param name="Smtp">The endpoint mail is delivered through, which is how a test seeds the mailbox.</param>
/// <remarks>
/// The endpoints arrive as URIs because that is what the orchestration publishes, and are unpacked here rather than at
/// each call site: a mail client takes a host and a port, and repeating that unpacking would be repeating a decision
/// about which part of the URI means what.
/// </remarks>
public sealed record OrchestratedMailServerEndpoints(Uri Imap, Uri Smtp)
{
    /// <summary>Gets the host the IMAP listener is published on.</summary>
    public string ImapHost => this.Imap.Host;

    /// <summary>Gets the port the IMAP listener is published on.</summary>
    public int ImapPort => this.Imap.Port;

    /// <summary>Gets the host the SMTP listener is published on.</summary>
    public string SmtpHost => this.Smtp.Host;

    /// <summary>Gets the port the SMTP listener is published on.</summary>
    public int SmtpPort => this.Smtp.Port;
}
