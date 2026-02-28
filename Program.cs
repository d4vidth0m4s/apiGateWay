using apiGateWay.Extensions;

var builder = WebApplication.CreateBuilder(args);

var MyAllowSpecificOrigins = "AllowFrontend";
var frontendUrls = builder.Configuration["fronendUrl"]?.Split(",") 
                   ?? new[] { "http://localhost:4002","http://localhost:4001" };
var internalSecret = builder.Configuration["InternalSecret"];

if (string.IsNullOrWhiteSpace(internalSecret))
    internalSecret = builder.Configuration["INTERNAL_SECRET"];

if (string.IsNullOrWhiteSpace(internalSecret))
    internalSecret = builder.Configuration["expectedSecret"];

if (string.IsNullOrWhiteSpace(internalSecret))
    throw new InvalidOperationException(
        "InternalSecret no configurado. Define 'InternalSecret' en appsettings o la variable de entorno 'INTERNAL_SECRET'.");

builder.Services.AddCors(options =>
{
    options.AddPolicy(MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins(frontendUrls)  // acepta array
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.AddJwtAuthentication();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy =>
        policy.RequireAuthenticatedUser());
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseWebSockets();
app.UseCors(MyAllowSpecificOrigins);
app.UseAuthentication();
app.UseAuthorization();
app.UseHeaderInjection(internalSecret);
app.MapGet("/", () => "Communication with API Gateway"); 
app.MapReverseProxy();
app.Run();
