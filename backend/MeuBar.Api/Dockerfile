FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/MeuBar.Api/MeuBar.Api.csproj backend/MeuBar.Api/
RUN dotnet restore backend/MeuBar.Api
COPY backend/MeuBar.Api backend/MeuBar.Api
RUN dotnet publish backend/MeuBar.Api -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet","MeuBar.Api.dll"]
