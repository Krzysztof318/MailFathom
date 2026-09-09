// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAccount } from '@mailfathom/client-backend';
import type { MailScope } from './mailScope';
import type { Workspace } from './useWorkspace';

// What a question is asked about, which is not the same thing as what the mail space is showing. Somebody reading one
// correspondence out of a folder means that correspondence when they ask, and somebody who has ticked four rows means
// those four — so the scope is named beside the field before the question is sent rather than discovered in the answer.
//
// It is one value derived from two, rather than a second scope kept beside the mail space's own: what is on the screen
// answers unless the person named something else in the field, and naming something else there changes what the next
// question reads without moving the list out from under them. Two scopes free to disagree would be a field that says
// one thing and asks another, which here is a disclosure rather than a display defect — what is in scope is what is
// read and sent.

/** What a question is asked about. */
export type AskScope =
    /** A mailbox, a folder, or every mailbox at once — the same scope the mail space is read under. */
    | { readonly kind: 'mail'; readonly scope: MailScope }

    /** The correspondence being read, whatever folder it was reached from. */
    | { readonly kind: 'thread'; readonly threadId: string }

    /** The messages picked out of the list, in the order the list draws them. */
    | { readonly kind: 'selection'; readonly messages: readonly string[] };

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
 * What the mail space is pointing at, narrowest first.
 *
 * A selection is narrower than the correspondence it was made in, and that is narrower than the mailbox it was reached
 * from. Nothing here is a guess about what somebody meant: it is what the screen is showing, and the field draws it
 * so that choosing differently is one control away rather than something to discover afterwards.
 */
export function askScopeOnScreen(workspace: Workspace): AskScope {
    if (workspace.selected.length > 0) {
        return { kind: 'selection', messages: workspace.selected };
    }

    if (workspace.conversation !== null) {
        return { kind: 'thread', threadId: workspace.conversation.threadId };
    }

    return { kind: 'mail', scope: workspace.scope };
}

/**
 * The scope the next question would actually be asked under.
 *
 * What the person named in the field wins over what is on the screen. A mailbox they named that the deployment no
 * longer declares is not one — a chosen scope outlives the answer it was chosen from — so it falls back to the screen
 * rather than asking about a mailbox nobody has. What that check has to cover is decided by what may reach this value
 * at all, which `stillOffered` below states.
 */
export function askScopeInForce(workspace: Workspace, accounts: readonly MailAccount[]): AskScope {
    return workspace.askScope !== null && stillOffered(workspace.askScope, accounts)
        ? { kind: 'mail', scope: workspace.askScope }
        : askScopeOnScreen(workspace);
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

// A mailbox somebody pointed the field at is offered while the deployment still declares the account it names. The
// account is the only kind that has to be asked about: the field offers every mailbox at once or one of them and
// nothing else, and `rememberedWorkspace.ts` refuses anything else on the way back in, so a folder or a role never
// reaches this value to go stale in it. Checking the account against the accounts is therefore the whole of it rather
// than a narrower reading of `mailScope.ts`'s own check, which needs the folder directory this field is never handed.
function stillOffered(scope: MailScope, accounts: readonly MailAccount[]): boolean {
    return scope.kind !== 'account' || accounts.some((account) => account.id === scope.accountId);
}
