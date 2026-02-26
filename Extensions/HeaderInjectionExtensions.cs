using System.Security.Claims;

namespace apiGateWay.Extensions
{


    public static class HeaderInjectionExtensions
    {
        private static readonly Dictionary<string, string[]> _headersPerRoute = new()
        {
            { "/Comercios/CrearComercios",   new[] { "X-User-Id" } },
            {"/Comercios/ok", new[] {"X-User-Id"} },
            {"/Comercios/ActualizarComercio", new[] { "X-User-Id" } },

        };
        public static IApplicationBuilder UseHeaderInjection(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                var config = context.RequestServices.GetRequiredService<IConfiguration>();
                var secret = config["InternalSecret"]
                         ?? throw new InvalidOperationException("InternalSecret no configurado");
                context.Request.Headers["X-Internal-Secret"] = secret;

                var path = context.Request.Path.Value ?? "";
                var route = _headersPerRoute.Keys.FirstOrDefault(r => path.StartsWith(r));

                if (route is not null)
                {
                    var allowedHeaders = _headersPerRoute[route];

                    if (allowedHeaders.Contains("X-User-Id"))
                        context.Request.Headers["X-User-Id"] = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    

                }
                await next();
            });
    }
    }
}
