// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Configures inline guardrail checks for the policy backend (InvokeGuardrailChecks). The checks are
    /// supplied in the request, so no pre-created Bedrock guardrail is needed. At least one category or
    /// entity must be set. A check trips the deny when its score meets the matching threshold.
    /// </summary>
    public sealed class GuardrailChecksOptions
    {
        /// <summary>Content-filter categories to evaluate (e.g. HATE, INSULTS, SEXUAL, VIOLENCE, MISCONDUCT).</summary>
        public IList<string> ContentFilterCategories { get; } = new List<string>();

        /// <summary>Prompt-attack categories to evaluate (e.g. JAILBREAK, PROMPT_INJECTION, PROMPT_LEAKAGE).</summary>
        public IList<string> PromptAttackCategories { get; } = new List<string>();

        /// <summary>PII entity types to detect (e.g. US_SOCIAL_SECURITY_NUMBER, EMAIL, PHONE).</summary>
        public IList<string> SensitiveInformationEntities { get; } = new List<string>();

        /// <summary>
        /// Content-filter and prompt-attack severity at or above which the call is denied (0.0 to 1.0).
        /// </summary>
        public double SeverityThreshold
        {
            get => _severityThreshold;
            set => _severityThreshold = ValidateThreshold(value, nameof(SeverityThreshold));
        }

        /// <summary>PII confidence at or above which the call is denied (0.0 to 1.0).</summary>
        public double ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => _confidenceThreshold = ValidateThreshold(value, nameof(ConfidenceThreshold));
        }

        private double _severityThreshold = 0.5;
        private double _confidenceThreshold = 0.5;

        // Bedrock caps these scores at 1.0, so a threshold outside [0, 1] (or NaN) can never be met and would
        // silently allow every detection through — a fail-open misconfiguration. Reject it at assignment.
        private static double ValidateThreshold(double value, string name)
        {
            if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
            {
                throw new ArgumentOutOfRangeException(name, value, "Threshold must be a finite value between 0.0 and 1.0 inclusive.");
            }

            return value;
        }

        /// <summary>True when at least one category or entity is configured.</summary>
        public bool HasAnyCheck =>
            ContentFilterCategories.Count > 0 || PromptAttackCategories.Count > 0 || SensitiveInformationEntities.Count > 0;
    }
}
