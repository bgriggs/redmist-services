#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["BigMission.AlarmProcessor/BigMission.AlarmProcessor.csproj", "BigMission.AlarmProcessor/"]
COPY ["BigMission.Database/BigMission.Database.csproj", "BigMission.Database/"]
COPY ["BigMission.ServiceStatusTools/BigMission.ServiceStatusTools.csproj", "BigMission.ServiceStatusTools/"]
RUN dotnet restore "BigMission.AlarmProcessor/BigMission.AlarmProcessor.csproj"
COPY . .
WORKDIR "/src/BigMission.AlarmProcessor"
RUN dotnet build "BigMission.AlarmProcessor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BigMission.AlarmProcessor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BigMission.AlarmProcessor.dll"]