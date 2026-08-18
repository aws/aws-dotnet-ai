// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AWS.Bedrock.MAG.Internal
{
    /// <summary>
    /// Interprets an <see cref="ApplyGuardrailResponse"/> for both features so they can't drift in how
    /// they read a guardrail assessment.
    /// </summary>
    internal static class GuardrailResponseMapper
    {
        /// <summary>True when the guardrail took action (blocked or masked); the policy backend maps this to a deny.</summary>
        public static bool Intervened(ApplyGuardrailResponse response)
            => GuardrailAction.GUARDRAIL_INTERVENED.Equals(response.Action);

        /// <summary>
        /// The guardrail's rewritten (masked/redacted) text, or the original when the guardrail returned
        /// no output content.
        /// </summary>
        // TODO: this fallback to the original is not fail-closed; a later PR (the PII sanitizer) will change
        // how an intervention with no masked output is handled (block instead of returning the original).
        public static string GetRedactedText(ApplyGuardrailResponse response, string original)
            => response.Outputs?.FirstOrDefault()?.Text ?? original;

        /// <summary>Distinct PII entity types the guardrail detected across all assessments (e.g. NAME, US_SSN).</summary>
        public static IReadOnlyList<string> GetDetectedPiiTypes(ApplyGuardrailResponse response)
        {
            if (response.Assessments is null || response.Assessments.Count == 0)
            {
                return System.Array.Empty<string>();
            }

            return response.Assessments
                .Select(a => a.SensitiveInformationPolicy)
                .Where(p => p?.PiiEntities is not null)
                .SelectMany(p => p!.PiiEntities)
                .Select(e => e.Type?.Value)
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Compact, human-readable summary of what the guardrail did, for decision metadata and audit
        /// reasons (e.g. "action=GUARDRAIL_INTERVENED; pii=[NAME,US_SSN]").
        /// </summary>
        public static string SummarizeAssessment(ApplyGuardrailResponse response)
        {
            var action = response.Action?.Value ?? "NONE";
            var pii = GetDetectedPiiTypes(response);
            return pii.Count == 0
                ? $"action={action}"
                : $"action={action}; pii=[{string.Join(",", pii)}]";
        }

        /// <summary>
        /// Evaluates an inline-checks (InvokeGuardrailChecks) response. A content-filter or prompt-attack
        /// finding trips when its severity meets <paramref name="severityThreshold"/>; a PII finding trips
        /// when its confidence meets <paramref name="confidenceThreshold"/>. A finding with no score is
        /// treated as tripped (fail-safe). Returns true when any check tripped, with a summary of which.
        /// </summary>
        public static bool ChecksTripped(
            InvokeGuardrailChecksResponse response,
            double severityThreshold,
            double confidenceThreshold,
            out string summary)
        {
            var tripped = new List<string>();
            var results = response.Results;

            if (results?.ContentFilter?.Results is { } contentFilter)
            {
                foreach (var entry in contentFilter.Where(e => Meets(e.SeverityScore, severityThreshold)))
                {
                    tripped.Add($"content:{entry.Category}");
                }
            }

            if (results?.PromptAttack?.Results is { } promptAttack)
            {
                foreach (var entry in promptAttack.Where(e => Meets(e.SeverityScore, severityThreshold)))
                {
                    tripped.Add($"promptAttack:{entry.Category}");
                }
            }

            if (results?.SensitiveInformation?.Results is { } sensitive)
            {
                foreach (var entry in sensitive.Where(e => Meets(e.ConfidenceScore, confidenceThreshold)))
                {
                    tripped.Add($"pii:{entry.Type}");
                }
            }

            summary = tripped.Count == 0 ? "no checks tripped" : string.Join(",", tripped);
            return tripped.Count > 0;
        }

        // A finding with no score is treated as meeting the threshold (fail-safe toward deny).
        private static bool Meets(double? score, double threshold) => !score.HasValue || score.Value >= threshold;
    }
}
