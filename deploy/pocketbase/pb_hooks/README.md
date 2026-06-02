PocketBase JavaScript hook files (`*.pb.js`) go here.

Implemented hook:

- `POST /api/eve-industry/auth/eve/callback`
  - accepts the EVE SSO authorization code and PKCE verifier
  - exchanges the code with CCP
  - verifies the EVE identity
  - upserts `users` and `eve_accounts`
  - returns a PocketBase auth response

Expected JSON body:

```json
{
  "code": "authorization-code-from-eve",
  "code_verifier": "pkce-verifier-if-used",
  "redirect_uri": "http://localhost:8080/callback/"
}
```

If an existing PocketBase `users` auth token is sent in the `Authorization` header, the EVE character is linked to that user. Otherwise the hook finds or creates a user for the EVE character.
