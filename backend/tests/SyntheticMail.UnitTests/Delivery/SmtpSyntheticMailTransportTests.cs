// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NSubstitute;
using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Delivery;

/// <summary>What the submission session does with the credential, the socket, and the envelope.</summary>
/// <remarks>
/// Driven through MailKit's own published <see cref="ISmtpClient" /> rather than a hand-written port, which is what
/// lets the three decisions worth protecting be observed at all: none of them produces a return value, and every one
/// of them would otherwise need a real server to see.
/// </remarks>
public sealed class SmtpSyntheticMailTransportTests
{
    private static readonly MailboxAddress Recipient = new("Developer", "developer@example.com");

    [Theory]
    [InlineData(nameof(SmtpTransportSecurity.StartTls), nameof(SecureSocketOptions.StartTls))]
    [InlineData(nameof(SmtpTransportSecurity.ImplicitTls), nameof(SecureSocketOptions.SslOnConnect))]
    public void ResolveSocketOptions_ASecurity_ChoosesTheOptionThatCannotContinueUnencrypted(
        string securityName,
        string expectedOptionName)
    {
        // Arrange
        // The security is named rather than passed: it is internal, and widening it so a public test signature could
        // carry it would change a production type to suit a test.
        var security = Enum.Parse<SmtpTransportSecurity>(securityName);

        // Act
        var option = SmtpSyntheticMailTransport.ResolveSocketOptions(security);

        // Assert
        Assert.Equal(Enum.Parse<SecureSocketOptions>(expectedOptionName), option);
    }

    [Fact]
    public void ResolveSocketOptions_EverySecurity_RefusesEveryOptionThatWouldSendThePasswordInTheClear()
    {
        // Arrange
        SecureSocketOptions[] downgrading =
        [
            SecureSocketOptions.None,
            SecureSocketOptions.Auto,
            SecureSocketOptions.StartTlsWhenAvailable,
        ];

        // Act
        var chosen = Enum
            .GetValues<SmtpTransportSecurity>()
            .Select(SmtpSyntheticMailTransport.ResolveSocketOptions)
            .ToArray();

        // Assert
        // Written over the whole enumeration rather than over the two values it holds today, so a third one added
        // later fails here instead of quietly reintroducing the downgrade this class exists to refuse.
        Assert.NotEmpty(chosen);
        Assert.All(chosen, option => Assert.DoesNotContain(option, downgrading));
    }

    [Fact]
    public async Task OpenAsync_StartTls_RequiresTheUpgradeRatherThanTakingItWhenOffered()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();

        // Act
        await using var transport = new SmtpSyntheticMailTransport(Account(), client);
        await transport.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        await client.Received(1).ConnectAsync(
            "smtp.example.test",
            587,
            SecureSocketOptions.StartTls,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAsync_ImplicitTls_HandshakesBeforeAnythingIsSent()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        var account = Account() with { Security = SmtpTransportSecurity.ImplicitTls, Port = 465 };

        // Act
        await using var transport = new SmtpSyntheticMailTransport(account, client);
        await transport.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        await client.Received(1).ConnectAsync(
            "smtp.example.test",
            465,
            SecureSocketOptions.SslOnConnect,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAsync_Always_AuthenticatesOnlyAfterTheConnectionIsSecured()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        var calls = new List<string>();

        // Recorded through callbacks rather than through NSubstitute's ordered assertion, whose `Received` collides
        // with MimeKit's own type of that name; a recorded sequence also says what the actual order was when it fails.
        client
            .When(substitute => substitute.ConnectAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<SecureSocketOptions>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => calls.Add("connect"));
        client
            .When(substitute => substitute.AuthenticateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => calls.Add("authenticate"));

        // Act
        await using var transport = new SmtpSyntheticMailTransport(Account(), client);
        await transport.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["connect", "authenticate"], calls);
    }

    [Fact]
    public async Task OpenAsync_AServerThatCannotSecureTheConnection_IsReportedAsAFailureNamingTheEndpoint()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();

        // MailKit raises this when a server advertises no STARTTLS extension, which is the refusal that keeps the
        // password off an unencrypted socket.
        client
            .ConnectAsync("smtp.example.test", 587, SecureSocketOptions.StartTls, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new NotSupportedException("The SMTP server does not support the STARTTLS extension.")));

        await using var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => transport.OpenAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("smtp.example.test:587", failure.Message, StringComparison.Ordinal);
        Assert.Contains("STARTTLS", failure.Message, StringComparison.Ordinal);
        await client.DidNotReceive().AuthenticateAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAsync_AServerThatRefusesTheCredential_IsReportedWithoutTheCredentialInIt()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();

        client
            .AuthenticateAsync("throwaway@example.test", "not-a-real-password", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AuthenticationException("535 authentication failed")));

        await using var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => transport.OpenAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("refused the configured credential", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-password", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_Always_StatesTheEnvelopeRatherThanLettingTheHeadersSupplyOne()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        var account = Account();

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Ada Almqvist", "ada.almqvist@harbourline.test"));
        message.To.Add(Recipient);
        message.Cc.Add(new MailboxAddress("Frida Hjelm", "frida.hjelm@blueheron.test"));
        message.Body = new TextPart("plain") { Text = "The tidal buoy surveys the quay." };

        // Act
        await using var transport = new SmtpSyntheticMailTransport(account, client);
        await transport.SendAsync(message, Recipient, TestContext.Current.CancellationToken);

        // Assert
        // The invented participants stay in the headers and reach the envelope of nothing, so a reserved-domain
        // address the server could never resolve is never a delivery it is asked to attempt.
        await client.Received(1).SendAsync(
            message,
            account.Address,
            Arg.Is<IEnumerable<MailboxAddress>>(recipients =>
                recipients != null && recipients.Single().Address == Recipient.Address),
            Arg.Any<CancellationToken>(),
            Arg.Any<MailKit.ITransferProgress>());
    }

    [Fact]
    public async Task SendAsync_AServerRefusal_BecomesAFailureAboutThatOneMessage()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();

        client
            .SendAsync(
                Arg.Any<MimeMessage>(),
                Arg.Any<MailboxAddress>(),
                Arg.Any<IEnumerable<MailboxAddress>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<MailKit.ITransferProgress>())
            .Returns(Task.FromException<string>(new SmtpProtocolException("552 message too large")));

        using var message = new MimeMessage();

        await using var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => transport.SendAsync(message, Recipient, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("552 message too large", failure.Message);
    }

    [Fact]
    public async Task OpenAsync_AMechanismThatCannotNegotiate_IsReportedThroughTheAuthenticationBranch()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();

        client
            .AuthenticateAsync("throwaway@example.test", "not-a-real-password", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new SaslException("PLAIN", SaslErrorCode.InvalidChallenge, "malformed challenge")));

        await using var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => transport.OpenAsync(TestContext.Current.CancellationToken));

        // Assert
        // `SaslException` derives from MailKit's own `AuthenticationException`, so the type pattern in
        // `IsTransportFailure` already covers it — asserted here rather than reasoned about, because that is a fact
        // about a library this code does not own and a future MailKit could move it.
        Assert.IsAssignableFrom<AuthenticationException>(failure.InnerException);
        Assert.Contains("refused the configured credential", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_AConnectedSession_QuitsBeforeDisposingTheClient()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        client.IsConnected.Returns(true);

        var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        await transport.DisposeAsync();

        // Assert
        await client.Received(1).DisconnectAsync(quit: true, Arg.Any<CancellationToken>());
        client.Received(1).Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ASessionThatNeverConnected_DisposesWithoutSpeakingToAnything()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        client.IsConnected.Returns(false);

        var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        await transport.DisposeAsync();

        // Assert
        await client.DidNotReceive().DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
        client.Received(1).Dispose();
    }

    [Fact]
    public async Task DisposeAsync_AConnectionThatFailsOnTheWayDown_DisposesAnyway()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        client.IsConnected.Returns(true);
        client
            .DisconnectAsync(quit: true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("the connection was reset")));

        var transport = new SmtpSyntheticMailTransport(Account(), client);

        // Act
        await transport.DisposeAsync();

        // Assert
        // A session being torn down has nothing left to report, and letting this out of an `await using` would replace
        // whatever actually went wrong with the noise of the connection noticing afterwards.
        client.Received(1).Dispose();
    }

    [Fact]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The subject of this test is a DisposeAsync that throws, so no path can dispose the transport in the way this rule looks for. The client underneath is what must survive that, and the assertion below is what says it did.")]
    public async Task DisposeAsync_ATeardownFailureOutsideTheTransportSet_StillDisposesTheClient()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();
        client.IsConnected.Returns(true);
        client
            .DisconnectAsync(quit: true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ObjectDisposedException(nameof(ISmtpClient))));

        var transport = new SmtpSyntheticMailTransport(Account(), client);
        ObjectDisposedException? thrown = null;

        // Act
        try
        {
            await transport.DisposeAsync();
        }
        catch (ObjectDisposedException failure)
        {
            thrown = failure;
        }

        // Assert
        Assert.NotNull(thrown);
        // The catch filter deliberately admits only what the network produced, so anything else leaves through
        // `DisposeAsync` — and the socket underneath would leave with it undisposed were the disposal not in a
        // `finally`. The failure still propagates: it is a defect here rather than a connection noticing a reset.
        client.Received(1).Dispose();
    }

    [Fact]
    public void Construct_ANullArgument_IsRefused()
    {
        // Arrange
        var client = Substitute.For<ISmtpClient>();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SmtpSyntheticMailTransport(null!, client));
        Assert.Throws<ArgumentNullException>(() => new SmtpSyntheticMailTransport(Account(), null!));
    }

    private static SendingAccount Account() => new(
        "smtp.example.test",
        587,
        SmtpTransportSecurity.StartTls,
        new MailboxAddress("Throwaway", "throwaway@example.test"),
        "throwaway@example.test",
        "not-a-real-password",
        SyntheticAuthorIdentity.Fabricated);
}
