// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Bedrock;
using Amazon.Bedrock.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests.Infrastructure
{
    /// <summary>
    /// Provisions a real Bedrock guardrail (a sentinel blocked word plus US SSN anonymize) and a CloudWatch
    /// log group for the run, and deletes both on dispose. Shared across the integration test classes.
    /// Requires AWS credentials; a provisioning failure fails the suite (see <see cref="InitializeAsync"/>).
    /// </summary>
    public sealed class GuardrailFixture : IAsyncLifetime
    {
        public const string BlockWord = "MAGINTTESTBLOCK";
        public const string SsnSample = "The patient SSN is 123-45-6789 on file.";
        public const string SsnPlaceholder = "{US_SOCIAL_SECURITY_NUMBER}";

        public string GuardrailId { get; private set; } = string.Empty;
        public string GuardrailVersion => "DRAFT";
        public RegionEndpoint Region { get; } = IntegrationConfig.Region;
        public string LogGroupName { get; } = $"/mag-inttest/{Guid.NewGuid():N}";

        private IAmazonBedrock? _control;

        public async ValueTask InitializeAsync()
        {
            // A provisioning failure must FAIL the suite (with context), not be silently swallowed, so broken
            // credentials/permissions/fixture can't let the tests pass green without ever exercising Bedrock.
            try
            {
                _control = new AmazonBedrockClient(Region);
                var created = await _control.CreateGuardrailAsync(new CreateGuardrailRequest
                {
                    Name = $"mag-inttest-{Guid.NewGuid():N}",
                    BlockedInputMessaging = "Blocked by guardrail.",
                    BlockedOutputsMessaging = "Blocked by guardrail.",
                    WordPolicyConfig = new GuardrailWordPolicyConfig
                    {
                        WordsConfig = new List<GuardrailWordConfig> { new() { Text = BlockWord } }
                    },
                    SensitiveInformationPolicyConfig = new GuardrailSensitiveInformationPolicyConfig
                    {
                        PiiEntitiesConfig = new List<GuardrailPiiEntityConfig>
                        {
                            new()
                            {
                                Type = GuardrailPiiEntityType.US_SOCIAL_SECURITY_NUMBER,
                                Action = GuardrailSensitiveInformationAction.ANONYMIZE
                            }
                        }
                    }
                });

                GuardrailId = created.GuardrailId;
                await WaitUntilReadyAsync();

                // Create the log group up front so the fixture actually provisions what it advertises (and
                // DisposeAsync has something to delete), rather than relying on AWS.Logger.Core to create it
                // lazily during the audit test.
                using var logs = new AmazonCloudWatchLogsClient(Region);
                await logs.CreateLogGroupAsync(new CreateLogGroupRequest { LogGroupName = LogGroupName });
            }
            catch (Exception ex)
            {
                throw new System.InvalidOperationException(
                    "Failed to provision AWS resources for the integration suite. Ensure AWS credentials with " +
                    "Bedrock and CloudWatch Logs permissions are configured.", ex);
            }
        }

        private async Task WaitUntilReadyAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (true)
            {
                var status = (await _control!.GetGuardrailAsync(new GetGuardrailRequest
                {
                    GuardrailIdentifier = GuardrailId,
                    GuardrailVersion = GuardrailVersion
                }, cts.Token)).Status;

                if (status == GuardrailStatus.READY)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_control is not null && !string.IsNullOrEmpty(GuardrailId))
            {
                try
                {
                    await _control.DeleteGuardrailAsync(new DeleteGuardrailRequest { GuardrailIdentifier = GuardrailId });
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            _control?.Dispose();

            try
            {
                using var logs = new AmazonCloudWatchLogsClient(Region);
                await logs.DeleteLogGroupAsync(new DeleteLogGroupRequest { LogGroupName = LogGroupName });
            }
            catch
            {
                // Log group may not have been created; best-effort cleanup.
            }
        }
    }

    [CollectionDefinition("bedrock-integration")]
    public sealed class BedrockIntegrationCollection : ICollectionFixture<GuardrailFixture>
    {
    }
}
