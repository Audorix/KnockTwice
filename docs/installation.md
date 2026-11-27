# Installation

Knock Twice is one ASP.NET Core API plus a static web app. Choose:

- **Docker** (recommended for servers): [deployment-docker.md](deployment-docker.md). PostgreSQL,
  migrations as a job, nginx in front.
- **Without Docker** (this page): publish the API, build the web app, put a reverse proxy in front.
  Works with **SQLite** (one file, small installs) or **PostgreSQL** (recommended).
- **For development** (hot reload, Swagger, demo data): [development/getting-started.md](development/getting-started.md).

## Requirements

| Component | Version |
| --- | --- |
| .NET | SDK 10.0.404+ to build; the ASP.NET Core 10 runtime to run |
| Node.js | 22.12+ (only to build the web app) |
| Database | SQLite (bundled) or PostgreSQL 15+ |
| Reverse proxy | nginx, Caddy or similar, with HTTPS (sign-in cookies are `Secure` outside Development) |
| Email | An SMTP server or a Postmark account (account confirmation, password reset) |

## 1. Build

From the repository root:

```sh
dotnet publish src/Api/Audorix.KnockTwice.Api -c Release -o /opt/chatx/api
cd web && npm ci && npm run build && cd ..
mkdir -p /opt/chatx/web && cp -r web/apps/chat-web/dist/* /opt/chatx/web/
```

On Windows use the same commands in PowerShell with Windows paths (for example `C:\chatx\api`).

## 2. Run the API

```sh
cd /opt/chatx/api
ASPNETCORE_ENVIRONMENT=Production dotnet Audorix.ChatX.Api.dll --urls http://127.0.0.1:5080
```

Run it as a service in production (a systemd unit, a Windows service, …) with the working
directory set to the publish folder. The account needs write access to `App_Data/` in that folder
(the settings file, a SQLite database and attachments live there by default; see
`Setup:SettingsPath`, `Storage:Local:Path`).

Example systemd unit (`/etc/systemd/system/chatx.service`):

```ini
[Unit]
Description=Knock Twice API
After=network.target postgresql.service

[Service]
WorkingDirectory=/opt/chatx/api
ExecStart=/usr/bin/dotnet /opt/chatx/api/Audorix.ChatX.Api.dll --urls http://127.0.0.1:5080
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=Network__TrustedProxies__0=127.0.0.1
User=chatx
Restart=always

[Install]
WantedBy=multi-user.target
```

## 3. Reverse proxy

Serve `/opt/chatx/web` as static files and forward the API paths to the API. Start from
[`docker/nginx/default.conf`](../docker/nginx/default.conf) and replace `http://api:8080` with
`http://127.0.0.1:5080`. That file already contains:

- forwarding of `/api`, `/health`, `/openapi`, `/swagger`;
- WebSocket forwarding for `/hubs/`;
- the frame policy for `/embed.html` (website chat), checked through `auth_request`;
- security headers.

Then add your TLS `server` block. Keep `client_max_body_size` in line with
`Attachments:MaxFileSize` (25 MB by default).

When the proxy runs on the same machine, `Network__TrustedProxies__0=127.0.0.1` (as in the unit
above) lets per-address limits see the real client address.

## 4. Setup wizard

Open `https://your-domain`. On the first start the API runs in **setup mode** and the web app shows
the setup wizard. It asks for the **setup code**, which proves you operate the server; see
[Finding the setup code](#finding-the-setup-code).

1. **Database**
   - *SQLite:* a file path, default `App_Data/chatx.db`. Back it up with the rest of `App_Data`.
   - *PostgreSQL:* host, port, database, user, password and SSL mode. *Test connection* checks it.
     The database is created if it is missing and the user may create databases; otherwise create
     it first with `CREATE DATABASE chatx OWNER chatx;`.
2. **Email:** SMTP or Postmark, with *Send test email*.
3. **Administrator:** the first installation administrator; their address is confirmed automatically.
4. **Options:**
   - keep development mode **off** in production;
   - set the public web address (`https://your-domain`);
   - choose the default language;
   - enter Cloudflare Turnstile keys, or switch human verification off.

*Install* migrates the database, creates the administrator, writes `App_Data/chatx.settings.json`
(it contains your database and email passwords: keep the folder private and backed up) and switches
to normal mode. Nothing is written before *Install*.

In the default interactive install, the wizard owns the database connection and stores it in this
owner-only settings file. Docker unattended deployments can instead set `Database:Provider` and
`Database:ConnectionString` through environment configuration, or set
`Database:ConnectionStringFile` to a read-only secret file containing the connection string. Either
external source locks the database fields in the wizard. Direct `Database:ConnectionString`
configuration takes precedence over the secret-file value; both take precedence over the settings
file. See [Deploying with Docker](deployment-docker.md) for examples.

**Unattended installs** can skip the wizard.

1. Configure everything in `appsettings.Production.json` next to the DLL, or through environment
   variables. Start from
   [samples/appsettings.Production.example.json](samples/appsettings.Production.example.json).
   `Setup:Completed: true` tells the API not to start the wizard.
2. Run `dotnet Audorix.ChatX.Api.dll migrate`.
3. Start the API.
4. Register the first account in the web app.
5. Make it an administrator with `grant-role` (below).

All keys are described in [the configuration reference](development/configuration.md).

### Finding the setup code

In setup mode the API prints a new code at every start, in three places:

- **the console**, as a banner:

  ```text
  ================================================================
    Knock Twice setup code:  K7QM-2XRA-9PDN
    Open https://your-domain to complete setup.
    The code is also saved to /opt/chatx/api/App_Data/setup-code.txt
  ================================================================
  ```

- **the log**, as a warning starting with `Setup required:`;
- **the file `setup-code.txt`** next to the settings file. It is removed when setup completes or the
  API stops.

| Installation | Where to look |
| --- | --- |
| Without Docker (systemd) | `journalctl -u chatx \| grep -i "setup code"`, or `App_Data/setup-code.txt` next to the DLL |
| Docker | `docker compose -f docker/compose.production.yaml --env-file docker/.env.production logs api \| grep -i "setup code"`, or `docker compose ... exec api cat /app/data/setup-code.txt` |
| Local development (`dotnet run`) | the API console, or `src/Api/Audorix.KnockTwice.Api/App_Data/setup-code.txt` |

Only the most recent code works: after a restart, use the new one. To choose the code yourself (for
automated installs), set `Setup:Token` to at least 12 characters; the API then logs that a
configured code is in use instead of printing it.

**The wizard appears on an installation that was already set up?** The API did not find its
settings file. Check that `App_Data` (or the folder in `Setup:SettingsPath`) is still where it was,
for example after moving or renaming the install folder, instead of running setup again.

## Administrators

Make an existing account an installation administrator:

```sh
dotnet Audorix.ChatX.Api.dll grant-role someone@example.org InstallationAdministrator
```

Use `Auditor` for read-only access to the administration. Afterwards roles are managed in the web
app under **Admin → Users**.

## Deleting accounts and data retention

- People delete their own account under **Account → Delete account** (password, or their email
  address for accounts that only use Google/Microsoft sign-in). Administrators delete accounts under
  **Admin → Users**. Personal data is erased; messages stay and show "Deleted user". Someone who is
  the only owner of an organization must hand it over or delete it first; the last administrator
  cannot be deleted.
- Owners delete an organization on its page (they type its name); administrators can too, under
  **Admin → Organizations**. Everything in it goes, including messages and files.
- **Admin → Settings → Data retention** (or `Retention:*`, see the configuration reference) removes
  anonymous website visitors after 90 days without activity and accounts never confirmed after 30
  days by default. Message retention is off until you set a number of days. The job runs every six
  hours and records what it removed in the audit log (`retention.applied`).

Backups keep deleted data until they expire ([backup-restore.md](backup-restore.md)).

## Upgrading

1. Read `CHANGELOG.md` and back up the database and `App_Data` ([backup-restore.md](backup-restore.md)).
2. Stop the API, publish the new version over the old folder (keep `App_Data`), rebuild the web app.
3. Apply migrations: `dotnet Audorix.ChatX.Api.dll migrate` (safe to repeat). Until you do, the
   API logs the pending migrations and `/health/ready` reports not ready.
4. Start the API.

More detail in [database-migrations.md](database-migrations.md).

## Checking an installation

| URL | Expect |
| --- | --- |
| `/health/live` | 200 while the process runs |
| `/health/ready` | 200 when the database is reachable and migrated |
| `/api/v1/meta` | Product name, API version, languages |

## Production checklist

- [ ] HTTPS in front; `App:PublicBaseUrl` is the `https://` address users open.
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (no Swagger, no development mode).
- [ ] `App_Data` (or the configured paths) private, writable by the service account only, and in the
      backups together with the database.
- [ ] Email works (send a test from **Admin → Providers**).
- [ ] Human verification configured, or registration restricted (**Admin → Settings**).
- [ ] `Network:TrustedProxies` set to the proxy address if the API is reachable only through it.
- [ ] Optional: S3-compatible storage and ClamAV scanning for attachments.
- [ ] Backups of the database, attachments and settings file, and a restore you have practised
      ([backup-restore.md](backup-restore.md)).
- [ ] Data retention reviewed (**Admin → Settings → Data retention**).
