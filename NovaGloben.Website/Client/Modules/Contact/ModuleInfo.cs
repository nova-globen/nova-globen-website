using Stomme.Models;
using Stomme.Modules;

namespace NovaGloben.Website.Client.Modules.Contact
{
    /// <summary>
    /// The contact form: a plain HTML form posted to the server's handler (Server/Contact), checked there
    /// - fields, honeypot, rate limit, Cloudflare Turnstile - and sent by email to the company's inbox; the site keeps no copy.
    /// </summary>
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Contact Form",
            Description = "The website's contact form: checked on the server and sent by email; nothing is stored.",
            Version = "1.0.0",
            Categories = "Common",
            Owner = "Nova Globen AB",
            PackageName = "NovaGloben.Website.Client",
            Dependencies = "NovaGloben.Website.Shared.Stomme"
        };
    }
}
