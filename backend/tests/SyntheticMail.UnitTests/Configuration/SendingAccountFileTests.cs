// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Configuration;

/// <summary>What the command demands of a credential file before it will connect to anything.</summary>
public sealed class SendingAccountFileTests
{
    private const string Origin = "synthetic-mail.local.json";

    private const string Complete = """
        {
          "host": "smtp.example.test",
          "port": 2525,
          "security": "ImplicitTls",
          "address": "Throwaway <throwaway@example.test>",
          "userName": "throwaway",
          "password": "not-a-real-password",
          "author": "SendingAccount"
        }
        """;

    [Fact]
    public void ReadFrom_ACompleteFile_ReadsEveryValue()
    {
        // Arrange, Act
        var account = Read(Complete);

        // Assert
        Assert.Equal("smtp.example.test", account.Host);
        Assert.Equal(2525, account.Port);
        Assert.Equal(MailTransportSecurity.ImplicitTls, account.Security);
        Assert.Equal("throwaway@example.test", account.Address.Address);
        Assert.Equal("throwaway", account.UserName);
        Assert.Equal("not-a-real-password", account.Password);
        Assert.Equal(SyntheticAuthorIdentity.SendingAccount, account.AuthorIdentity);
    }

    [Theory]
    [InlineData("host", """{ "address": "a@example.test", "password": "p" }""")]
    [InlineData("address", """{ "host": "h", "password": "p" }""")]
    [InlineData("password", """{ "host": "h", "address": "a@example.test" }""")]
    public void ReadFrom_AFileMissingARequiredValue_IsRefusedNamingTheKey(string key, string contents)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        Assert.Contains($"'{key}' is not set in '{Origin}'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_AFileNamingNoSecurity_DefaultsToTheUpgradingOne()
    {
        // Arrange, Act
        var account = Read("""{ "host": "h", "address": "a@example.test", "password": "p" }""");

        // Assert
        Assert.Equal(MailTransportSecurity.StartTls, account.Security);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Auto")]
    [InlineData("StartTlsWhenAvailable")]
    public void ReadFrom_ASecurityThatWouldNotSecureTheConnection_IsRefused(string security)
    {
        // Arrange
        var contents = $$"""{ "host": "h", "address": "a@example.test", "password": "p", "security": "{{security}}" }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        // The names an operator might reach for from another mail client are the opportunistic ones, and none of them
        // is here: a connection either secures itself or says outright that it does not, which is what the message
        // points the reader at instead.
        Assert.Contains("There is no opportunistic option", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MailTransportSecurity.Unsecured), failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("StartTls", 587)]
    [InlineData("ImplicitTls", 465)]
    public void ReadFrom_AFileNamingNoPort_DefaultsToTheConventionalOneForItsSecurity(string security, int expected)
    {
        // Arrange
        var contents = $$"""{ "host": "h", "address": "a@example.test", "password": "p", "security": "{{security}}" }""";

        // Act
        var account = Read(contents);

        // Assert
        Assert.Equal(expected, account.Port);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("mailserver.localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("172.17.0.2")]
    [InlineData("10.88.0.4")]
    [InlineData("10.89.1.7")]
    [InlineData("host.docker.internal")]
    [InlineData("host.containers.internal")]
    public void ReadFrom_AnUnsecuredConnectionToAHostBesideTheCommand_IsRead(string host)
    {
        // Arrange
        var contents = $$"""{ "host": "{{host}}", "address": "a@example.test", "password": "p", "security": "Unsecured" }""";

        // Act
        var account = Read(contents);

        // Assert
        // Reaching a throwaway mail server on the machine is the whole case this value exists for, so a corpus lands
        // in a mailbox without a real account and without a credential anybody has to hold.
        Assert.Equal(MailTransportSecurity.Unsecured, account.Security);
        Assert.Equal(25, account.Port);
    }

    [Theory]
    [InlineData("smtp.example.test")]
    [InlineData("localhost.example.test")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    [InlineData("192.168.1.50")]
    [InlineData("10.4.2.9")]
    [InlineData("172.20.3.4")]
    [InlineData("169.254.7.7")]
    [InlineData("fd00::1")]
    public void ReadFrom_AnUnsecuredConnectionToAnythingElse_IsRefusedNamingBothKeys(string host)
    {
        // Arrange
        var contents = $$"""{ "host": "{{host}}", "address": "a@example.test", "password": "p", "security": "Unsecured" }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        // The host is judged as written rather than resolved, so a name nothing here recognizes is refused instead of
        // being looked up — a reader that made a network call could be pointed at a loopback answer today and
        // somewhere else tomorrow, with the password on the wire either way. The private addresses here are the point
        // of the narrow match: a home network, an employer's, a bridge nobody's runtime hands out by default, and a
        // link-local fallback are all networks a real mail server can sit on, and none of them is beside this command.
        Assert.Contains($"'security' in '{Origin}' is 'Unsecured'", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"'host' is '{host}'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_AFileNamingNoUserName_AuthenticatesAsTheAddress()
    {
        // Arrange, Act
        var account = Read("""{ "host": "h", "address": "a@example.test", "password": "p" }""");

        // Assert
        Assert.Equal("a@example.test", account.UserName);
    }

    [Fact]
    public void ReadFrom_AFileNamingNoAuthor_FabricatesTheAuthor()
    {
        // Arrange, Act
        var account = Read("""{ "host": "h", "address": "a@example.test", "password": "p" }""");

        // Assert
        Assert.Equal(SyntheticAuthorIdentity.Fabricated, account.AuthorIdentity);
    }

    [Fact]
    public void ReadFrom_AnAddressThatIsNotOne_IsRefused()
    {
        // Arrange
        var contents = """{ "host": "h", "address": "not an address", "password": "p" }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        Assert.Contains("is not a mail address", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_ContentsThatAreNotJson_IsRefusedNamingTheFile()
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read("host = smtp.example.test"));

        // Assert
        Assert.Contains($"'{Origin}' could not be read", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_AnEmptyDocument_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<SyntheticMailFailure>(() => Read("null"));
    }

    [Fact]
    public void ReadFrom_KeysWrittenInAnotherCase_AreStillRead()
    {
        // Arrange, Act
        var account = Read("""{ "Host": "h", "Address": "a@example.test", "Password": "p" }""");

        // Assert
        Assert.Equal("h", account.Host);
    }

    [Fact]
    public void Read_NoFileAtAll_IsRefusedNamingWhatToWriteAndWhere()
    {
        // Arrange
        var path = Path.Combine(AppContext.BaseDirectory, $"nothing-writes-this-{Guid.NewGuid():N}.local.json");

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => SendingAccountFile.Read(path, UnconfiguredUserSecrets.Store()));

        // Assert
        Assert.Contains(path, failure.Message, StringComparison.Ordinal);
        Assert.Contains("throwaway account", failure.Message, StringComparison.Ordinal);
        Assert.Contains("git-ignored", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_AnAccount_PrintsEveryValueButThePassword()
    {
        // Arrange
        var account = Read(Complete);

        // Act
        var printed = account.ToString();

        // Assert
        // The synthesized printing a record would otherwise carry puts the real credential into any interpolation,
        // log line, or debugger inspection of the whole object, so the redaction belongs to the type rather than to
        // the habits of its call sites.
        Assert.DoesNotContain("not-a-real-password", printed, StringComparison.Ordinal);
        Assert.Contains("Password = ***", printed, StringComparison.Ordinal);
        Assert.Contains("smtp.example.test", printed, StringComparison.Ordinal);
        Assert.Contains("throwaway@example.test", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_AnAccountInsideAnInterpolation_StillHidesThePassword()
    {
        // Arrange
        var account = Read(Complete);

        // Act
        var interpolated = $"submitting as {account}";

        // Assert
        Assert.DoesNotContain("not-a-real-password", interpolated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(65535)]
    public void ReadFrom_AFileNamingAPortInRange_KeepsIt(int port)
    {
        // Arrange, Act
        var account = Read($$"""{ "host": "h", "address": "a@example.test", "password": "p", "port": {{port}} }""");

        // Assert
        Assert.Equal(port, account.Port);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(70000)]
    public void ReadFrom_AFileNamingAPortOutsideTheRange_IsRefusedNamingTheKey(int port)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(
            () => Read($$"""{ "host": "h", "address": "a@example.test", "password": "p", "port": {{port}} }"""));

        // Assert
        // MailKit throws ArgumentOutOfRangeException for a port outside this range, and that is neither a transport
        // failure the delivery layer translates nor one the runner reports — so without this check a mistyped digit
        // reaches the terminal as a stack trace rather than as the one line every other malformed value produces.
        Assert.Contains($"'port' in '{Origin}' is {port}", failure.Message, StringComparison.Ordinal);
        Assert.Contains("0 to 65535", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("security", "2")]
    [InlineData("security", "-1")]
    [InlineData("author", "0")]
    [InlineData("author", "7")]
    [InlineData("author", "0x1")]
    public void ReadFrom_AFileNamingAnEnumerationValueAsANumber_IsRefusedNamingTheOptions(string key, string written)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(
            () => Read($$"""{ "host": "h", "address": "a@example.test", "password": "p", "{{key}}": "{{written}}" }"""));

        // Assert
        // `Enum.TryParse` accepts a string of digits and answers with whatever number it holds, and asking only
        // whether that number is defined is a different question: '2' is the number of a declared member, so a file
        // could select an unsecured connection without naming one. A file writes a name or is refused.
        Assert.Contains($"'{key}' in '{Origin}' is '{written}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("which is not one of", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_ADocument_HidesThePasswordBeforeAnythingIsValidated()
    {
        // Arrange
        var document = new SendingAccountDocument
        {
            Host = "smtp.example.test",
            Port = 2525,
            Address = "throwaway@example.test",
            Password = "not-a-real-password",
        };

        // Act
        var printed = $"{document}";

        // Assert
        // This type holds the credential between parsing and validation, which is exactly the window a message about
        // a file that failed validation would be written in.
        Assert.DoesNotContain("not-a-real-password", printed, StringComparison.Ordinal);
        Assert.Contains("Password = ***", printed, StringComparison.Ordinal);
        Assert.Contains("smtp.example.test", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_ANullArgument_IsRefused()
    {
        // Arrange
        using var contents = new MemoryStream();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SendingAccountFile.ReadFrom(null!, Origin));
        Assert.Throws<ArgumentNullException>(() => SendingAccountFile.ReadFrom(contents, null!));
    }

    private static SendingAccount Read(string contents)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));

        return SendingAccountFile.ReadFrom(stream, Origin);
    }
}
