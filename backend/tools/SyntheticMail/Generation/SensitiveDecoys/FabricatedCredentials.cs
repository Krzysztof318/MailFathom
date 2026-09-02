// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>Credentials that are not credentials: values shaped exactly like the ones a secret scanner recognises.</summary>
/// <remarks>
/// <para>
/// Every value is assembled here, at run time, out of a constant opening and characters drawn from the corpus seed.
/// That is not a stylistic choice. This repository is public and its own scanning refuses a push carrying anything
/// shaped like a credential, so a file holding a finished token as a literal would be rejected before it could ever
/// reach a mailbox — and it would be worth rejecting, since nothing distinguishes a fabricated one on sight. Splitting
/// it into a prefix and a draw leaves no committed string for either scanner to find while producing a value the
/// deployment's scanner matches exactly.
/// </para>
/// <para>
/// The shapes are the ones the deployment's own corpus looks for, so each is named beside its entry in
/// <see cref="SensitiveDecoyCatalog" /> rather than only described here. A value that stopped matching would produce a
/// corpus that quietly tests nothing, which is why the tests assert the shapes rather than the values.
/// </para>
/// <para>
/// Every fabricated host is under the reserved <c>.test</c> top-level domain, exactly as an invented participant's is,
/// so a link a decoy carries cannot reach a service any more than an invented address can reach a person.
/// </para>
/// </remarks>
internal static class FabricatedCredentials
{
    /// <summary>The alphabet an access-key identifier is spelled in, which is base32 without its padding.</summary>
    private const string AccessKeyAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>What a password inside a connection string may hold, which excludes everything that would end it early.</summary>
    private const string PasswordAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";

    /// <summary>What a credential in a query string may hold, which is the unreserved set a service issues links with.</summary>
    private const string QueryTokenAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    /// <summary>How many base64 characters one line of an armoured key carries.</summary>
    private const int ArmouredLineLength = 64;

    /// <summary>Fabricates a personal access token of the shape a hosting provider issues.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A token that is not one.</returns>
    internal static string ProviderToken(Random source) => "dop_v1_" + RandomDraw.HexadecimalDigits(source, 64);

    /// <summary>Fabricates a cloud access-key identifier.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A key identifier that identifies nothing.</returns>
    internal static string CloudAccessKey(Random source) => "AKIA" + RandomDraw.From(source, AccessKeyAlphabet, 16);

    /// <summary>Fabricates an armoured private key.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A PEM block whose body is drawn bytes rather than a key.</returns>
    /// <remarks>
    /// The body is deliberately arbitrary bytes rather than a generated key pair. A scanner reads the armour and the
    /// length of what it wraps, so generating real key material would spend a dependency and a second of CPU on a
    /// property nothing here reads — and would produce a value somebody could mistake for one worth protecting.
    /// </remarks>
    internal static string PrivateKey(Random source)
    {
        var body = Convert.ToBase64String(RandomDraw.Bytes(source, 192));
        var armoured = new StringBuilder();

        armoured.Append("-----BEGIN OPENSSH PRIVATE KEY-----\n");

        for (var index = 0; index < body.Length; index += ArmouredLineLength)
        {
            armoured
                .Append(body.AsSpan(index, Math.Min(ArmouredLineLength, body.Length - index)))
                .Append('\n');
        }

        return armoured.Append("-----END OPENSSH PRIVATE KEY-----").ToString();
    }

    /// <summary>Fabricates a JSON Web Token.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>Three base64url segments separated by dots, signed by nothing.</returns>
    /// <remarks>
    /// The header and the payload are encoded rather than written out as text that happens to start with <c>ey</c>,
    /// because anything decoding one and finding no JSON would be entitled to discard it — and because a payload
    /// carrying claims is what makes the redacted result readable as a token having been removed.
    /// </remarks>
    internal static string JsonWebToken(Random source)
    {
        var reference = RandomDraw.DecimalDigits(source, 6);
        var header = Encode("""{"alg":"HS256","typ":"JWT"}""");
        var payload = Encode($$"""{"sub":"survey-{{reference}}","iss":"harbourline.test","exp":1893456000}""");

        return $"{header}.{payload}.{Base64Url.EncodeToString(RandomDraw.Bytes(source, 32))}";
    }

    /// <summary>Fabricates a connection string carrying the credential it connects with.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A URI naming a database nobody runs.</returns>
    internal static string ConnectionString(Random source) =>
        $"postgresql://survey_reader:{RandomDraw.From(source, PasswordAlphabet, 20)}@db.harbourline.test:5432/tideline";

    /// <summary>Fabricates a link whose query string is the credential.</summary>
    /// <param name="source">What the draw comes from.</param>
    /// <returns>A download link that downloads nothing.</returns>
    internal static string CredentialUrl(Random source) =>
        $"https://files.harbourline.test/exports/tide-table.csv?access_token={RandomDraw.From(source, QueryTokenAlphabet, 40)}";

    private static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
}
