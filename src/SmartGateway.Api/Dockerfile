FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["src/SmartGateway.Api/SmartGateway.Api.csproj", "src/SmartGateway.Api/"]
COPY ["src/SmartGateway.Core/SmartGateway.Core.csproj", "src/SmartGateway.Core/"]
RUN dotnet restore "src/SmartGateway.Api/SmartGateway.Api.csproj"
COPY . .
RUN dotnet publish "src/SmartGateway.Api/SmartGateway.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartGateway.Api.dll"]
