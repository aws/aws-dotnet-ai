// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using Amazon;

namespace AWS.Bedrock.MAG.IntegrationTests.Infrastructure
{
    /// <summary>
    /// Integration-test configuration. These tests hit real AWS (Bedrock Guardrails, CloudWatch): they
    /// provision and tear down real resources, so they require AWS credentials with Bedrock and CloudWatch
    /// Logs permissions. Region defaults to us-west-2 (override with <c>MAG_TEST_REGION</c>).
    /// </summary>
    internal static class IntegrationConfig
    {
        public static RegionEndpoint Region =>
            RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("MAG_TEST_REGION") ?? "us-west-2");
    }
}
