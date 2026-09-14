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
        /// Evaluates an inline-checks (InvokeGuardrailChecks) response. Content-filter and prompt-attack
        /// results are PER-EVALUATED-CATEGORY: InvokeGuardrailChecks returns one entry for every category you
        /// asked it to check, present even on benign input, with a nullable severity — so such a finding trips
        /// ONLY when its severity is present and meets <paramref name="severityThreshold"/>; a missing/absent
        /// severity does NOT trip (treating it as tripped would deny all benign traffic). Sensitive-information
        /// results are DETECTION-ONLY: an entry appears only when PII was actually found, so a PII finding trips
        /// when its confidence meets <paramref name="confidenceThreshold"/> OR is absent (fail-safe — a detected
        /// entity with no confidence must still deny). Returns true when any check tripped, with a summary of which.
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
                foreach (var entry in contentFilter.Where(e => MeetsSeverity(e.SeverityScore, severityThreshold)))
                {
                    tripped.Add($"content:{entry.Category}");
                }
            }

            if (results?.PromptAttack?.Results is { } promptAttack)
            {
                foreach (var entry in promptAttack.Where(e => MeetsSeverity(e.SeverityScore, severityThreshold)))
                {
                    tripped.Add($"promptAttack:{entry.Category}");
                }
            }

            if (results?.SensitiveInformation?.Results is { } sensitive)
            {
                foreach (var entry in sensitive.Where(e => DetectedPii(e.ConfidenceScore, confidenceThreshold)))
                {
                    tripped.Add($"pii:{entry.Type}");
                }
            }

            summary = tripped.Count == 0 ? "no checks tripped" : string.Join(",", tripped);
            return tripped.Count > 0;
        }

        // Content-filter / prompt-attack: results are PER-EVALUATED-CATEGORY. An entry is returned for every
        // category requested, even for benign input, and its severity is nullable. So a finding trips ONLY when
        // a severity is present AND meets the threshold; a missing/absent severity must NOT trip — otherwise
        // every benign request (which still carries an entry per category) would deny-all.
        private static bool MeetsSeverity(double? score, double threshold) => score.HasValue && score.Value >= threshold;

        // Sensitive-information: results are DETECTION-ONLY. An entry appears only when the guardrail actually
        // detected a PII entity, so its mere presence is the signal. It trips when confidence meets the
        // threshold, and also when confidence is absent (fail-safe: a real detection with no score must still
        // deny rather than slip through).
        private static bool DetectedPii(double? score, double threshold) => !score.HasValue || score.Value >= threshold;
    }
}
