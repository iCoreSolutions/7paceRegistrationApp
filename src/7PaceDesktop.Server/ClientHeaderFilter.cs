namespace PaceDesktop.Server;

/// <summary>
/// Requires a custom header on mutating endpoints. Combined with binding to 127.0.0.1 and
/// configuring no CORS policy, this stops a page on another origin from reaching the local
/// API: a custom header forces a preflight, and without a CORS policy the browser refuses it.
/// </summary>
public sealed class ClientHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-Pace-Client";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Headers[HeaderName].ToString() != "1")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        return await next(context);
    }
}
