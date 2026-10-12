// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.clientStatesSample = {
  accounts: {
    work: "Nordwind · work",
    workHeading: "Nordwind · work",
    personal: "Personal mail",
    workSynced: "synced 2 min ago",
    personalSynced: "synced 5 min ago",
  },

  people: {
    frida: { initials: "FI", name: "Frida Iversen", address: "frida.iversen@contoso.example" },
    karolina: { initials: "KK", name: "Karolina Kowalska" },
    marta: { initials: "MN", name: "Marta Nowak" },
    tomasz: { initials: "TB", name: "Tomasz Bąk", address: "t.bak@fabrikam.example" },
    emma: { initials: "EV", name: "Emma Vance", organisation: "Procurement · Fabrikam", address: "e.vance@fabrikam.example" },
  },

  discover: {
    question: "What did we agree with Contoso about weekend cover?",
    questionNothingComposed: "Did Nordwind ever agree to penalty clauses?",
    scopeAllMailboxes: "All mailboxes",
    scopePeriod: "2019-2026",
    plan: "plan: contract terms",
    syncStatus: "Synced 2 min ago · 2 accounts",
    answerWeekendCover: "Weekend cover is a 4 h response on Saturdays and Sundays, 08:00-20:00",
    answerWeekendCoverShort: "Weekend cover is a 4 h response on Saturdays and Sundays",
    answerAddendum: ", added by addendum 3 on 14 March",
    twoSourcesAgree: "2 sources agree",
    threeSourcesAgree: "3 sources agree",
    runLine: "Answered by eu-central-2 (fathom-large-3) · read 214 messages in 11 s · 1 of 40 questions this month",
    runLineNothingComposed: "Answered by eu-central-2 (fathom-large-3) · read 86 messages",
    readingMoreThreads: "Reading 3 more threads from Contoso…",
    stillComing: "2 more parts",
    nothingComposedBody: "The run read 86 messages and found nothing that answers this. Naming a person, a period or a single mailbox usually helps.",
    greeting: "Good morning, Karolina",
  },

  evidence: {
    positionSecond: "2 / 5",
    positionThird: "3 / 5",
    positionFourth: "4 / 5",
    addendumThreadSubject: "Re: Weekend cover - addendum 3",
    addendumByline: "Frida Iversen · 14 March 2026 · Nordwind · work",
    quotedPassage: "“Weekend cover applies Saturday and Sunday, 08:00 to 20:00, with a four-hour response.”",
    privateAnswer: "Legal signed off on the weekend rate in April",
    privateTitle: "Weekend rate - approval",
    privateByline: "Legal · shared · 22 April 2026",
    privateMailbox: "Legal · shared",
    privateAccessHint: "To read it, ask your administrator for access to Legal · shared.",
    wholeMessageAnswer: "Contoso accepted the weekend terms without changes",
    wholeMessageTitle: "Accepted: addendum 3",
    wholeMessageByline: "Frida Iversen · 16 March 2026 · Nordwind · work",
    wholeMessageGreeting: "Hi Karolina,",
    wholeMessageBody: "We have gone through addendum 3 with our operations team and accept it as sent, including the weekend window and the response time.",
    wholeMessageClosing: "Signed copy follows by Friday.",
    wholeMessageSignature: "Frida",
    attachmentName: "Addendum-3-signed.pdf",
    attachmentMeta: "PDF · 412 KB · page 2",
    attachmentQuote: "“§2.1 Weekend cover: Saturday and Sunday, 08:00-20:00.”",
    archiveQuote: "“Weekend support is out of scope for this agreement.”",
    imageName: "scan-page-2.jpg",
    imageQuote: "“Response within 4 hours on weekends.”",
  },

  askedBefore: [
    { question: "What SLA commitments do we have to clients?", scope: "All mailboxes" },
    { question: "Who owns Q4 renewals?", scope: "Nordwind · work" },
    { question: "Which invoices from August are unpaid?", scope: "Personal · 2026" },
  ],

  mail: {
    bankDetailsSubject: "Updated bank details for invoice 114",
    bankDetailsThreadCount: "1 message · Nordwind · work",
    bankDetailsTime: "10:42",
    bankDetailsVerdict: "This may not be from Tomasz Bąk - authenticating its author failed.",
    bankDetailsVerdictDetail: "Authenticated by sendwave.example - that is who actually sent it, not the name above.",
    bankDetailsBody: "Hello, please note our bank account has changed. Use the details below for invoice 2026/08/114 from today.",
    healthySenderTime: "Mon",
    healthyVerdictDetail: "Authenticated by contoso.example.",
    unauthenticatedVerdict: "This may not be from Jacek Wrona - authenticating its author failed.",
    longThreadCount: "31 messages · Nordwind · work",
    longThreadParticipants: "Frida Iversen, Marta Nowak, you · 31 messages",
    longThreadReadCount: "12 of 31 messages read",
    messageFridaText: "Attaching the signed copy as promised.",
    messageDayFriday: "Fri",
    messageKarolinaText: "Thanks - filing it with the contract.",
    messageMartaText: "Budget covers the weekend rate.",
    messageMartaDay: "Wed",
    folderNameExample: "e.g. Contracts 2027",
    folderInsideDefault: "Top level of Nordwind · work",
    searchDescription: "unpaid invoices from Tomasz before the holidays, most urgent first",
    searchDescriptionInterpreting: "unpaid invoices from Tomasz before the holidays",
    searchFilterFrom: "From: Tomasz Bąk",
    searchFilterHas: "Has: invoice",
    searchFilterMailbox: "Mailbox: Work",
    searchRankUnpaid: "not yet paid",
    searchRankDueDate: "nearest due date",
    pendingMoveRefused: "Moving “Budget line for Q4 renewals” to Clients was refused - Clients was renamed on the server.",
    pendingNotArchived: "2 messages were not archived.",
    pendingNotArchivedToast: "2 messages were not archived",
    pendingFlag: "Flag on “Invoice 2026/08/114”.",
  },

  work: {
    eventTitle: "Call with Frida - addendum 3",
    eventDay: "Fri 2 Oct",
    eventStartTime: "13:30",
    eventEndTime: "14:15",
    eventDuration: "45 min",
    eventAllDayEndDay: "Sat 3 Oct",
    eventConflictEndTime: "12:00",
    capacityLine: "5 h 20 min free between meetings · 6 open tasks, about 4 h.",
    didNotFit: "2 tasks did not fit today: Review §7.2 wording, Prepare Q4 budget notes.",
    taskReply: "Reply to Frida about the SLA",
    taskReplyMeta: "Today · 30 min",
    taskProposed: "Send the signed addendum to Legal",
    taskProposedMeta: "Proposed from mail · Frida Iversen, Fri",
    taskCollect: "Collect weekend-cover rates from other clients",
    taskCollectMeta: "No day · 1 h",
    taskTidy: "Tidy the Contracts folder",
    taskTidyMeta: "No day",
  },

  agent: {
    userRequest: "Draft a reply to Frida confirming the weekend terms.",
    agentReply: "I found addendum 3 and Frida's acceptance of 16 March. Drafting now.",
    steeringNote: "Write it in Polish, and copy Marta.",
    steeringTaken: "Drafting the reply in Polish, Marta in copy…",
    failedQuestion: "Which clients still have no weekend cover?",
    failedAnswerPartial: "Contoso and Adventure Works have it in their addenda. For Fabrikam I was reading",
    unsentMessage: "And which of them renew this year?",
  },
};

MailFathomDesign.data.clientStatesMessageRows = [
  { initials: "FI", from: "Frida Iversen", subject: "Re: Weekend cover - addendum 3", time: "10:42" },
  { initials: "MN", from: "Marta Nowak", subject: "Budget line for Q4 renewals", time: "09:15" },
  { initials: "JW", from: "Jacek Wrona", subject: "Wording of §7.2 indexation", time: "Mon" },
  { initials: "TB", from: "Tomasz Bąk", subject: "Invoice 2026/08/114", time: "Mon" },
];

MailFathomDesign.data.clientStatesSearchResultRows = [
  { subject: "Invoice 2026/08/114 - reminder, due 5 Oct", time: "Mon" },
  { subject: "Invoice 2026/08/102 - second notice", time: "12 Sep" },
  { subject: "Invoice 2026/07/088", time: "3 Aug" },
];

MailFathomDesign.data.clientStatesFolders = [
  { icon: "inbox", label: "Inbox", count: "12", active: true },
  { icon: "flag", label: "Flagged", count: "3" },
  { icon: "label_important", label: "Important", count: "5" },
  { icon: "draft", label: "Drafts", count: "1" },
  { icon: "outbox", label: "Outbox", count: "2 waiting", waiting: true },
  { icon: "send", label: "Sent", count: "" },
  { icon: "all_inbox", label: "All mail", count: "" },
  { icon: "archive", label: "Archive", count: "" },
  { icon: "report", label: "Spam", count: "4" },
  { icon: "delete", label: "Trash", count: "" },
  { icon: "folder", label: "Cases", count: "" },
  { icon: "folder", label: "Contracts", count: "" },
];

MailFathomDesign.data.clientStatesTasksToday = [
  { title: "Reply to Frida about the SLA", meta: "Today · 30 min" },
  { title: "Check Marta's budget line", meta: "Today · 20 min" },
  { title: "Review §7.2 wording", meta: "Today · 1 h 30 min" },
];

MailFathomDesign.data.clientStatesPlacements = [
  { at: "10:30-11:00", title: "Reply to Frida about the SLA" },
  { at: "11:00-11:20", title: "Check Marta's budget line" },
  { at: "14:15-15:15", title: "Collect weekend-cover rates" },
  { at: "16:00-16:30", title: "Send the signed addendum to Legal" },
];

MailFathomDesign.data.clientStatesPeople = [
  { initials: "EV", name: "Emma Vance", organisation: "Fabrikam · Procurement" },
  { initials: "FI", name: "Frida Iversen", organisation: "Contoso · Operations" },
  { initials: "JW", name: "Jacek Wrona", organisation: "Nordwind · Legal" },
  { initials: "MN", name: "Marta Nowak", organisation: "Nordwind · Finance" },
];

MailFathomDesign.data.clientStatesConversationHistory = [
  { title: "Weekend cover terms", meta: "now · running" },
  { title: "Q4 renewals owners", meta: "Mon · 6 messages" },
  { title: "Unpaid August invoices", meta: "12 Sep · 4 messages" },
];
