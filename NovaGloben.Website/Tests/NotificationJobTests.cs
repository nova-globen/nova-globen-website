using Microsoft.Extensions.Logging.Abstractions;
using NovaGloben.Website.Sites;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>A host with a mail relay switches Stomme's notification job on, so a password reset mail leaves.</summary>
public class NotificationJobTests
{
    private sealed class Jobs : IJobRepository
    {
        public List<Job> All { get; } = new();
        public int Updates { get; private set; }

        public Task<IEnumerable<Job>> GetJobsAsync() => Task.FromResult<IEnumerable<Job>>(All);
        public Task<Job> AddJobAsync(Job job) { job.JobId = All.Count + 1; All.Add(job); return Task.FromResult(job); }
        public Task<Job> UpdateJobAsync(Job job) { Updates++; return Task.FromResult(job); }
        public Task<Job> GetJobAsync(string jobType) => Task.FromResult(All.FirstOrDefault(j => j.JobType == jobType)!);
        public Task<Job> GetJobAsync(int jobId) => Task.FromResult(All.FirstOrDefault(j => j.JobId == jobId)!);
        public Task<Job> GetJobAsync(int jobId, bool tracking) => GetJobAsync(jobId);
        public Task DeleteJobAsync(int jobId) { All.RemoveAll(j => j.JobId == jobId); return Task.CompletedTask; }
    }

    private static NotificationJob Template() => new(null!);

    [Fact]
    public async Task A_missing_job_is_added_switched_on_with_stommes_own_schedule()
    {
        var jobs = new Jobs();

        Assert.True(await NotificationJobSwitch.EnsureOnAsync(jobs, Template(), NullLogger.Instance));

        var job = Assert.Single(jobs.All);
        Assert.Equal(NotificationJobSwitch.JobType, job.JobType);
        Assert.True(job.IsEnabled);
        Assert.Equal(Template().Frequency, job.Frequency);
        Assert.Equal(Template().Interval, job.Interval);
    }

    [Fact]
    public async Task A_job_left_off_is_switched_on()
    {
        var jobs = new Jobs();
        jobs.All.Add(new Job { JobId = 7, JobType = NotificationJobSwitch.JobType, IsEnabled = false });

        Assert.True(await NotificationJobSwitch.EnsureOnAsync(jobs, Template(), NullLogger.Instance));

        Assert.True(Assert.Single(jobs.All).IsEnabled);
        Assert.Equal(1, jobs.Updates);
    }

    [Fact]
    public async Task A_job_already_on_is_left_alone()
    {
        var jobs = new Jobs();
        jobs.All.Add(new Job { JobId = 7, JobType = NotificationJobSwitch.JobType, IsEnabled = true });

        Assert.False(await NotificationJobSwitch.EnsureOnAsync(jobs, Template(), NullLogger.Instance));

        Assert.Equal(0, jobs.Updates);
        Assert.Single(jobs.All);
    }
}
