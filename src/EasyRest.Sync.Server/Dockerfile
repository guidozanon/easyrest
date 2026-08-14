# Imagen del servidor de sync. Se construye desde la raíz del repo:
#   docker build -f src/EasyRest.Sync.Server/Dockerfile -t easyrest-sync .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/EasyRest.Sync.Server/EasyRest.Sync.Server.csproj src/EasyRest.Sync.Server/
RUN dotnet restore src/EasyRest.Sync.Server/EasyRest.Sync.Server.csproj
COPY src/EasyRest.Sync.Server/ src/EasyRest.Sync.Server/
RUN dotnet publish src/EasyRest.Sync.Server/EasyRest.Sync.Server.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# La base y su carpeta son del usuario no root que corre la app
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# El server expone GET /health para que lo consulte el orquestador que uses (no se declara
# HEALTHCHECK acá porque la imagen de aspnet no trae curl ni wget).

ENTRYPOINT ["dotnet", "EasyRest.Sync.Server.dll"]
