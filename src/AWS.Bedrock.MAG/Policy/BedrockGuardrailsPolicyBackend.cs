// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using AgentGovernance.Policy;
using AWS.Bedrock.MAG.Internal;

namespace AWS.Bedrock.MAG.Policy
{
    /// <summary>
    /// Evaluates tool-call context through a Bedrock Guardrail as a toolkit
    /// <see cref="IExternalPolicyBackend"/>. Added alongside (not replacing) the toolkit's rule, OPA, and
    /// Cedar backends. Fails closed on a Bedrock or AWS error by default.
    /// </summary>
    public sealed class BedrockGuardrailsPolicyBackend : IExternalPolicyBackend
    {
        /// <summary>The backend name shown in policy decision metadata.</summary>
        public const string BackendName = "bedrock-guardrails";

        private readonly BedrockGuardrailsPolicyOptions _options;
        private readonly BedrockGuardrailClient _client;

        /// <summary>
        /// Creates a policy backend. Pass a client for full control; otherwise one is built from
        /// <see cref="BedrockGuardrailsPolicyOptions.Region"/> and
        /// <see cref="BedrockGuardrailsPolicyOptions.Credentials"/>, falling back to the default chain.
        /// </summary>
        public BedrockGuardrailsPolicyBackend(BedrockGuardrailsPolicyOptions options, IAmazonBedrockRuntime? client = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            var hasGuardrail = !string.IsNullOrWhiteSpace(_options.GuardrailId);
            var hasInlineChecks = _options.InlineChecks?.HasAnyCheck == true;
            if (!hasGuardrail && !hasInlineChecks)
            {
                throw new ArgumentException(
                    "Set either GuardrailId (ApplyGuardrail) or InlineChecks with at least one category/entity (InvokeGuardrailChecks).",
                    nameof(options));
            }

            _client = new BedrockGuardrailClient(client ?? CreateClient(_options.Region, _options.Credentials));
        }

        /// <inheritdoc />
        public string Name => BackendName;

        /// <summary>
        /// Synchronous evaluation the toolkit's <see cref="PolicyEngine"/> calls. Bridges to the async
        /// Bedrock call by blocking, because the engine is sync-only and the net8.0 AWS SDK has no
        /// synchronous ApplyGuardrail.
        /// </summary>
        public ExternalPolicyDecision Evaluate(IReadOnlyDictionary<string, object> context)
            => EvaluateAsync(context).GetAwaiter().GetResult();

        /// <summary>Evaluates the context against the guardrail. Used directly by async callers.</summary>
        public async Task<ExternalPolicyDecision> EvaluateAsync(IReadOnlyDictionary<string, object> context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                // Serialize inside the try so a throwing ContextSerializer still fails closed/open per policy
                // instead of escaping to the (sync) PolicyEngine and breaking the whole governance call.
                var text = (_options.ContextSerializer ?? DefaultContextSerializer)(context);

                // A configured guardrail (ApplyGuardrail) wins; otherwise run inline checks.
                return string.IsNullOrWhiteSpace(_options.GuardrailId)
                    ? await EvaluateWithInlineChecksAsync(text, cancellationToken).ConfigureAwait(false)
                    : await EvaluateWithGuardrailAsync(text, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return BuildErrorDecision(ex);
            }
        }

        private async Task<ExternalPolicyDecision> EvaluateWithGuardrailAsync(string text, CancellationToken cancellationToken)
        {
            var invocation = await _client
                .ApplyAsync(_options.GuardrailId!, _options.GuardrailVersion, GuardrailContentSource.INPUT, text, cancellationToken)
                .ConfigureAwait(false);

            var intervened = GuardrailResponseMapper.Intervened(invocation.Response);
            var summary = GuardrailResponseMapper.SummarizeAssessment(invocation.Response);

            return new ExternalPolicyDecision
            {
                Backend = Name,
                Allowed = !intervened,
                Reason = intervened
                    ? $"Denied by Bedrock guardrail ({summary})."
                    : "Allowed by Bedrock guardrail.",
                EvaluationMs = invocation.EvaluationMs,
                Metadata = new Dictionary<string, object> { ["assessment"] = summary }
            };
        }

        private async Task<ExternalPolicyDecision> EvaluateWithInlineChecksAsync(string text, CancellationToken cancellationToken)
        {
            var checks = _options.InlineChecks!;
            var invocation = await _client.InvokeChecksAsync(checks, text, cancellationToken).ConfigureAwait(false);
            var tripped = GuardrailResponseMapper.ChecksTripped(
                invocation.Response, checks.SeverityThreshold, checks.ConfidenceThreshold, out var summary);

            return new ExternalPolicyDecision
            {
                Backend = Name,
                Allowed = !tripped,
                Reason = tripped
                    ? $"Denied by Bedrock inline checks ({summary})."
                    : "Allowed by Bedrock inline checks.",
                EvaluationMs = invocation.EvaluationMs,
                Metadata = new Dictionary<string, object> { ["checks"] = summary }
            };
        }

        // Fail-closed: set BOTH Error and Allowed=false so the engine denies (engine denies when
        // !IsNullOrWhiteSpace(Error) || !Allowed). Fail-open: Allowed=true and leave Error EMPTY, or the
        // engine would still deny; the error is kept in Metadata for the audit.
        private ExternalPolicyDecision BuildErrorDecision(Exception ex)
        {
            // Null means unset; a standalone backend defaults to fail-closed (the safe default-deny posture).
            if (_options.FailClosed ?? true)
            {
                return new ExternalPolicyDecision
                {
                    Backend = Name,
                    Allowed = false,
                    Reason = $"Denied: Bedrock guardrail error, failing closed ({ex.Message}).",
                    Error = ex.Message
                };
            }

            return new ExternalPolicyDecision
            {
                Backend = Name,
                Allowed = true,
                Reason = "Allowed: Bedrock guardrail error, failing open.",
                Metadata = new Dictionary<string, object> { ["error"] = ex.Message }
            };
        }

        // AOT-safe context serializer: writes a flat JSON object with Utf8JsonWriter, no reflection.
        private static string DefaultContextSerializer(IReadOnlyDictionary<string, object> context)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var pair in context)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteValue(writer, pair.Value);
                }
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                // Tool arguments deserialized from JSON arrive as JsonElement; write the real value (object,
                // array, string, number) so the guardrail inspects the content, not "System.Text.Json.JsonElement".
                case JsonElement je:
                    je.WriteTo(writer);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case double d:
                    WriteDouble(writer, d);
                    break;
                case float f:
                    WriteDouble(writer, f);
                    break;
                case decimal m:
                    writer.WriteNumberValue(m);
                    break;
                // Nested dictionaries/collections are serialized recursively rather than stringified to a CLR
                // type name, so PII buried in a structured argument is still shown to the guardrail. The
                // non-generic IDictionary case catches the common concrete maps whose value type is not object
                // (Dictionary<string, string>, Dictionary<string, int>, ...) — these do NOT implement
                // IEnumerable<KeyValuePair<string, object>> and would otherwise fall through to the IEnumerable
                // branch below and be written as an array of stringified KeyValuePair entries.
                case System.Collections.IDictionary dict:
                    writer.WriteStartObject();
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        writer.WritePropertyName(entry.Key?.ToString() ?? "null");
                        WriteValue(writer, entry.Value);
                    }

                    writer.WriteEndObject();
                    break;
                // Read-only dictionaries that don't implement the non-generic IDictionary (e.g. some
                // IReadOnlyDictionary<string, object> implementations) are still written as JSON objects.
                case IEnumerable<KeyValuePair<string, object>> map:
                    writer.WriteStartObject();
                    foreach (var pair in map)
                    {
                        writer.WritePropertyName(pair.Key);
                        WriteValue(writer, pair.Value);
                    }

                    writer.WriteEndObject();
                    break;
                case System.Collections.IEnumerable seq:
                    writer.WriteStartArray();
                    foreach (var item in seq)
                    {
                        WriteValue(writer, item);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }

        // Utf8JsonWriter throws on NaN/Infinity. Render non-finite values as text so a stray non-finite number
        // doesn't fail the whole serialization (which would skip guardrail evaluation of the surrounding text).
        private static void WriteDouble(Utf8JsonWriter writer, double value)
        {
            if (double.IsFinite(value))
            {
                writer.WriteNumberValue(value);
            }
            else
            {
                writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static IAmazonBedrockRuntime CreateClient(RegionEndpoint? region, AWSCredentials? credentials)
        {
            if (credentials is not null)
            {
                return region is null
                    ? new AmazonBedrockRuntimeClient(credentials)
                    : new AmazonBedrockRuntimeClient(credentials, region);
            }

            return region is null ? new AmazonBedrockRuntimeClient() : new AmazonBedrockRuntimeClient(region);
        }
    }
}
