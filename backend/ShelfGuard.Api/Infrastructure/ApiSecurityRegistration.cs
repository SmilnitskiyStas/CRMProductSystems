using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>Shared by the deployed API and isolated HTTP acceptance tests.</summary>
public static class ApiSecurityRegistration
{
    public const string ConsumerSupportHubPath = "/api/hubs/consumer-support";

    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
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

            // TASK-333: public landing lead capture — 5 req/min per client IP (spam mitigation)
            options.AddPolicy("public-leads", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window      = TimeSpan.FromMinutes(1),
                        QueueLimit  = 0,
                    }));

            // Forgot-password — 5 req/min per client IP. This endpoint queues a recovery
            // email, unlike a plain login attempt — same limit
            // shape as public-leads for that reason.
            options.AddPolicy("auth-forgot-password", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window      = TimeSpan.FromMinutes(1),
                        QueueLimit  = 0,
                    }));

            // TASK-554: RetailersController.GetRetailerPublic (GET /api/v1/retailers/{slug}/public) is
            // the QR/deep-link onboarding fallback (TASK-549) and, unlike every other action on that
            // controller, carries [AllowAnonymous] — no ConsumerAccount JWT, no per-account
            // accountability. That makes it the most attractive target on the whole consumer-platform
            // surface for slug enumeration/scraping (unknown/inactive/loyalty-less/paused slugs all
            // collapse to the same 404, so brute-forcing slugs costs an attacker nothing but requests).
            // 20 req/min per IP is deliberately looser than public-leads (5/min, spam-cost mitigation for
            // a side-effectful submit) — this is a read-only lookup, and legitimate traffic can burst from
            // a single IP (shared retail wifi/CGNAT, several shoppers scanning the same in-store QR code
            // within the same minute).
            options.AddPolicy("retailer-public-lookup", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window      = TimeSpan.FromMinutes(1),
                        QueueLimit  = 0,
                    }));

            // Cashier app: register activation (single-use code) - 10 req/min per client IP.
            options.AddPolicy("register-activate", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window      = TimeSpan.FromMinutes(1),
                        QueueLimit  = 0,
                    }));

            // Cashier app: PIN sign-in - 20 req/min per client IP (a till shares one IP; per-user and
            // per-register lockouts in RegisterService are the real brute-force defence).
            options.AddPolicy("register-session", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window      = TimeSpan.FromMinutes(1),
                        QueueLimit  = 0,
                    }));

            // Cashier app: PIN generate/clear by managers - 10 req/min per acting user.
            options.AddPolicy("register-pin-manage", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                  ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window      = TimeSpan.FromMinutes(1),
                        QueueLimit  = 0,
                    }));

            // managed-AI Phase 4b: the consumer AI assistant makes a real (paid) model call per
            // request. Partitioned by consumer_account_id (falls back to IP for an odd tokenless
            // hit) — 6 questions / 3 min per consumer keeps a single account from running up the
            // provider's model bill while leaving room for a normal back-and-forth.
            // Shelf-photo AI check: one paid vision call per upload; partitioned per user.
            options.AddPolicy("shelf-scan-ai", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                  ?? httpContext.User.FindFirst("sub")?.Value
                                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                                  ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window      = TimeSpan.FromMinutes(3),
                        QueueLimit  = 0,
                    }));

            options.AddPolicy("consumer-ai", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.FindFirst("consumer_account_id")?.Value
                                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                                  ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 6,
                        Window      = TimeSpan.FromMinutes(3),
                        QueueLimit  = 0,
                    }));
        });

        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        // TASK-625: single source of truth for the realtime Hub's route — read both here (query-string
        // bearer-token fallback below) and at the MapHub call near the bottom of this file, so the two
        // can never drift apart.


        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = configuration["Jwt:Issuer"],
                    // Staff/consumer tokens use the base audience; cashier-app operator tokens a separate one
                    // that OperatorTokenValidation only lets through on [AllowOperatorToken] endpoints.
                    ValidAudiences           = new[]
                    {
                        configuration["Jwt:Audience"],
                        configuration["Jwt:Audience"] + ShelfGuard.Application.Services.JwtAudiences.RegisterSuffix,
                    },
                    IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ClockSkew                = TimeSpan.Zero,
                };

                // TASK-625: browser/RN SignalR clients cannot set an Authorization header on the
                // WebSocket handshake request, so the client library instead appends
                // "?access_token=..." to the connection URL (standard SignalR pattern). Restricted to
                // exactly the Hub path — without the PathStartsWithSegments guard this would accept a
                // bearer token via query string on every endpoint in the API, which is a token-leakage
                // risk (query strings land in access logs, proxy logs, and browser history).
                var registerAudience = configuration["Jwt:Audience"] + ShelfGuard.Application.Services.JwtAudiences.RegisterSuffix;
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ctx => AccessSessionValidation.ValidateAsync(ctx, registerAudience),
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments(ConsumerSupportHubPath))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        // ADR-020 (TASK-346): stateless singleton handler backing every RoleOrCapability* policy.
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RoleOrCapabilityHandler>();
        services.AddAuthorization(AppPolicies.Configure);

        return services;
    }

    public static IApplicationBuilder UseApiSecurity(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        // Subject-partitioned limits must receive authenticated claims (SEC-012).
        app.UseRateLimiter();
        app.UseAuthorization();
        return app;
    }
}
