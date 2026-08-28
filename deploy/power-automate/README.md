# Power Automate workflow template

Azure Logic Apps is the deployable default. This directory provides the matching Power Automate cloud-flow contract for customers who want business users to own Teams and Outlook routing.

Power Automate connection references are tenant-owned and cannot be safely exported with reusable credentials. Build the flow inside a Power Platform Solution using these artifacts, then export that solution from the customer tenant for promotion between its environments.

For a notification-email-only installation, set `NOTIFICATION_CHANNELS=Outlook` on the
`plan-notifications` Worker and follow [How to send notification email](../../docs/how-to-send-notification-emails.md).
The dashboard, GitHub budget write permission, and Microsoft Graph identity sync are not
required when admin or owner recipients are configured.

## Flow

1. Use **When a HTTP request is received** or a Service Bus custom connector trigger.
2. Apply [event.schema.json](event.schema.json) to the trigger body.
3. Reject events whose `schemaVersion` is not `1`.
4. Use `recipients.userPrincipalNames` for direct user delivery and `recipients.ownerPrincipalNames` for accountable owners.
5. For `budget.change.requested.v1`, post [budget-approval-card.json](budget-approval-card.json) with **Post adaptive card and wait for a response** to the enterprise-admin approval chat or channel.
6. Call the API approval or rejection URL with the `requestId`, `expectedConcurrencyToken`, and the approver's Entra identity. The API still validates the caller's `EnterpriseAdmin` role.
7. For coaching, stale-data, synchronization, applied, conflict, or failure events, use [notification-card.json](notification-card.json) and send an Outlook copy when the event requests both channels.
8. Record failed flow runs and configure a Power Platform alert. Service Bus deliveries are at least once; the API and event fingerprint make repeated callbacks harmless.

Do not put detailed individual metrics into a Teams channel. Direct user messages may include the user's own coaching evidence; owner/admin messages may include the minimum financial or operational detail needed for action.

## Connection setup

- Microsoft Teams connection using an approved service account.
- Microsoft 365 Outlook connection using the same or another approved service account.
- API connection authenticated with Microsoft Entra delegated access to the Budget Manager API.
- Optional Service Bus custom connector using a managed identity or an environment-scoped connection reference.

The Teams connector requires the Workflows app to be allowed by the Teams administrator. Connector throttling is why the application deduplicates and batches notifications before publishing them.