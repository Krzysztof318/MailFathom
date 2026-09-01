// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

export { resolveDeploymentEntry, type DeploymentEntryRefusal, type DeploymentEntryResult } from './deployment';
export { failureReasonForStatus, type ClientFailure, type ClientFailureReason, type ClientResult } from './failure';
export {
    mailBodyRoute,
    readMailBody,
    type MailBlockAlignment,
    type MailBody,
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
export { clientRoutePrefix, headersFor, routeFor, type ClientSession, type DeploymentAddress } from './session';
export {
    deploymentSessionRoute,
    reachDeployment,
    signIn,
    type DeploymentGreeting,
    type SignInOutcome,
    type SignInRefusal,
} from './signIn';
export { longestResponseBody, type ClientRequest, type ClientResponse, type MailFathomTransport } from './transport';
