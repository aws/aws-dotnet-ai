// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using NativeAotAnnotations.Models;

namespace NativeAotAnnotations;

[JsonSerializable(typeof(PromptRequest))]
internal partial class AppJsonContext : JsonSerializerContext;
