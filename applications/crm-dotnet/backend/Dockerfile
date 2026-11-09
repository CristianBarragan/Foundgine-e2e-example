# Set ENABLE_NATIVE_AOT=true to publish a self-contained Linux Native AOT executable.
ARG ENABLE_NATIVE_AOT=false
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG ENABLE_NATIVE_AOT
WORKDIR /src
COPY . .
RUN dotnet restore
RUN if [ "$ENABLE_NATIVE_AOT" = "true" ]; then \
      dotnet publish -c Release -r linux-x64 -p:EnableNativeAot=true -p:SelfContained=true -o /app/publish; \
    else \
      dotnet publish -c Release -o /app/publish; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS jit
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Foundgine.Crm.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS aot
WORKDIR /app
COPY --from=build /app/publish/Foundgine.Crm.Api ./Foundgine.Crm.Api
EXPOSE 8080
ENTRYPOINT ["./Foundgine.Crm.Api"]
