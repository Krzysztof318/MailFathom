// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState } from 'react';
import {
    draftMailReply,
    longestDraftInstruction,
    longestDraftSelection,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { draftingRead } from '../composer/draftingRead';
import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';
import type { ReplyDrafting, ThreadDraft } from './replyDrafting';

// What the deployment is told the reply should be, before whatever the reader typed. It travels in English however the
// screen is read, for the reason `composer/DraftingBlock.tsx` gives: it is an instruction to a writer rather than a
// sentence anybody reads, and the deployment takes the language of the reply from the correspondence it answers.
const writeTheReply = 'Write a reply to this message.';

/**
 * Holds the reply the field under a correspondence asked for, and asks the deployment for it.
 *
 * The frame calls it and hands what it returns to both places that read it, which is what keeps the field and the card
 * drawing one draft rather than two.
 *
 * @param session Who is asking, or `null` where nobody is signed in — which asks for nothing.
 * @param transport How the request goes out.
 */
export function useReplyDraftingState(session: ClientSession | null, transport: MailFathomTransport): ReplyDrafting {
    const { translate } = useLocalization();
    const toasts = useToasts();
    const [held, setHeld] = useState<{ readonly session: ClientSession; readonly draft: ThreadDraft } | null>(null);
    const field = useRef<HTMLInputElement>(null);

    // Which request is the one the screen still wants. Letting a draft go, or asking for another, makes whatever is
    // still on the wire an answer nobody is waiting for, and it is dropped rather than drawn over what replaced it.
    const current = useRef(0);

    // A draft belongs to the credential it was written under. The frame outlives signing out and in again, and what
    // one person's deployment wrote about their mail is not something the next credential may be shown.
    const draft = held !== null && held.session === session ? held.draft : null;

    function draftReply(answering: string, typed: string, passage: string | null): void {
        // One reply at a time: a second press while the first is being written would spend the allowance twice and put
        // whichever answered last on the screen.
        if (session === null || draft?.body === null) {
            return;
        }

        const asked = ++current.current;
        const asking = session;
        const instruction = [writeTheReply, typed.trim()].filter((part) => part !== '').join(' ');

        setHeld({ session: asking, draft: { answering, body: null } });

        void draftMailReply(asking, transport, {
            answeredEmailId: answering,
            selection: passage === null ? null : passage.slice(0, longestDraftSelection),
            instruction: instruction.slice(0, longestDraftInstruction),
        }).then((answer) => {
            if (current.current !== asked) {
                return;
            }

            const read = draftingRead(answer);

            if (!read.drafted) {
                setHeld(null);
                toasts.raise({ kind: read.kind, title: translate('compose.notDraftedTitle'), body: translate(read.said) });

                return;
            }

            setHeld({ session: asking, draft: { answering, body: read.body } });
        });
    }

    function letGo(): void {
        current.current += 1;
        setHeld(null);
    }

    function holdField(element: HTMLInputElement | null): void {
        field.current = element;
    }

    function returnToField(): void {
        field.current?.focus();
    }

    return { draft, draftReply, letGo, holdField, returnToField };
}
