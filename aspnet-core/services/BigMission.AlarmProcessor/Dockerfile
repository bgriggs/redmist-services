#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["services/BigMission.AlarmProcessor/BigMission.AlarmProcessor.csproj", "services/BigMission.AlarmProcessor/"]
COPY ["common/BigMission.Database/BigMission.Database.csproj", "common/BigMission.Database/"]
COPY ["common/BigMission.DeviceApp.Shared/BigMission.DeviceApp.Shared.csproj", "common/BigMission.DeviceApp.Shared/"]
COPY ["common/BigMission.TestHelpers/BigMission.TestHelpers.csproj", "common/BigMission.TestHelpers/"]
COPY ["common/NLog.Targets.Redis/NLog.Targets.Redis/NLog.Targets.Redis.csproj", "common/NLog.Targets.Redis/NLog.Targets.Redis/"]
COPY ["services/BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "services/BigMission.ServiceStatusTools/"]
COPY ["common/BigMission.Cache.Models/BigMission.Cache.Models.csproj", "common/BigMission.Cache.Models/"]
RUN dotnet restore "services/BigMission.AlarmProcessor/BigMission.AlarmProcessor.csproj"
COPY . .
WORKDIR "/src/services/BigMission.AlarmProcessor"
RUN dotnet build "BigMission.AlarmProcessor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.AlarmProcessor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.AlarmProcessor.dll"]