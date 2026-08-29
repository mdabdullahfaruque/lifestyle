# Build from the repository root:
#   docker build -f deploy/api.Dockerfile -t lifestyle-api:$TAG .

# ─────────────────────────── build ───────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy the manifests first so `restore` is cached and only re-runs when a dependency changes.
COPY Directory.Build.props Directory.Packages.props global.json Lifestyle.slnx ./
COPY src/Lifestyle.Api/*.csproj                        src/Lifestyle.Api/
COPY src/Lifestyle.SharedKernel/*.csproj               src/Lifestyle.SharedKernel/
COPY src/Lifestyle.Infrastructure/*.csproj             src/Lifestyle.Infrastructure/
COPY src/Modules/Lifestyle.Modules.Identity/*.csproj   src/Modules/Lifestyle.Modules.Identity/
COPY src/Modules/Lifestyle.Modules.Platform/*.csproj   src/Modules/Lifestyle.Modules.Platform/
COPY src/Modules/Lifestyle.Modules.Vendors/*.csproj    src/Modules/Lifestyle.Modules.Vendors/
COPY src/Modules/Lifestyle.Modules.Catalog/*.csproj    src/Modules/Lifestyle.Modules.Catalog/
COPY src/Modules/Lifestyle.Modules.Media/*.csproj      src/Modules/Lifestyle.Modules.Media/

RUN dotnet restore src/Lifestyle.Api/Lifestyle.Api.csproj

COPY src/ src/
RUN dotnet publish src/Lifestyle.Api/Lifestyle.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

# ─────────────────────────── runtime ───────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# ICU is needed for the invariant-culture formatting the money and slug code relies on, and for
# citext/collation-sensitive behaviour to match the database. Do not switch on InvariantGlobalization.
RUN apk add --no-cache icu-libs curl \
    && adduser --system --uid 64198 --disabled-password lifestyle \
    && mkdir -p /app/.storage && chown -R 64198 /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build --chown=64198 /app ./

USER 64198
EXPOSE 8080

# The reverse proxy gates on this before routing traffic to a new container.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=4 \
    CMD curl -fsS http://localhost:8080/v1/internal/health || exit 1

ENTRYPOINT ["dotnet", "Lifestyle.Api.dll"]
