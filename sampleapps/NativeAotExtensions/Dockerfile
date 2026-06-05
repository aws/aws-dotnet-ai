FROM mcr.microsoft.com/dotnet/sdk:10.0-aot AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["sampleapps/NativeAotExtensions/NativeAotExtensions.csproj", "sampleapps/NativeAotExtensions/"]
COPY ["src/AWS.AgentCore.Hosting/AWS.AgentCore.Hosting.csproj", "src/AWS.AgentCore.Hosting/"]
COPY ["src/AWS.AgentCore.SourceGenerator/AWS.AgentCore.SourceGenerator.csproj", "src/AWS.AgentCore.SourceGenerator/"]
RUN dotnet restore "sampleapps/NativeAotExtensions/NativeAotExtensions.csproj"
COPY . .
WORKDIR "/src/sampleapps/NativeAotExtensions"
RUN dotnet publish "./NativeAotExtensions.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["./NativeAotExtensions"]
