// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using Amazon;
using Amazon.Runtime;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Configures the CloudWatch audit sink: where governance events are written and whether metrics are
    /// emitted. Log delivery (batching, retries, log group/stream creation) is handled by AWS.Logger.Core.
    /// </summary>
    public sealed class CloudWatchAuditOptions
    {
        /// <summary>The CloudWatch Logs log group governance events are written to.</summary>
        public string LogGroupName { get; set; } = "/agent-governance/audit";

        /// <summary>The log stream name. Null lets AWS.Logger.Core generate one.</summary>
        public string? LogStreamName { get; set; }

        /// <summary>The CloudWatch metric namespace for governance metrics.</summary>
        public string MetricNamespace { get; set; } = "AgentGovernance/Bedrock";

        /// <summary>When true, aggregated governance counters are published to CloudWatch metrics.</summary>
        public bool EmitMetrics { get; set; } = true;

        /// <summary>How often logs are pushed (AWS.Logger.Core batch interval) and metrics are flushed.</summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>The region for the CloudWatch clients. Null uses the default credential/region chain.</summary>
        public RegionEndpoint? Region { get; set; }

        /// <summary>
        /// Explicit credentials for the CloudWatch Logs and metrics clients. Null (default) falls back to the
        /// default credential chain (environment, profile, EC2/ECS/Lambda role, etc.).
        /// </summary>
        public AWSCredentials? Credentials { get; set; }
    }
}
