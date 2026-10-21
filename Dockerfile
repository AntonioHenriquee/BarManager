FROM node:24-alpine AS frontend
WORKDIR /src
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/Directory.Build.props backend/
COPY backend/MeuBar.Api/MeuBar.Api.csproj backend/MeuBar.Api/packages.lock.json backend/MeuBar.Api/
RUN dotnet restore backend/MeuBar.Api --locked-mode
COPY backend/MeuBar.Api/ backend/MeuBar.Api/
RUN dotnet publish backend/MeuBar.Api -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /out ./
COPY --from=frontend /src/dist/meu-bar/browser ./wwwroot/
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MeuBar.Api.dll"]
