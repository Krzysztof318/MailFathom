// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the conversations, the identifier bindings, and the mail one assembly walks, in memory.</summary>
/// <remarks>
/// A hand-written state fake rather than a substitute, because what these tests are about is a relation across several
/// rows rather than a sequence of calls: a reply hangs from a message stored earlier, a third message proves two
/// conversations were always one, and running the whole thing twice must change nothing. Only state can say that.
/// </remarks>
internal sealed class FakeEmailThreadStore
{
    private readonly Dictionary<(MailAccountIdentity Account, string Identifier), EmailThreadId> bindings = [];
    private readonly Dictionary<EmailThreadId, ThreadState> threads = [];
    private readonly Dictionary<StoredEmailId, MailState> mail = [];
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the fake over the clock its conversations are stamped from.</summary>
    /// <param name="timeProvider">The clock a started conversation records, which is what decides a merge.</param>
    public FakeEmailThreadStore(TimeProvider timeProvider) => this.timeProvider = timeProvider;

    /// <summary>Gets how many conversations have been started, merged ones included.</summary>
    public int ThreadCount => this.threads.Count;

    /// <summary>Gets the port as the assembly consumes it.</summary>
    public IEmailThreadStore Store => new Adapter(this);

    /// <summary>Records one stored email, as the write path would have before it assembled anything.</summary>
    /// <param name="account">The account the mail belongs to, named as the owner and the identifier together.</param>
    /// <param name="storedEmailId">The identity to store it under.</param>
    /// <param name="internetMessageId">The message's own identifier, or <see langword="null" /> when it carried none.</param>
    /// <param name="answeredInternetMessageId">The identifier it answers, or <see langword="null" /> when it answers none.</param>
    /// <param name="referencedInternetMessageIds">The ancestors it refers to, in header order.</param>
    /// <returns>The email as assembly sees it.</returns>
    public ThreadedEmail Add(
        MailAccountIdentity account,
        StoredEmailId storedEmailId,
        string? internetMessageId,
        string? answeredInternetMessageId = null,
        params string[] referencedInternetMessageIds)
    {
        var email = new ThreadedEmail
        {
            StoredEmailId = storedEmailId,
            InternetMessageId = internetMessageId,
            AnsweredInternetMessageId = answeredInternetMessageId,
            ReferencedInternetMessageIds = referencedInternetMessageIds,
        };

        this.mail[storedEmailId] = new MailState(account, email, ThreadId: null);

        return email;
    }

    /// <summary>Reads the email as assembly would see it now, with whatever placement it has been given.</summary>
    public ThreadedEmail Read(StoredEmailId storedEmailId) => this.mail[storedEmailId].Email;

    /// <summary>Reads the conversation one email belongs to, or nothing when it belongs to none.</summary>
    public EmailThreadId? ThreadOf(StoredEmailId storedEmailId) => this.mail[storedEmailId].ThreadId;

    /// <summary>Reads the message one email answers, or nothing when it answers none held here.</summary>
    public StoredEmailId? AnsweredBy(StoredEmailId storedEmailId) => this.mail[storedEmailId].Email.AnsweredStoredEmailId;

    /// <summary>Reads the conversation one merged conversation was folded into, or nothing while it is its own.</summary>
    public EmailThreadId? MergedInto(EmailThreadId threadId) => this.threads[threadId].MergedInto;

    private sealed record ThreadState(
        MailAccountIdentity Account,
        DateTimeOffset AssembledAt,
        EmailThreadId? MergedInto);

    private sealed record MailState(MailAccountIdentity Account, ThreadedEmail Email, EmailThreadId? ThreadId);

    /// <summary>The port surface, kept apart from the state so a test reads the state directly.</summary>
    private sealed class Adapter(FakeEmailThreadStore state) : IEmailThreadStore
    {
        public Task<IReadOnlyList<EmailThreadBinding>> FindBindingsAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            IReadOnlyList<string> identifiers,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmailThreadBinding>>(
            [
                .. identifiers
                    .Where(identifier => state.bindings.ContainsKey((account, identifier)))
                    .Select(identifier => new EmailThreadBinding(
                        identifier,
                        state.bindings[(account, identifier)],
                        state.threads[state.bindings[(account, identifier)]].AssembledAt)),
            ]);

        public Task<EmailThreadId> StartThreadAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            CancellationToken cancellationToken)
        {
            var assembledAt = state.timeProvider.GetUtcNow();
            var threadId = EmailThreadId.Create(Guid.CreateVersion7(assembledAt));

            state.threads[threadId] = new ThreadState(account, assembledAt, MergedInto: null);

            return Task.FromResult(threadId);
        }

        public Task BindIdentifiersAsync(
            IPersistenceSession session,
            MailAccountIdentity account,
            IReadOnlyList<string> identifiers,
            EmailThreadId threadId,
            CancellationToken cancellationToken)
        {
            foreach (var identifier in identifiers)
            {
                state.bindings[(account, identifier)] = threadId;
            }

            return Task.CompletedTask;
        }

        public Task MergeThreadsAsync(
            IPersistenceSession session,
            IReadOnlyList<EmailThreadId> mergedThreadIds,
            EmailThreadId survivingThreadId,
            CancellationToken cancellationToken)
        {
            foreach (var key in state.bindings
                         .Where(binding => mergedThreadIds.Contains(binding.Value))
                         .Select(binding => binding.Key)
                         .ToArray())
            {
                state.bindings[key] = survivingThreadId;
            }

            foreach (var storedEmailId in state.mail
                         .Where(held => held.Value.ThreadId is { } threadId && mergedThreadIds.Contains(threadId))
                         .Select(held => held.Key)
                         .ToArray())
            {
                state.mail[storedEmailId] = state.mail[storedEmailId] with { ThreadId = survivingThreadId };
            }

            foreach (var threadId in mergedThreadIds)
            {
                state.threads[threadId] = state.threads[threadId] with { MergedInto = survivingThreadId };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ThreadedEmail>> FindByMessageIdentifierAsync(
            IPersistenceSession session,
            EmailThreadId threadId,
            string internetMessageId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ThreadedEmail>>(
            [
                .. state.mail.Values
                    .Where(held => held.ThreadId == threadId
                        && string.Equals(held.Email.InternetMessageId, internetMessageId, StringComparison.Ordinal))
                    .Select(held => held.Email)
                    .OrderBy(email => email.StoredEmailId.Value),
            ]);

        public Task<IReadOnlyList<ThreadedEmail>> FindUnplacedAnswersAsync(
            IPersistenceSession session,
            EmailThreadId threadId,
            string answeredIdentifier,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ThreadedEmail>>(
            [
                .. state.mail.Values
                    .Where(held => held.ThreadId == threadId
                        && held.Email.AnsweredStoredEmailId is null
                        && string.Equals(
                            held.Email.AnsweredInternetMessageId,
                            answeredIdentifier,
                            StringComparison.Ordinal))
                    .Select(held => held.Email)
                    .OrderBy(email => email.StoredEmailId.Value),
            ]);

        public Task<ThreadedEmail?> FindThreadedEmailAsync(
            IPersistenceSession session,
            StoredEmailId storedEmailId,
            CancellationToken cancellationToken) =>
            Task.FromResult(state.mail.TryGetValue(storedEmailId, out var held) ? held.Email : null);

        public Task PlaceAsync(
            IPersistenceSession session,
            StoredEmailId storedEmailId,
            EmailThreadId threadId,
            StoredEmailId? answeredStoredEmailId,
            CancellationToken cancellationToken)
        {
            var held = state.mail[storedEmailId];

            state.mail[storedEmailId] = held with
            {
                Email = held.Email with { AnsweredStoredEmailId = answeredStoredEmailId },
                ThreadId = threadId,
            };

            return Task.CompletedTask;
        }

        public Task LinkAnswerAsync(
            IPersistenceSession session,
            StoredEmailId answerStoredEmailId,
            StoredEmailId answeredStoredEmailId,
            CancellationToken cancellationToken)
        {
            var held = state.mail[answerStoredEmailId];

            state.mail[answerStoredEmailId] = held with
            {
                Email = held.Email with { AnsweredStoredEmailId = answeredStoredEmailId },
            };

            return Task.CompletedTask;
        }
    }
}
