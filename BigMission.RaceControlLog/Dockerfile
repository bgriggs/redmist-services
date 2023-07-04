#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["BigMission.RaceControlLog/BigMission.RaceControlLog.csproj", "BigMission.RaceControlLog/"]
COPY ["BigMission.Database/BigMission.Database.csproj", "BigMission.Database/"]
COPY ["BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "BigMission.ServiceStatusTools/"]
ARG FEED_ACCESSTOKEN
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/bigmission/_packaging/bmm-artifacts/nuget/v3/index.json\", \"username\":\"docker\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}"
RUN echo $VSS_NUGET_EXTERNAL_FEED_ENDPOINTS
RUN dotnet restore "BigMission.RaceControlLog/BigMission.RaceControlLog.csproj"
COPY . .
WORKDIR "/src/BigMission.RaceControlLog"
RUN dotnet build "BigMission.RaceControlLog.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.RaceControlLog.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.RaceControlLog.dll"]