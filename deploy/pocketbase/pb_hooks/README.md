PocketBase JavaScript hook files (`*.pb.js`) go here.

Planned hook:

- `POST /api/eve-industry/auth/eve/callback`
  - accepts the EVE SSO authorization code and PKCE verifier
  - exchanges the code with CCP
  - verifies the EVE identity
  - upserts `users` and `eve_accounts`
  - returns a PocketBase auth response
