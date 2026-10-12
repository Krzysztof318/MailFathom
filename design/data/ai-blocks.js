// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.aiBlocksAnswerParts = [
  { text: "The terms changed in three stages: the master agreement of April 2021", cite: "1", source: "Master agreement.pdf" },
  { text: ", CPI indexation with no upper cap introduced in February 2023", cite: "3", source: "CPI addendum 2023.pdf" },
  { text: ", the SLA cut from 4 h to 2 h in the November 2025 addendum", cite: "5", source: "SLA addendum.pdf" },
  { text: ". The 2027 proposal raises the price by 8% and keeps indexation uncapped", cite: "7", source: "Proposed terms 2027 - message" },
];

MailFathomDesign.data.aiBlocksEvidence = [
  { n: "1", source: "Master agreement.pdf", srcType: "attachment", snippet: "“Monthly remuneration is €1,200 net…”", relevance: "94%", freshness: "current · 12.04.2021", verdict: "supported" },
  { n: "3", source: "SLA addendum.pdf", srcType: "attachment", snippet: "“Response time shortened from 4 h to 2 h…”", relevance: "88%", freshness: "current · 18.11.2025", verdict: "outdated" },
  { n: "5", source: "Calculation_2027.xlsx", srcTypes: ["attachment", "mail"], snippet: "“Indexation at 4.1% CPI adds €10,542…”", relevance: "81%", freshness: "fresh · yesterday", verdict: "conflicting" },
];

MailFathomDesign.data.aiBlocksPrivateEvidence = { n: "7", source: "Private message - Karolina Kowalska", srcType: "mail", relevance: "76%", freshness: "today" };

MailFathomDesign.data.aiBlocksTimeline = [
  { date: "12.04.2021", title: "Master agreement", detail: "€1,200/mo · SLA 4 h", cite: "1" },
  { date: "03.02.2023", title: "CPI indexation", detail: "no upper cap", cite: "3" },
  { date: "18.11.2025", title: "SLA addendum", detail: "4 h → 2 h · +8%", cite: "5" },
  { date: "26.08.2026", title: "2027 proposal", detail: "decision due 28.08", cite: "7" },
];

MailFathomDesign.data.aiBlocksFactTable = [
  { version: "Agreement 2021", versionCite: "1", price: "€1,200", priceCite: "1", sla: "4 h", slaCite: "1", index: "none", indexCite: null },
  { version: "Addendum 2023", versionCite: "3", price: "€1,261", priceCite: "1", sla: "4 h", slaCite: "1", index: "CPI, uncapped", indexCite: "3" },
  { version: "Addendum 2025", versionCite: "5", price: "€1,452", priceCite: "5", sla: "2 h", slaCite: "5", index: "CPI, uncapped", indexCite: "3" },
  { version: "Proposal 2027", versionCite: "7", price: "€1,568", priceCite: "7", priceVerdict: "unsupported", sla: "2 h", slaCite: "5", index: "CPI, uncapped", indexCite: "3" },
];

MailFathomDesign.data.aiBlocksPeople = [
  { initials: "KK", name: "Karolina Kowalska", role: "You · sender", last: "today 08:47" },
  { initials: "MN", name: "Marta Nowak", role: "Controlling · Finance", last: "yesterday 11:20" },
  { initials: "AK", name: "Anna Kowalska", role: "Contoso · waiting for a reply", last: "today 08:47" },
];

MailFathomDesign.data.aiBlocksAttachments = [
  { type: "PDF", name: "Master agreement.pdf", meta: "312 kB · msg: “Master agreement - signatures”", avail: "available" },
  { type: "PDF", name: "SLA addendum.pdf", meta: "248 kB · msg: “Contract addendum”", avail: "available" },
  { type: "XLSX", name: "Calculation_2027.xlsx", meta: "42 kB · msg: “CPI calculation 2027”", avail: "needs permission" },
];

MailFathomDesign.data.aiBlocksThread = { subject: "Contract addendum", messageCount: 6 };

MailFathomDesign.data.aiBlocksCommitments = [
  { who: "Karolina", what: "5% CPI cap counter-proposal", when: "28.08" },
  { who: "Contoso", what: "Addendum version with the cap", when: "02.09" },
];

MailFathomDesign.data.aiBlocksAgreed = [{ text: "SLA cut from 4 h to 2 h" }, { text: "2027 price: +8% versus 2025" }];

MailFathomDesign.data.aiBlocksOpenQuestions = [{ text: "Do we accept a 5% upper indexation cap?" }];

MailFathomDesign.data.aiBlocksParticipants = [{ initials: "KK", name: "Karolina Kowalska" }, { initials: "AK", name: "Anna Kowalska - Contoso" }];

MailFathomDesign.data.aiBlocksDraft = {
  recipient: "Anna Kowalska",
  to: "Anna Kowalska - Contoso",
  subject: "Re: proposed terms for 2027",
  body: "We accept shortening the response time to 2 h. In return we ask for an upper CPI indexation cap of 5% per year, effective 1 January 2027.",
  partialBody: "We accept shortening the response time to 2 h. […] the text is still being edited.",
  sources: [{ n: "3", source: "CPI addendum 2023.pdf" }, { n: "5", source: "SLA addendum.pdf" }],
};

MailFathomDesign.data.aiBlocksSuggestedAction = {
  title: "Send the CPI cap counter-proposal to Anna Kowalska (Contoso)",
  reason: "The decision is due 28.08 and Contoso has been waiting for 2 days.",
  effect: "the message goes into the “Contract addendum” thread as a reply; nothing is sent automatically",
  partialEffect: "not determined yet - simulation running",
};

MailFathomDesign.data.aiBlocksUnknownBlock = { name: "RiskScore", planVersion: "v3", clientVersion: "v2" };

MailFathomDesign.data.aiBlocksDraftParagraphs = [
  "Thank you for the addendum. We accept shortening the response time for critical incidents to 2 hours on business days.",
  "In return we ask for an upper cap on CPI indexation of 5% per year. At the 4.1% forecast this keeps the cost predictable for both sides through 2027.",
  "If the cap is acceptable, we are ready to close the decision by 28 August.",
];

/* The blocks that gain controls in a conversation. EVENT and TASK earn a block of their own only
   because the object has to be seen and corrected before it is approved; move to folder, mark as
   read, add to a case and close a case stay one-line SUGGESTED ACTIONs. */
MailFathomDesign.data.aiBlocksProposals = [
  {
    type: "event", phase: "pending", meta: "calendar · work",
    title: "Focus block: IT contract audit list",
    fields: [
      { name: "When", value: "today 16:00-17:00" },
      { name: "Duration", value: "1 h" },
      { name: "Attendees", value: "You" },
      { name: "From", value: "thread “IT contract audit”" },
    ],
    conflict: "overlaps Rollout status with Piotr (15:00-16:30)",
    nav: [{ label: "Open the calendar", icon: "calendar_month" }],
  },
  {
    type: "event", phase: "pending", meta: "calendar · work",
    title: "Focus block: CPI cap counter-proposal",
    fields: [
      { name: "When", value: "tomorrow 09:00-10:00" },
      { name: "Duration", value: "1 h" },
      { name: "Attendees", value: "You" },
      { name: "From", value: "thread “Contract addendum - signatures”" },
    ],
    noConflict: "The slot is clear - nothing else in the calendar.",
  },
  {
    type: "event", phase: "accepted", meta: "calendar · work",
    title: "Focus block: CPI cap counter-proposal",
    fields: [
      { name: "When", value: "today 10:00-11:00" },
      { name: "Duration", value: "1 h" },
      { name: "Attendees", value: "You" },
      { name: "From", value: "thread “Contract addendum - signatures”" },
    ],
    result: "Added to the calendar · 09:42",
    nav: [{ label: "Open the calendar", icon: "calendar_month" }],
  },
  { type: "event", phase: "declined", meta: "calendar · work", title: "Focus block: IT contract audit list" },
  {
    type: "task", phase: "pending", meta: "tasks · from mail",
    title: "Send the notice-period list to the audit",
    fields: [
      { name: "Due", value: "tomorrow, 17:00" },
      { name: "From", value: "message “IT contract audit”" },
      { name: "Reminder", value: "09:00 tomorrow" },
    ],
    nav: [{ label: "Open the thread", icon: "forum" }],
  },
  {
    type: "task", phase: "accepted", meta: "tasks · from mail",
    title: "Send the notice-period list to the audit",
    fields: [
      { name: "Due", value: "tomorrow, 17:00" },
      { name: "From", value: "message “IT contract audit”" },
    ],
    result: "Added to tasks · 08:21",
  },
  {
    type: "draft", phase: "pending", meta: "reply · 96 words",
    title: "Reply to Anna Kowalska",
    fields: [
      { name: "To", value: "Anna Kowalska" },
      { name: "Subject", value: "Re: Contract addendum - signatures" },
    ],
    paras: MailFathomDesign.data.aiBlocksDraftParagraphs,
  },
  {
    type: "draft", phase: "accepted", meta: "reply · 96 words",
    title: "Reply to Anna Kowalska",
    fields: [
      { name: "To", value: "Anna Kowalska" },
      { name: "Subject", value: "Re: Contract addendum - signatures" },
    ],
    paras: MailFathomDesign.data.aiBlocksDraftParagraphs,
    result: "Sent into “Contract addendum - signatures” · 11:07",
    nav: [{ label: "Open the thread", icon: "forum" }],
  },
  { type: "draft", phase: "declined", meta: "reply", title: "Reply to Anna Kowalska" },
  {
    type: "action", phase: "pending", meta: "one step",
    title: "Attach the legal opinion to the Contoso 2027 case",
    reason: "The opinion answers the open question about the indexation cap and is not in the case file.",
    effect: "Adds the document to the case - nothing is sent to anyone.",
    cta: "Attach it", nav: [{ label: "Open the case", icon: "folder_open" }],
  },
  {
    type: "action", phase: "accepted", meta: "one step",
    title: "Attach the legal opinion to the Contoso 2027 case",
    effect: "Adds the document to the case - nothing is sent to anyone.",
    result: "Attached to Contoso 2027 · 14:16",
  },
  {
    type: "action", phase: "accepted", failed: true, meta: "one step",
    title: "Add the 12.11 date to the Fabrikam case",
    effect: "Writes the date into the case file.",
    result: "Could not add it - the case server rejected the write · 07:31",
  },
  { type: "action", phase: "declined", meta: "one step", title: "Mark the Fabrikam thread as waiting on them" },
];
