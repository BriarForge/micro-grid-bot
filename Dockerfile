# syntax=docker/dockerfile:1.7
# multi-stage build for the MicroGrid.Bot worker. Pin by digest in CI.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for better layer caching
COPY MicroGrid.sln Directory.Build.props ./
COPY src/MicroGrid.Domain/MicroGrid.Domain.csproj           src/MicroGrid.Domain/
COPY src/MicroGrid.Application/MicroGrid.Application.csproj src/MicroGrid.Application/
COPY src/MicroGrid.Bot/MicroGrid.Bot.csproj                 src/MicroGrid.Bot/
COPY tests/MicroGrid.Domain.Tests/MicroGrid.Domain.Tests.csproj tests/MicroGrid.Domain.Tests/
RUN dotnet restore MicroGrid.sln

COPY src/   src/
COPY tests/ tests/
RUN dotnet publish src/MicroGrid.Bot/MicroGrid.Bot.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained false \
    /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Run as non-root. UID 10001 is the .NET images' default non-root convention.
RUN groupadd --system --gid 10001 microgrid \
    && useradd  --system --uid 10001 --gid microgrid --home /app --shell /sbin/nologin microgrid
USER 10001:10001

# State on a mounted volume. Path can be overridden via MICROGRID_STATE_DIR.
ENV MICROGRID_STATE_DIR=/data
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_EnableDiagnostics=0
ENV MICROGRID_URL=http://0.0.0.0:8080
EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "MicroGrid.Bot.dll"]
