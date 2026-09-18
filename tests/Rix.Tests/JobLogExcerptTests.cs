using Rix.Repository;

namespace Rix.Tests;

/// <summary>Covers the excerpt's shaping rules on their own, without the HTTP round trip that
/// <see cref="CiFailureHostTests"/> puts in front of them - these are decisions about what fits in
/// a prompt, so they are stated here in terms of logs and budgets rather than canned responses.</summary>
[TestClass]
public class JobLogExcerptTests
{
    private static readonly FailedJob Build = new(1, "build");
    private static readonly FailedJob Test = new(2, "test");

    [TestMethod]
    public void TailCharsPerJob_SplitsTheBudgetEvenly_AcrossTheIncludedJobs()
    {
        Assert.AreEqual(300, JobLogExcerpt.TailCharsPerJob(900, 3));
        Assert.AreEqual(900, JobLogExcerpt.TailCharsPerJob(900, 1));
    }

    [TestMethod]
    public void TailCharsPerJob_KeepsOneCharPerJob_WhenTheBudgetIsSmallerThanTheJobCount()
    {
        // Rounding down would reach zero here, which reads as "fetch the log, keep none of it" -
        // a worse answer than a useless-but-present one character.
        Assert.AreEqual(1, JobLogExcerpt.TailCharsPerJob(2, 5));
        Assert.AreEqual(1, JobLogExcerpt.TailCharsPerJob(0, 1));
    }

    [TestMethod]
    public void Render_HeadsEachJobsLog_WithItsOwnName()
    {
        var excerpt = JobLogExcerpt.Render([Build, Test], ["boom", "kaboom"], omittedJobs: 0);

        Assert.AreEqual("===== build =====\nboom\n===== test =====\nkaboom", excerpt);
    }

    [TestMethod]
    public void Render_NamesTheCountOfJobsItLeftOut_RatherThanDroppingThemSilently()
    {
        var excerpt = JobLogExcerpt.Render([Build], ["boom"], omittedJobs: 17);

        StringAssert.EndsWith(excerpt, "\n===== 17 further failed job(s) omitted =====");
    }

    [TestMethod]
    public void Render_AddsNoFooter_WhenEveryFailedJobIsIncluded()
    {
        var excerpt = JobLogExcerpt.Render([Build], ["boom"], omittedJobs: 0);

        Assert.AreEqual("===== build =====\nboom", excerpt);
    }
}
