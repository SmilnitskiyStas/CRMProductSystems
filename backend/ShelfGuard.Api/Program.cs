using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application;
using ShelfGuard.Infrastructure;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Local secrets (gitignored) — e.g. PRRO license key / cashier creds for dev runs.
// Env vars (PRRO__*, Claude__* etc.) still take precedence in Docker/production.
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// All error responses follow the { "error": "..." } contract (api-contracts.md):
// - InvalidModelStateResponseFactory covers automatic 400s from model binding/validation
// - ErrorBodyClientErrorFactory covers body-less results (bare NotFound(), Forbid(), ...)
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Invalid request.";
            return new BadRequestObjectResult(new { error = message });
        });
builder.Services.AddSingleton<IClientErrorFactory, ErrorBodyClientErrorFactory>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = Microsoft.OpenApi.Models.ParameterLocation.Header,
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// TASK-329: the API sits behind nginx — take the client IP from X-Forwarded-For
// so rate limiting and login auditing see the real caller, not the proxy.
// KnownNetworks/KnownProxies are cleared because the API port is bound to
// 127.0.0.1 (devops task) — only nginx can reach it.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// TASK-329: per-IP rate limiting on auth endpoints (brute-force mitigation).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"Too many requests. Try again later.\"}", ct);
    };

    // login + 2FA verify: 10 req/min per client IP
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));

    // refresh: 30 req/min per client IP (frontend polls every 15 min per tab)
    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization(AppPolicies.Configure);

var allowedOrigins = (builder.Configuration["Cors:Origins"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

var app = builder.Build();

// Auto-apply pending migrations on startup (dev convenience)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Must run FIRST so downstream middleware (rate limiter, auth, logging) sees the real client IP.
app.UseForwardedHeaders();

// TASK-329: security headers for API responses (CSP not needed for a JSON API).
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"]        = "DENY";
    headers["Referrer-Policy"]        = "no-referrer";
    headers["Permissions-Policy"]     = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseCors();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
