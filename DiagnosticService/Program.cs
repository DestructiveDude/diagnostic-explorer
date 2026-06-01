using System.Configuration;
using System.Text.Json.Serialization;
using DiagnosticExplorer;
using DiagnosticExplorer.Common;
using Diagnostics.Service.Common.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddWindowsService(options => {
            options.ServiceName = "DiagnosticExplorer";
        });

        builder.Configuration.AddJsonFile(Expand(Path.Combine("Config", "settings.json"))!);

        builder.Services.Configure<DiagServiceSettings>(builder.Configuration.GetSection(nameof(DiagServiceSettings)));
        builder.Services.AddDiagnosticExplorer(builder.Configuration);

        var services = builder.Services;

        // CORS services only; the policy is selected from configuration in the request pipeline
        // below (H2). The old named "CorsPolicy" was dead config (never applied) and is removed.
        services.AddCors();

        // H1: API-key authentication is opt-in. In None mode (the default) no scheme is registered
        // and the hubs stay open — today's behaviour; in ApiKey mode every hub connection must
        // present a valid key. Bind the whole settings section ONCE here (rather than a second
        // GetValue over a hand-built key path) so the registration mode and the pipeline below read
        // the same value — no drift, and an unparseable AuthMode throws here instead of silently
        // defaulting to None (fail closed, not open).
        DiagServiceSettings configuredSettings =
            builder.Configuration.GetSection(nameof(DiagServiceSettings)).Get<DiagServiceSettings>() ?? new();
        AuthMode authMode = configuredSettings.Security.AuthMode;

        if (authMode != AuthMode.None)
        {
            services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
            services.AddAuthorization();
        }
        // Register the managers as hosted services so the host drives Start/StopAsync eagerly
        // and deterministically. They were AddSingleton-only and self-wired their lifecycle in
        // their constructors via ApplicationStarted.Register — which only fired if the singleton
        // happened to be constructed (lazily, on first hub injection) before ApplicationStarted,
        // so retro logging / alert decay could silently never start. The AddHostedService factory
        // resolves the same singleton the hubs inject.
        services.AddSingleton<RealtimeManager>();
        services.AddHostedService(sp => sp.GetRequiredService<RealtimeManager>());
        services.AddSingleton<RetroManager>();
        services.AddHostedService(sp => sp.GetRequiredService<RetroManager>());
        bool enableDetailedHubErrors = builder.Environment.IsDevelopment();
        services.AddSignalR().AddHubOptions<DiagnosticHub>(options => {
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB — finite cap (was int.MaxValue, an unbounded-payload DoS)
            options.MaximumParallelInvocationsPerClient = 5;
        }).AddHubOptions<WebHub>(options => {
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB — finite cap (was int.MaxValue, an unbounded-payload DoS)
            options.MaximumParallelInvocationsPerClient = 5;
            // Only expose detailed hub exception text to browser clients in development; in production
            // it leaks internal error detail. enableDetailedHubErrors is IsDevelopment(). (A4)
            options.EnableDetailedErrors = enableDetailedHubErrors;
        }).AddJsonProtocol(options => {
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
        });

        string spaDir = builder.Configuration.GetValue<string>("DiagServiceSettings:SpaDirectory")!;
        string spaPath = Expand(spaDir)!;
        services.AddSpaStaticFiles(conf => { conf.RootPath = spaPath; });

        var app = builder.Build();

        var settings = app.Services.GetRequiredService<IOptions<DiagServiceSettings>>().Value;

        // Fail closed on a half-configured ApiKey mode rather than booting into a broken/insecure
        // state: no usable key would reject every connection (a silent outage), and an empty CORS
        // allowlist under auth would otherwise fall through to credentialed any-origin reflection.
        if (authMode == AuthMode.ApiKey)
        {
            SecuritySettings security = settings.Security;
            if (security.ApiKeys is not { Length: > 0 } || security.ApiKeys.All(string.IsNullOrWhiteSpace))
                throw new ApplicationException(
                    "DiagServiceSettings:Security:AuthMode is ApiKey but no non-empty ApiKeys are configured — every hub connection would be rejected. Configure at least one key.");
            if (security.AllowedCorsOrigins is not { Length: > 0 })
                throw new ApplicationException(
                    "DiagServiceSettings:Security:AuthMode is ApiKey but AllowedCorsOrigins is empty — credentialed any-origin CORS is not allowed with auth enabled. Configure the allowlist.");
        }

        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();
        else
            app.UseExceptionHandler(errorApp => errorApp.Run(async context => {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("An unexpected error occurred.");
            }));

        app.UseRouting();

        // H2: a real allowlist when configured; otherwise keep the historical permissive policy but
        // make the risk visible at startup (reflecting any origin with credentials is a CSRF/exfil
        // surface — see the audit). Never AllowAnyOrigin()+AllowCredentials(), which is invalid.
        string[] corsOrigins = settings.Security.AllowedCorsOrigins;
        if (corsOrigins is { Length: > 0 })
        {
            app.UseCors(policy => policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        }
        else
        {
            app.Logger.LogWarning(
                "DiagServiceSettings:Security:AllowedCorsOrigins is empty — CORS is reflecting ANY origin with credentials (H2). Configure an allowlist to lock this down.");
            app.UseCors(policy => policy
                .SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        }

        // H1: only enforce when opted in; in None mode these are skipped so the hubs stay open.
        if (authMode != AuthMode.None)
        {
            app.UseAuthentication();
            app.UseAuthorization();

            // F9: CORS does not police the WebSocket upgrade, so the allowlist alone doesn't stop a
            // cross-origin browser (holding a key) opening a raw WS to a hub. Validate the Origin
            // header explicitly on the hub paths. A browser always sends Origin on the negotiate
            // POST and the WS upgrade; native clients (the .NET hosting client) send none, so an
            // absent Origin is allowed and remains gated by the API key.
            var allowedOrigins = new HashSet<string>(settings.Security.AllowedCorsOrigins, StringComparer.OrdinalIgnoreCase);
            app.Use(async (context, next) =>
            {
                PathString path = context.Request.Path;
                bool isHub = path.StartsWithSegments("/web-hub") || path.StartsWithSegments("/diagnostics");
                if (isHub)
                {
                    string origin = context.Request.Headers["Origin"].ToString();
                    if (!string.IsNullOrEmpty(origin) && !allowedOrigins.Contains(origin))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                }
                await next();
            });
        }

        app.UseEndpoints(endpoints => {
            var webHub = endpoints.MapHub<WebHub>("/web-hub");
            var diagHub = endpoints.MapHub<DiagnosticHub>("/diagnostics");
            if (authMode != AuthMode.None)
            {
                webHub.RequireAuthorization();
                diagHub.RequireAuthorization();
            }
        });

        if (!settings.UseSpaProxy && !Directory.Exists(spaPath))
            throw new ApplicationException($"Diagnostics SPA directory not found: {spaPath}");

        app.UseSpa(spa => {
            spa.Options.DefaultPage = "/index.html";
            if (!settings.UseSpaProxy)
                app.UseSpaStaticFiles();

            if (settings.UseSpaProxy)
                spa.UseProxyToSpaDevelopmentServer(settings.SpaProxy);
        });

        if (!app.Urls.IsReadOnly)
        {
            app.Urls.Clear();

            foreach (string url in settings.Urls)
                app.Urls.Add(url);
        }

        app.Run();
    }


    static string? Expand(string? path) =>
        path == null
            ? null
            : Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
}