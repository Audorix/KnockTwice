# syntax=docker/dockerfile:1
# Knock Twice API image. Build context: repository root.
#   docker build -f docker/api.Dockerfile -t ikemyle/knocktwice-api:dev .
# The same image serves HTTP (default) or applies migrations: `... ikemyle/knocktwice-api:dev migrate`.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers are cached independently of source edits.
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props ./
COPY src/Core/Audorix.KnockTwice.Domain/Audorix.KnockTwice.Domain.csproj src/Core/Audorix.KnockTwice.Domain/
COPY src/Core/Audorix.KnockTwice.Application/Audorix.KnockTwice.Application.csproj src/Core/Audorix.KnockTwice.Application/
COPY src/Core/Audorix.KnockTwice.Contracts/Audorix.KnockTwice.Contracts.csproj src/Core/Audorix.KnockTwice.Contracts/
COPY src/Infrastructure/Audorix.KnockTwice.Infrastructure/Audorix.KnockTwice.Infrastructure.csproj src/Infrastructure/Audorix.KnockTwice.Infrastructure/
COPY src/Infrastructure/Audorix.KnockTwice.Persistence.Sqlite/Audorix.KnockTwice.Persistence.Sqlite.csproj src/Infrastructure/Audorix.KnockTwice.Persistence.Sqlite/
COPY src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql/Audorix.KnockTwice.Persistence.PostgreSql.csproj src/Infrastructure/Audorix.KnockTwice.Persistence.PostgreSql/
COPY src/Api/Audorix.KnockTwice.Api/Audorix.KnockTwice.Api.csproj src/Api/Audorix.KnockTwice.Api/
RUN dotnet restore src/Api/Audorix.KnockTwice.Api/Audorix.KnockTwice.Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/Audorix.KnockTwice.Api/Audorix.KnockTwice.Api.csproj \
    --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# /app/data holds the SQLite database in the SQLite profile; mount a volume there.
RUN mkdir -p /app/data && chown "$APP_UID" /app/data

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Audorix.ChatX.Api.dll"]
