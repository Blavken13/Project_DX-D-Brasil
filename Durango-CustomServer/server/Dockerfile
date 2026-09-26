# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore "./DurangoServer.csproj"

RUN dotnet publish "./DurangoServer.csproj" \
    -c Release \
    -o /out \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
    && rm -rf /var/lib/apt/lists/*

RUN useradd \
    --create-home \
    --uid 10001 \
    --shell /usr/sbin/nologin \
    durango

WORKDIR /app

COPY --from=build --chown=durango:durango /out/ ./

RUN mkdir -p /app/AppData-nx \
    && chown -R durango:durango /app/AppData-nx

USER durango

EXPOSE 8190/tcp
EXPOSE 8191/tcp

ENTRYPOINT ["dotnet", "DurangoServer.dll"]
