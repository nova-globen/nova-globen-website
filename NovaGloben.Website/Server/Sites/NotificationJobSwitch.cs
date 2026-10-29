using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Stomme's notification job sends every site's queued mail - a password reset link, an account notice - and is
    /// created switched off. A host that names a mail relay for the company site (<c>Website:Smtp:Host</c>) switches it
    /// on, so that mail leaves; Lek does the same for its own. A site without SMTP settings is skipped by the job, and
    /// mail queued more than a day earlier is dropped by it rather than sent, so switching it on sends no backlog.
    /// </summary>
    public static class NotificationJobSwitch
    {
        public static string JobType => Utilities.GetFullTypeName(typeof(NotificationJob).AssemblyQualifiedName);

        /// <summary>Adds the job switched on, or switches the existing one on. True when it changed anything.</summary>
        public static async Task<bool> EnsureOnAsync(IJobRepository jobs, NotificationJob template, ILogger logger)
        {
            var job = await jobs.GetJobAsync(JobType);
            if (job is null)
            {
                await jobs.AddJobAsync(new Job
                {
                    JobType = JobType,
                    Name = template.Name,
                    Frequency = template.Frequency,
                    Interval = template.Interval,
                    StartDate = template.StartDate,
                    EndDate = template.EndDate,
                    MaximumDuration = template.MaximumDuration,
                    RetentionHistory = template.RetentionHistory,
                    IsEnabled = true,
                    IsStarted = true,
                    IsExecuting = false,
                    NextExecution = null,
                });
            }
            else if (!job.IsEnabled)
            {
                job.IsEnabled = true;
                await jobs.UpdateJobAsync(job);
            }
            else
            {
                return false;
            }
            logger.LogInformation("Website: the notification job is on, so the site's account mail (a password reset) leaves through the relay.");
            return true;
        }
    }
}
