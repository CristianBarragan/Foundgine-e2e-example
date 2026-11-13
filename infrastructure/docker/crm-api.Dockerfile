# Build context: repository root (needs nuget-local/ for the Foundgine packages).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config ./
COPY nuget-local nuget-local
COPY crm/src crm/src
RUN dotnet publish crm/src/Crm.Api/Crm.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Crm.Api.dll"]
