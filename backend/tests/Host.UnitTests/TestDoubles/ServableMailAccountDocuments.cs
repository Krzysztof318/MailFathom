// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Completes a mail account document a test states with the connection settings every served account carries.</summary>
/// <remarks>
/// A document is read the way the roster's judge reads it, which refuses an account it could not synchronize. A test
/// about one setting states that setting alone, and this supplies the host, user name, and password reference beside it
/// so the document is refused only for what the test is about. A document that is not a JSON object is returned as it is.
/// </remarks>
internal static class ServableMailAccountDocuments
{
    /// <summary>Adds the connection settings a document leaves out.</summary>
    /// <param name="document">The document as the test states it.</param>
    /// <returns>The document with a host, a user name, and a password reference wherever it named none.</returns>
    internal static string Completing(string document)
    {
        if (JsonNode.Parse(document) is not JsonObject declaration)
        {
            return document;
        }

        declaration.TryAdd("Host", "imap.example.test");
        declaration.TryAdd("UserName", "alex@example.test");
        declaration.TryAdd(
            "Secrets",
            new JsonObject
            {
                ["Password"] = new JsonObject
                {
                    ["Name"] = "imap-password",
                    ["SecretReference"] = "systemd-credential:imap-password",
                },
            });

        return declaration.ToJsonString();
    }
}
