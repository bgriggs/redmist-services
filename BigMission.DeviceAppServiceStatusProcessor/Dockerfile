#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["BigMission.DeviceAppServiceStatusProcessor/BigMission.DeviceAppServiceStatusProcessor.csproj", "BigMission.DeviceAppServiceStatusProcessor/"]
COPY ["BigMission.Database/BigMission.Database.csproj", "BigMission.Database/"]
COPY ["BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "BigMission.ServiceStatusTools/"]
RUN dotnet restore "BigMission.DeviceAppServiceStatusProcessor/BigMission.DeviceAppServiceStatusProcessor.csproj"
COPY . .
WORKDIR "/src/BigMission.DeviceAppServiceStatusProcessor"
RUN dotnet build "BigMission.DeviceAppServiceStatusProcessor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.DeviceAppServiceStatusProcessor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.DeviceAppServiceStatusProcessor.dll"]