using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class ReviewPolicyTests
{
    [Fact]
    public void CriticalAlwaysBlocks()
    {
        var review = TestData.Review(ReviewDecision.Pass, FindingSeverity.Critical);
        Assert.False(new ReviewPolicy(new ReviewPolicyOptions()).Passes(review));
    }

    [Fact]
    public void BlockingHighBlocksButMediumDoesNot()
    {
        var policy = new ReviewPolicy(new ReviewPolicyOptions());
        Assert.False(policy.Passes(TestData.Review(ReviewDecision.Pass, FindingSeverity.High)));
        Assert.True(policy.Passes(TestData.Review(ReviewDecision.Pass, FindingSeverity.Medium)));
    }

    [Fact]
    public void ExplicitFailBlocksEvenWithoutFindings()
        => Assert.False(new ReviewPolicy(new ReviewPolicyOptions()).Passes(new ReviewResult(ReviewDecision.Fail, [], "failed")));

    [Theory]
    [InlineData("architecture")]
    [InlineData("unitTests")]
    [InlineData("regressionTests")]
    [InlineData("responsiveDesign")]
    [InlineData("overflowSafety")]
    public void BlockingEngineeringQualityFailureBlocks(string category)
    {
        var quality = new QualityAssessment(category, QualityStatus.Fail, "required standard violated", true);
        var review = new ReviewResult(ReviewDecision.Pass, [], "reviewed", Quality: [quality]);

        Assert.False(new ReviewPolicy(new ReviewPolicyOptions()).Passes(review));
    }

    [Fact]
    public void NotApplicableQualityCategoryDoesNotBlock()
    {
        var quality = new QualityAssessment("responsiveDesign", QualityStatus.NotApplicable, "not a UI task");
        var review = new ReviewResult(ReviewDecision.Pass, [], "reviewed", Quality: [quality]);

        Assert.True(new ReviewPolicy(new ReviewPolicyOptions()).Passes(review));
    }

    [Fact]
    public void PrototypeConformityPassRequiresEvidenceFromBothSides()
    {
        var quality = new QualityAssessment("prototypeConformity", QualityStatus.Pass, "compared", true);
        var policy = new ReviewPolicy(new ReviewPolicyOptions());

        Assert.False(policy.Passes(new ReviewResult(ReviewDecision.Pass, [], "reviewed", Quality: [quality])));
        Assert.True(policy.Passes(new ReviewResult(
            ReviewDecision.Pass,
            [],
            "reviewed",
            Quality: [quality],
            PrototypeEvidence: new PrototypeReviewEvidence("T01", "reference.png", "T01", "flutter.png"))));
    }
}
