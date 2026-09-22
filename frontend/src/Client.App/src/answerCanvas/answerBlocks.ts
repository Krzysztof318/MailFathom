// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ComponentType } from 'react';
import type { AnswerBlock, AnswerBlockType } from '@mailfathom/client-backend';
import { AttachmentGallery } from './blocks/AttachmentGallery';
import { Draft } from './blocks/Draft';
import { EventProposal } from './blocks/EventProposal';
import { EvidenceList } from './blocks/EvidenceList';
import { FactTable } from './blocks/FactTable';
import { People } from './blocks/People';
import { SuggestedAction } from './blocks/SuggestedAction';
import { SynthesizedAnswer } from './blocks/SynthesizedAnswer';
import { TaskProposal } from './blocks/TaskProposal';
import { ThreadState } from './blocks/ThreadState';
import { Timeline } from './blocks/Timeline';

// What a block is drawn as, and which component draws which type. It sits apart from the canvas and from the card
// because both read it and neither owns it: adding a renderer is registering one here rather than editing the host,
// which is the whole reason the host looks a type up instead of branching on it.

/**
 * What a block is doing, which every renderer states in the same six words rather than in six of its own.
 *
 * It belongs to the block rather than to the run because a run is not in one state: one block is drawn while the next
 * is still being composed, and a source one of them rests on can be unreachable while the rest of the answer stands.
 */
export type AnswerBlockState = 'loading' | 'ready' | 'partial' | 'empty' | 'error' | 'offline';

/** What draws one block, which is an ordinary component so that a renderer may hold state and read the localization. */
export type AnswerBlockRenderer = ComponentType<{ readonly block: AnswerBlock }>;

/** Which component draws which type, and nothing for a type this build has no renderer for. */
export type AnswerBlockRenderers = Partial<Readonly<Record<AnswerBlockType, AnswerBlockRenderer>>>;

/**
 * The renderers this build carries.
 *
 * A type with no entry is drawn exactly as a type the contract does not carry is: named to the reader, with the rest
 * of the answer standing. That is the same sentence for the two reasons a client cannot draw a block — it was built
 * before the type existed, or before the renderer did — and neither is something a reader can act on differently.
 */
export const answerBlockRenderers: AnswerBlockRenderers = {
    answer: SynthesizedAnswer,
    evidenceList: EvidenceList,
    timeline: Timeline,
    factTable: FactTable,
    people: People,
    threadState: ThreadState,
    attachmentGallery: AttachmentGallery,
    draft: Draft,
    suggestedAction: SuggestedAction,
    eventProposal: EventProposal,
    taskProposal: TaskProposal,
};
