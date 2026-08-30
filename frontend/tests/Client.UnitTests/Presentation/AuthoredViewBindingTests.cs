// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Settings;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.UnitTests.Strings;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>
/// Holds the authored views to the bindings, templates, and visual states a running head depends on.
/// </summary>
/// <remarks>
/// A <c>ListView</c> bound to an <c>IListFeed</c> sits empty on the browser head after the deployment has already
/// answered, which is the defect these assertions exist for. The views are read as files because this host has no
/// visual tree, and a bind to a member that does not exist on the mapped model is the other way the same emptiness
/// arrives.
/// </remarks>
public sealed class AuthoredViewBindingTests
{
    /// <summary>The collection <c>FeedView</c>s whose lists have to bind <c>Data</c> inside the value template.</summary>
    public static TheoryData<string, string> CollectionFeedViews =>
        new()
        {
            { "WorkspacePage.xaml", "Mailboxes" },
            { "MailPage.xaml", "Messages" },
            { "MailThreadView.xaml", "ThreadMessages" },
            { "MailSearchView.xaml", "SearchResults" },
        };

    /// <summary>The record <c>FeedView</c>s that author value, progress, and error templates.</summary>
    public static TheoryData<string, string> RecordFeedViews =>
        new()
        {
            { "WorkspacePage.xaml", "Session" },
            { "SettingsPage.xaml", "Session" },
            { "SettingsPage.xaml", "Build" },
            { "MailMessagePage.xaml", "OpenedThreadMessage" },
        };



    /// <summary>Every <c>FeedView</c> <c>Source</c> names a public member on the model the view is mapped to.</summary>
    [Fact]
    public void FeedView_SourceBinding_NamesAMemberOnTheMappedModel()
    {
        // Arrange
        var feedViews = MappedFeedViews();

        // Act, Assert
        Assert.NotEmpty(feedViews);
        Assert.All(
            feedViews,
            feedView =>
            {
                var model = AuthoredXaml.MappedModel(feedView.ViewFile);
                Assert.NotNull(model);
                Assert.False(string.IsNullOrEmpty(feedView.Source), feedView.ViewFile);
                Assert.True(
                    AuthoredXaml.NamesMember(model, feedView.Source),
                    $"{feedView.ViewFile} Source={feedView.Source}");
            });
    }

    /// <summary>
    /// A list drawn inside a <c>FeedView</c> binds <c>Data</c>, never the feed itself — that is the empty-mailbox
    /// shape on the browser head.
    /// </summary>
    [Fact]
    public void CollectionFeedView_ListItemsSource_BindsDataInsideTheValueTemplate()
    {
        // Arrange
        var lists = MappedFeedViews()
            .SelectMany(feedView => feedView.ListItemsSources().Select(source => (feedView.ViewFile, feedView.Source, source)))
            .ToArray();

        // Act, Assert
        Assert.NotEmpty(lists);
        Assert.All(lists, list => Assert.Equal("Data", list.source));
    }

    /// <summary>The four collection feeds still author value, progress, none, and error templates.</summary>
    [Theory]
    [MemberData(nameof(CollectionFeedViews))]
    public void CollectionFeedView_HasValueProgressNoneAndErrorTemplates(string viewFile, string source)
    {
        // Arrange
        var feedView = FeedView(viewFile, source);

        // Act, Assert
        Assert.True(feedView.DrawsAList, $"{viewFile} {source}");
        Assert.True(feedView.HasValueTemplate, $"{viewFile} {source} ValueTemplate");
        Assert.True(feedView.HasProgressTemplate, $"{viewFile} {source} ProgressTemplate");
        Assert.True(feedView.HasNoneTemplate, $"{viewFile} {source} NoneTemplate");
        Assert.True(feedView.HasErrorTemplate, $"{viewFile} {source} ErrorTemplate");
    }

    /// <summary>The session, build, and opened-message feeds still author value, progress, and error templates.</summary>
    [Theory]
    [MemberData(nameof(RecordFeedViews))]
    public void RecordFeedView_HasValueProgressAndErrorTemplates(string viewFile, string source)
    {
        // Arrange
        var feedView = FeedView(viewFile, source);

        // Act, Assert
        Assert.False(feedView.DrawsAList, $"{viewFile} {source}");
        Assert.True(feedView.HasValueTemplate, $"{viewFile} {source} ValueTemplate");
        Assert.True(feedView.HasProgressTemplate, $"{viewFile} {source} ProgressTemplate");
        Assert.True(feedView.HasErrorTemplate, $"{viewFile} {source} ErrorTemplate");
    }

    /// <summary>Every Toolkit <c>LoadingView</c> <c>Source</c> names a generated command on the mapped model.</summary>
    [Fact]
    public void LoadingView_Source_NamesACommandOnTheMappedModel()
    {
        // Arrange
        (string ViewFile, string Source)[] expected =
        [
            ("MailPage.xaml", "ShowMore"),
            ("MailSearchView.xaml", "ShowMoreSearchResults"),
            ("MailThreadView.xaml", "ShowMoreThreadMessages"),
        ];

        var sources = MappedViews()
            .SelectMany(view => view.LoadingViewSources().Select(source => (ViewFile: view.FileName, Source: source)))
            .ToArray();

        // Act, Assert
        Assert.Equal(expected.ToHashSet(), sources.ToHashSet());
        Assert.All(
            sources,
            pair =>
            {
                var model = AuthoredXaml.MappedModel(pair.ViewFile);
                Assert.NotNull(model);
                Assert.True(AuthoredXaml.NamesMember(model, pair.Source), $"{pair.ViewFile} {pair.Source}");
            });
    }

    /// <summary>Every command binding that is not a navigation request names a public member on the mapped model.</summary>
    [Fact]
    public void CommandBinding_NamesAMemberOnTheMappedModel()
    {
        // Arrange
        var commands = MappedViews()
            .SelectMany(view => view.ModelCommands().Select(command => (view.FileName, command)))
            .ToArray();

        // Act, Assert
        Assert.NotEmpty(commands);
        Assert.All(
            commands,
            pair =>
            {
                var model = AuthoredXaml.MappedModel(pair.FileName);
                Assert.NotNull(model);
                Assert.True(
                    AuthoredXaml.NamesMember(model, pair.command.ModelPath),
                    $"{pair.FileName} Command={pair.command.Path}");
            });
    }

    /// <summary>Every two-way binding that writes the mapped model names a state on it.</summary>
    [Fact]
    public void TwoWayBinding_NamesAStateOnTheMappedModel()
    {
        // Arrange
        var bindings = MappedViews()
            .SelectMany(view => view.ModelTwoWayBindings().Select(binding => (view.FileName, binding)))
            .ToArray();

        // Act, Assert
        Assert.NotEmpty(bindings);
        Assert.All(
            bindings,
            pair =>
            {
                var model = AuthoredXaml.MappedModel(pair.FileName);
                Assert.NotNull(model);
                Assert.True(
                    AuthoredXaml.NamesMember(model, pair.binding.ModelPath),
                    $"{pair.FileName} TwoWay={pair.binding.Path}");
            });
    }

    /// <summary>The named layout breakpoints the frame and the mail space compose from remain authored.</summary>
    [Fact]
    public void VisualState_NamedLayoutBreakpoints_RemainAuthored()
    {
        // Arrange
        var workspace = AuthoredXaml.File("WorkspacePage.xaml").VisualStateNames();
        var columns = AuthoredXaml.File("WorkspaceColumns.xaml").VisualStateNames();
        var mail = AuthoredXaml.File("MailPage.xaml").VisualStateNames();

        // Act, Assert
        Assert.Contains("Narrow", workspace);
        Assert.Contains("Normal", workspace);
        Assert.Contains("Wide", workspace);
        Assert.Contains("SingleColumn", columns);
        Assert.Contains("TwoColumns", columns);
        Assert.Contains("WideMail", mail);
    }

    /// <summary>The connect screen still two-way binds the address, the submit command, and the refusal bar.</summary>
    [Fact]
    public void ConnectPage_TypedFields_RemainBound()
    {
        // Arrange
        var view = AuthoredXaml.File("ConnectPage.xaml");

        // Act, Assert
        Assert.Empty(view.FeedViews());
        Assert.Contains(view.ModelTwoWayBindings(), binding => binding.ModelPath == "Address");
        Assert.True(view.HasBindingPath("Connect"));
        Assert.True(view.HasBindingPath("CanAsk"));
        Assert.True(view.HasBindingPath("IsRefused"));
        Assert.True(view.HasBindingPath("Refusal"));
        Assert.True(view.HasBindingPath("IsAsking"));
    }

    /// <summary>The sign-in screen still two-way binds the credential, the submit command, and both bars.</summary>
    [Fact]
    public void SignInPage_TypedFields_RemainBound()
    {
        // Arrange
        var view = AuthoredXaml.File("SignInPage.xaml");

        // Act, Assert
        Assert.Empty(view.FeedViews());
        Assert.Contains(view.ModelTwoWayBindings(), binding => binding.ModelPath == "Username");
        Assert.Contains(view.ModelTwoWayBindings(), binding => binding.ModelPath == "Password");
        Assert.True(view.HasBindingPath("SignIn"));
        Assert.True(view.HasBindingPath("CanSignIn"));
        Assert.True(view.HasBindingPath("IsRefused"));
        Assert.True(view.HasBindingPath("Refusal"));
        Assert.True(view.HasBindingPath("SaysHowLongItLasts"));
        Assert.True(view.HasBindingPath("Keeping"));
    }

    /// <summary>The frame still names its rail, mailbox pane, and spaces, and still binds the offer flags they follow.</summary>
    [Fact]
    public void WorkspacePage_Frame_RemainsAuthored()
    {
        // Arrange
        var view = AuthoredXaml.File("WorkspacePage.xaml");
        var named = view.NamedElements();

        // Act, Assert
        Assert.Contains("Rail", named);
        Assert.Contains("MailboxPane", named);
        Assert.Contains("Spaces", named);
        Assert.True(view.HasBindingPath("OffersDiscover"));
        Assert.True(view.HasBindingPath("OffersMail"));
        Assert.True(view.HasBindingPath("OffersCases"));
        Assert.True(view.HasBindingPath("WithholdsMail"));
        Assert.True(AuthoredXaml.NamesMember(typeof(WorkspaceModel), "Session"));
        Assert.True(AuthoredXaml.NamesMember(typeof(WorkspaceModel), "Mailboxes"));
    }

    /// <summary>The mail space still binds the list, the empty-state bars, the withheld overlay, and ShowEarlier.</summary>
    [Fact]
    public void MailPage_Timeline_RemainsBound()
    {
        // Arrange
        var view = AuthoredXaml.File("MailPage.xaml");

        // Act, Assert
        Assert.True(view.HasBindingPath("ShowsTimeline"));
        Assert.True(view.HasBindingPath("HasMoreBefore"));
        Assert.True(view.HasBindingPath("ShowEarlier"));
        Assert.True(view.HasBindingPath("KeepsEverything"));
        Assert.True(view.HasBindingPath("KeepsLessThanEverything"));
        Assert.True(view.HasBindingPath("WithholdsMail"));
        Assert.Contains("WideMail", view.VisualStateNames());
    }

    /// <summary>The conversation still binds the participants and the command that loads more of it.</summary>
    [Fact]
    public void MailThreadView_Conversation_RemainsBound()
    {
        // Arrange
        var view = AuthoredXaml.File("MailThreadView.xaml");

        // Act, Assert
        Assert.True(view.HasBindingPath("Thread.Participants"));
        Assert.True(view.HasBindingPath("ShowMoreThreadMessages"));
        Assert.True(AuthoredXaml.NamesMember(typeof(MailModel), "Thread.Participants"));
    }

    /// <summary>Search still binds the ranked list, the recents, and the command that loads more of it.</summary>
    [Fact]
    public void MailSearchView_Results_RemainBound()
    {
        // Arrange
        var view = AuthoredXaml.File("MailSearchView.xaml");

        // Act, Assert
        Assert.True(view.HasBindingPath("SearchResults"));
        Assert.True(view.HasBindingPath("RecentSearches"));
        Assert.True(view.HasBindingPath("ShowMoreSearchResults"));
    }

    /// <summary>The language and theme pickers still bind the lists whose selection operator carries the choice.</summary>
    [Fact]
    public void SettingsPage_Pickers_RemainBound()
    {
        // Arrange
        var view = AuthoredXaml.File("SettingsPage.xaml");

        // Act, Assert
        Assert.True(view.HasBindingPath("Languages"));
        Assert.True(view.HasBindingPath("ThemeOptions"));
        Assert.True(AuthoredXaml.NamesMember(typeof(SettingsModel), "ChosenLanguage"));
        Assert.True(AuthoredXaml.NamesMember(typeof(SettingsModel), "ChosenTheme"));
    }

    /// <summary>The reading pane still <c>x:Bind</c>s the subject, sender infobars, headers, and attachments.</summary>
    [Fact]
    public void MailMessageView_Reading_RemainsBound()
    {
        // Arrange
        var view = AuthoredXaml.File("MailMessageView.xaml");

        // Act, Assert
        Assert.True(view.HasXBindPath("Reading.Subject"));
        Assert.True(view.HasXBindPath("Reading.ShowsTrustedSender"));
        Assert.True(view.HasXBindPath("Reading.ShowsSenderWarning"));
        Assert.True(view.HasXBindPath("Reading.Headers"));
        Assert.True(view.HasXBindPath("Reading.Attachments"));
    }

    /// <summary>Discover still stands over the withheld overlay and still authors both placeholders.</summary>
    [Fact]
    public void DiscoverPage_PlaceholderAndOverlay_RemainAuthored()
    {
        // Arrange
        var view = AuthoredXaml.File("DiscoverPage.xaml");

        // Act, Assert
        Assert.True(view.HasElement("SpacePlaceholder"));
        Assert.True(view.HasBindingPath("WithholdsDiscover"));
    }

    /// <summary>Cases still stands over the withheld overlay and still authors both placeholders.</summary>
    [Fact]
    public void CasesPage_PlaceholderAndOverlay_RemainAuthored()
    {
        // Arrange
        var view = AuthoredXaml.File("CasesPage.xaml");

        // Act, Assert
        Assert.True(view.HasElement("SpacePlaceholder"));
        Assert.True(view.HasBindingPath("WithholdsCases"));
    }

    private static IReadOnlyList<AuthoredXaml.AuthoredViewFile> MappedViews() =>
        [.. AuthoredXaml.Files().Where(view => AuthoredXaml.MappedModel(view.FileName) is not null)];

    private static IReadOnlyList<AuthoredXaml.AuthoredFeedView> MappedFeedViews() =>
        [.. MappedViews().SelectMany(view => view.FeedViews())];

    private static AuthoredXaml.AuthoredFeedView FeedView(string viewFile, string source)
    {
        var feedView = AuthoredXaml.File(viewFile).FeedViews()
            .SingleOrDefault(candidate => string.Equals(candidate.Source, source, StringComparison.Ordinal));

        Assert.NotNull(feedView);
        return feedView;
    }
}
