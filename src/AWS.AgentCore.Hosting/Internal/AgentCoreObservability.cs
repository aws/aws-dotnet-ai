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
/// Encapsulates all OpenTelemetry registration logic for AgentCore.
/// Called from AddAgentCore() to configure tracing, metrics, and logging
/// with OTLP export to the AgentCore Runtime sidecar.
/// </summary>
internal static class AgentCoreObservability
{
    internal const string ActivitySourceName = "AWS.AgentCore.Hosting";
    internal const string MeterName = "AWS.AgentCore.Hosting";

    // Default ActivitySource and Meter names used by Microsoft Agent Framework's OpenTelemetryAgent
    // when no explicit sourceName is provided to .UseOpenTelemetry(). The "Experimental" prefix is
    // intentional: the MS AF team's telemetry schema may evolve.
    internal const string MsAgentFrameworkSource = "Experimental.Microsoft.Agents.AI";

    // Default ActivitySource and Meter names used by Microsoft.Extensions.AI's OpenTelemetryChatClient
    // when no explicit sourceName is provided to .UseOpenTelemetry(). Different from the agent default
    // because they are separate components — wrapping IChatClient directly emits under MEAI's source.
    internal const string MsExtensionsAiSource = "Experimental.Microsoft.Extensions.AI";

    internal const string DefaultOtlpEndpoint = "http://localhost:4318";
    private const string OtlpEndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";

    internal static void ConfigureOpenTelemetry(
        WebApplicationBuilder builder,
        AgentCoreOptions options)
    {
        if (options.DisableObservability)
            return;

        // OTLP exporter configuration:
        // - When the user has configured ANY OTEL_EXPORTER_OTLP_*_ENDPOINT env var, call
        //   AddOtlpExporter() with no args so the SDK resolves all OTLP-related env vars
        //   naturally (per-signal endpoints, protocol, headers, compression, timeout).
        // - Otherwise, default to the AgentCore Runtime sidecar at http://localhost:4318
        //   over HTTP/Protobuf.
        var hasUserOtlpConfig = HasUserConfiguredOtlpEndpoint();

        // Tracing and Metrics
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(ConfigureResourceBuilder)
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ActivitySourceName)
                    .AddSource(MsAgentFrameworkSource)
                    .AddSource(MsExtensionsAiSource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAWSInstrumentation();

                AddOtlpExporter(tracing, hasUserOtlpConfig);

                options.ConfigureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MeterName)
                    .AddMeter(MsAgentFrameworkSource)
                    .AddMeter(MsExtensionsAiSource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                AddOtlpExporter(metrics, hasUserOtlpConfig);

                options.ConfigureMetrics?.Invoke(metrics);
            });

        // Logging
        builder.Logging.AddOpenTelemetry(logging =>
        {
            var resourceBuilder = ResourceBuilder.CreateDefault();
            ConfigureResourceBuilder(resourceBuilder);
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

    private static void ConfigureResourceBuilder(ResourceBuilder r)
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
    }
}
