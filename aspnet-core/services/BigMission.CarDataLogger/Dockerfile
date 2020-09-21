#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["services/BigMission.CarDataLogger/BigMission.CarDataLogger.csproj", "services/BigMission.CarDataLogger/"]
COPY ["src/BigMission.EntityFrameworkCore/BigMission.EntityFrameworkCore.csproj", "src/BigMission.EntityFrameworkCore/"]
COPY ["src/BigMission.Core/BigMission.Core.csproj", "src/BigMission.Core/"]
COPY ["src/BigMission.Core.Shared/BigMission.Core.Shared.csproj", "src/BigMission.Core.Shared/"]
COPY ["services/BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "services/BigMission.ServiceStatusTools/"]
RUN dotnet restore "services/BigMission.CarDataLogger/BigMission.CarDataLogger.csproj"
COPY . .
WORKDIR "/src/services/BigMission.CarDataLogger"
RUN dotnet build "BigMission.CarDataLogger.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.CarDataLogger.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.CarDataLogger.dll"]