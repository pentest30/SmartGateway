FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["src/SmartGateway.Host/SmartGateway.Host.csproj", "src/SmartGateway.Host/"]
COPY ["src/SmartGateway.Core/SmartGateway.Core.csproj", "src/SmartGateway.Core/"]
COPY ["src/SmartGateway.Resilience/SmartGateway.Resilience.csproj", "src/SmartGateway.Resilience/"]
RUN dotnet restore "src/SmartGateway.Host/SmartGateway.Host.csproj"
COPY . .
RUN dotnet publish "src/SmartGateway.Host/SmartGateway.Host.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartGateway.Host.dll"]
