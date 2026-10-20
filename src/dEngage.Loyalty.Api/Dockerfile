# Build context: the repo root (see .dockerignore), as for every dEngage service:
#   docker build -t dengage.loyalty.api -f src/dEngage.Loyalty.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble AS build

WORKDIR /app
COPY ["NuGet.Config", "."]
COPY . .

RUN dotnet restore "src/dEngage.Loyalty.Api/dEngage.Loyalty.Api.csproj"
RUN dotnet publish "src/dEngage.Loyalty.Api/dEngage.Loyalty.Api.csproj" -c Release -o out --no-restore

#---------------------------------------------------------------#

FROM dockerh.dengage.com/dengage-cdmp/dengage-aspnet-noble-9.0:master AS runtime
ARG user=dengageuser
ARG uid=1566

RUN useradd -u ${uid} -g 0 -s /bin/sh -l -M ${user}

# do update, remove curl
RUN apt-get -y remove --auto-remove curl \
    && apt-get update \
    && apt-get -y upgrade \
    && apt-get clean \
    && rm -rf /var/lib/{apt,dpkg,cache,log}/

RUN mkdir -p /dengage && chown -R ${user}:root /dengage

WORKDIR /app
COPY --chown=${user}:root --from=build /app/out ./

USER dengageuser

# Configuration is mounted by the deployment: CONFIG_PATH (appsettings*.json, including secrets),
# optionally NLOG_CONFIG_PATH, and LOYALTY_MASTER_KEY_FILE if any value uses ENC(...).
# Port: 8080 (ASPNETCORE_HTTP_PORTS from the ASP.NET base image) unless the mounted appsettings
# defines a "Kestrel" endpoint. The committed appsettings.json deliberately has none, because a
# Kestrel endpoint there would override the local launch profiles (http :5173, https :7142).
ENTRYPOINT ["dotnet", "/app/dEngage.Loyalty.Api.dll"]
