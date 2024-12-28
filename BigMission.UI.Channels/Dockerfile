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
COPY ["nuget.config", "."]
COPY ["BigMission.UI.Channels/BigMission.UI.Channels.csproj", "BigMission.UI.Channels/"]
COPY ["BigMission.Backend.Shared/BigMission.Backend.Shared.csproj", "BigMission.Backend.Shared/"]
COPY ["BigMission.Database.V2/BigMission.Database.V2.csproj", "BigMission.Database.V2/"]
COPY ["BigMission.Database/BigMission.Database.csproj", "BigMission.Database/"]
COPY ["BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "BigMission.ServiceStatusTools/"]
RUN dotnet restore "./BigMission.UI.Channels/BigMission.UI.Channels.csproj"
COPY . .
WORKDIR "/src/BigMission.UI.Channels"
RUN dotnet build "./BigMission.UI.Channels.csproj" -c $BUILD_CONFIGURATION -o /app/build

#Use token passed in from the host to access Azure Artifacts
ARG FEED_ACCESSTOKEN
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/bigmission/_packaging/bmm-artifacts/nuget/v3/index.json\", \"username\":\"docker\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}"

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BigMission.UI.Channels.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.UI.Channels.dll"]