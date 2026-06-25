// ASP.NET Core host for Traveller Map.
// Replaces Global.asax.cs and IIS/Web.config hosting.
// Route stubs return 501; filled in by issues #5–#8.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using System;

var builder = WebApplication.CreateBuilder(args);


// Port 8080 is set via ASPNETCORE_URLS env var in the container (issue #9).
// For local dev: set ASPNETCORE_URLS=http://+:8080 or use the default port.

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

// Static files: serve from the project root (index.html, index.js, etc.),
// with custom MIME types for Traveller data formats.
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".sec"]   = "text/plain";
provider.Mappings[".msec"]  = "text/plain";
provider.Mappings[".tab"]   = "text/plain";
provider.Mappings[".t5col"] = "text/plain";
provider.Mappings[".t5tab"] = "text/plain";

// Serve frontend files from the project root (content root), not the default wwwroot/.
// Use GetCurrentDirectory() since ContentRootPath may not match during dotnet run.
var webRootPath = System.IO.Path.GetFullPath(System.IO.Directory.GetCurrentDirectory());
var contentRoot = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRootPath);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = contentRoot });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = contentRoot,
    ContentTypeProvider = provider,
});

// AdminKey middleware: protect /admin/* routes.
string adminKey = app.Configuration["AdminKey"] ?? "";
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin", System.StringComparison.OrdinalIgnoreCase))
    {
        string? requestKey = context.Request.Query["key"].ToString();
        if (!string.IsNullOrEmpty(adminKey) && requestKey != adminKey)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Forbidden");
            return;
        }
    }
    await next(context);
});

// Stub delegate used until handlers are ported in issues #5–#8.
static IResult Stub(string handler) =>
    Results.StatusCode(501);

// ── Navigation redirects ────────────────────────────────────────────────────

app.MapGet("/go/{sector}", (string sector) =>
    Results.Redirect($"/?sector={sector}", permanent: false));

app.MapGet("/go/{sector}/{hex}", (string sector, string hex) =>
    System.Text.RegularExpressions.Regex.IsMatch(hex, @"^\d{4}$")
        ? Results.Redirect($"/?sector={sector}&hex={hex}", permanent: false)
        : Results.Redirect($"/?sector={sector}&subsector={hex}", permanent: false));

app.MapGet("/booklet/{sector}", (string sector) =>
    Results.Redirect($"/make/booklet?sector={sector}", permanent: false));

app.MapGet("/sheet/{sector}/{hex}", (string sector, string hex) =>
    Results.Redirect($"/print/world?sector={sector}&hex={hex}", permanent: false));

// ── Administration ──────────────────────────────────────────────────────────

app.MapMethods("/admin/admin",   ["GET","POST"], () => Stub("AdminHandler"));
app.MapMethods("/admin/flush",   ["GET","POST"], () => Stub("AdminHandler/flush"));
app.MapMethods("/admin/reindex", ["GET","POST"], () => Stub("AdminHandler/reindex"));
app.MapMethods("/admin/profile", ["GET","POST"], () => Stub("AdminHandler/profile"));
app.MapMethods("/admin/uptime",  ["GET","POST"], () => Stub("AdminHandler/uptime"));
app.MapGet("/admin/codes",    () => Stub("CodesHandler"));
app.MapGet("/admin/routes",   () => Stub("RoutesHandler"));
app.MapGet("/admin/dump",     () => Stub("DumpHandler"));
app.MapGet("/admin/errors",   () => Stub("ErrorsHandler"));
app.MapGet("/admin/overview", () => Stub("OverviewHandler"));

// ── Search / route ──────────────────────────────────────────────────────────

app.MapGet("/api/search", () => Stub("SearchHandler"));
app.MapGet("/api/route",  () => Stub("RouteHandler"));

// ── Rendering ───────────────────────────────────────────────────────────────

app.MapMethods("/api/jumpmap", ["GET","POST"], () => Stub("JumpMapHandler"));
app.MapMethods("/api/poster",  ["GET","POST"], () => Stub("PosterHandler"));
app.MapMethods("/api/poster/{sector}", ["GET","POST"], (string sector) => Stub("PosterHandler"));
app.MapMethods("/api/poster/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}", ["GET","POST"],
    (string sector, string quadrant) => Stub("PosterHandler/quadrant"));
app.MapMethods("/api/poster/{sector}/{subsector}", ["GET","POST"],
    (string sector, string subsector) => Stub("PosterHandler/subsector"));
app.MapGet("/api/tile", () => Stub("TileHandler"));

// ── Location queries ────────────────────────────────────────────────────────

app.MapGet("/api/coordinates", () => Stub("CoordinatesHandler"));
app.MapGet("/api/credits",     () => Stub("CreditsHandler"));
app.MapGet("/api/jumpworlds",  () => Stub("JumpWorldsHandler"));

// ── Data retrieval (API-centric) ────────────────────────────────────────────

app.MapGet("/api/universe", () => Stub("UniverseHandler"));
app.MapGet("/api/milieux",  () => Stub("MilieuxCodesHandler"));

app.MapMethods("/api/sec",                          ["GET","POST"], () => Stub("SECHandler"));
app.MapMethods("/api/sec/{sector}",                 ["GET","POST"], (string sector) => Stub("SECHandler"));
app.MapMethods("/api/sec/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}", ["GET","POST"],
    (string sector, string quadrant) => Stub("SECHandler/quadrant"));
app.MapMethods("/api/sec/{sector}/{subsector}",     ["GET","POST"],
    (string sector, string subsector) => Stub("SECHandler/subsector"));

app.MapMethods("/api/metadata",          ["GET","POST"], () => Stub("SectorMetaDataHandler"));
app.MapMethods("/api/metadata/{sector}", ["GET","POST"], (string sector) => Stub("SectorMetaDataHandler"));

app.MapGet("/api/msec",          () => Stub("MSECHandler"));
app.MapGet("/api/msec/{sector}", (string sector) => Stub("MSECHandler"));

// ── Data retrieval (RESTful /data) ──────────────────────────────────────────

app.MapGet("/data", () => Stub("UniverseHandler"));

// Sector
app.MapMethods("/data/{sector}",        ["GET","POST"], (string sector) => Stub("SECHandler"));
app.MapGet("/data/{sector}/sec",        (string sector) => Stub("SECHandler"));
app.MapGet("/data/{sector}/tab",        (string sector) => Stub("SECHandler/tab"));
app.MapGet("/data/{sector}/coordinates",(string sector) => Stub("CoordinatesHandler"));
app.MapGet("/data/{sector}/credits",    (string sector) => Stub("CreditsHandler"));
app.MapGet("/data/{sector}/metadata",   (string sector) => Stub("SectorMetaDataHandler"));
app.MapGet("/data/{sector}/msec",       (string sector) => Stub("MSECHandler"));
app.MapMethods("/data/{sector}/image",  ["GET","POST"], (string sector) => Stub("PosterHandler"));
app.MapGet("/data/{sector}/booklet",    (string sector) =>
    Results.Redirect($"/make/booklet?sector={sector}", permanent: false));

// Quadrant
app.MapGet("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}",
    (string sector, string quadrant) => Stub("SECHandler/quadrant"));
app.MapGet("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}/sec",
    (string sector, string quadrant) => Stub("SECHandler/quadrant"));
app.MapGet("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}/tab",
    (string sector, string quadrant) => Stub("SECHandler/quadrant/tab"));
app.MapMethods("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}/image", ["GET","POST"],
    (string sector, string quadrant) => Stub("PosterHandler/quadrant"));

// Subsector by single letter (A-P)
app.MapGet("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}",
    (string sector, string subsector) => Stub("SECHandler/subsector-index"));
app.MapGet("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}/sec",
    (string sector, string subsector) => Stub("SECHandler/subsector-index"));
app.MapGet("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}/tab",
    (string sector, string subsector) => Stub("SECHandler/subsector-index/tab"));
app.MapMethods("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}/image", ["GET","POST"],
    (string sector, string subsector) => Stub("PosterHandler/subsector"));

// World (4-digit hex)
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}",
    (string sector, string hex) => Stub("JumpWorldsHandler/world"));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/coordinates",
    (string sector, string hex) => Stub("CoordinatesHandler"));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/credits",
    (string sector, string hex) => Stub("CreditsHandler"));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/jump/{jump:int}",
    (string sector, string hex, int jump) => Stub("JumpWorldsHandler"));
app.MapMethods("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/image", ["GET","POST"],
    (string sector, string hex) => Stub("JumpMapHandler"));
app.MapMethods("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/jump/{jump:int}/image", ["GET","POST"],
    (string sector, string hex, int jump) => Stub("JumpMapHandler"));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/sheet",
    (string sector, string hex) =>
        Results.Redirect($"/print/world?sector={sector}&hex={hex}", permanent: false));

// Subsector by name (catch-all, must come after hex and single-letter patterns)
app.MapGet("/data/{sector}/{subsector}",
    (string sector, string subsector) => Stub("SECHandler/subsector-name"));
app.MapGet("/data/{sector}/{subsector}/sec",
    (string sector, string subsector) => Stub("SECHandler/subsector-name"));
app.MapGet("/data/{sector}/{subsector}/tab",
    (string sector, string subsector) => Stub("SECHandler/subsector-name/tab"));
app.MapMethods("/data/{sector}/{subsector}/image", ["GET","POST"],
    (string sector, string subsector) => Stub("PosterHandler/subsector-name"));

// ── T5SS reference data ─────────────────────────────────────────────────────

app.MapGet("/t5ss/allegiances", () => Stub("AllegianceCodesHandler"));
app.MapGet("/t5ss/sophonts",    () => Stub("SophontCodesHandler"));

// ───────────────────────────────────────────────────────────────────────────

app.Run();
