FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["src/SmartGateway.Admin/SmartGateway.Admin.csproj", "src/SmartGateway.Admin/"]
COPY ["src/SmartGateway.Core/SmartGateway.Core.csproj", "src/SmartGateway.Core/"]
RUN dotnet restore "src/SmartGateway.Admin/SmartGateway.Admin.csproj"
COPY . .
RUN dotnet publish "src/SmartGateway.Admin/SmartGateway.Admin.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartGateway.Admin.dll"]
