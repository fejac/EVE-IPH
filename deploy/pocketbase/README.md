# Eve Industry Planner PocketBase

This folder contains a lightweight PocketBase deployment for Coolify/Docker.

The deployment builds a thin image from the community image `ghcr.io/muchobien/pocketbase` and copies this repository's migrations/hooks into it. This avoids unreliable relative bind mounts in Coolify.

PocketBase stores its SQLite database and uploaded files in `/pb_data`, mapped to the `pocketbase-data` Docker volume.

## Local Run

```powershell
cd deploy/pocketbase
copy .env.example .env
docker compose up --build
```

Open:

- App/API: `http://localhost:8090`
- Admin UI: `http://localhost:8090/_/`

Create the first superuser in the Admin UI after the first start.

## Coolify

Use this directory as a Docker Compose application.

Set these environment variables in Coolify:

- `PB_VERSION`
- `TZ`
- `PB_ADMIN_EMAIL`
- `PB_ADMIN_PASSWORD`
- `POCKETBASE_ENCRYPTION_KEY`
- `EVE_SSO_CLIENT_ID`
- `EVE_SSO_CLIENT_SECRET`
- `EVE_SSO_REDIRECT_URI`
- `TOKEN_ENCRYPTION_KEY`

Expose port `8090`. Let Coolify terminate HTTPS at the public domain.

For production, pin `PB_VERSION` to a tested image tag instead of `latest`.

## Initial Data Model

The initial migration creates:

- `users` auth collection
- `eve_accounts`
- `user_settings`
- `facilities`
- `production_ledgers`
- `market_scan_cache`

Most app-specific data starts as JSON payloads. This keeps the server schema stable while the WPF planner is still changing.

## Migration Files

The `pb_migrations` and `pb_hooks` folders are copied into the image at build time.

After changing migrations or hooks:

1. Commit and push the repository.
2. Redeploy/rebuild the Coolify application.
3. Check the container shell:

```sh
ls -la /pb_migrations
```

## EVE SSO Flow

EVE SSO is handled by a custom PocketBase hook:

1. Desktop app opens EVE SSO with PKCE.
2. Desktop app catches the localhost callback.
3. Desktop app sends the authorization code and PKCE verifier to:

```text
POST /api/eve-industry/auth/eve/callback
```

3. PocketBase exchanges the code with CCP, verifies the character identity and upserts `users` + `eve_accounts`.
4. PocketBase returns a normal PocketBase auth response to the desktop app.
5. ESI refresh tokens are stored server-side, encrypted with `TOKEN_ENCRYPTION_KEY`.

`TOKEN_ENCRYPTION_KEY` must be exactly 32 characters because PocketBase `$security.encrypt` uses AES-256-GCM.

The hook file is currently a placeholder so the deployment is usable immediately. The custom EVE auth endpoint should be added before moving refresh-token storage to the server.
