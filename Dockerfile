# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution + project files first (layer cache for NuGet restore)
COPY LlmModelProxy.sln ./
COPY src/LlmModelProxy.Core/LlmModelProxy.Core.csproj                       src/LlmModelProxy.Core/
COPY src/LlmModelProxy.Providers/LlmModelProxy.Providers.csproj             src/LlmModelProxy.Providers/
COPY src/LlmModelProxy.Infrastructure/LlmModelProxy.Infrastructure.csproj   src/LlmModelProxy.Infrastructure/
COPY src/LlmModelProxy.Api/LlmModelProxy.Api.csproj                         src/LlmModelProxy.Api/
COPY tests/LlmModelProxy.Unit.Tests/LlmModelProxy.Unit.Tests.csproj         tests/LlmModelProxy.Unit.Tests/
COPY tests/LlmModelProxy.Integration.Tests/LlmModelProxy.Integration.Tests.csproj  tests/LlmModelProxy.Integration.Tests/

RUN dotnet restore

# Copy everything else and publish
COPY . .
RUN dotnet publish src/LlmModelProxy.Api/LlmModelProxy.Api.csproj \
    -c Release -o /app/publish --no-restore

# ── Stage 2: runtime ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user for security (Debian slim: groupadd/useradd, not addgroup/adduser)
RUN groupadd --system appgroup && useradd --system --gid appgroup --no-create-home --shell /bin/false appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8766
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8766

ENTRYPOINT ["dotnet", "LlmModelProxy.Api.dll"]
