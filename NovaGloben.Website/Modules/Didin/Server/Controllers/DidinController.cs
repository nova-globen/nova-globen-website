using System;
using Didin.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// Refuses, with 404, every request not resolved to the Didin site. The host maps a module's controllers for every
    /// site of the installation and strips a folder site's folder from "api/..." addresses, so without this a request to
    /// another site's /api/catalog would reach Didin. A resource filter: it runs before anything is bound or read.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DidinSiteOnlyAttribute : Attribute, IResourceFilter
    {
        internal const string ItemKey = "Didin.Site";

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (DidinSite.From(context.HttpContext) is { } site)
            {
                context.HttpContext.Items[ItemKey] = site;
            }
            else
            {
                context.Result = new NotFoundResult();
            }
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }

    /// <summary>The base of every Didin controller: it answers on the Didin site only, and knows which site that is.</summary>
    [DidinSiteOnly]
    public abstract class DidinController : ControllerBase
    {
        /// <summary>The Didin site this request was resolved to (set by <see cref="DidinSiteOnlyAttribute"/>).</summary>
        protected DidinSite Site => (DidinSite)HttpContext.Items[DidinSiteOnlyAttribute.ItemKey];
    }
}
