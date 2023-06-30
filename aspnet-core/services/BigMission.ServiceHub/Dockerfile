#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["services/BigMission.ServiceHub/BigMission.ServiceHub.csproj", "services/BigMission.ServiceHub/"]
COPY ["common/BigMission.Cache.Models/BigMission.Cache.Models.csproj", "common/BigMission.Cache.Models/"]
COPY ["common/BigMission.CommandTools/BigMission.CommandTools.csproj", "common/BigMission.CommandTools/"]
COPY ["common/BigMission.DeviceApp.Shared/BigMission.DeviceApp.Shared.csproj", "common/BigMission.DeviceApp.Shared/"]
COPY ["common/BigMission.TestHelpers/BigMission.TestHelpers.csproj", "common/BigMission.TestHelpers/"]
COPY ["common/BigMission.Database/BigMission.Database.csproj", "common/BigMission.Database/"]
COPY ["common/NLog.Targets.ServiceHub/NLog.Targets.ServiceHub.csproj", "common/NLog.Targets.ServiceHub/"]
RUN dotnet restore "services/BigMission.ServiceHub/BigMission.ServiceHub.csproj"
COPY . .
WORKDIR "/src/services/BigMission.ServiceHub"
RUN dotnet build "BigMission.ServiceHub.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.ServiceHub.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.ServiceHub.dll"]