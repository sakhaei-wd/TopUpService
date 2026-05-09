# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY TopUpService.sln .
COPY src/TopUpService.Domain/TopUpService.Domain.csproj src/TopUpService.Domain/
COPY src/TopUpService.Application/TopUpService.Application.csproj src/TopUpService.Application/
COPY src/TopUpService.Infrastructure/TopUpService.Infrastructure.csproj src/TopUpService.Infrastructure/
COPY src/TopUpService.API/TopUpService.API.csproj src/TopUpService.API/

RUN dotnet restore src/TopUpService.API/TopUpService.API.csproj

COPY . .

RUN dotnet publish src/TopUpService.API/TopUpService.API.csproj \
    -c Release \
    -o /app/publish


# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

COPY --from=build /app/publish .

# create database directory
RUN mkdir -p /data && chmod 777 /data

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TopUpService.API.dll"]