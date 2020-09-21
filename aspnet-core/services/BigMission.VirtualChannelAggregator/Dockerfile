#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["services/BigMission.VirtualChannelAggregator/BigMission.VirtualChannelAggregator.csproj", "services/BigMission.VirtualChannelAggregator/"]
COPY ["common/BigMission.CommandTools/BigMission.CommandTools.csproj", "common/BigMission.CommandTools/"]
COPY ["common/BigMission.DeviceApp.Shared/BigMission.DeviceApp.Shared.csproj", "common/BigMission.DeviceApp.Shared/"]
COPY ["services/BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "services/BigMission.ServiceStatusTools/"]
COPY ["src/BigMission.EntityFrameworkCore/BigMission.EntityFrameworkCore.csproj", "src/BigMission.EntityFrameworkCore/"]
COPY ["src/BigMission.Core/BigMission.Core.csproj", "src/BigMission.Core/"]
COPY ["src/BigMission.Core.Shared/BigMission.Core.Shared.csproj", "src/BigMission.Core.Shared/"]
RUN dotnet restore "services/BigMission.VirtualChannelAggregator/BigMission.VirtualChannelAggregator.csproj"
COPY . .
WORKDIR "/src/services/BigMission.VirtualChannelAggregator"
RUN dotnet build "BigMission.VirtualChannelAggregator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.VirtualChannelAggregator.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.VirtualChannelAggregator.dll"]