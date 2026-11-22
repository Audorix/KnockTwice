# syntax=docker/dockerfile:1
# Audorix.ChatX API image. Build context: repository root.
#   docker build -f docker/api.Dockerfile -t audorix/chatx-api:dev .
# The same image serves HTTP (default) or applies migrations: `... audorix/chatx-api:dev migrate`.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so dependency layers are cached independently of source edits.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Core/Audorix.ChatX.Domain/Audorix.ChatX.Domain.csproj src/Core/Audorix.ChatX.Domain/
COPY src/Core/Audorix.ChatX.Application/Audorix.ChatX.Application.csproj src/Core/Audorix.ChatX.Application/
COPY src/Core/Audorix.ChatX.Contracts/Audorix.ChatX.Contracts.csproj src/Core/Audorix.ChatX.Contracts/
COPY src/Infrastructure/Audorix.ChatX.Infrastructure/Audorix.ChatX.Infrastructure.csproj src/Infrastructure/Audorix.ChatX.Infrastructure/
COPY src/Infrastructure/Audorix.ChatX.Persistence.Sqlite/Audorix.ChatX.Persistence.Sqlite.csproj src/Infrastructure/Audorix.ChatX.Persistence.Sqlite/
COPY src/Infrastructure/Audorix.ChatX.Persistence.PostgreSql/Audorix.ChatX.Persistence.PostgreSql.csproj src/Infrastructure/Audorix.ChatX.Persistence.PostgreSql/
COPY src/Api/Audorix.ChatX.Api/Audorix.ChatX.Api.csproj src/Api/Audorix.ChatX.Api/
RUN dotnet restore src/Api/Audorix.ChatX.Api/Audorix.ChatX.Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/Audorix.ChatX.Api/Audorix.ChatX.Api.csproj \
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
