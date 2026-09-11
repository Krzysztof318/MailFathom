// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Descriptions;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers the one reading of the chat section that composition and a reload both judge a declaration by.</summary>
/// <remarks>
/// The section is reloadable, so a rule that held at startup and lapsed on reload would publish a declaration nothing
/// proved. These tests establish that the same reading reports the section's own bounds, both rules that span sections,
/// and the two settings composition acted on and a reload therefore cannot move.
/// </remarks>
public sealed class ChatDeclarationRulesTests
{
    [Fact]
    public void FindDeclarationErrors_AUsableDeclaration_ReportsNothing()
    {
        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(Declared(), null, new MailAnsweringOptions());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An absent section is a deployment that answers no questions, which is supported rather than refused.</summary>
    [Fact]
    public void FindDeclarationErrors_AnAbsentSection_ReportsNothing()
    {
        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(null, null, new MailAnsweringOptions());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A chat endpoint declared to carry no image is refused beside the one feature whose whole request is a picture.</summary>
    /// <remarks>
    /// Zero is a supported declaration on its own — a text-only model carries it — so the rule spans the two sections
    /// rather than sitting on either. Without it the describer answers every picture in the mailbox with
    /// <c>ImageTooLarge</c>, which names the pictures rather than the declaration.
    /// </remarks>
    [Fact]
    public void FindDeclarationErrors_ImageDescriptionOnBesideAZeroImageBudget_NamesTheKeyAnOperatorEdits()
    {
        // Arrange
        var candidate = Declared();
        candidate.MaxRequestImageOctets = 0;
        var embeddings = new EmbeddingOptions { ImageDescription = { Enabled = true } };

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(candidate, embeddings, new MailAnsweringOptions());

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Chat:MaxRequestImageOctets", StringComparison.Ordinal));
    }

    /// <summary>A description sends two turns whatever the picture is, so an endpoint admitting one cannot carry it.</summary>
    [Fact]
    public void FindDeclarationErrors_ImageDescriptionOnBesideAOneTurnEndpoint_NamesTheKeyAnOperatorEdits()
    {
        // Arrange
        var candidate = Declared();
        candidate.MaxMessagesPerRequest = 1;
        var embeddings = new EmbeddingOptions { ImageDescription = { Enabled = true } };

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(candidate, embeddings, new MailAnsweringOptions());

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Chat:MaxMessagesPerRequest", StringComparison.Ordinal));
    }

    /// <summary>The instruction is fixed, so an endpoint admitting fewer characters than it occupies refuses every description.</summary>
    [Fact]
    public void FindDeclarationErrors_ImageDescriptionOnBesideAnEndpointTooNarrowForTheInstruction_NamesTheKeyAnOperatorEdits()
    {
        // Arrange
        var candidate = Declared();
        candidate.MaxRequestCharacters = ImageDescriptionInstructions.SmallestRequestCharacters - 1;
        var embeddings = new EmbeddingOptions { ImageDescription = { Enabled = true } };

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(candidate, embeddings, new MailAnsweringOptions());

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Chat:MaxRequestCharacters", StringComparison.Ordinal));
    }

    /// <summary>Each of the three bounds is a legal declaration on its own — a text-only endpoint carries all three — so nothing refuses one where description is off.</summary>
    [Fact]
    public void FindDeclarationErrors_TheImageDescriptionBoundsWithThatFeatureOff_ReportsNothing()
    {
        // Arrange
        var candidate = Declared();
        candidate.MaxRequestImageOctets = 0;
        candidate.MaxMessagesPerRequest = 1;
        candidate.MaxRequestCharacters = 1;
        var embeddings = new EmbeddingOptions { ImageDescription = { Enabled = false } };

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(candidate, embeddings, new MailAnsweringOptions());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The attribute bounds are the half no reload would otherwise report, because the options framework drops the candidate before anything can log it.</summary>
    [Fact]
    public void FindDeclarationErrors_ABoundOutsideItsRange_NamesTheKeyAnOperatorEdits()
    {
        // Arrange
        var candidate = Declared();
        candidate.MaxOutputTokens = 0;

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(candidate, null, new MailAnsweringOptions());

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Chat:MaxOutputTokens — ", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_ADeclarationWithNoModel_ReportsTheSectionsOwnRule()
    {
        // Arrange
        var candidate = Declared();
        candidate.Model = string.Empty;

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(candidate, null, new MailAnsweringOptions());

        // Assert
        Assert.Contains(errors, error => error.Contains("declares no Model", StringComparison.Ordinal));
    }

    /// <summary>An alias names one AI endpoint across the deployment, and neither options type can see the other section.</summary>
    [Fact]
    public void FindDeclarationErrors_AnAliasAnEmbeddingEndpointAlreadyDeclares_ReportsTheCollision()
    {
        // Arrange
        var embeddings = new EmbeddingOptions();
        embeddings.Endpoints.Add(new EmbeddingEndpointOptions
        {
            Alias = "answering",
            Model = "an-embedding-model",
            Dimension = 4,
        });

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(Declared(), embeddings, new MailAnsweringOptions());

        // Assert
        Assert.Contains(errors, error => error.Contains("both declare the alias 'answering'", StringComparison.Ordinal));
    }

    /// <summary>The second rule spanning two sections, and the reason a reloaded filter count is judged rather than adopted.</summary>
    [Fact]
    public void FindDeclarationErrors_AFilterJudgingMoreThanOneLookupHandsOver_ReportsTheDisagreement()
    {
        // Arrange
        var candidate = Declared();
        candidate.RelevanceFilter.Enabled = true;
        candidate.RelevanceFilter.MaxCandidates = 9;

        // Act
        var errors = ChatDeclarationRules.FindDeclarationErrors(
            candidate,
            null,
            new MailAnsweringOptions { MaxPassagesPerRetrieval = 8 });

        // Assert
        Assert.Contains(errors, error => error.Contains("Chat:RelevanceFilter:MaxCandidates", StringComparison.Ordinal));
    }

    [Fact]
    public void FindDeclarationErrors_WithoutTheAnsweringDeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ChatDeclarationRules.FindDeclarationErrors(Declared(), null, null!));
    }

    [Fact]
    public void FindChangesNeedingRestart_ADeclarationThatMovesNothingCompositionRead_ReportsNothing()
    {
        // Arrange
        var candidate = Declared();
        candidate.Model = "a-corrected-model";
        candidate.Temperature = 0.4f;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, Declared());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Whether the endpoint exists decides which services were registered, and the container is built once.</summary>
    [Fact]
    public void FindChangesNeedingRestart_ARemovedEndpoint_RefusesRatherThanTakingTheCapabilityOffline()
    {
        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(new ChatModelOptions(), Declared());

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:Alias — ", StringComparison.Ordinal));
    }

    /// <summary>Adopting this silently would report the setting as taken while the tool went on answering nothing.</summary>
    [Fact]
    public void FindChangesNeedingRestart_ADeclaredEndpointWhereNoneWasComposed_RefusesRatherThanBeingIgnored()
    {
        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(Declared(), new ChatModelOptions());

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:Alias — ", StringComparison.Ordinal));
    }

    /// <summary>The filter's registration decorates the retrieval, so turning the pass on is a composition decision; its two numbers are not.</summary>
    [Fact]
    public void FindChangesNeedingRestart_ARelevanceFilterTurnedOn_RefusesTheSwitchAndNotTheNumbersBesideIt()
    {
        // Arrange
        var candidate = Declared();
        candidate.RelevanceFilter.Enabled = true;
        candidate.RelevanceFilter.MinimumRelevance = 70;

        var composed = Declared();
        composed.RelevanceFilter.MinimumRelevance = 50;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, composed);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:RelevanceFilter:Enabled — ", StringComparison.Ordinal));
        Assert.Single(errors);
    }

    /// <summary>
    /// Which enricher the arrival pipeline resolves is decided while the container is built, so a reload that flipped
    /// this would report the setting as taken while every arriving message went on being told the deployment has not
    /// turned enrichment on.
    /// </summary>
    [Fact]
    public void FindChangesNeedingRestart_EnrichmentTurnedOn_RefusesRatherThanBeingIgnored()
    {
        // Arrange
        var candidate = Declared();
        candidate.Enrichment.Enabled = true;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, Declared());

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:Enrichment:Enabled — ", StringComparison.Ordinal));
        Assert.Single(errors);
    }

    /// <summary>
    /// Which deriver the pass resolves is decided while the container is built, so a reload that flipped this would
    /// report the setting as taken while every conversation went on being told the deployment has not turned it on.
    /// </summary>
    [Fact]
    public void FindChangesNeedingRestart_TheConversationStateTurnedOn_RefusesRatherThanBeingIgnored()
    {
        // Arrange
        var candidate = Declared();
        candidate.ThreadState.Enabled = true;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, Declared());

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:ThreadState:Enabled — ", StringComparison.Ordinal));
        Assert.Single(errors);
    }

    /// <summary>
    /// Whether a reply is drafted is decided while the container is built, and the composer asks the deployment once.
    /// A reload that flipped it would report the setting as taken while every composer went on offering a draft this
    /// deployment had stopped writing, or withholding one it had started writing.
    /// </summary>
    [Fact]
    public void FindChangesNeedingRestart_ReplyDraftingTurnedOff_RefusesRatherThanBeingIgnored()
    {
        // Arrange
        var candidate = Declared();
        candidate.ReplyDrafting.Enabled = false;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, Declared());

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:ReplyDrafting:Enabled — ", StringComparison.Ordinal));
        Assert.Single(errors);
    }

    /// <summary>
    /// The second switch is captured into the registration beside the first, so a reload that flipped it would leave
    /// every draft written from the mail the composed deployment reads rather than from the mail the operator has just
    /// said it may read — which is a privacy decision going unapplied rather than a preference going unheard.
    /// </summary>
    [Fact]
    public void FindChangesNeedingRestart_TheDraftingStyleSourceTurnedOff_RefusesRatherThanBeingIgnored()
    {
        // Arrange
        var candidate = Declared();
        candidate.ReplyDrafting.StyleFromSentMail = false;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, Declared());

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Chat:ReplyDrafting:StyleFromSentMail — ", StringComparison.Ordinal));
        Assert.Single(errors);
    }

    /// <summary>
    /// Whether a sentence is read into filters is decided while the container is built, and the search screen asks the
    /// deployment once. A reload that flipped it would report the setting as taken while every field went on promising
    /// a description this deployment had stopped reading, or refusing one it had started reading.
    /// </summary>
    [Fact]
    public void FindChangesNeedingRestart_PhraseReadingTurnedOff_RefusesRatherThanBeingIgnored()
    {
        // Arrange
        var candidate = Declared();
        candidate.SearchPhrasing.Enabled = false;

        // Act
        var errors = ChatDeclarationRules.FindChangesNeedingRestart(candidate, Declared());

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:SearchPhrasing:Enabled — ", StringComparison.Ordinal));
        Assert.Single(errors);
    }

    [Fact]
    public void FindChangesNeedingRestart_WithoutADeclarationToCompare_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatDeclarationRules.FindChangesNeedingRestart(null!, Declared()));
        Assert.Throws<ArgumentNullException>(() => ChatDeclarationRules.FindChangesNeedingRestart(Declared(), null!));
    }

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = "env:CHAT_KEY" },
    };
}
