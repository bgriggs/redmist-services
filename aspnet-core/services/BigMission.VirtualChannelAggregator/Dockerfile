#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["services/BigMission.VirtualChannelAggregator/BigMission.VirtualChannelAggregator.csproj", "services/BigMission.VirtualChannelAggregator/"]
COPY ["common/BigMission.CommandTools/BigMission.CommandTools.csproj", "common/BigMission.CommandTools/"]
COPY ["common/BigMission.DeviceApp.Shared/BigMission.DeviceApp.Shared.csproj", "common/BigMission.DeviceApp.Shared/"]
COPY ["common/BigMission.TestHelpers/BigMission.TestHelpers.csproj", "common/BigMission.TestHelpers/"]
COPY ["common/BigMission.Database/BigMission.Database.csproj", "common/BigMission.Database/"]
COPY ["common/NLog.Targets.Redis/NLog.Targets.Redis/NLog.Targets.Redis.csproj", "common/NLog.Targets.Redis/NLog.Targets.Redis/"]
COPY ["services/BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "services/BigMission.ServiceStatusTools/"]
COPY ["common/BigMission.Cache.Models/BigMission.Cache.Models.csproj", "common/BigMission.Cache.Models/"]
RUN dotnet restore "services/BigMission.VirtualChannelAggregator/BigMission.VirtualChannelAggregator.csproj"
COPY . .
WORKDIR "/src/services/BigMission.VirtualChannelAggregator"
RUN dotnet build "BigMission.VirtualChannelAggregator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.VirtualChannelAggregator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.VirtualChannelAggregator.dll"]