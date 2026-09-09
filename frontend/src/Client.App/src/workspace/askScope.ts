// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAccount } from '@mailfathom/client-backend';
import { accountInScope, everything, scopeKey, scopeOfAccount, type MailScope } from './mailScope';
import type { Workspace } from './useWorkspace';

// What a question is asked about, which is not the same thing as what the mail space is showing. Somebody reading one
// correspondence out of a folder means that correspondence when they ask, somebody who has ticked four rows means those
// four, and somebody who has highlighted a paragraph means the paragraph rather than the message around it — so the
// scope is named beside the field before the question is sent rather than discovered in the answer.
//
// It is one value derived from two, rather than a second scope kept beside the mail space's own: what is on the screen
// answers unless the person named something else in the field, and naming something else there changes what the next
// question reads without moving the list out from under them. Two scopes free to disagree would be a field that says
// one thing and asks another, which here is a disclosure rather than a display defect — what is in scope is what is
// read and sent.

/** A passage of one message somebody selected, which is a scope of its own rather than a mark on the message. */
export interface SelectedFragment {
    /** The message the words were selected in, which is what makes the passage distinguishable from the message. */
    readonly messageId: string;

    /** The words themselves, because what the field does with a fragment is quote it back before the question is sent. */
    readonly text: string;
}

/** What a question is asked about. */
export type AskScope =
    /** A mailbox, a folder, or every mailbox at once — the same scope the mail space is read under. */
    | { readonly kind: 'mail'; readonly scope: MailScope }

    /** The correspondence being read, whatever folder it was reached from. */
    | { readonly kind: 'thread'; readonly threadId: string }

    /** The messages picked out of the list, in the order the list draws them. */
    | { readonly kind: 'selection'; readonly messages: readonly string[] }

    /** The one message being read, whether on its own or inside a correspondence. */
    | { readonly kind: 'message'; readonly messageId: string }

    /** The passage of a message somebody selected, which is narrower than the message that carries it. */
    | { readonly kind: 'fragment'; readonly messageId: string; readonly text: string };

/** A question that was asked, and the scope it was asked under. */
export interface AskedQuestion {
    readonly question: string;
    readonly scope: AskScope;
}

/**
 * How many questions are offered back before the oldest is dropped.
 *
 * Read where a store is read back and written by the field that keeps them, so the bound one tab writes under and the
 * bound a stored list is held to are one number rather than two that drift.
 */
export const mostAskedQuestions = 8;

/**
 * The scope's identity as one string, which is what the field names a scope by and what the workspace keeps.
 *
 * A key rather than the scope itself, because what somebody chose in the field only means anything against what the
 * field is offering: a passage they highlighted stops being on offer the moment they read something else, and a scope
 * that outlived the screen it was chosen from would ask about words nobody can see. Resolving a key against the offered
 * list is what makes the scope shown and the scope used the same value rather than two that have to be kept in step.
 *
 * A selection is keyed by being one, not by which messages are in it: ticking a fifth row after choosing the selection
 * is still choosing the selection, and re-keying it there would silently drop the choice back to the screen.
 *
 * A fragment is keyed by the message it was taken from and never by the words, because a key is written to a store and
 * a passage of somebody's mail is not.
 */
export function askScopeKey(scope: AskScope): string {
    switch (scope.kind) {
        case 'mail':
            return scopeKey(scope.scope);
        case 'thread':
            return `thread:${scope.threadId}`;
        case 'selection':
            return 'selection';
        case 'message':
            return `message:${scope.messageId}`;
        case 'fragment':
            return `fragment:${scope.messageId}`;
    }
}

/**
 * What the mail space is pointing at, narrowest first.
 *
 * A highlighted passage is narrower than the message it is in, a set of ticked rows is what somebody picked out on
 * purpose, and a correspondence is narrower than the mailbox it was reached from. Nothing here is a guess about what
 * somebody meant: it is what the screen is showing, and the field draws it so that choosing differently is one control
 * away rather than something to discover afterwards.
 */
export function askScopeOnScreen(workspace: Workspace): AskScope {
    const [narrowest] = narrowerThanMail(workspace);

    return narrowest ?? { kind: 'mail', scope: workspace.scope };
}

/**
 * Everything the field offers to ask about, what is in force first.
 *
 * The order is precedence rather than width: the first entry is what the next question would be asked under with
 * nothing chosen, and everything after it is a way to widen or narrow without moving the mail space. What the screen is
 * showing comes first, then the mailbox the folder in scope belongs to, then every mailbox at once and each of them by
 * name — which is what widening after too narrow an answer actually is. Nothing is offered twice: a screen already
 * reading one mailbox does not offer it again below.
 */
export function askScopesOffered(workspace: Workspace, accounts: readonly MailAccount[]): readonly AskScope[] {
    const account = accountInScope(workspace.scope);

    const mailboxes: readonly MailScope[] = [
        workspace.scope,
        ...(account === null ? [] : [scopeOfAccount(account)]),
        everything,
        ...accounts.map((one) => scopeOfAccount(one.id)),
    ];

    const offered: AskScope[] = [
        ...narrowerThanMail(workspace),
        ...mailboxes.map((scope): AskScope => ({ kind: 'mail', scope })),
    ];

    const seen = new Set<string>();

    return offered.filter((scope) => {
        const key = askScopeKey(scope);
        const first = !seen.has(key);
        seen.add(key);

        return first;
    });
}

/**
 * The scope the next question would actually be asked under.
 *
 * What the person named in the field wins over what is on the screen, for as long as the field is still offering it.
 * A mailbox the deployment no longer declares, a correspondence that has been closed, and a passage whose message is no
 * longer being read all stop being offered — and a name that answers nothing falls back to the screen rather than
 * scoping a question to something nobody can see.
 */
export function askScopeInForce(workspace: Workspace, accounts: readonly MailAccount[]): AskScope {
    const named = workspace.askScopeKey;

    return (
        askScopesOffered(workspace, accounts).find((scope) => askScopeKey(scope) === named) ??
        askScopeOnScreen(workspace)
    );
}

/**
 * The questions asked before with this one at the front, held to what one tab may accumulate.
 *
 * A question asked again moves rather than repeating, which is what keeps the list short without anything having to
 * look for a duplicate before writing one — `search/MailSearch.tsx` keeps what was searched for the same way. It moves
 * on the words alone, because asking the same question under a wider scope is the commonest thing somebody does after
 * an answer that was too narrow, and two rows reading the same sentence is a list nobody can pick from.
 */
export function withAsked(
    asked: readonly AskedQuestion[],
    question: string,
    scope: AskScope,
): readonly AskedQuestion[] {
    return [{ question, scope }, ...asked.filter((before) => before.question !== question)].slice(
        0,
        mostAskedQuestions,
    );
}

/**
 * The same question and the scope it was asked under with a passage reduced to the message it came from.
 *
 * A question asked about a highlighted paragraph is kept; the paragraph is not. What was asked is a sentence somebody
 * typed and the message is a name the service assigned, but the passage is mail content, and this is what lets the list
 * outlive a reload without a store holding any — `rememberedWorkspace.ts` is the one caller and says why.
 */
export function withoutFragmentText(asked: AskedQuestion): AskedQuestion {
    return asked.scope.kind === 'fragment'
        ? { question: asked.question, scope: { kind: 'message', messageId: asked.scope.messageId } }
        : asked;
}

// Everything the screen is showing that is narrower than a mailbox, in the order the field reads them. Written once
// because the scope in force and the list the field offers are the same ladder read to different depths, and a second
// copy of it is how the chip and the control would come to disagree about which of the four is in force.
function narrowerThanMail(workspace: Workspace): readonly AskScope[] {
    const fragment = fragmentBeingRead(workspace);

    return [
        ...(fragment === null
            ? []
            : [{ kind: 'fragment', messageId: fragment.messageId, text: fragment.text } as const]),
        ...(workspace.selected.length > 0 ? [{ kind: 'selection', messages: workspace.selected } as const] : []),
        ...(workspace.conversation === null
            ? []
            : [{ kind: 'thread', threadId: workspace.conversation.threadId } as const]),
        ...(workspace.selection === null ? [] : [{ kind: 'message', messageId: workspace.selection } as const]),
    ];
}

// A passage counts while the message it was taken from is one of the messages being read: the message opened on its
// own, or any of the ones a correspondence draws, which the workspace names collectively rather than one at a time.
// Reading it this way rather than clearing the value from each screen that closes is what keeps a scope from outliving
// its words — a question scoped to a paragraph nobody can see any more is the disclosure this field exists to prevent.
function fragmentBeingRead(workspace: Workspace): SelectedFragment | null {
    if (workspace.fragment === null) {
        return null;
    }

    return workspace.conversation !== null || workspace.selection === workspace.fragment.messageId
        ? workspace.fragment
        : null;
}
