FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY ["sampleapps/NativeAotAnnotations/NativeAotAnnotations.csproj", "sampleapps/NativeAotAnnotations/"]
COPY ["src/AWS.AgentCore/AWS.AgentCore.csproj", "src/AWS.AgentCore/"]
COPY ["src/AWS.AgentCore.SourceGenerator/AWS.AgentCore.SourceGenerator.csproj", "src/AWS.AgentCore.SourceGenerator/"]
RUN dotnet restore "sampleapps/NativeAotAnnotations/NativeAotAnnotations.csproj"
COPY . .
WORKDIR "/src/sampleapps/NativeAotAnnotations"
RUN dotnet publish "./NativeAotAnnotations.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["./NativeAotAnnotations"]
