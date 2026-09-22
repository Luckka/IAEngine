using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class ReviewPolicy(ReviewPolicyOptions options)
{
    public bool Passes(ReviewResult review)
    {
        if (review.Decision == ReviewDecision.Fail) return false;
        if (review.Quality?.Any(x => x.Status == QualityStatus.Fail && x.Blocking) == true) return false;
        if (review.Quality?.Any(x => x.Category.Equals("prototypeConformity", StringComparison.OrdinalIgnoreCase)
                && x.Status is QualityStatus.Fail or QualityStatus.NotApplicable) == true) return false;
        if (review.Quality?.Any(x => x.Category.Equals("prototypeConformity", StringComparison.OrdinalIgnoreCase)
                && x.Status == QualityStatus.Pass) == true
            && !HasPrototypeEvidence(review.PrototypeEvidence)) return false;
        return !review.Findings.Any(finding =>
            options.BlockingSeverities.Contains(finding.Severity.ToString(), StringComparer.OrdinalIgnoreCase)
            || (options.BlockingHighFails && finding.Severity == FindingSeverity.High && finding.Blocking));
    }

    public bool PassesPrototypeConformity(ReviewResult review, PrototypeEvidenceCheck evidence)
        => evidence.Passed && Passes(review) && HasPrototypeEvidence(review.PrototypeEvidence)
            && !review.Findings.Any(x => x.Blocking &&
                (x.Severity is FindingSeverity.Critical or FindingSeverity.High));

    private static bool HasPrototypeEvidence(PrototypeReviewEvidence? evidence) => evidence is not null
        && !string.IsNullOrWhiteSpace(evidence.ReferenceState)
        && !string.IsNullOrWhiteSpace(evidence.ReferenceScreenshot)
        && !string.IsNullOrWhiteSpace(evidence.ImplementationState)
        && !string.IsNullOrWhiteSpace(evidence.ImplementationScreenshot);
}

public sealed record ReviewProgressSnapshot(IReadOnlyList<string> FindingFingerprints, bool ProgressObserved, int UnchangedOccurrences)
{
    public string Signature => string.Join("|", FindingFingerprints.OrderBy(x => x, StringComparer.Ordinal));
}

public static class ReviewConvergence
{
    public static ReviewProgressSnapshot Observe(RunRecord run, ReviewResult review)
    {
        var fingerprints = review.Findings
            .Where(x => x.Blocking)
            .Select(Fingerprint)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var signature = string.Join("|", fingerprints);
        var previous = run.ReviewProgressHistory.LastOrDefault();
        // A changed normalized signature represents meaningful movement. This covers
        // disappearing findings, changed findings, and a smaller set without relying
        // on a delimiter that may also occur inside a fingerprint.
        var progress = previous is null || !string.Equals(previous, signature, StringComparison.Ordinal);
        var unchanged = run.ReviewProgressHistory.Count(x => string.Equals(x, signature, StringComparison.Ordinal)) + 1;
        run.ReviewProgressHistory.Add(signature);
        run.ReviewFindingFingerprints.Clear();
        run.ReviewFindingFingerprints.AddRange(fingerprints);
        return new ReviewProgressSnapshot(fingerprints, progress, unchanged);
    }

    public static string Fingerprint(ReviewFinding finding)
    {
        static string Normalize(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{finding.Severity}|{Normalize(finding.File)}|{Normalize(finding.Location)}|{Normalize(finding.Problem)}";
    }
}
