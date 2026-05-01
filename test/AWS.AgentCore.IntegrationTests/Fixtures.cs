// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.IntegrationTests.Infrastructure;

namespace AWS.AgentCore.IntegrationTests;

public class MicrosoftAgentFrameworkFixture : SampleAppFixture
{
    public MicrosoftAgentFrameworkFixture() : base("MicrosoftAgentFrameworkSample") { }
}

public class AnnotationsSampleFixture : SampleAppFixture
{
    public AnnotationsSampleFixture() : base("AnnotationsSample") { }
}

public class StreamingAgentFixture : SampleAppFixture
{
    public StreamingAgentFixture() : base("StreamingAgent") { }
}

public class AnnotationsStreamingAgentFixture : SampleAppFixture
{
    public AnnotationsStreamingAgentFixture() : base("AnnotationsStreamingAgent") { }
}

public class NativeAotExtensionsFixture : SampleAppFixture
{
    public NativeAotExtensionsFixture() : base("NativeAotExtensions") { }
}

public class NativeAotAnnotationsFixture : SampleAppFixture
{
    public NativeAotAnnotationsFixture() : base("NativeAotAnnotations") { }
}
