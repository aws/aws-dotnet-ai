using System.ComponentModel;
using System.Text.Json;
using AWS.AgentCore;
using MeetingJudgeAgent.Models;
using Microsoft.Extensions.AI;

namespace MeetingJudgeAgent;

public class Agent(IChatClient chatClient, ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        MeetingRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Meeting Judge invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        const string systemPrompt = """
            You are the Honorable Judge Meetingsworth, a stern but hilarious arbiter of meeting worthiness.
            Think Judge Judy meets corporate middle management, with a dash of Gordon Ramsay's disappointment.

            Your job is to evaluate whether a proposed meeting deserves to exist. You have ZERO tolerance
            for meetings that waste human potential.

            PROCEDURE:
            1. Use ALL available tools to gather evidence about the meeting
            2. Analyze the evidence with righteous indignation (or rare approval)
            3. Deliver a formal ruling in this format:

            === RULING OF THE MEETING COURT ===
            Case: [Meeting Title]
            Docket Number: [Make up a funny one like "MTG-2024-WASTE-0042"]

            EVIDENCE SUMMARY:
            - [Key findings from each tool]

            VERDICT: [APPROVED / DENIED / CONDITIONAL APPROVAL]

            REASONING:
            [2-3 sentences of savage but funny commentary]

            COST ANALYSIS:
            - Estimated productivity cost: $[amount]
            - Cost per minute: $[amount]
            - Equivalent value: [something absurd, like "3.7 carrier pigeons" or "half a pizza party"]

            ALTERNATIVE SENTENCE:
            [If denied, what should they do instead]

            Court is adjourned. *bangs gavel*
            ===

            Rules:
            - Always use ALL five tools before rendering judgment
            - Be savage but never mean-spirited — this is comedy, not cruelty
            - Meetings with clear agendas, few attendees, and specific outcomes MAY be approved
            - Any meeting with "sync", "align", or "touch base" in the title starts with -50 points
            - Meetings with 10+ attendees require extraordinary justification
            - If the agenda is empty or vague, show no mercy
            """;

        var agent = chatClient.AsAIAgent(tools:
        [
            AIFunctionFactory.Create(CountAttendees),
            AIFunctionFactory.Create(AnalyzeAgenda),
            AIFunctionFactory.Create(CheckIfCouldBeAnEmail),
            AIFunctionFactory.Create(EstimateProductivityLoss),
            AIFunctionFactory.Create(SuggestAlternative),
        ]);

        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var fullPrompt = $"[System: {systemPrompt}]\n\nUser: Please judge this meeting:\n{request.Prompt ?? "No meeting details provided. Judge the audacity of sending an empty meeting invite."}";
        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", courtInSession = true, time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    [Description("Counts the number of attendees in a meeting and provides commentary on whether that many humans really need to be in one room. Pass the attendee list or count as a string.")]
    static string CountAttendees(
        [Description("The attendee list or description from the meeting invite (e.g., '15 people' or 'Alice, Bob, Charlie, the entire marketing team').")] string attendeeInfo)
    {
        var count = 0;
        var words = attendeeInfo.Split([' ', ',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (int.TryParse(word, out var parsed))
            {
                count = parsed;
                break;
            }
        }

        if (count == 0)
        {
            count = attendeeInfo.Split(',').Length;
        }

        var commentary = count switch
        {
            <= 2 => "A cozy duo. This might actually be productive. Suspicious.",
            3 => "The holy trinity of meetings. Three is acceptable... barely.",
            4 or 5 => "A reasonable headcount. The meeting gods nod in cautious approval.",
            >= 6 and <= 8 => "Getting crowded. At least 2 people will be checking their phones.",
            >= 9 and <= 12 => $"{count} people?! That's not a meeting, that's a flash mob with a conference line.",
            >= 13 and <= 20 => $"{count} attendees detected. This is a TED talk, not a meeting. Does someone have slides?",
            _ => $"{count} PEOPLE?! This isn't a meeting — it's a census event. You need a venue, not a calendar invite."
        };

        var silentObserverEstimate = Math.Max(0, count - 4);

        return JsonSerializer.Serialize(new
        {
            attendeeCount = count,
            commentary,
            estimatedSilentObservers = silentObserverEstimate,
            estimatedActualContributors = Math.Min(count, 4),
            crowdClassification = count <= 3 ? "Intimate" : count <= 6 ? "Working Group" : count <= 10 ? "Committee" : count <= 20 ? "Town Hall" : "Flash Mob"
        });
    }

    [Description("Analyzes the meeting agenda for specificity, buzzword density, and actual actionable content. Returns a quality score and detailed roast.")]
    static string AnalyzeAgenda(
        [Description("The meeting agenda text, or description of what the meeting is about.")] string agenda)
    {
        var buzzwords = new[] { "sync", "align", "touch base", "circle back", "leverage", "synergy",
            "deep dive", "take offline", "move the needle", "bandwidth", "low-hanging fruit",
            "paradigm", "pivot", "holistic", "streamline", "optimize", "ideate", "unpack",
            "double-click", "socialize", "level-set", "net-net", "boil the ocean" };

        var agendaLower = agenda.ToLowerInvariant();
        var foundBuzzwords = buzzwords.Where(b => agendaLower.Contains(b)).ToList();
        var buzzwordDensity = (double)foundBuzzwords.Count / Math.Max(1, agenda.Split(' ').Length) * 100;

        var hasActionItems = agendaLower.Contains("decide") || agendaLower.Contains("approve") ||
                             agendaLower.Contains("review") || agendaLower.Contains("finalize") ||
                             agendaLower.Contains("deadline") || agendaLower.Contains("vote");

        var agendaLength = agenda.Trim().Length;
        var specificity = agendaLength switch
        {
            0 => "NONEXISTENT — They didn't even try. The audacity.",
            < 20 => "VAPOROUS — More of a vibe than an agenda.",
            < 50 => "THIN — Like a meeting invite written on a Post-it note.",
            < 100 => "MODERATE — At least someone thought about this for 30 seconds.",
            < 200 => "DETAILED — Promising! Someone actually planned something.",
            _ => "COMPREHENSIVE — This person meetings professionally."
        };

        var qualityScore = 50;
        qualityScore -= foundBuzzwords.Count * 15;
        qualityScore += hasActionItems ? 30 : -20;
        qualityScore += agendaLength > 50 ? 20 : -10;
        qualityScore = Math.Clamp(qualityScore, 0, 100);

        var roast = qualityScore switch
        {
            >= 80 => "Against all odds, this agenda shows signs of intelligent life.",
            >= 60 => "Acceptable. Not great, not terrible. The meeting equivalent of room-temperature coffee.",
            >= 40 => "This agenda is doing the bare minimum, like a student who Googled the assignment 5 minutes before class.",
            >= 20 => "I've seen better planning from a squirrel hiding nuts.",
            _ => "This 'agenda' is a war crime against productivity. Geneva should be notified."
        };

        return JsonSerializer.Serialize(new
        {
            qualityScore,
            specificity,
            buzzwordsFound = foundBuzzwords,
            buzzwordDensityPercent = Math.Round(buzzwordDensity, 1),
            hasActionableItems = hasActionItems,
            roast
        });
    }

    [Description("Analyzes the meeting topic and returns the probability (0-100%) that this meeting could have been an email, Slack message, or carrier pigeon instead.")]
    static string CheckIfCouldBeAnEmail(
        [Description("The meeting title and/or topic description.")] string meetingTopic)
    {
        var topicLower = meetingTopic.ToLowerInvariant();

        var emailProbability = 50;

        if (topicLower.Contains("update") || topicLower.Contains("status")) emailProbability += 25;
        if (topicLower.Contains("fyi") || topicLower.Contains("inform")) emailProbability += 30;
        if (topicLower.Contains("weekly") || topicLower.Contains("standup")) emailProbability += 15;
        if (topicLower.Contains("sync") || topicLower.Contains("catch up")) emailProbability += 20;
        if (topicLower.Contains("announce") || topicLower.Contains("share")) emailProbability += 20;
        if (topicLower.Contains("touch base")) emailProbability += 35;

        if (topicLower.Contains("brainstorm") || topicLower.Contains("workshop")) emailProbability -= 30;
        if (topicLower.Contains("debate") || topicLower.Contains("decide")) emailProbability -= 25;
        if (topicLower.Contains("interview") || topicLower.Contains("review")) emailProbability -= 20;
        if (topicLower.Contains("retrospective") || topicLower.Contains("postmortem")) emailProbability -= 15;
        if (topicLower.Contains("conflict") || topicLower.Contains("resolution")) emailProbability -= 35;
        if (topicLower.Contains("demo") || topicLower.Contains("presentation")) emailProbability -= 20;

        emailProbability = Math.Clamp(emailProbability, 5, 99);

        var verdict = emailProbability switch
        {
            >= 90 => "This is ABSOLUTELY an email wearing a meeting's clothing. Shameful disguise.",
            >= 75 => "Strong email energy detected. This meeting is an email that got ideas above its station.",
            >= 60 => "More email than meeting. Like a centaur, but worse — half email, half waste of time.",
            >= 40 => "Borderline. Could go either way. The meeting is in quantum superposition.",
            >= 25 => "Okay, there might be a legitimate reason for humans to occupy the same space-time for this.",
            _ => "Fine. This actually needs real-time human interaction. The court grudgingly acknowledges this."
        };

        var betterFormat = emailProbability switch
        {
            >= 80 => "A 3-sentence email",
            >= 60 => "A Slack message with a poll",
            >= 40 => "A shared document with comments",
            >= 20 => "A 15-minute standup (max)",
            _ => "An actual meeting (you've earned it)"
        };

        return JsonSerializer.Serialize(new
        {
            couldBeEmailProbability = emailProbability,
            verdict,
            suggestedFormat = betterFormat,
            emailWouldSave = $"{emailProbability * 0.6:F0} minutes of collective human life"
        });
    }

    [Description("Calculates the estimated productivity cost of the meeting based on attendee count, duration, and context-switching penalties. Returns a mock financial analysis.")]
    static string EstimateProductivityLoss(
        [Description("Number of attendees.")] int attendeeCount,
        [Description("Meeting duration in minutes.")] int durationMinutes)
    {
        const double fakeHourlyRate = 85.0;
        const double contextSwitchPenaltyMinutes = 23.0;

        var meetingCostPerPerson = (durationMinutes / 60.0) * fakeHourlyRate;
        var totalMeetingCost = meetingCostPerPerson * attendeeCount;
        var contextSwitchCost = contextSwitchPenaltyMinutes * attendeeCount * (fakeHourlyRate / 60.0);
        var totalCost = totalMeetingCost + contextSwitchCost;
        var costPerMinute = totalCost / Math.Max(1, durationMinutes);

        var pizzaEquivalent = totalCost / 18.0;
        var netflixMonths = totalCost / 15.99;
        var rubberDucks = totalCost / 3.50;

        var severity = totalCost switch
        {
            < 100 => "Minor Infraction — like jaywalking, but for calendars.",
            < 500 => "Misdemeanor — noticeable damage to the productivity ecosystem.",
            < 1000 => "Felony — HR should be notified. Not for any real reason, just so they know.",
            < 5000 => "Grand Theft Productivity — this meeting is committing robbery in broad daylight.",
            _ => "Crime Against Humanity — the UN Productivity Council has been alerted."
        };

        return JsonSerializer.Serialize(new
        {
            directMeetingCost = $"${totalMeetingCost:F2}",
            contextSwitchingPenalty = $"${contextSwitchCost:F2}",
            totalProductivityCost = $"${totalCost:F2}",
            costPerMinute = $"${costPerMinute:F2}",
            equivalentPizzas = $"{pizzaEquivalent:F1} large pizzas",
            equivalentNetflixMonths = $"{netflixMonths:F1} months of Netflix",
            equivalentRubberDucks = $"{rubberDucks:F0} rubber ducks for debugging",
            severityClassification = severity,
            assumedHourlyRate = $"${fakeHourlyRate}/hr (conservative — your actual worth is immeasurable)",
            contextSwitchNote = $"Each attendee loses {contextSwitchPenaltyMinutes} minutes refocusing after. Science said so."
        });
    }

    [Description("Suggests a humorous but potentially valid alternative to holding the meeting. Takes into account the meeting topic to provide relevant alternatives.")]
    static string SuggestAlternative(
        [Description("The meeting title/topic to suggest an alternative for.")] string meetingTopic,
        [Description("Number of attendees.")] int attendeeCount)
    {
        var topicLower = meetingTopic.ToLowerInvariant();

        var alternatives = new List<(string alternative, string reasoning)>
        {
            ("Send an email with bullet points and a deadline for responses",
                "The written word has served humanity for 5,000 years. Trust the process."),
            ("A Slack message with exactly one emoji poll",
                "Democracy doesn't require 60 minutes and a conference room.")
        };

        if (topicLower.Contains("status") || topicLower.Contains("update"))
        {
            alternatives.Add(("A shared dashboard that updates automatically",
                "Robots are better at status updates. Let them do their job."));
            alternatives.Add(("A 280-character tweet-style update in a team channel",
                "If Twitter can run on character limits, so can your status update."));
        }

        if (topicLower.Contains("brainstorm") || topicLower.Contains("ideate"))
        {
            alternatives.Add(("A shared doc where people add ideas asynchronously over 48 hours",
                "The best ideas come in the shower, not in Conference Room B."));
            alternatives.Add(("Each person submits one idea via carrier pigeon",
                "Pigeons are surprisingly reliable. Unlike this meeting's ROI."));
        }

        if (topicLower.Contains("align") || topicLower.Contains("sync"))
        {
            alternatives.Add(("A well-written README.md",
                "Documentation: the meeting that keeps on giving without wasting anyone's Tuesday."));
            alternatives.Add(("Interpretive dance posted to the team Slack",
                "At least interpretive dance has artistic value."));
        }

        if (attendeeCount > 8)
        {
            alternatives.Add(("Break into 3 groups of humans who actually need to talk to each other",
                $"No decision in history was improved by adding person #{attendeeCount}."));
            alternatives.Add(("A pre-recorded video that people can watch at 2x speed",
                "Respect people's time by letting them fast-forward through the boring parts (all of it)."));
        }

        alternatives.Add(("Resolve the issue via competitive rock-paper-scissors",
            "Faster, cheaper, and statistically as effective as most alignment meetings."));

        var selected = alternatives.Take(3).ToList();

        return JsonSerializer.Serialize(new
        {
            primaryRecommendation = selected[0].alternative,
            primaryReasoning = selected[0].reasoning,
            alternatives = selected.Skip(1).Select(a => new { suggestion = a.alternative, reasoning = a.reasoning }),
            timeSaved = $"{attendeeCount * 45} person-minutes returned to humanity",
            courtOrder = "The court strongly recommends the primary alternative. Failure to comply will result in a stern emoji in Slack."
        });
    }
}
