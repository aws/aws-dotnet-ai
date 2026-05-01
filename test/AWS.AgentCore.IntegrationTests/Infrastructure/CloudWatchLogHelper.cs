// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;

namespace AWS.AgentCore.IntegrationTests.Infrastructure;

/// <summary>
/// Fetches CloudWatch logs for AgentCore runtimes to help diagnose 500 errors.
/// Writes to stderr so output is visible in CI runners that capture stdout per-test.
/// </summary>
public static class CloudWatchLogHelper
{
    private static readonly List<string> _logBuffer = new();

    private static void Log(string message)
    {
        var line = $"[CloudWatch] {message}";
        lock (_logBuffer)
            _logBuffer.Add(line);
        Console.Error.WriteLine(line);
    }

    /// <summary>Returns all captured log lines and clears the buffer.</summary>
    public static string FlushLogBuffer()
    {
        lock (_logBuffer)
        {
            var result = string.Join(Environment.NewLine, _logBuffer);
            _logBuffer.Clear();
            return result;
        }
    }

    /// <summary>
    /// Fetches recent log events for an AgentCore runtime and writes them to stderr.
    /// </summary>
    public static async Task DumpRuntimeLogsAsync(string runtimeArn, string region, CancellationToken ct = default)
    {
        var runtimeId = runtimeArn.Split('/').Last();
        var logGroupName = $"/aws/bedrock-agentcore/runtimes/{runtimeId}-DEFAULT";

        Log($"Attempting to fetch logs for runtime {runtimeId}");
        Log($"Log group: {logGroupName}");
        Log($"Region: {region}");

        using var client = new AmazonCloudWatchLogsClient(RegionEndpoint.GetBySystemName(region));

        // First, check if the log group exists
        try
        {
            var describeResponse = await client.DescribeLogGroupsAsync(new DescribeLogGroupsRequest
            {
                LogGroupNamePrefix = logGroupName,
                Limit = 5,
            }, ct);

            Log($"DescribeLogGroups returned {describeResponse.LogGroups.Count} group(s):");
            foreach (var group in describeResponse.LogGroups)
            {
                Log($"  - {group.LogGroupName} (stored bytes: {group.StoredBytes})");
            }

            if (describeResponse.LogGroups.Count == 0)
            {
                Log($"Log group {logGroupName} does not exist. Trying broader search...");

                var broadSearch = await client.DescribeLogGroupsAsync(new DescribeLogGroupsRequest
                {
                    LogGroupNamePrefix = "/aws/bedrock-agentcore/",
                    Limit = 20,
                }, ct);

                Log($"Broad search (/aws/bedrock-agentcore/) returned {broadSearch.LogGroups.Count} group(s):");
                foreach (var group in broadSearch.LogGroups)
                {
                    Log($"  - {group.LogGroupName}");
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"Error describing log groups: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // List log streams in the group
        try
        {
            var streamsResponse = await client.DescribeLogStreamsAsync(new DescribeLogStreamsRequest
            {
                LogGroupName = logGroupName,
                OrderBy = OrderBy.LastEventTime,
                Descending = true,
                Limit = 10,
            }, ct);

            Log($"Found {streamsResponse.LogStreams.Count} log stream(s):");
            foreach (var stream in streamsResponse.LogStreams)
            {
                Log($"  - {stream.LogStreamName} (last event: {stream.LastEventTimestamp:u})");
            }

            if (streamsResponse.LogStreams.Count == 0)
            {
                Log($"No log streams found in {logGroupName}");
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"Error listing log streams: {ex.GetType().Name}: {ex.Message}");
        }

        // Fetch log events with pagination
        try
        {
            var allEvents = new List<FilteredLogEvent>();
            string? nextToken = null;

            // Get events from the last 5 minutes to focus on recent invocations
            var startTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();

            do
            {
                var response = await client.FilterLogEventsAsync(new FilterLogEventsRequest
                {
                    LogGroupName = logGroupName,
                    StartTime = startTime,
                    NextToken = nextToken,
                }, ct);

                allEvents.AddRange(response.Events);
                nextToken = response.NextToken;

                // Cap at 500 events to avoid flooding output
                if (allEvents.Count >= 500) break;
            }
            while (!string.IsNullOrEmpty(nextToken));

            Log($"Total log events: {allEvents.Count}");
            if (allEvents.Count > 0)
            {
                Log($"=== Logs from {logGroupName} ===");
                string? currentStream = null;
                foreach (var evt in allEvents)
                {
                    var stream = evt.LogStreamName ?? "unknown";
                    if (stream != currentStream)
                    {
                        Log($"--- Stream: {stream} ---");
                        currentStream = stream;
                    }
                    var message = evt.Message?.TrimEnd() ?? "";
                    Log(message);
                }
                Log($"=== End of logs ===");
            }
            else
            {
                Log($"No log events found in {logGroupName}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error fetching log events: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
