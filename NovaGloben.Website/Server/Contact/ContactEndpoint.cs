#nullable enable
using System;
using System.Threading.Tasks;
using Stomme.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NovaGloben.Website.Contact
{
    /// <summary>
    /// The contact form's handler: a plain HTML form post to /contact-form (or /sv/contact-form), so the
    /// form needs no script of its own. In order: the antiforgery token, the honeypot, the rate limit, the fields, the
    /// Turnstile token, then the email. It always ends in a 303 back to the contact page with a status - never an error
    /// page, and never anything the visitor typed in the address.
    /// </summary>
    public static class ContactEndpoint
    {
        private const long MaxBody = 64 * 1024;

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            var path = context.Request.Path.Value ?? "";
            var folder = Folder(path);
            if (!HttpMethods.IsPost(context.Request.Method) || folder == null)
            {
                await next();
                return;
            }

            var back = folder.Length == 0 ? "/contact" : "/" + folder + "/contact";
            var services = context.RequestServices;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ContactEndpoint));
            string status;
            try
            {
                status = await HandleAsync(context, services, folder.Length == 0 ? "en" : folder, logger);
            }
            catch (Exception ex) when (ex is InvalidOperationException or BadHttpRequestException or System.IO.InvalidDataException)
            {
                // ASP.NET's antiforgery middleware has already marked a post without a valid token, and reading its form
                // then throws (measured: a 500); a malformed body throws too. Either way: refused, with the same answer.
                logger.LogInformation("Contact form: a post was refused ({Reason}).", ex.GetType().Name);
                status = ContactKeys.StatusError;
            }
            context.Response.StatusCode = StatusCodes.Status303SeeOther;
            context.Response.Headers.Location = $"{back}?{ContactKeys.StatusParameter}={status}#contact-status";
            context.Response.Headers.CacheControl = "no-store";
        }

        /// <summary>The site folder of a post to the handler ("" for the root site), or null when the path is not the handler's.</summary>
        public static string? Folder(string path)
        {
            if (path.Equals("/" + ContactKeys.PostPath, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            if (path.Equals("/sv/" + ContactKeys.PostPath, StringComparison.OrdinalIgnoreCase))
            {
                return "sv";
            }
            return null;
        }

        private static async Task<string> HandleAsync(HttpContext context, IServiceProvider services, string language, ILogger logger)
        {
            if (!context.Request.HasFormContentType || context.Request.ContentLength is null or > MaxBody)
            {
                return ContactKeys.StatusInvalid;
            }

            var antiforgery = services.GetRequiredService<IAntiforgery>();
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                logger.LogInformation("Contact form: a post without a valid antiforgery token was refused.");
                return ContactKeys.StatusError;
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!string.IsNullOrEmpty(form[ContactKeys.Honeypot].ToString()))
            {
                // A bot filled the hidden field: answer as if sent, so it learns nothing; send nothing.
                logger.LogInformation("Contact form: a post that filled the honeypot was dropped.");
                return ContactKeys.StatusSent;
            }

            var address = ClientAddress(context);
            if (!services.GetRequiredService<ContactRateLimit>().TryAcquire(address))
            {
                logger.LogInformation("Contact form: the rate limit refused a post.");
                return ContactKeys.StatusLimit;
            }

            var (message, invalid) = ContactMessage.Read(form, language);
            if (message == null)
            {
                logger.LogInformation("Contact form: a post with invalid fields ({Fields}) was refused.", string.Join(",", invalid));
                return ContactKeys.StatusInvalid;
            }

            if (!await services.GetRequiredService<Turnstile>().VerifyAsync(form[ContactKeys.TurnstileResponse].ToString(), address, context.RequestAborted))
            {
                return ContactKeys.StatusVerify;
            }

            var alias = await services.GetRequiredService<ITenantManager>().GetAliasAsync();
            if (alias == null)
            {
                return ContactKeys.StatusError;
            }
            return await services.GetRequiredService<ContactMailer>().SendAsync(alias.SiteId, message, context.RequestAborted)
                ? ContactKeys.StatusSent
                : ContactKeys.StatusError;
        }

        /// <summary>The visitor's address: Cloudflare's CF-Connecting-IP when the request came through it, else the connection's.</summary>
        public static string ClientAddress(HttpContext context)
        {
            var cloudflare = context.Request.Headers["CF-Connecting-IP"].ToString();
            return System.Net.IPAddress.TryParse(cloudflare, out var parsed) ? parsed.ToString() : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
