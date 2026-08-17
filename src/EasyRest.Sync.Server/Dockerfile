# Imagen del servidor de sync. Se construye desde la raíz del repo:
#   docker build -f src/EasyRest.Sync.Server/Dockerfile -t easyrest-sync .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# primero sólo los csproj, para que el restore quede cacheado si no cambian las dependencias
COPY src/EasyRest.Sync.Server/EasyRest.Sync.Server.csproj src/EasyRest.Sync.Server/
COPY src/EasyRest.Sync.Data/EasyRest.Sync.Data.csproj src/EasyRest.Sync.Data/
COPY src/EasyRest.Sync.Server.Migrations.Sqlite/EasyRest.Sync.Server.Migrations.Sqlite.csproj \
     src/EasyRest.Sync.Server.Migrations.Sqlite/
COPY src/EasyRest.Sync.Server.Migrations.Postgres/EasyRest.Sync.Server.Migrations.Postgres.csproj \
     src/EasyRest.Sync.Server.Migrations.Postgres/
RUN dotnet restore src/EasyRest.Sync.Server/EasyRest.Sync.Server.csproj

COPY src/EasyRest.Sync.Server/ src/EasyRest.Sync.Server/
COPY src/EasyRest.Sync.Data/ src/EasyRest.Sync.Data/
COPY src/EasyRest.Sync.Server.Migrations.Sqlite/ src/EasyRest.Sync.Server.Migrations.Sqlite/
COPY src/EasyRest.Sync.Server.Migrations.Postgres/ src/EasyRest.Sync.Server.Migrations.Postgres/
RUN dotnet publish src/EasyRest.Sync.Server/EasyRest.Sync.Server.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# La base y su carpeta son del usuario no root que corre la app. El montaje de /data lo declara
# quien corre la imagen —el compose, el volumen de Railway, el -v de docker run—: `VOLUME` acá no
# agregaría nada (salvo volúmenes anónimos si nadie monta) y hay plataformas que rechazan la
# imagen por tenerlo.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# El server expone GET /health para que lo consulte el orquestador que uses (no se declara
# HEALTHCHECK acá porque la imagen de aspnet no trae curl ni wget).

ENTRYPOINT ["dotnet", "EasyRest.Sync.Server.dll"]
