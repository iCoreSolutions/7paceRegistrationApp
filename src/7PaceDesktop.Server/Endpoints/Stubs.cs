namespace PaceDesktop.Server;

// Each method here is replaced by a real endpoint group in Tasks 7, 8 and 9.
public static class EndpointStubs
{
    public static void MapConfigEndpoints(this WebApplication app) =>
        app.MapPut("/api/config", () => Results.Ok()).AddEndpointFilter<ClientHeaderFilter>();

    public static void MapMonthEndpoints(this WebApplication app) { }

    public static void MapRegisterEndpoints(this WebApplication app) { }
}
