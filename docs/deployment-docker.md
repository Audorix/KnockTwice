# Deploying with Docker

This guide runs Knock Twice on one server with Docker Compose, PostgreSQL and HTTPS. For local
development profiles (SQLite or PostgreSQL, Mailpit, Swagger) see
[development/docker.md](development/docker.md).

## What you get

`docker/compose.production.yaml` starts:

| Service | Role | Exposed |
| --- | --- | --- |
| `postgres` | PostgreSQL 18, data in the `chatx_postgres-data` volume | no |
| `migrate` | Explicit one-off migration command for upgrades; uses the saved settings file | no |
| `api` | ASP.NET Core API in `Production` (no Swagger), data (settings file, attachments) in `chatx_api-data`; runs the first-install wizard when no settings exist | no |
| `web` | nginx with the web app; forwards `/api`, `/hubs`, `/health` to the API; serves the website-chat loader and embed page | `127.0.0.1:8080` |

The web port is bound to localhost on purpose: put a TLS reverse proxy on the host in front of it.
Sign-in cookies are `Secure`, so the application must be reached over HTTPS.

## Requirements

- A Linux server with Docker Engine and Compose v2, 2 CPU / 2 GB RAM is plenty for small teams.
- A DNS name, e.g. `chat.example.org`, pointing at the server.
- A TLS reverse proxy on the host (examples below) and outbound SMTP or a Postmark account for
  account emails.

## 1. Get the code and configure secrets

```sh
git clone https://github.com/Audorix/KnockTwice.git
cd KnockTwice
cp docker/.env.production.example docker/.env.production
chmod 600 docker/.env.production
mkdir -p docker/secrets
umask 077
openssl rand -base64 32 > docker/secrets/postgres_password
```

The PostgreSQL container reads its bootstrap password from the ignored, host-side secret file
`docker/secrets/postgres_password`. Keep this file private and include it in the protected backup
and recovery procedure. The password is used only when PostgreSQL initializes an empty data
volume; changing the file later does not change the PostgreSQL role password. The database
password is not included in the API image or Compose environment. The setup wizard asks for the
database connection; use host `postgres`, port `5432`, database `chatx`, username `chatx`, and the
password from this file.

## 2. Start

Use the published images from Docker Hub (quickest), or build them yourself from the code.

**Option A: published images.** The API and web images are published on Docker Hub as
[`audorix/chatx-api`](https://hub.docker.com/r/audorix/chatx-api) and
[`audorix/chatx-web`](https://hub.docker.com/r/audorix/chatx-web). The Compose file already names
them; download them and start without building:

```sh
docker compose -f docker/compose.production.yaml --env-file docker/.env.production pull
docker compose -f docker/compose.production.yaml --env-file docker/.env.production up -d
docker compose -f docker/compose.production.yaml --env-file docker/.env.production ps
```

Leave out `--build`, which would compile the images from the checked-out code instead. The
default tags match the Compose file in the release you cloned. To run another version, set
`CHATX_API_IMAGE` and `CHATX_WEB_IMAGE` in `docker/.env.production`, for example
`CHATX_API_IMAGE=audorix/chatx-api:1.1.0`, and always use the same version for both images.

**Option B: build from the code.**

```sh
docker compose -f docker/compose.production.yaml --env-file docker/.env.production up --build -d
docker compose -f docker/compose.production.yaml --env-file docker/.env.production ps
```

The first build takes a few minutes (it compiles the API and the web app inside the images).

## 3. TLS reverse proxy

Forward everything for your domain to `http://127.0.0.1:8080`, keep the `Host` header, pass
`X-Forwarded-For`/`X-Forwarded-Proto`, allow WebSocket upgrades (realtime) and request bodies of at
least 26 MB (attachments).

**Caddy** (`/etc/caddy/Caddyfile`; obtains certificates automatically):

```text
chat.example.org {
    reverse_proxy 127.0.0.1:8080
    request_body {
        max_size 26MB
    }
}
```

**nginx** on the host (with a certificate from certbot or similar):

```nginx
server {
    listen 443 ssl;
    server_name chat.example.org;
    ssl_certificate     /etc/letsencrypt/live/chat.example.org/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/chat.example.org/privkey.pem;
    client_max_body_size 26m;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $http_connection;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        proxy_read_timeout 120s;
    }
}
```

The API trusts forwarded headers from the compose network (`Network__TrustedProxies__0`), which
includes the gateway your host proxy connects through, so per-address limits (sign-in attempts,
anonymous website-chat sessions) see real client addresses. Keep the web port bound to
`127.0.0.1` so nobody can reach nginx directly with a forged `X-Forwarded-For`.

## 4. First start: the setup wizard

Open `https://chat.example.org`. The web app shows the setup wizard. Find the setup code (more in
[Finding the setup code](installation.md#finding-the-setup-code)):

```sh
docker compose -f docker/compose.production.yaml --env-file docker/.env.production logs api | grep -i "setup code"
```

The database step is editable in this default interactive profile. Enter the PostgreSQL values
above, then use **Test connection**. Enter:

- **Email:** your SMTP server or a Postmark server token. Use *Send test email*.
- **Administrator:** your name, email and a strong password.
- **Options:**
  - leave development mode **off**;
  - set the web address to `https://chat.example.org`;
  - enter Cloudflare Turnstile keys, or switch human verification off.

*Install* applies the initial migrations, creates the first administrator, writes
`/app/data/chatx.settings.json` in the `chatx_api-data` volume with owner-only permissions, and
restarts the API. The wizard-managed database connection is stored there. Sign in. Further
administrators: see [installation.md](installation.md#administrators).

## Operating it

| Task | Command |
| --- | --- |
| Logs | `docker compose -f docker/compose.production.yaml --env-file docker/.env.production logs -f api` |
| Health | `curl -fsS https://chat.example.org/health/ready` |
| Stop (keeps data) | `docker compose -f docker/compose.production.yaml --env-file docker/.env.production down` |

**Upgrades.** Get the new code and images, run migrations, restart. With the published images:

```sh
git pull
docker compose -f docker/compose.production.yaml --env-file docker/.env.production pull
docker compose -f docker/compose.production.yaml --env-file docker/.env.production run --rm migrate
docker compose -f docker/compose.production.yaml --env-file docker/.env.production up -d
```

If you pinned versions with `CHATX_API_IMAGE`/`CHATX_WEB_IMAGE`, update both first. When you build
the images yourself, run `build` instead of `pull`.

Read the release notes (`CHANGELOG.md`) first and back up before upgrading.

**Upgrading an older Compose install.** Earlier production Compose files supplied the database
connection only through environment variables, so it may not exist in `chatx.settings.json`. Before
upgrading, keep the existing PostgreSQL password and select either the environment-key or
secret-file configuration below for both `api` and `migrate`. For secret-file mode, write the full
connection string with the existing password to `docker/secrets/database_connection_string` and
use the override for every build, migration, and startup command. For the new bootstrap secret file,
use the existing database password when reusing an existing PostgreSQL volume; `POSTGRES_PASSWORD_FILE`
does not change the password in a database that has already been initialized. Once the application
is running from the new image, future deployments can continue using that external configuration
or perform a planned move to wizard-managed settings.

**Backups.** Back up both volumes together: the database and the API data (settings file with
provider secrets, and attachment files).

```sh
docker compose -f docker/compose.production.yaml --env-file docker/.env.production exec -T postgres \
  pg_dump -U chatx -d chatx --format=custom > chatx-$(date +%F).dump
docker run --rm -v chatx_api-data:/data -v "$PWD":/backup alpine tar czf /backup/chatx-data-$(date +%F).tgz -C /data .
```

The Data Protection keys (they protect sign-in cookies and widget secrets) are stored in the
database, so the database backup includes them.

**Restore** into a fresh deployment: start only `postgres`, restore with
`pg_restore -U chatx -d chatx --clean`, unpack the data archive into the `chatx_api-data` volume,
then run `migrate` and start the rest. The general procedure, and a script that practises it on
throwaway data, are in [backup-restore.md](backup-restore.md).

## Configuration with environment variables

Everything the wizard sets can also come from environment variables, which take precedence over
the settings file (see [the configuration reference](development/configuration.md)). Put them in a
`docker/compose.override.production.yaml` you keep outside version control, for example:

```yaml
services:
  api:
    environment:
      App__PublicBaseUrl: https://chat.example.org
      Email__Provider: Smtp
      Email__FromAddress: chat@example.org
      Email__Smtp__Host: smtp.example.org
      Email__Smtp__Port: "587"
      Email__Smtp__Security: StartTls
      Email__Smtp__Username: chat@example.org
      Email__Smtp__Password: ${CHATX_SMTP_PASSWORD}
```

and add `-f docker/compose.override.production.yaml` to every command. Object storage
(`Storage__Provider=S3`, `Storage__S3__*`) and malware scanning (`Scanning__Provider=ClamAv`) are
configured the same way.

### Database connection from an environment key

For unattended installs, set both `Database__Provider` and `Database__ConnectionString` on the
`api` and `migrate` services in a private Compose override. The same connection string can be
provided through a protected environment file as `CHATX_DATABASE_CONNECTION_STRING` and expanded
in that override. The wizard then shows the database as locked. Environment values are visible to
Docker administrators through container inspection, so use the secret-file option below when that
exposure is not acceptable.

```yaml
services:
  api:
    environment:
      Database__Provider: PostgreSql
      Database__ConnectionString: ${CHATX_DATABASE_CONNECTION_STRING:?Set in the protected env file}
  migrate:
    environment:
      Database__Provider: PostgreSql
      Database__ConnectionString: ${CHATX_DATABASE_CONNECTION_STRING:?Set in the protected env file}
```

### Database connection from a secret file

Create a host-side file containing only the PostgreSQL connection string, protect it, and keep it
outside version control:

```sh
umask 077
cat > docker/secrets/database_connection_string <<'EOF'
Host=postgres;Port=5432;Database=chatx;Username=chatx;Password=replace-with-the-database-password;SSL Mode=Prefer
EOF
```

Add a private Compose override (also ignored by Git) that mounts the file into both the API and
migration service and selects it through a non-secret path setting:

```yaml
services:
  api:
    environment:
      Database__Provider: PostgreSql
      Database__ConnectionStringFile: /run/secrets/database_connection_string
    secrets: [database_connection_string]
  migrate:
    environment:
      Database__Provider: PostgreSql
      Database__ConnectionStringFile: /run/secrets/database_connection_string
    secrets: [database_connection_string]

secrets:
  database_connection_string:
    file: ./secrets/database_connection_string
```

The file is mounted read-only. The API reads it at startup, and the wizard treats the database as
locked. The direct environment key `Database__ConnectionString` takes precedence if both methods
are configured. File-backed Compose secrets are not encrypted at rest on the host: restrict file
permissions, protect the host disk and backups. To rotate a database password, change the PostgreSQL
role password and update the active connection source (the wizard-written settings file, secret
file, or environment key) before restarting the API. Also update `postgres_password` for future
empty-volume initialization.

## Limits of this setup

- One API instance by default. To run several, set `ScaleOut__Redis` and the required,
  environment-specific `ScaleOut__Prefix` (a Redis connection string; see the
  [configuration reference](development/configuration.md)). Use the same values on each node and a
  different prefix in staging and production. This enables the SignalR backplane, shared presence,
  shared sign-in and visitor rate limits, and Redis readiness (ADR-035). Configure WebSocket
  upgrades and cookie affinity for hub negotiation and connection/fallback requests. Private chats
  still live in one instance's memory, so keep everyone on one instance or switch them off. A
  reproducible two-node PostgreSQL/Redis/HAProxy stack and real-client test are in
  [Phase 52](releases/phase-52-multi-node-signalr.md); the actual two-node test has not yet run in
  this environment.
- SQLite is supported for small single-server installs (use
  `docker/compose.sqlite.yaml` as a starting point and set `CHATX_ENVIRONMENT=Production`), but
  PostgreSQL is the recommended production database.
