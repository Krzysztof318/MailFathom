// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the contact commands accept, what they refuse without reaching a deployment, and what they print.</summary>
/// <remarks>
/// <para>
/// Three things are asserted throughout. What a command sends, because an amendment states the whole record and a
/// command that sent a difference would have the deployment refuse a record it never meant to write. What it refuses on
/// its own, because a choice the operator has to make — which address is preferred, whether an erasure is agreed to —
/// must not be resolved by the command on their behalf. And what reaches which stream, because a name or an address on
/// standard error is a contact's personal data in whatever captures a scripted run's failures.
/// </para>
/// <para>
/// The erasure is the one covered from both sides: that it asks before erasing, that a refused question erases nothing,
/// and that the flag is the only way to state the agreement where nobody is at the terminal.
/// </para>
/// </remarks>
public sealed class ContactCommandTests : IDisposable
{
    private const string Endpoint = "https://mail.example.test:8443";

    private static readonly Uri EndpointAddress = new(Endpoint);

    private static readonly string Identity = FakeContactDeployment.ContactIdentity.ToString("D");

    /// <summary>One page of the book holding a contact the deployment reports nothing optional about.</summary>
    /// <remarks>Written here rather than through the fixture, whose builder supplies a name, an address, and an origin for every contact it makes.</remarks>
    private const string SparseContactPage =
        """
        {"contacts":[{"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","displayName":null,"addresses":[],"preferredAddress":null,"note":null,"origin":null,"recordedAt":"2026-08-01T10:00:00+00:00","amendedAt":"2026-08-02T11:00:00+00:00"}],"nextCursor":null}
        """;

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-contact-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

    /// <summary>One address is no choice, so the record prefers it without the operator having to say so.</summary>
    [Fact]
    public async Task Create_OneAddress_SendsItAsThePreferredOneAndPrintsTheWrittenRecord()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(write: FakeContactDeployment.Written());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "create",
            "--name",
            "Anna Kowalska",
            "--address",
            "anna@example.test",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal("anna@example.test", SentRecord(deployment).GetProperty("preferredAddress").GetString());
        Assert.Contains(this.console.Lines, line => line.Contains("Anna Kowalska", StringComparison.Ordinal));
    }

    /// <summary>
    /// Which address a message to somebody goes to is the operator's decision, so several addresses without a stated
    /// preference is refused here rather than resolved by the order the arguments were typed in.
    /// </summary>
    [Fact]
    public async Task Create_SeveralAddressesAndNoPreference_RefusesWithoutWritingAnything()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(write: FakeContactDeployment.Written());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "create",
            "--name",
            "Anna Kowalska",
            "--address",
            "anna@example.test",
            "--address",
            "a.kowalska@work.example",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Post));
        Assert.Contains(this.console.Errors, line => line.Contains("--preferred", StringComparison.Ordinal));
    }

    /// <summary>
    /// The deployment names the contact that holds a claimed address, and the command repeats that identity and nothing
    /// else: reading that person is a lookup the operator performs rather than something a refused write hands them.
    /// </summary>
    [Fact]
    public async Task Create_AnAddressAnotherContactHolds_ReportsTheHoldersIdentityAndFails()
    {
        // Arrange
        var holder = new Guid("99999999-8888-7777-6666-555555555555");
        using var deployment = FakeContactDeployment.Holding(
            write: FakeContactDeployment.Refused("AddressHeldByAnotherContact", holder));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "create",
            "--name",
            "Anna Kowalska",
            "--address",
            "anna@example.test",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains(holder.ToString("D"), StringComparison.Ordinal));
    }

    /// <summary>Naming a contact both ways, or neither, is a mistake in the invocation rather than a lookup to attempt.</summary>
    [Theory]
    [InlineData("--id", "--address")]
    [InlineData(null, null)]
    public async Task Show_TheContactNamedBothWaysOrNeither_RefusesWithoutReachingTheBook(string? first, string? second)
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(lookup: FakeContactDeployment.Lookup());

        string[] arguments = first is null
            ? ["contact", "show", "--endpoint", Endpoint]
            : ["contact", "show", first, Identity, second!, "anna@example.test", "--endpoint", Endpoint];

        // Act
        var exitCode = await this.RunAsync(deployment, arguments);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Get));
    }

    /// <summary>A lookup by address answers with a person, which is what makes the book a book rather than a list.</summary>
    [Fact]
    public async Task Show_AnAddress_PrintsThePersonWhoUsesIt()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(lookup: FakeContactDeployment.Lookup());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "show",
            "--address",
            "ANNA@EXAMPLE.TEST",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.console.Lines, line => line.Contains("Anna Kowalska", StringComparison.Ordinal));
    }

    /// <summary>
    /// A book holding nobody is reported without the address being written back: it is somebody's address whether or not
    /// the book holds them, and this sentence is what a scripted run captures.
    /// </summary>
    [Fact]
    public async Task Show_AnAddressNobodyUses_FailsWithoutRepeatingTheAddress()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "show",
            "--address",
            "nobody@example.test",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.All(
            this.console.Errors,
            line => Assert.DoesNotContain("nobody@example.test", line, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every value lands under the heading naming it, which is the whole of what the column order is worth.</summary>
    /// <remarks>
    /// The listing replaced a line per contact whose fields were separated by punctuation, so nothing but position now
    /// says which reading a cell is. Two contacts rather than one, because a single row cannot show that the order is
    /// the same for every record.
    /// </remarks>
    [Fact]
    public async Task List_APageOfTheBook_SetsEveryContactUnderItsOwnHeading()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            page: FakeContactDeployment.Page(nextCursor: null, "Anna Kowalska", "Jan Nowak"));

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(
            this.console.Lines, "Contact", "Name", "Preferred address", "Origin");

        Assert.Equal(2, listing.Rows.Count);
        Assert.Equal(
            ["Anna Kowalska", "Jan Nowak"],
            listing.Rows.Select(row => listing.Cell(row, "Name")));
        Assert.Equal(
            ["person0@example.test", "person1@example.test"],
            listing.Rows.Select(row => listing.Cell(row, "Preferred address")));
        Assert.All(listing.Rows, row => Assert.Equal("Asserted", listing.Cell(row, "Origin")));
        Assert.All(
            listing.Rows,
            row => Assert.True(Guid.TryParse(listing.Cell(row, "Contact"), out _)));
    }

    /// <summary>What an absent value reads as, which is a sentence rather than a cell the operator has to interpret.</summary>
    /// <remarks>
    /// A listing draws every cell whether the deployment reported one or not, so a contact with no name would otherwise
    /// leave a gap that reads as a column having shifted rather than as a name nobody recorded.
    /// </remarks>
    [Fact]
    public async Task List_AContactTheDeploymentReportsLittleAbout_NamesWhatIsMissingInEveryCell()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(page: SparseContactPage);

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(
            this.console.Lines, "Contact", "Name", "Preferred address", "Origin");
        var row = Assert.Single(listing.Rows);

        Assert.Equal("none recorded", listing.Cell(row, "Name"));
        Assert.Equal("none reported", listing.Cell(row, "Preferred address"));
        Assert.Equal("unreported", listing.Cell(row, "Origin"));
    }

    /// <summary>The narrowing and the page bound reach the deployment as the query it reads them from.</summary>
    [Fact]
    public async Task List_AnOriginAndAPageSize_AsksTheDeploymentForExactlyThose()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            page: FakeContactDeployment.Page(nextCursor: null, "Anna Kowalska"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "list",
            "--origin",
            "Collected",
            "--page-size",
            "25",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal("?origin=Collected&pageSize=25", deployment.LastListingQuery());
    }

    /// <summary>
    /// A page that ends with a cursor says how to continue, because there is deliberately no command that walks the
    /// whole book: the operator asks for the next page rather than having it printed at them.
    /// </summary>
    [Fact]
    public async Task List_APageTheBookContinuesAfter_PrintsTheCursorTheNextPageIsAskedWith()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            page: FakeContactDeployment.Page("a-cursor-the-deployment-issued", "Anna Kowalska", "Bartosz Nowak"));

        // Act
        await this.RunAsync(deployment, "contact", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.console.Lines,
            line => line.Contains("--cursor a-cursor-the-deployment-issued", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole record rather than the difference. Correcting a name has to send back every address the contact holds,
    /// because the book replaces what it is given — a command sending the name alone would erase the addresses.
    /// </summary>
    [Fact]
    public async Task Update_AName_SendsTheHeldAddressesBackWithIt()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test", "a.kowalska@work.example"]),
            write: FakeContactDeployment.Written("Anna Nowak"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "update",
            "--id",
            Identity,
            "--name",
            "Anna Nowak",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = SentRecord(deployment);
        Assert.Equal("Anna Nowak", sent.GetProperty("displayName").GetString());
        Assert.Equal(["anna@example.test", "a.kowalska@work.example"], AddressesOf(sent));
        Assert.Equal("anna@example.test", sent.GetProperty("preferredAddress").GetString());
    }

    /// <summary>An invocation naming nothing to change is refused before the contact is read, rather than restating it.</summary>
    [Fact]
    public async Task Update_NothingNamed_RefusesWithoutReachingTheBook()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(lookup: FakeContactDeployment.Lookup());

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "update", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Get));
    }

    /// <summary>
    /// A collected record is not amended in place, and the refusal says what unlocks the write rather than reporting a
    /// permission the operator cannot act on.
    /// </summary>
    [Fact]
    public async Task Update_AContactTheDeploymentCollected_ReportsThatPromotingItComesFirst()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(origin: "Collected"),
            write: FakeContactDeployment.Refused("OriginRefusesWriter"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "update",
            "--id",
            Identity,
            "--name",
            "Anna Nowak",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.console.Errors, line => line.Contains("contact promote", StringComparison.Ordinal));
    }

    /// <summary>The added address joins the ones held, and the contact goes on preferring the one it preferred.</summary>
    [Fact]
    public async Task AddAddress_AnAddressTheContactDoesNotHold_SendsTheHeldOnesWithItAndKeepsThePreference()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test"]),
            write: FakeContactDeployment.Written());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "add-address",
            "--id",
            Identity,
            "--address",
            "a.kowalska@work.example",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = SentRecord(deployment);
        Assert.Equal(["anna@example.test", "a.kowalska@work.example"], AddressesOf(sent));
        Assert.Equal("anna@example.test", sent.GetProperty("preferredAddress").GetString());
    }

    /// <summary>
    /// The book merges two spellings of one address, so a command that sent one it already holds would report a record
    /// as changed when nothing was added. Comparison is the book's own, which ignores case across the whole address.
    /// </summary>
    [Fact]
    public async Task AddAddress_AnAddressTheContactAlreadyHoldsInAnotherCasing_RefusesWithoutWriting()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test"]),
            write: FakeContactDeployment.Written());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "add-address",
            "--id",
            Identity,
            "--address",
            "ANNA@EXAMPLE.TEST",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Put));
    }

    /// <summary>
    /// <c>--preferred</c> means the same thing in every command that takes it: the address to use by default
    /// afterwards. Naming the one being added is how an operator adds an address and makes it the default at once.
    /// </summary>
    [Fact]
    public async Task AddAddress_PreferringTheAddedAddress_SendsItAsTheOneUsedByDefault()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test"]),
            write: FakeContactDeployment.Written());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "add-address",
            "--id",
            Identity,
            "--address",
            "a.kowalska@work.example",
            "--preferred",
            "a.kowalska@work.example",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = SentRecord(deployment);
        Assert.Equal(["anna@example.test", "a.kowalska@work.example"], AddressesOf(sent));
        Assert.Equal("a.kowalska@work.example", sent.GetProperty("preferredAddress").GetString());
    }

    /// <summary>A contact holds at least one address, so the last one is not something this command takes away.</summary>
    [Fact]
    public async Task RemoveAddress_TheOnlyAddressTheContactHolds_RefusesAndNamesTheCommandThatErasesInstead()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test"]));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "remove-address",
            "--id",
            Identity,
            "--address",
            "anna@example.test",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Put));
        Assert.Contains(this.console.Errors, line => line.Contains("contact delete", StringComparison.Ordinal));
    }

    /// <summary>
    /// Removing the address a contact uses by default leaves the record without one, and choosing the next in the list
    /// would decide on the operator's behalf which address a message to that person goes to.
    /// </summary>
    [Fact]
    public async Task RemoveAddress_ThePreferredAddressWithNoReplacementNamed_RefusesWithoutWriting()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test", "a.kowalska@work.example"]));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "remove-address",
            "--id",
            Identity,
            "--address",
            "anna@example.test",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Put));
        Assert.Contains(this.console.Errors, line => line.Contains("--preferred", StringComparison.Ordinal));
    }

    /// <summary>The address the operator named is gone from the record the command sends, and the rest stay.</summary>
    [Fact]
    public async Task RemoveAddress_AnAddressTheContactDoesNotPrefer_SendsTheRecordWithoutIt()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(addresses: ["anna@example.test", "a.kowalska@work.example"]),
            write: FakeContactDeployment.Written());

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "remove-address",
            "--id",
            Identity,
            "--address",
            "a.kowalska@work.example",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        Assert.Equal(["anna@example.test"], AddressesOf(SentRecord(deployment)));
    }

    /// <summary>Promoting is a write of its own, sent to a path of its own rather than carried as a field.</summary>
    /// <remarks>
    /// It is also the one write the deployment answers without a record, because the command stated none and the grant
    /// that writes the book does not admit reading it. So what the command reports is the identity it was given, and an
    /// operator who wants the record reads it with <c>contact show</c>.
    /// </remarks>
    [Fact]
    public async Task Promote_ACollectedContact_AsksThePromotionPathAndReportsItWithoutTheRecord()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(write: FakeContactDeployment.Promoted());

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "promote", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            deployment.RecordedRequests,
            request => request.RequestUri?.AbsolutePath.EndsWith("/promotion", StringComparison.Ordinal) == true);
        Assert.Contains(this.console.Lines, line => line.Contains($"Took on contact {Identity}", StringComparison.Ordinal));
        Assert.DoesNotContain(this.console.Lines, line => line.Contains("Anna Kowalska", StringComparison.Ordinal));
    }

    /// <summary>Erasing cannot be undone, so the record is shown and the question asked before anything is removed.</summary>
    [Fact]
    public async Task Delete_WithoutTheFlag_ShowsTheRecordAndAsksBeforeErasing()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(),
            erasure: FakeContactDeployment.Erasure(wasHeld: true, addressesErased: 2));
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(this.console.Questions);
        Assert.Contains(this.console.Lines, line => line.Contains("Anna Kowalska", StringComparison.Ordinal));
        Assert.Contains(this.console.Lines, line => line.Contains("2 addresses", StringComparison.Ordinal));
    }

    /// <summary>The question is about a person, and what it names is the act rather than who it is about.</summary>
    [Fact]
    public async Task Delete_WithoutTheFlag_AsksAQuestionNamingNothingAboutThePerson()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(),
            erasure: FakeContactDeployment.Erasure(wasHeld: true, addressesErased: 1));
        this.console.AnswerToGive = true;

        // Act
        await this.RunAsync(deployment, "contact", "delete", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.DoesNotContain("Anna Kowalska", Assert.Single(this.console.Questions), StringComparison.Ordinal);
        Assert.DoesNotContain("anna@example.test", Assert.Single(this.console.Questions), StringComparison.Ordinal);
    }

    /// <summary>A question answered no erases nothing, and says so where a scripted run captures failures.</summary>
    [Fact]
    public async Task Delete_TheQuestionDeclined_ErasesNothing()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(lookup: FakeContactDeployment.Lookup());
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Delete));
        Assert.Contains("Nothing was erased.", this.console.Errors);
    }

    /// <summary>
    /// Nobody at the terminal is told to state the agreement in the command, because reading it out of whatever was
    /// piped in would turn a stray line into an agreement to erase somebody.
    /// </summary>
    [Fact]
    public async Task Delete_WithNobodyAtTheTerminalAndNoFlag_RefusesRatherThanAssuming()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(lookup: FakeContactDeployment.Lookup());
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Delete));
        Assert.Contains(this.console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>The flag is what a scripted erasure states its agreement with, and nothing is asked.</summary>
    [Fact]
    public async Task Delete_WithTheFlag_ErasesWithoutAsking()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            lookup: FakeContactDeployment.Lookup(),
            erasure: FakeContactDeployment.Erasure(wasHeld: true, addressesErased: 1));
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "contact",
            "delete",
            "--id",
            Identity,
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.console.Questions);
        Assert.Equal(1, deployment.ContactRequestCount(HttpMethod.Delete));
    }

    /// <summary>Erasing somebody the book does not hold is the state the operator asked for, not a failure.</summary>
    [Fact]
    public async Task Delete_AContactTheBookDoesNotHold_SucceedsWithoutAskingOrErasing()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.console.Questions);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Delete));
    }

    /// <summary>The way out for an owner who changed their mind, so what it removed is stated in both figures.</summary>
    [Fact]
    public async Task DeleteCollected_WithTheFlag_ErasesWhatTheDeploymentCollectedWithoutAsking()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            erasure: FakeContactDeployment.CollectedErasure(contactsErased: 12, addressesErased: 17));
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete-collected", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.console.Questions);
        Assert.Equal(1, deployment.ContactRequestCount(HttpMethod.Delete));
        Assert.Contains(this.console.Lines, line => line.Contains("12 contacts", StringComparison.Ordinal));
        Assert.Contains(this.console.Lines, line => line.Contains("17 addresses", StringComparison.Ordinal));
    }

    /// <summary>It cannot be undone, so it asks first, and what it asks names nobody it is about to erase.</summary>
    [Fact]
    public async Task DeleteCollected_WithoutTheFlag_AsksBeforeErasingAndNamesNobody()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            erasure: FakeContactDeployment.CollectedErasure(contactsErased: 1, addressesErased: 1));
        this.console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete-collected", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.DoesNotContain("@", Assert.Single(this.console.Questions), StringComparison.Ordinal);
        Assert.Contains(this.console.Lines, line => line.Contains("1 contact ", StringComparison.Ordinal));
    }

    /// <summary>A question answered no erases nothing, and says so where a scripted run captures failures.</summary>
    [Fact]
    public async Task DeleteCollected_TheQuestionDeclined_ErasesNothing()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding();
        this.console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete-collected", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Delete));
        Assert.Contains("Nothing was erased.", this.console.Errors);
    }

    /// <summary>Reading agreement out of whatever was piped in would turn a stray line into an agreement to erase people.</summary>
    [Fact]
    public async Task DeleteCollected_WithNobodyAtTheTerminalAndNoFlag_RefusesRatherThanAssuming()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding();
        this.console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete-collected", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Delete));
        Assert.Contains(this.console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>A deployment that collected nobody is the state the operator asked for rather than a failure.</summary>
    [Fact]
    public async Task DeleteCollected_ADeploymentThatCollectedNobody_SaysSoAndSucceeds()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(
            erasure: FakeContactDeployment.CollectedErasure(contactsErased: 0, addressesErased: 0));

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "delete-collected", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.console.Lines, line => line.Contains("collected nobody", StringComparison.Ordinal));
    }

    /// <summary>
    /// The export is one document on standard output, so redirecting the command produces the file an owner hands to
    /// the person who asked, with nothing of the command's own mixed into it.
    /// </summary>
    [Fact]
    public async Task Export_AContactTheBookHolds_WritesOneParsableDocumentAndNothingElse()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(export: FakeContactDeployment.Export());

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "export", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var document = Assert.Single(this.console.Lines);
        Assert.Contains("Anna Kowalska", document, StringComparison.Ordinal);
        Assert.Contains("producedAt", document, StringComparison.Ordinal);
    }

    /// <summary>A contact the book does not hold has nothing to export, and the failure carries only the identifier.</summary>
    [Fact]
    public async Task Export_AContactTheBookDoesNotHold_FailsWithoutWritingADocument()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", "export", "--id", Identity, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(this.console.Lines);
        Assert.Contains(this.console.Errors, line => line.Contains(Identity, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every command that acts on one contact needs to be told which, because guessing would act on a person.</summary>
    [Theory]
    [InlineData("update")]
    [InlineData("add-address")]
    [InlineData("remove-address")]
    [InlineData("promote")]
    [InlineData("delete")]
    [InlineData("export")]
    public async Task ContactCommands_WithNoIdentityNamed_RefuseWithoutReachingTheDeployment(string command)
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding(lookup: FakeContactDeployment.Lookup());

        // Act
        var exitCode = await this.RunAsync(deployment, "contact", command, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ContactRequestCount(HttpMethod.Get));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    /// <summary>Reads the record a write carried, as the deployment would.</summary>
    /// <remarks>
    /// Parsed rather than matched as text, because the command writes the body indented: an assertion over the raw
    /// string would be about the serializer's layout rather than about what the record said.
    /// </remarks>
    private static JsonElement SentRecord(FakeHttpMessageHandler deployment)
    {
        using var document = JsonDocument.Parse(deployment.LastWriteRequest() ?? "{}");

        return document.RootElement.Clone();
    }

    /// <summary>Reads the addresses a written record carried, in the order it stated them.</summary>
    private static IReadOnlyList<string?> AddressesOf(JsonElement record) =>
        [.. record.GetProperty("addresses").EnumerateArray().Select(address => address.GetString())];

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args)
    {
        var store = new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");

        var context = new CliContext(
            this.console,
            store,
            (endpoint, trust) => FakeDeploymentTransport.Over(deployment, endpoint, trust),
            FakeMailboxRedirect.Silent(),
            _ => false,
            this.clock);

        return CliRunner.RunAsync(context, args);
    }
}
