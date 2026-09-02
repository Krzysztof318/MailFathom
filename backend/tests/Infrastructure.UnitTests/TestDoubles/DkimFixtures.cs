// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MimeKit;
using MimeKit.Cryptography;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Signs messages so the local verification can be proved against real signatures rather than stubs.</summary>
/// <remarks>
/// The key pair is generated per fixture and never leaves the process, so nothing here is a committed credential and no
/// test depends on a key somebody else's mailbox uses. Signing for real is what makes the tests worth writing: a
/// substitute for the verifier would assert that MailFathom calls MimeKit, while this asserts that a signature made
/// with one key verifies against the record that publishes it and does not verify once the bytes move.
/// </remarks>
internal static class DkimFixtures
{
    /// <summary>The selector every fixture signs under.</summary>
    public const string Selector = "mailfathom";

    /// <summary>The domain every fixture signs as, unless a test names another.</summary>
    public const string SigningDomain = "signer.example.test";

    /// <summary>Signs one message and publishes the key it was signed with.</summary>
    /// <param name="fromAddress">The address the message displays as its author.</param>
    /// <param name="signingDomain">The domain the signature is made for.</param>
    /// <param name="body">The message's text.</param>
    /// <returns>The signed message's bytes and the record a resolver would answer with.</returns>
    public static SignedMessage Sign(
        string fromAddress = "anna@signer.example.test",
        string signingDomain = SigningDomain,
        string body = "Dzień dobry.")
    {
        using var key = RSA.Create(2048);

        using var message = new MimeMessage
        {
            Subject = "Quarterly invoice",
            Body = new TextPart("plain") { Text = body },
        };
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse("bob@example.test"));
        message.MessageId = "signed-1@example.test";

        using var privateKey = new MemoryStream(Encoding.ASCII.GetBytes(key.ExportPkcs8PrivateKeyPem()));
        new DkimSigner(privateKey, signingDomain, Selector, DkimSignatureAlgorithm.RsaSha256)
            .Sign(message, [HeaderId.From, HeaderId.To, HeaderId.Subject, HeaderId.Date, HeaderId.MessageId]);

        using var rawMime = new MemoryStream();
        message.WriteTo(rawMime);

        return new SignedMessage(
            rawMime.ToArray(),
            $"v=DKIM1; k=rsa; p={Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}");
    }

    /// <summary>Loads a message's bytes back into the parsed form the verification is handed.</summary>
    /// <param name="rawMime">The message's bytes.</param>
    /// <returns>The parsed message; the caller disposes it.</returns>
    public static MimeMessage Parse(byte[] rawMime)
    {
        using var source = new MemoryStream(rawMime);

        return MimeMessage.Load(source);
    }

    /// <summary>One signed message and the key record that verifies it.</summary>
    /// <param name="RawMime">The message exactly as a transport would carry it.</param>
    /// <param name="PublicKeyRecord">The text a signing domain publishes at the selector's name.</param>
    public sealed record SignedMessage(byte[] RawMime, string PublicKeyRecord);
}
