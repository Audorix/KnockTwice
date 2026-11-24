# Deploying with Docker

This guide runs Audorix.ChatX on one server with Docker Compose, PostgreSQL and HTTPS. For local
development profiles (SQLite or PostgreSQL, Mailpit, Swagger) see
[development/docker.md](development/docker.md).

## What you get

`docker/compose.production.yaml` starts:

| Service | Role | Exposed |
| --- | --- | --- |
| `postgres` | PostgreSQL 18, data in the `chatx_postgres-data` volume | no |
| `migrate` | Applies database migrations once, then exits; the API starts only after it succeeded | no |
| `api` | ASP.NET Core API in `Production` (no Swagger), data (settings file, attachments) in `chatx_api-data` | no |
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
```

Set `CHATX_POSTGRES_PASSWORD` in `docker/.env.production` to a long random value without `;`
(for example `openssl rand -base64 32 | tr -d ';/+='`).

## 2. Start

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

Open `https://chat.example.org`. The web app shows the setup wizard. Find the setup code:

```sh
docker compose -f docker/compose.production.yaml --env-file docker/.env.production logs api | grep -i "setup code"
```

The database step is locked (it comes from the compose file). Enter:

- **Email:** your SMTP server or a Postmark server token. Use *Send test email*.
- **Administrator:** your name, email and a strong password.
- **Options:**
  - leave development mode **off**;
  - set the web address to `https://chat.example.org`;
  - enter Cloudflare Turnstile keys, or switch human verification off.

*Install* writes `/app/data/chatx.settings.json` in the `chatx_api-data` volume and restarts the
API. Sign in. Further administrators: see [installation.md](installation.md#administrators).

## Operating it

| Task | Command |
| --- | --- |
| Logs | `docker compose -f docker/compose.production.yaml --env-file docker/.env.production logs -f api` |
| Health | `curl -fsS https://chat.example.org/health/ready` |
| Stop (keeps data) | `docker compose -f docker/compose.production.yaml --env-file docker/.env.production down` |

**Upgrades.** Pull the new code, rebuild, run migrations, restart:

```sh
git pull
docker compose -f docker/compose.production.yaml --env-file docker/.env.production build
docker compose -f docker/compose.production.yaml --env-file docker/.env.production run --rm migrate
docker compose -f docker/compose.production.yaml --env-file docker/.env.production up -d
```

Read the release notes (`CHANGELOG.md`) first and back up before upgrading.

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

## Limits of this setup

- One API instance by default. To run several, set `ScaleOut__Redis` (a Redis connection string; see
  the [configuration reference](development/configuration.md)): it adds the SignalR backplane,
  shared presence and shared rate limits (ADR-035). Private chats still live in one instance's
  memory, so with several instances keep everyone on one instance or switch private chats off (a
  startup warning says so). The bundled Compose files start one instance and no Redis; this setup
  has not been tested against a real Redis.
- SQLite is supported for small single-server installs (use
  `docker/compose.sqlite.yaml` as a starting point and set `CHATX_ENVIRONMENT=Production`), but
  PostgreSQL is the recommended production database.
