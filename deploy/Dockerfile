# syntax=docker/dockerfile:1
# Build from the repository root:  docker build -f deploy/Dockerfile -t coachdb-api .

# ---- 0. Extract only *.csproj files (keeping folders) so new projects need no Dockerfile change ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS projects
WORKDIR /src-all
COPY src/ ./
RUN mkdir -p /projects/src && find . -name '*.csproj' -exec cp --parents {} /projects/src/ \;

# ---- 1. Restore (cached until a project file or package version changes) ----
# COPY --from is cached by content, so editing a .cs file does not re-run restore.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props CoachDB.slnx ./
COPY --from=projects /projects/ ./
RUN dotnet restore src/Host/CoachDB.Api/CoachDB.Api.csproj

# ---- 2. Build + publish ----
FROM restore AS publish
COPY .editorconfig ./
COPY src/ src/
RUN dotnet publish src/Host/CoachDB.Api/CoachDB.Api.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- 3. Runtime: chiseled image, no shell, runs as non-root ($APP_UID) ----
# -extra adds ICU and tzdata: tenant time zones (Africa/Cairo) and culture-aware text need them.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
USER $APP_UID
COPY --from=publish /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "CoachDB.Api.dll"]
