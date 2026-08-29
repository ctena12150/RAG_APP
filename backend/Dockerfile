# Imagen del API .NET (RAG.Api + RAG.Domain + RAG.Infrastructure)
# Build: docker build -t ragapp/backend:local .
# Nota: el contexto es backend/ (ver deploy/docker-compose.yml); tests/ quedan fuera.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore cacheable: primero solo los csproj
COPY src/RAG.Domain/RAG.Domain.csproj src/RAG.Domain/
COPY src/RAG.Infrastructure/RAG.Infrastructure.csproj src/RAG.Infrastructure/
COPY src/RAG.Api/RAG.Api.csproj src/RAG.Api/
RUN dotnet restore src/RAG.Api/RAG.Api.csproj

COPY src/ src/
RUN dotnet publish src/RAG.Api/RAG.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl solo para el healthcheck del compose (la imagen aspnet no lo trae)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# la configuración llega por entorno (deploy/docker-compose.yml), nunca en la imagen
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

# usuario no-root que trae la imagen oficial de .NET
USER $APP_UID

ENTRYPOINT ["dotnet", "RAG.Api.dll"]
