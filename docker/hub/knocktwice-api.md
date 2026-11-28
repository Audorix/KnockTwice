# Knock Twice API

The server for **Knock Twice by Audorix**: open-source, self-hosted team and customer chat. One messaging engine serves personal chat, business communication and an embeddable website chat. Built with ASP.NET Core (.NET 10), runs on SQLite or PostgreSQL, in English and Spanish. MIT licensed.

Use it together with [`ikemyle/knocktwice-web`](https://hub.docker.com/r/ikemyle/knocktwice-web), which serves the web app and forwards requests to this API. Start them together with Docker Compose, not with `docker run` or the Docker Desktop **Run** button: Knock Twice needs several containers working together.

## Quick start: try it on your computer

You need Docker (Docker Desktop on Windows or Mac) and one file. Nothing is built, and there is no password or settings file to prepare.

1. Download [`compose.quickstart.yaml`](https://raw.githubusercontent.com/Audorix/KnockTwice/master/docker/compose.quickstart.yaml) into an empty folder.
2. In that folder, run:

   ```sh
   docker compose -f compose.quickstart.yaml up -d
   ```

3. Open http://localhost:8080 and follow the setup wizard. The setup code is in the log of the `api` container (Docker Desktop: **Containers > knocktwice > api > Logs**). For email, use host `mailpit`, port `1025`, security *None*; the emails arrive at http://localhost:8025.

This uses SQLite and is reachable only from your computer. Step-by-step guide: [Quick start with Docker](https://github.com/Audorix/KnockTwice/blob/master/docs/quick-start-docker.md)

## Run it on a server

For a server use the production Compose file from the source repository (PostgreSQL, HTTPS behind a reverse proxy):

```sh
git clone https://github.com/Audorix/KnockTwice.git
cd KnockTwice
# follow docs/deployment-docker.md to create docker/.env.production and the database password file
docker compose -f docker/compose.production.yaml --env-file docker/.env.production pull
docker compose -f docker/compose.production.yaml --env-file docker/.env.production up -d
```

Full guide: [Deploying with Docker](https://github.com/Audorix/KnockTwice/blob/master/docs/deployment-docker.md)

## About the image

- Listens on port `8080` and runs as a non-root user.
- Data (settings file, attachments, SQLite database) belongs in a volume at `/app/data`.
- The same image applies database migrations when started with the `migrate` argument.
- No secrets are built in: database, email and captcha settings come from the setup wizard or environment variables.

## Tags

- `1.1.0`: release 1.1.0
- `latest`: the newest release

Use the same version for the API and web images.

## Links

- Source and issues: https://github.com/Audorix/KnockTwice
- Changelog: https://github.com/Audorix/KnockTwice/blob/master/CHANGELOG.md
