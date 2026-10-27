// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

// This file exists only to satisfy the Exe OutputType requirement for Microsoft.NET.Sdk.Web.
// The actual web servers are started programmatically via ChatAppServer.Create(),
// RuntimeEmulatorServer.Create(), and MemoryEmulatorServer.Create().
// This entry point is never invoked directly by consumers.
return;

// The Microsoft.NET.Sdk.Web + top-level statements combination would otherwise emit a
// 'public class Program' into the global namespace, which this package exports via
// lib/<tfm>/AWS.AgentCore.Testing.dll. Consumers that reference this package transitively
// and use WebApplicationFactory<Program> then get two global Program types and fail to
// compile. Declaring Program as internal here merges with the compiler-generated partial
// and keeps it out of consumers' compilations while still satisfying the Exe entry point.
internal partial class Program { }
