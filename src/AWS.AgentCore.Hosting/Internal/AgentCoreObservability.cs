// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// OpenTelemetry registration logic for AgentCore.
/// <para>
/// <see cref="EnrichTracing"/> and <see cref="EnrichMetrics"/> add AgentCore activity sources,
/// meters, and AWS instrumentation to a TracerProviderBuilder/MeterProviderBuilder. Used by both
/// the public <c>AddAgentCoreInstrumentation()</c> extensions (for users wiring their own OTel
/// pipeline) and the internal default-pipeline registration.
/// </para>
/// <para>
/// <see cref="RegisterDefaultPipeline"/> registers a turnkey OTel pipeline targeting the
/// AgentCore Runtime OTLP sidecar. Called from <c>AddAgentCore()</c> only when
/// <see cref="AgentCoreOptions.EnableObservability"/> is <c>true</c>.
/// </para>
/// </summary>
internal static class AgentCoreObservability
{
    internal const string ActivitySourceName = "AWS.AgentCore.Hosting";
    internal const string MeterName = "AWS.AgentCore.Hosting";

    // Default ActivitySource and Meter names used by Microsoft Agent Framework's OpenTelemetryAgent
    // when no explicit sourceName is provided to .UseOpenTelemetry().
    internal const string MsAgentFrameworkSource = "Experimental.Microsoft.Agents.AI";

    // Default ActivitySource and Meter names used by Microsoft.Extensions.AI's OpenTelemetryChatClient
    // when no explicit sourceName is provided to .UseOpenTelemetry().
    internal const string MsExtensionsAiSource = "Experimental.Microsoft.Extensions.AI";

    internal const string DefaultOtlpEndpoint = "http://localhost:4318";
    private const string OtlpEndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Adds the AgentCore activity sources and AWS SDK instrumentation to the
    /// <see cref="TracerProviderBuilder"/>.
    /// </summary>
    internal static void EnrichTracing(TracerProviderBuilder tracing)
    {
        tracing
            .AddSource(ActivitySourceName)
            .AddSource(MsAgentFrameworkSource)
            .AddSource(MsExtensionsAiSource)
            .AddAWSInstrumentation();
    }

    /// <summary>
    /// Adds the AgentCore meters to the <see cref="MeterProviderBuilder"/>.
    /// </summary>
    internal static void EnrichMetrics(MeterProviderBuilder metrics)
    {
        metrics
            .AddMeter(MeterName)
            .AddMeter(MsAgentFrameworkSource)
            .AddMeter(MsExtensionsAiSource);
    }

    /// <summary>
    /// Registers a default OpenTelemetry pipeline targeting the AgentCore Runtime OTLP sidecar
    /// (localhost:4318, HTTP/Protobuf) with ASP.NET Core, HttpClient, and AWS SDK instrumentation
    /// plus an OTLP exporter for traces, metrics, and logs.
    /// Honors all standard <c>OTEL_EXPORTER_OTLP_*</c> environment variables — when any is set,
    /// defers to the OTel SDK's native env-var resolution.
    /// </summary>
    internal static void RegisterDefaultPipeline(
        WebApplicationBuilder builder,
        AgentCoreOptions options)
    {
        var hasUserOtlpConfig = HasUserConfiguredOtlpEndpoint();

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(r => ConfigureResourceBuilder(r, options))
            .WithTracing(tracing =>
            {
                EnrichTracing(tracing);

                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                AddOtlpExporter(tracing, hasUserOtlpConfig);

                options.ConfigureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                EnrichMetrics(metrics);

                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                AddOtlpExporter(metrics, hasUserOtlpConfig);

                options.ConfigureMetrics?.Invoke(metrics);
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            var resourceBuilder = ResourceBuilder.CreateDefault();
            ConfigureResourceBuilder(resourceBuilder, options);
            logging.SetResourceBuilder(resourceBuilder);

            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;

            if (hasUserOtlpConfig)
                logging.AddOtlpExporter();
            else
                logging.AddOtlpExporter(ApplyDefaultOtlpExporterOptions);

            options.ConfigureLogging?.Invoke(logging);
        });
    }

    private static void AddOtlpExporter(TracerProviderBuilder tracing, bool hasUserOtlpConfig)
    {
        if (hasUserOtlpConfig)
            tracing.AddOtlpExporter();
        else
            tracing.AddOtlpExporter(ApplyDefaultOtlpExporterOptions);
    }

    private static void AddOtlpExporter(MeterProviderBuilder metrics, bool hasUserOtlpConfig)
    {
        if (hasUserOtlpConfig)
            metrics.AddOtlpExporter();
        else
            metrics.AddOtlpExporter((OtlpExporterOptions otlp, MetricReaderOptions _) => ApplyDefaultOtlpExporterOptions(otlp));
    }

    private static void ApplyDefaultOtlpExporterOptions(OtlpExporterOptions otlp)
    {
        otlp.Endpoint = new Uri(DefaultOtlpEndpoint);
        otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
    }

    internal static bool HasUserConfiguredOtlpEndpoint()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OtlpEndpointEnvVar))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"));
    }

    private static void ConfigureResourceBuilder(ResourceBuilder r, AgentCoreOptions options)
    {
        // service.name / service.version default to the entry assembly when not otherwise set.
        // The OTel SDK's CreateDefault() also reads OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES,
        // which take precedence over what we set here when the user provides them.
        var assembly = Assembly.GetEntryAssembly();
        var serviceName = assembly?.GetName().Name;
        var serviceVersion = assembly?.GetName().Version?.ToString();

        if (!string.IsNullOrEmpty(serviceName))
            r.AddService(serviceName, serviceVersion: serviceVersion);

        // Standard OTel semantic-convention cloud attributes from the AWS SDK env vars.
        // In production, AgentCore Runtime injects OTEL_RESOURCE_ATTRIBUTES with cloud.resource_id
        // (the agent runtime ARN), which the OTel SDK merges automatically via CreateDefault().
        // We only add cloud.region/cloud.provider here so they're populated during local dev when
        // OTEL_RESOURCE_ATTRIBUTES is not set.
        var region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        if (!string.IsNullOrWhiteSpace(region))
        {
            r.AddAttributes(new[]
            {
                new KeyValuePair<string, object>("cloud.provider", "aws"),
                new KeyValuePair<string, object>("cloud.region", region)
            });
        }

        // OpenTelemetry GenAI semantic-convention attribute. Default to "aws.bedrock" when the
        // user supplied a Bedrock ModelId. Users override by setting OTEL_RESOURCE_ATTRIBUTES,
        // which the OTel SDK merges and which takes precedence over programmatic attributes.
        if (!string.IsNullOrWhiteSpace(options.ModelId))
        {
            r.AddAttributes(new[]
            {
                new KeyValuePair<string, object>("gen_ai.provider.name", "aws.bedrock")
            });
        }
    }
}
