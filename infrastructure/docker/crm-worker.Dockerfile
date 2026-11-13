# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config ./
COPY nuget-local nuget-local
COPY crm/src crm/src
RUN dotnet publish crm/src/Crm.Worker/Crm.Worker.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Crm.Worker.dll"]
