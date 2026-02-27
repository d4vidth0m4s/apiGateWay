using apiGateWay.Extensions;

var builder = WebApplication.CreateBuilder(args);

var MyAllowSpecificOrigins = "AllowFrontend";
var frontendUrl = builder.Configuration["FRONTEND_URL"] ?? "http://localhost:3000";

builder.Services.AddCors(options =>
{
    options.AddPolicy(MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins(frontendUrl)
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
app.UseHeaderInjection();
app.MapReverseProxy();

app.Run();