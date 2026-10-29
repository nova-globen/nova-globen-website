using Stomme.Infrastructure;

namespace Lek.Server.Startup
{
    /// <summary>
    /// The games' static files, under <see cref="LekAssets.Root"/>. They are answered before any site is resolved, so this
    /// keys on that path, which only Lek serves:
    /// <list type="bullet">
    /// <item>a 3D model (.glb) is served here, as model/gltf-binary, to a GET and to a HEAD alike - the host's static
    /// files know no type for it and would not serve it;</item>
    /// <item>a page addressed with its content hash (<c>?v=</c>) is immutable for a year; any other file of the games is
    /// revalidated, so an edge or a browser never keeps an old copy past a release.</item>
    /// </list>
    /// </summary>
    public sealed class LekStaticFiles : IServerPipelineStartup
    {
        public const string Immutable = "public, max-age=31536000, immutable";
        public const string Revalidate = "no-cache";
        public const string ModelType = "model/gltf-binary";

        public ServerPipelineStage Stage => ServerPipelineStage.BeforeStaticFiles;

        public static bool IsOurs(HttpRequest request) =>
            (request.Path.Value ?? "").StartsWith(LekAssets.Root, StringComparison.OrdinalIgnoreCase);

        public static bool IsModel(HttpRequest request) =>
            IsOurs(request) && (request.Path.Value ?? "").EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

        /// <summary>A request this answers itself: a model, read (GET) or asked about (HEAD).</summary>
        public static bool AnswersModel(HttpRequest request) =>
            IsModel(request) && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method));

        public static string CacheControl(HttpRequest request) =>
            string.IsNullOrEmpty(request.Query["v"]) ? Revalidate : Immutable;

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) =>
            app.Use(async (context, next) =>
            {
                if (!IsOurs(context.Request))
                {
                    await next(context);
                    return;
                }
                var header = CacheControl(context.Request);
                if (AnswersModel(context.Request))
                {
                    var file = env.WebRootFileProvider.GetFileInfo(context.Request.Path.Value!);
                    if (file.Exists && !file.IsDirectory)
                    {
                        context.Response.ContentType = ModelType;
                        context.Response.ContentLength = file.Length;
                        context.Response.Headers.CacheControl = header;
                        if (HttpMethods.IsGet(context.Request.Method))
                        {
                            await context.Response.SendFileAsync(file, context.RequestAborted);
                        }
                        return;
                    }
                }
                context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                    {
                        context.Response.Headers.CacheControl = header;
                    }
                    return Task.CompletedTask;
                });
                await next(context);
            });
    }
}
