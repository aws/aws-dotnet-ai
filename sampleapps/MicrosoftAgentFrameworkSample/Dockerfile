FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["sampleapps/MicrosoftAgentFrameworkSample/MicrosoftAgentFrameworkSample.csproj", "sampleapps/MicrosoftAgentFrameworkSample/"]
COPY ["src/AWS.AgentCore/AWS.AgentCore.csproj", "src/AWS.AgentCore/"]
COPY ["src/AWS.AgentCore.SourceGenerator/AWS.AgentCore.SourceGenerator.csproj", "src/AWS.AgentCore.SourceGenerator/"]
RUN dotnet restore "sampleapps/MicrosoftAgentFrameworkSample/MicrosoftAgentFrameworkSample.csproj"
COPY . .
WORKDIR "/src/sampleapps/MicrosoftAgentFrameworkSample"
RUN dotnet build "./MicrosoftAgentFrameworkSample.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MicrosoftAgentFrameworkSample.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MicrosoftAgentFrameworkSample.dll"]

