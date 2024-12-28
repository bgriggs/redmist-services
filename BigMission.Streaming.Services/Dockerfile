# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

#Download nuget credentials manager for Azure Artifacts access
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh

ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["services/nuget.config", "services/"]
COPY ["services/BigMission.Streaming.Services/BigMission.Streaming.Services.csproj", "services/BigMission.Streaming.Services/"]
COPY ["services/BigMission.Database.V2/BigMission.Database.V2.csproj", "services/BigMission.Database.V2/"]
COPY ["services/BigMission.Database/BigMission.Database.csproj", "services/BigMission.Database/"]
COPY ["services/BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "services/BigMission.ServiceStatusTools/"]
COPY ["services/BigMission.Streaming.Shared/BigMission.Streaming.Shared.csproj", "services/BigMission.Streaming.Shared/"]
RUN dotnet restore "./services/BigMission.Streaming.Services/BigMission.Streaming.Services.csproj"
COPY . .
WORKDIR "/src/services/BigMission.Streaming.Services"
RUN dotnet build "./BigMission.Streaming.Services.csproj" -c $BUILD_CONFIGURATION -o /app/build

#Use token passed in from the host to access Azure Artifacts
ARG FEED_ACCESSTOKEN
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/bigmission/_packaging/bmm-artifacts/nuget/v3/index.json\", \"username\":\"docker\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}"

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BigMission.Streaming.Services.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.Streaming.Services.dll"]