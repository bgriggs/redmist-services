#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

#Download nuget credentials manager for Azure Artifacts access
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh

WORKDIR /src
COPY ["nuget.config", "."]
COPY ["BigMission.UI.Channels/BigMission.UI.Channels.csproj", "BigMission.UI.Channels/"]
COPY ["BigMission.Database/BigMission.Database.csproj", "BigMission.Database/"]
COPY ["BigMission.Database.V2/BigMission.Database.V2.csproj", "BigMission.Database.V2/"]
COPY ["BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "BigMission.ServiceStatusTools/"]
COPY ["BigMission.Backend.Shared/BigMission.Backend.Shared.csproj", "BigMission.Backend.Shared/"]

#Use token passed in from the host to access Azure Artifacts
ARG FEED_ACCESSTOKEN
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/bigmission/_packaging/bmm-artifacts/nuget/v3/index.json\", \"username\":\"docker\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}"

RUN dotnet restore "BigMission.UI.Channels/BigMission.UI.Channels.csproj"
COPY . .
WORKDIR "/src/BigMission.UI.Channels"
RUN dotnet build "BigMission.UI.Channels.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.UI.Channels.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.UI.Channels.dll"]