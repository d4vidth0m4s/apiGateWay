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
        public static IApplicationBuilder UseHeaderInjection(this IApplicationBuilder app, string internalSecret)
        {
            if (string.IsNullOrWhiteSpace(internalSecret))
                throw new InvalidOperationException("InternalSecret no configurado");

            return app.Use(async (context, next) =>
            {
                context.Request.Headers["X-Internal-Secret"] = internalSecret;

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
