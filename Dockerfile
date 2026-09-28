# Single image: ASP.NET Core API that also serves the Angular SPA from wwwroot/.
# Build from repo root:
#   docker build -t azure-admin .

# --- Angular SPA -------------------------------------------------------------
FROM node:24-alpine AS frontend
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY angular.json tsconfig.json tsconfig.app.json ngsw-config.json ./
COPY src ./src
COPY public ./public
RUN npm run build

# --- .NET API ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src

COPY src-backend/AzureAdmin.API/AzureAdmin.API.csproj .
RUN dotnet restore AzureAdmin.API.csproj

COPY src-backend/AzureAdmin.API/ .
# Set by the release workflow (semantic-release); baked into the assembly version.
ARG VERSION=0.0.0-dev
RUN dotnet publish AzureAdmin.API.csproj -c Release -o /app/publish --no-restore -p:Version=${VERSION}

# --- Runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=backend /app/publish .
COPY --from=frontend /app/dist/azure-admin/browser ./wwwroot

# The aspnet base image already sets ASPNETCORE_HTTP_PORTS=8080.
EXPOSE 8080

# Non-root user (UID 1654) is built into the aspnet images.
USER $APP_UID

ENTRYPOINT ["dotnet", "AzureAdmin.API.dll"]
