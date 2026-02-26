using apiGateWay.Extensions;

var builder = WebApplication.CreateBuilder(args);
var MyAllowSpecificOrigins = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(MyAllowSpecificOrigins, policy =>
    {
        policy.WithOrigins(
            "http://127.0.0.1:3001",
            "http://localhost:3001",
            "https://127.0.0.1:3001",
            "https://localhost:3001",
            "http://127.0.0.1:4002",
            "http://localhost:4002",
            "https://127.0.0.1:4002",
            "https://localhost:4002")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // 👈 necesario para SignalR
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
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseHeaderInjection();
app.MapReverseProxy();

app.Run();