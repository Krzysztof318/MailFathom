// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock } from '@mailfathom/client-backend';
import { wordCalendarDay } from '../../localization/instants';
import { useLocalization } from '../../localization/useLocalization';
import { UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { useAnswerSources } from '../answerSources';
import { ProposalCard, ProposalFields, ProposalTitle, type ProposalControl } from '../ProposalCard';
import { citedThread, useProposalAnswering } from '../proposalAnswering';

// A task the agent would have the person owe, drawn with the day it would be due and the message it came out of — the
// two things somebody corrects before they take a task on.

export function TaskProposal({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();
    const { sources } = useAnswerSources();
    const answering = useProposalAnswering();

    if (block.type !== 'taskProposal') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { proposal, evidence } = block;
    const title = proposal.title;
    const due = proposal.dueOn === null ? null : wordCalendarDay(proposal.dueOn, locale);
    const thread = citedThread(evidence.citations, sources);

    const controls: readonly ProposalControl[] =
        answering === null
            ? []
            : [
                  {
                      said: translate('taskProposal.add'),
                      name:
                          due === null
                              ? translate('taskProposal.addNameUndated', { title })
                              : translate('taskProposal.addName', { title, due }),
                      primary: true,
                      run: () => {
                          answering.answer('accepted');
                      },
                  },
                  {
                      said: translate('proposal.decline'),
                      name: translate('taskProposal.declineName', { title }),
                      run: () => {
                          answering.answer('declined');
                      },
                  },
              ];

    return (
        <ProposalCard
            citations={evidence.citations}
            controls={controls}
            kind="taskProposal"
            label={translate('taskProposal.label')}
            title={title}
        >
            <ProposalTitle>{title}</ProposalTitle>

            <ProposalFields
                fields={[
                    ...(due === null ? [] : [{ name: translate('taskProposal.due'), value: due }]),
                    ...(thread === null
                        ? []
                        : [{ name: translate('taskProposal.from'), value: <q lang="">{thread.label}</q> }]),
                ]}
            />
        </ProposalCard>
    );
}
