# NexoMail Microsoft Graph Integration — Phase 1 Design

Date: 2026-09-11
Status: Approved in chat for design; implementation pending written-spec review
Branch: `feature/microsoft-graph`

## 1. Goal

Add a controlled first Microsoft 365 integration to NexoMail using Microsoft Graph and delegated OAuth 2.0 authorization.

Phase 1 is intentionally narrow. Success means a signed-in NexoMail user can connect a Microsoft 365 work or school account, complete Microsoft authentication including MFA/Authenticator outside NexoMail, return to NexoMail, see the connected account as `Microsoft 365`, list inbox messages, and open a message without NexoMail persistently storing message bodies or attachment contents.

This phase is a feasibility and architecture validation for the later invitation-only marcha blanca. It is not full Microsoft feature parity with Gmail.

## 2. Existing architecture to preserve

NexoMail already has provider-neutral mail abstractions:

- `MailProviderType` already includes `MicrosoftGraph`.
- `IMailProvider` defines the provider contract.
- `MailGateway` routes each connected account to the provider matching its `MailProviderType`.
- `UserScopedMailProvider` enforces authenticated-user ownership and provider matching.
- `MailAccountEntity` and `OAuthCredentialEntity` are generic enough to store Microsoft-connected accounts and protected refresh tokens.
- The frontend already renders `MicrosoftGraph` accounts as `Microsoft 365` in the accounts list.

The Microsoft implementation must follow these existing seams instead of introducing a parallel mailbox architecture.

## 3. Selected approach

Use Microsoft Graph directly through `HttpClient`, following the same overall pattern already used by the Gmail integration.

This is preferred over adopting the full Microsoft Graph SDK in Phase 1 because:

- the current codebase already uses explicit HTTP provider implementations;
- the required Graph surface is small;
- token exchange, refresh, request formation, response mapping, and error behavior remain visible and testable;
- it avoids adding a large dependency before the integration has been validated with real Microsoft 365 tenants.

No broad OAuth/provider refactor is included in Phase 1. Targeted extraction is allowed only where Microsoft needs to reuse an existing cross-provider policy, such as the commercial account-count limit.

## 4. Microsoft Entra application configuration

The external Microsoft Entra app registration is configured as follows:

- Application name: `NexoMail`
- Supported account type: multiple Microsoft Entra organizations / multitenant work and school accounts
- Platform: Web
- Local redirect URI: `http://localhost:5052/api/oauth/microsoft/callback`
- Delegated Microsoft Graph permissions registered:
  - `User.Read`
  - `Mail.ReadWrite`
  - `Mail.Send`
- One client secret exists for local development and is stored outside Git.

Phase 1 uses delegated user authorization only. NexoMail must not use application permissions and must not attempt organization-wide mailbox access.

The authorization request may include OpenID Connect protocol scopes required for sign-in and refresh-token issuance, including `openid`, `profile`, `email`, and `offline_access`, in addition to the delegated Graph scopes required by the connection.

The initial authority is the organizational multitenant v2 endpoint so that work and school accounts from different Entra tenants can authenticate.

## 5. Backend components

### 5.1 `MicrosoftGraphOptions`

Add a Microsoft configuration object under a `Microsoft` configuration section with:

- `ClientId`
- `ClientSecret`
- `RedirectUri`
- `FrontendUrl`
- authority/base endpoint values only if they are not kept as constants

The client secret must never be committed to the repository. The development example file may document the key name with an empty value.

### 5.2 `MicrosoftOAuthService`

Responsibilities:

1. Enforce the same effective-plan account connection limit already applied by Google.
2. Generate an OAuth authorization URL.
3. Generate a cryptographically protected `state` value bound to the current NexoMail user.
4. Include issue time and nonce in the state payload.
5. Reject invalid, tampered, expired, or wrong-user state.
6. Exchange the authorization code server-side using the Microsoft client secret.
7. Call Microsoft Graph `/me` to identify the authenticated mailbox owner.
8. Resolve the email address from `mail`, falling back to `userPrincipalName` when required.
9. Create or reactivate a `MailAccountEntity` with `Provider = MicrosoftGraph`.
10. Create or update its `OAuthCredentialEntity`.
11. Protect the refresh token through the existing `ITokenProtector` before persistence.
12. Redirect back to the frontend with an explicit success or error result.

Use a provider-specific Data Protection purpose such as `NexoMail.MicrosoftOAuth.State.v1`. The state lifetime is ten minutes, matching the current Google security model.

The success redirect is `.../settings/accounts?connected=microsoft`.

### 5.3 OAuth endpoints

Add authenticated API routes parallel to the Google flow:

- `GET /api/oauth/microsoft/start`
- `GET /api/oauth/microsoft/callback`

`/start` checks whether another account may be connected and redirects to Microsoft.

`/callback` handles Microsoft cancellation, incomplete callback data, OAuth exchange failures, tenant consent failures, and successful completion. User-facing errors must be concise and must not expose tokens, secrets, raw provider responses, or internal stack information.

## 6. Token handling

NexoMail must not persist Microsoft access tokens.

The database stores only the protected refresh token through the existing credential entity. When Graph access is required, the Microsoft provider obtains a short-lived access token using the protected refresh token.

The implementation must:

- cache short-lived access tokens in memory per account;
- refresh before token expiration;
- serialize concurrent refresh operations per account;
- use the existing protected refresh token as the durable credential;
- if Microsoft returns a replacement refresh token during refresh, protect and persist the replacement before continuing to use it.

Password, MFA codes, Microsoft Authenticator approvals, and other primary authentication factors never pass through NexoMail.

## 7. `MicrosoftGraphMailProvider`

Add `MicrosoftGraphMailProvider : IMailProvider`, wrapped with `UserScopedMailProvider` in dependency injection.

Phase 1 implements the minimum read path necessary to validate the integration in the current UI.

### 7.1 Message listing

`GetMessagesAsync` supports inbox listing for a specific Microsoft account.

Use Microsoft Graph message APIs with a narrow `$select` projection containing only fields required to build `MailSummary`, such as:

- message id
- sender/from
- subject
- body preview
- received date/time
- read status
- attachment flag
- conversation id where useful for later phases

The provider maps Graph messages into the existing NexoMail `MailSummary` model.

Pagination uses the continuation information supplied by Microsoft Graph. NexoMail treats the continuation value as opaque provider state and must not invent page indexes.

Phase 1 acceptance testing is limited to the inbox. Microsoft-specific search, full folder parity, and advanced filtering are not part of this phase.

### 7.2 Opening a message

`GetMessageAsync` fetches a single message from Graph and maps it into the existing `MailMessage` model.

The message body remains transient. It is returned to the frontend for display and is not written into NexoMail persistence.

Address fields are mapped into the existing `MailAddress` model. HTML body content continues through the frontend's existing email HTML sanitization path.

Attachment metadata may be represented if Graph returns it, but downloading attachment content is not a Phase 1 acceptance criterion.

### 7.3 Read-state compatibility

The existing message page automatically marks an unread message as read after it is opened. Therefore Phase 1 must implement `MarkReadAsync` for Microsoft Graph even though broader mailbox modification features are deferred.

### 7.4 Interface methods outside Phase 1

The remaining `IMailProvider` operations are not considered Microsoft-supported Phase 1 features:

- send
- reply / reply all
- forward
- drafts
- trash / move
- folder management beyond the minimal read path
- attachment download
- full thread reconstruction

They must not silently perform partial or incorrect work.

Where the existing interface requires an implementation, the provider must either return a safe neutral result for read-only ancillary calls or throw a clear provider-specific `NotSupportedException`/`InvalidOperationException` that the API translates into a user-safe response.

The frontend must not advertise unsupported Microsoft write operations as if they were functional. For Phase 1, provider-aware gating may disable or hide Microsoft-only unsupported actions while leaving Gmail behavior unchanged.

For thread loading, which the current message page requests automatically, Phase 1 must return a safe empty collection rather than cause message opening to fail. Full Graph `conversationId` thread reconstruction is deferred.

## 8. Gmail-specific services and capability isolation

Several current features are Gmail-specific rather than provider-neutral, including Google Contacts, Gmail rules, Gmail control-center services, and Gmail metadata/index activities.

Phase 1 must prevent a connected Microsoft account from being accidentally routed into Gmail-only services.

Required behavior:

- Gmail-only services continue to operate for Gmail accounts.
- Microsoft accounts are excluded from Gmail-only aggregation and indexing paths.
- Microsoft accounts do not claim support for Google Contacts or Gmail rules.
- Control Center parity for Microsoft is explicitly deferred.
- Nexi actions that depend on unsupported Microsoft write operations must not be offered as executable Microsoft actions in Phase 1.

This isolation is part of the Phase 1 safety boundary, not a later enhancement.

## 9. Frontend changes

Update the accounts page to expose two explicit provider actions:

- `Agregar Gmail`
- `Agregar Microsoft 365`

The Microsoft button navigates to `/api/oauth/microsoft/start` and obeys the same commercial account limit as Gmail.

After successful callback, `?connected=microsoft` displays a Microsoft-specific success notice.

Connected Microsoft accounts continue to use the existing provider label `Microsoft 365`.

Error messages returned from OAuth must distinguish at least:

- user cancellation;
- invalid/expired authorization attempt;
- Microsoft connection failure;
- organization/admin consent restriction.

The interface must not imply that NexoMail can bypass an organization's Microsoft Entra consent policy.

## 10. Tenant consent behavior

Because NexoMail is a new multitenant app and is not yet a verified Microsoft publisher, an external organization may reject user consent or require administrator approval even when the delegated permissions themselves are user-consentable.

This is an expected test outcome, not an application bug.

The first manual external-tenant test will use a real Microsoft 365 organizational account. If Microsoft or that tenant requires administrator approval, NexoMail must surface that outcome clearly and stop. It must not attempt to bypass tenant policy.

Publisher verification and broader cross-tenant production readiness are separate pre-marcha-blanca work and are not required to complete Phase 1 code validation.

## 11. Data protection and privacy

Phase 1 follows the same persistence principle as the existing Gmail implementation:

Persisted:

- NexoMail account identity
- provider type
- mailbox email address / display settings
- protected OAuth refresh token
- existing metadata already allowed by NexoMail's architecture

Not persistently stored by this Microsoft integration:

- Microsoft password
- MFA/Authenticator data
- access token
- full email body solely for Graph display
- attachment binary content solely for Graph display

Data Protection keys remain necessary to decrypt stored OAuth credentials. Local Windows development uses the existing persistent key directory and DPAPI protection. Production deployment must preserve and back up the Data Protection key ring; losing those keys would make protected refresh tokens unusable.

## 12. Error handling

Provider errors are normalized into user-safe behavior.

Expected categories:

- authentication/consent denied;
- invalid or expired OAuth state;
- invalid/expired refresh credential;
- Graph 401/403 authorization failure;
- Graph throttling / 429;
- temporary Microsoft service/network failure;
- unsupported Phase 1 operation.

No raw token payload, client secret, protected credential, or sensitive provider response is written to user-facing messages.

A failed Microsoft account request must not prevent Gmail accounts from continuing to load through the unified gateway where the current gateway's partial-failure behavior permits it.

## 13. Testing strategy

Implementation follows TDD.

Automated tests/smoke tests must cover at minimum:

1. Microsoft authorization URL contains the expected client id, callback, state, organizational authority, and required scopes.
2. OAuth state rejects tampering, expiration, and mismatched NexoMail user.
3. Successful callback maps `/me` identity to a `MicrosoftGraph` account.
4. Refresh token is protected before persistence.
5. Reconnection reactivates the existing account rather than creating a duplicate.
6. Commercial max-account limits also apply to Microsoft connections.
7. Token refresh obtains an access token without exposing or persisting it.
8. Rotated Microsoft refresh tokens are persisted protected.
9. Graph inbox responses map correctly into `MailSummary`.
10. Graph single-message responses map correctly into `MailMessage`.
11. Opening an unread Microsoft message can mark it read.
12. Microsoft accounts are not routed through Gmail-only services.
13. Unsupported Microsoft write operations do not report false success.
14. Frontend smoke verifies the Microsoft 365 connect control and success state.
15. Existing Gmail and commercial/Nexi tests continue passing.

Tests use fake HTTP handlers/responses. CI must not use real Microsoft credentials, client secrets, mailboxes, or live Graph calls.

## 14. Manual acceptance test

After automated verification passes, the developer performs a local real-account test:

1. Configure `Microsoft:ClientId`, `Microsoft:ClientSecret`, `Microsoft:RedirectUri`, and `Microsoft:FrontendUrl` through local secure development configuration.
2. Start backend on `http://localhost:5052` and frontend on `http://localhost:5173`.
3. Sign in to NexoMail.
4. Open Cuentas de correo.
5. Choose `Agregar Microsoft 365`.
6. Authenticate on Microsoft's own UI, including Microsoft Authenticator if requested.
7. Observe whether the external tenant permits consent or requires administrator approval.
8. If consent succeeds, verify the account appears as `Microsoft 365`.
9. Open the inbox and verify Microsoft messages are listed.
10. Open a message and verify sender, subject, body, recipients, received time, and read-state behavior.
11. Confirm no message body or attachment content was added to persistent storage as part of the Graph read path.
12. Confirm Gmail accounts continue to function.

A tenant-admin-approval screen is a valid diagnostic outcome for the external-tenant consent portion, but Phase 1 code is not considered functionally validated against Graph until at least one Microsoft 365 organizational account completes OAuth and the read path successfully.

## 15. Explicitly deferred work

The following are outside Phase 1:

- sending Microsoft mail;
- reply/reply-all/forward;
- draft lifecycle;
- attachment download/preview parity;
- complete folder parity;
- Microsoft contact autocomplete;
- inbox rules via Graph;
- Control Center parity;
- Microsoft-backed Nexi write actions;
- personal Outlook/Hotmail accounts;
- Exchange on-premises;
- publisher verification implementation/process;
- production redirect URI and production secret/certificate deployment;
- full production marcha blanca configuration.

These items should be planned only after Phase 1 proves that OAuth and core Graph read operations work reliably.

## 16. Completion criteria

Phase 1 is complete only when:

- automated Microsoft OAuth/Graph tests pass;
- existing solution/frontend tests remain green;
- the Microsoft 365 button is available under account settings;
- a Microsoft organizational account can complete the supported OAuth flow in an allowed tenant;
- the connected account is persisted as `MicrosoftGraph` with a protected refresh token;
- inbox messages load through `IMailProvider`/`MailGateway`;
- a message opens successfully and unread-to-read behavior works;
- Gmail-specific services do not process the Microsoft account;
- unsupported Microsoft actions are not presented as working features;
- no Microsoft client secret or real token is committed to Git.
