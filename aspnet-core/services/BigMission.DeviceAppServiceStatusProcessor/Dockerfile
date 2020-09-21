#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["services/BigMission.DeviceAppServiceStatusProcessor/BigMission.DeviceAppServiceStatusProcessor.csproj", "services/BigMission.DeviceAppServiceStatusProcessor/"]
COPY ["src/BigMission.EntityFrameworkCore/BigMission.EntityFrameworkCore.csproj", "src/BigMission.EntityFrameworkCore/"]
COPY ["src/BigMission.Core/BigMission.Core.csproj", "src/BigMission.Core/"]
COPY ["src/BigMission.Core.Shared/BigMission.Core.Shared.csproj", "src/BigMission.Core.Shared/"]
COPY ["common/BigMission.CommandTools/BigMission.CommandTools.csproj", "common/BigMission.CommandTools/"]
COPY ["common/BigMission.DeviceApp.Shared/BigMission.DeviceApp.Shared.csproj", "common/BigMission.DeviceApp.Shared/"]
COPY ["services/BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "services/BigMission.ServiceStatusTools/"]
RUN dotnet restore "services/BigMission.DeviceAppServiceStatusProcessor/BigMission.DeviceAppServiceStatusProcessor.csproj"
COPY . .
WORKDIR "/src/services/BigMission.DeviceAppServiceStatusProcessor"
RUN dotnet build "BigMission.DeviceAppServiceStatusProcessor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.DeviceAppServiceStatusProcessor.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.DeviceAppServiceStatusProcessor.dll"]