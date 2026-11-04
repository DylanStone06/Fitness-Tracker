namespace FitnessTracker.Web.Infrastructure;

/// <summary>
/// Requirements §9, from the security review of 14 October 2026: headers on every response, set just before it is
/// sent so error pages and redirects get them too.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Only this site's own scripts, styles and fonts, and its images (Bootstrap draws some icons as data: images);
    /// no plugins; forms post only here; no other site may show the app in a frame (clickjacking). No inline script
    /// or style is allowed, so injected markup can't run.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                // For browsers that don't read frame-ancestors.
                headers.XFrameOptions = "DENY";
                // Page addresses (which contain record ids) aren't sent to other sites.
                headers["Referrer-Policy"] = "same-origin";

                // Pages hold personal data, so the browser doesn't keep them: Back after signing out on a shared
                // computer can't show them. Style sheets and scripts keep their normal caching.
                if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            await next();
        });
}
