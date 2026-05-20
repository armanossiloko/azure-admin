# ── Stage 1: build Angular PWA ────────────────────────────────────────────────
FROM node:22-alpine AS ng-build
WORKDIR /ng

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# ── Stage 2: build .NET API ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src

COPY backend/AzureAdmin.Api/AzureAdmin.Api.csproj .
RUN dotnet restore AzureAdmin.Api.csproj

COPY backend/AzureAdmin.Api/ .
RUN dotnet publish AzureAdmin.Api.csproj -c Release -o /app/publish --no-restore

# ── Stage 3: runtime image ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=dotnet-build /app/publish .

# Angular build output becomes wwwroot; UseStaticFiles + MapFallbackToFile serve it.
COPY --from=ng-build /ng/dist/azure-admin/browser ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AzureAdmin.Api.dll"]
