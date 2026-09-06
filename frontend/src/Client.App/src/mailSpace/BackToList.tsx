// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Control } from '../controls/Control';
import { useLocalization } from '../localization/useLocalization';
import { useWorkspace } from '../workspace/useWorkspace';

// The way back to the list in the composition that draws one pane at a time, which is the arrow the design puts at
// the start of whatever the reading column holds. It is one component rather than a button written where each surface
// needs it, because three surfaces draw it — the column itself above most of what it holds, the message's head beside
// its subject, and the message's own waiting and failure states before there is a head — and the way back has to be
// the same act from all of them.
//
// It closes what is open by revising the workspace, which is why nothing has to be handed down to it: the space that
// opened a message and the head that draws it are four components apart, and a callback travelling that far is the
// tree being wrong rather than context being needed.

export function BackToList() {
    const { translate } = useLocalization();
    const { revise } = useWorkspace();

    return (
        <Control
            label={translate('mail.backToList')}
            icon="arrow_back"
            shape="symbol"
            onPress={() => {
                revise({ selection: null, conversation: null });
            }}
        />
    );
}
