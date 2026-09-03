// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

export {
    clientPreferencesRoute,
    longestPreferencesAnswer,
    readClientPreferences,
    unsetClientPreferences,
    writeClientPreferences,
    type ClientPreferences,
    type ClientThemePreference,
} from './clientPreferences';
export { resolveDeploymentEntry, type DeploymentEntryRefusal, type DeploymentEntryResult } from './deployment';
export {
    deploymentSessionRoute,
    mailPermissions,
    readDeploymentSession,
    type DeploymentSession,
    type MailFathomPermission,
} from './deploymentSession';
export { failureReasonForStatus, type ClientFailure, type ClientFailureReason, type ClientResult } from './failure';
export {
    mailBodyRoute,
    readMailBody,
    type MailBlockAlignment,
    type MailBody,
    type MailBodyAsk,
    type MailBodyAvailability,
    type MailBodyText,
    type MailBodyTruncation,
    type MailDocument,
    type MailDocumentBlock,
    type MailDocumentLink,
    type MailDocumentRefusal,
    type MailHeadingBlock,
    type MailImageBlock,
    type MailInlineImage,
    type MailInlineRun,
    type MailLinkDeception,
    type MailListBlock,
    type MailListItem,
    type MailParagraphBlock,
    type MailPreformattedBlock,
    type MailQuoteBlock,
    type MailTableBlock,
    type MailTableCell,
    type MailTableColumn,
    type MailTableRow,
    type MailTextEmphasis,
    type MailUnimplementedBlock,
} from './mailBody';
export {
    mailAccountsRoute,
    readMailAccounts,
    type MailAccount,
    type MailAccountDirectory,
    type MailSynchronizationState,
} from './mailAccounts';
// `mailDraftAttachmentUploadRequest` is deliberately unpublished, for the reason the download's own request is:
// composed by a caller it would carry no trace context and leave no record. `stageMailDraftAttachment` is how a file
// is staged.
export {
    discardMailDraft,
    mailDraftAttachmentRoute,
    mailDraftAttachmentsRoute,
    mailDraftRoute,
    mailDraftSendRoute,
    mailDraftsRoute,
    mostAttachmentsInDraft,
    mostRecipientsInDraft,
    reviseMailDraft,
    sendMailDraft,
    stageMailDraftAttachment,
    unstageMailDraftAttachment,
    writeMailDraft,
    type MailDraft,
    type MailDraftAnswer,
    type MailDraftComposition,
    type MailDraftRecipient,
    type MailRecipientRole,
    type MailSendOutcome,
    type MailSendRefusal,
    type MailStagedAttachment,
} from './mailDrafts';
export {
    mailOutboxCancellationRoute,
    withdrawOutgoingMail,
    type MailSendWithdrawal,
} from './mailOutbox';
export {
    mailFoldersRoute,
    readMailFolders,
    type MailAccountFolders,
    type MailFolder,
    type MailFolderDirectory,
    type MailFolderRole,
} from './mailFolders';
// `mailAttachmentRequest` is deliberately unpublished: a caller composing it would compose it outside the span this
// package opens, so the request would carry no trace context and no record would be kept of it. `readMailAttachment`
// is how a download is reached, and the same holds for the portrait requests further down.
export {
    attachmentRefusalForStatus,
    mailAttachmentRoute,
    readMailAttachment,
    type MailAttachmentRefusal,
} from './mailAttachment';
export {
    mailMessageRoute,
    readMailMessage,
    type MailAttachment,
    type MailAuthorAuthentication,
    type MailCarried,
    type MailDeploymentTrust,
    type MailMessage,
    type MailMessageBodyForms,
    type MailMessageHeaders,
    type MailParticipant,
    type MailParticipantRole,
    type MailSenderVerdict,
} from './mailMessage';
export {
    mailFlagMutationsRoute,
    markMailRead,
    mostMessagesPerMutation,
    type MailMutationOutcome,
    type MailMutationResult,
} from './mailMutations';
export {
    longestSearchPage,
    longestSearchText,
    mailSearchRoute,
    mostSearchResults,
    readMailSearch,
    searchQueryString,
    type MailSearchPage,
    type MailSearchQuery,
    type MailSearchRanking,
    type MailSearchResult,
    type MailSearchRetrieval,
    type MailSemanticSearch,
} from './mailSearch';
export {
    longestThreadPage,
    mailThreadRoute,
    readMailThread,
    threadQueryString,
    type MailThreadMessage,
    type MailThreadPage,
    type MailThreadParticipant,
} from './mailThread';
export {
    longestTimelinePage,
    mailTimelineRoute,
    readMailTimeline,
    timelineQueryString,
    type MailTimelineEntry,
    type MailTimelineOrder,
    type MailTimelinePage,
    type MailTimelinePageDirection,
    type MailTimelineQuery,
} from './mailTimeline';
export {
    changeOwnDisplayName,
    longestDisplayNameAnswer,
    ownDisplayNameRoute,
    readOwnDisplayName,
    type OwnDisplayName,
    type OwnDisplayNameChange,
} from './ownDisplayName';
// `readOwnPortraitRequest` and the two beside it are deliberately unpublished, for the reason the download above gives.
export {
    isPortraitImageType,
    largestPortraitOctets,
    ownPortraitRoute,
    portraitImageTypes,
    readOwnPortrait,
    removeOwnPortrait,
    replaceOwnPortrait,
    type PortraitImageType,
} from './ownPortrait';
export { clientRoutePrefix, headersFor, routeFor, type ClientSession, type DeploymentAddress } from './session';
export { telemetryEndpoints, telemetryName } from './telemetry';
export { reachDeployment, signIn, type DeploymentGreeting, type SignInOutcome, type SignInRefusal } from './signIn';
export { longestResponseBody, type ClientRequest, type ClientResponse, type MailFathomTransport } from './transport';
