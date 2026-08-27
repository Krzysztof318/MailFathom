// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Builds a plan holding one block of every catalogued type, for the tests that assert about the whole contract.</summary>
/// <remarks>
/// One example rather than one per test, because what most of these tests assert is a property of the catalogue rather
/// than of a block: that every type serializes, that every discriminator agrees with the enumeration, that every
/// reference resolves. A plan missing one block type would pass all three while proving nothing about that type.
/// </remarks>
internal static class PresentationPlanExample
{
    /// <summary>A timestamp every part of the example is dated from, so nothing here reads a clock.</summary>
    internal static readonly DateTimeOffset ObservedAt = new(2026, 3, 2, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The citation every block in the example rests on.</summary>
    internal static PresentationCitationId FirstCitation { get; } = PresentationCitationId.Create("c1");

    /// <summary>A second citation, so a conflict and a two-source block can be built.</summary>
    internal static PresentationCitationId SecondCitation { get; } = PresentationCitationId.Create("c2");

    /// <summary>A third citation, resolving to an attachment, which is what a gallery entry presents.</summary>
    internal static PresentationCitationId AttachmentCitation { get; } = PresentationCitationId.Create("c3");

    /// <summary>Builds a plan holding one block of every catalogued type.</summary>
    /// <returns>The plan.</returns>
    internal static PresentationPlan Compose() =>
        PresentationPlan.Compose(EveryBlock(), Citations(), [PresentationLimitation.RetrievalTruncated]);

    /// <summary>Builds one block of every catalogued type, in the catalogue's own order.</summary>
    /// <returns>The blocks.</returns>
    internal static IReadOnlyList<PresentationBlock> EveryBlock() =>
    [
        Answer(),
        EvidenceList(),
        Timeline(),
        FactTable(),
        People(),
        ThreadState(),
        AttachmentGallery(),
        Draft(),
        SuggestedAction(),
    ];

    /// <summary>Builds the citations the example's blocks rest on.</summary>
    /// <returns>The citations.</returns>
    internal static IReadOnlyList<PresentationCitation> Citations() =>
    [
        new(
            FirstCitation,
            new EmailCitationTarget(StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"))),
            Text("Revised figures, 2 March")),
        new(
            SecondCitation,
            new FragmentCitationTarget(
                StoredEmailId.Create(new Guid("22222222-2222-2222-2222-222222222222")),
                EmailChunkId.Create(new Guid("33333333-3333-3333-3333-333333333333"))),
            Text("Re: Revised figures, paragraph two")),
        new(
            AttachmentCitation,
            new AttachmentCitationTarget(
                StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111")),
                attachmentPosition: 0),
            Text("renewal.pdf")),
    ];

    /// <summary>Builds evidence resting on the example's first citation.</summary>
    /// <returns>The evidence.</returns>
    internal static PresentationEvidence Supported() =>
        new(PresentationSupport.Supported, [FirstCitation], PresentationFreshness.CurrentAt(ObservedAt));

    /// <summary>Wraps text without repeating the factory's name at every call site.</summary>
    /// <param name="text">The text to wrap.</param>
    /// <returns>The wrapped text.</returns>
    internal static PresentationText Text(string text) => PresentationText.Create(text);

    private static AnswerBlock Answer() =>
        new(Supported(), Text("They accepted the revised figure."), PresentationConfidence.High);

    private static EvidenceListBlock EvidenceList() =>
        new(
            Supported(),
            [new EvidenceEntry(FirstCitation, Text("we accept the revised figure"), 0.92d, PresentationFreshness.CurrentAt(ObservedAt))]);

    private static TimelineBlock Timeline() =>
        new(Supported(), [new TimelineEntry(ObservedAt, Text("Figure revised"), Text("Renewal"), [FirstCitation])]);

    private static FactTableBlock FactTable() =>
        new(
            Supported(),
            [FactTableColumn.Party, FactTableColumn.Amount],
            [
                new FactTableRow([
                    new FactTableCell(Text("Northwind"), [FirstCitation]),
                    new FactTableCell(Text("£40,000"), [FirstCitation]),
                ]),
                new FactTableRow([
                    new FactTableCell(Text("Contoso"), [FirstCitation]),
                    new FactTableCell(value: null, []),
                ]),
            ]);

    private static PeopleBlock People()
    {
        EmailAddress.TryCreate(displayName: null, "ada@northwind.example", out var address);

        return new PeopleBlock(
            Supported(),
            [new PersonEntry(Text("Ada Bell"), address, Text("Signs off the renewal"), ObservedAt, [FirstCitation])]);
    }

    private static ThreadStateBlock ThreadState() =>
        new(
            Supported(),
            [new ThreadParticipant(Text("Ada Bell"), address: null)],
            [new ThreadStatement(Text("The figure is agreed."), [FirstCitation])],
            [],
            [new ThreadCommitment(Text("Send the revised schedule."), owedBy: null, ObservedAt, [FirstCitation])]);

    private static AttachmentGalleryBlock AttachmentGallery() =>
        new(
            Supported(),
            [new AttachmentEntry(AttachmentCitation, Text("renewal.pdf"), Text("application/pdf"), 128_000L, AttachmentAvailability.Stored)]);

    private static DraftBlock Draft()
    {
        EmailAddress.TryCreate(displayName: null, "ada@northwind.example", out var recipient);

        return new DraftBlock(
            Supported(),
            [recipient],
            Text("Re: Revised figures"),
            Text("Confirming that we accept the revised figure."),
            DraftDisposition.Composed);
    }

    private static SuggestedActionBlock SuggestedAction() =>
        new(
            Supported(),
            SuggestedActionKind.ReplyToThread,
            Text("The thread is waiting on a confirmation from this side."),
            SuggestedActionImpact.SendsMail,
            requiresConfirmation: true);
}
