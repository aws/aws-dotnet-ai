FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["sampleapps/AnnotationsStreamingAgent/AnnotationsStreamingAgent.csproj", "sampleapps/AnnotationsStreamingAgent/"]
COPY ["src/AWS.AgentCore.Hosting/AWS.AgentCore.Hosting.csproj", "src/AWS.AgentCore.Hosting/"]
COPY ["src/AWS.AgentCore.SourceGenerator/AWS.AgentCore.SourceGenerator.csproj", "src/AWS.AgentCore.SourceGenerator/"]
RUN dotnet restore "sampleapps/AnnotationsStreamingAgent/AnnotationsStreamingAgent.csproj"
COPY . .
WORKDIR "/src/sampleapps/AnnotationsStreamingAgent"
RUN dotnet build "./AnnotationsStreamingAgent.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AnnotationsStreamingAgent.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AnnotationsStreamingAgent.dll"]

