// ASP.NET Core host for Traveller Map.
// Replaces Global.asax.cs and IIS/Web.config hosting.
using Maps.Database;
using Maps.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);


// Port 8080 is set via ASPNETCORE_URLS env var in the container (issue #9).
// For local dev: set ASPNETCORE_URLS=http://+:8080 or use the default port.

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Wire up the database connection factory based on configuration.
string connectionString = app.Configuration["ConnectionString"] ?? "";
string dbProvider      = app.Configuration["DatabaseProvider"] ?? "mariadb";

if (!string.IsNullOrEmpty(connectionString))
{
    DBUtil.Factory = dbProvider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
        ? new SqlServerConnectionFactory(connectionString)
        : new MariaDbConnectionFactory(connectionString);
}

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

app.MapMethods("/admin/admin", ["GET","POST"], (HttpContext ctx) =>
{
    string? action = ctx.Request.Query["action"].ToString();
    if (action == "flush")
    {
        Maps.SectorMap.Flush();
        return Results.Text("Flushed.");
    }
    if (action == "reindex")
    {
        if (DBUtil.Factory == null)
            return Results.Problem("Database not configured.", statusCode: 503);
        var lines = new System.Collections.Generic.List<string>();
        SearchEngine.PopulateDatabase(Maps.ResourceManager.GetDedicatedInstance(), s => lines.Add(s));
        return Results.Text(string.Join("\n", lines));
    }
    return Results.Text($"Admin. uptime: {DateTime.Now - Maps.GlobalAsax.startup_time}");
});
app.MapMethods("/admin/flush",   ["GET","POST"], () => { Maps.SectorMap.Flush(); return Results.Text("Flushed."); });
app.MapMethods("/admin/reindex", ["GET","POST"], () => Stub("AdminHandler/reindex"));
app.MapMethods("/admin/profile", ["GET","POST"], () => Stub("AdminHandler/profile"));
app.MapMethods("/admin/uptime",  ["GET","POST"], () => Stub("AdminHandler/uptime"));
app.MapGet("/admin/codes",    () => Stub("CodesHandler"));
app.MapGet("/admin/routes",   () => Stub("RoutesHandler"));
app.MapGet("/admin/dump",     () => Stub("DumpHandler"));
app.MapGet("/admin/errors",   () => Stub("ErrorsHandler"));
app.MapGet("/admin/overview", () => Stub("OverviewHandler"));

// ── Search / route ──────────────────────────────────────────────────────────

app.MapGet("/api/search", (HttpContext ctx) =>
{
    if (DBUtil.Factory == null)
        return Results.Problem("Database not configured. Set ConnectionString in configuration.", statusCode: 503);

    string? q       = ctx.Request.Query["q"];
    string? milieu  = ctx.Request.Query["milieu"];
    string? accept  = ctx.Request.Query["accept"];
    bool    random  = ctx.Request.Query["random"] == "1";
    int     limit   = int.TryParse(ctx.Request.Query["limit"], out int l) ? l : 160;

    try
    {
        var results = SearchEngine.PerformSearch(milieu, q,
            SearchEngine.SearchResultsType.Default, limit, random);
        return Results.Json(new { results = results.Select(r => r switch
        {
            SectorResult    s   => (object)new { type = "sector",    sx = s.SectorCoords.X, sy = s.SectorCoords.Y },
            SubsectorResult s   => (object)new { type = "subsector", sx = s.SectorLocation.X, sy = s.SectorLocation.Y, index = s.Index.ToString() },
            WorldResult     w   => (object)new { type = "world",     sx = w.Sector.X, sy = w.Sector.Y, hx = w.Hex.X, hy = w.Hex.Y },
            LabelResult     lab => (object)new { type = "label",     x  = lab.Coords.X, y = lab.Coords.Y, name = lab.Label, radius = lab.Radius },
            _                   => (object)new { type = "unknown" }
        }).ToArray() });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});
app.MapGet("/api/route",  () => Stub("RouteHandler"));

// ── Rendering ───────────────────────────────────────────────────────────────

app.MapMethods("/api/jumpmap", ["GET","POST"], (HttpContext ctx) =>
{
    string? sectorName = ctx.Request.Query["sector"];
    string? hexStr     = ctx.Request.Query["hex"];
    string? milieu     = ctx.Request.Query["milieu"];
    int jump = int.TryParse(ctx.Request.Query["jump"], out int jv) ? jv : 0;
    double scale = 64;

    try
    {
        var rm = Maps.ResourceManager.GetInstance();
        var map = Maps.SectorMap.ForMilieu(milieu);
        if (sectorName == null) return Results.BadRequest("sector required");

        var sector = map.FromName(sectorName);
        if (sector == null) return Results.NotFound($"Sector '{sectorName}' not found.");

        var tileRect = (System.Drawing.RectangleF)sector.Bounds;
        tileRect.Height += 0.5f;
        tileRect.Inflate(0.25f, 0.10f);

        var options = Maps.Rendering.MapOptions.SectorGrid | Maps.Rendering.MapOptions.BordersMajor
                    | Maps.Rendering.MapOptions.NamesMajor;
        var styles   = new Maps.Rendering.Stylesheet(scale, options, Maps.Rendering.Style.Poster);
        var selector = new Maps.SectorSelector(rm, sector);
        int w = (int)Math.Floor(tileRect.Width * scale * Maps.Astrometrics.ParsecScaleX);
        int h = (int)Math.Floor(tileRect.Height * scale * Maps.Astrometrics.ParsecScaleY);
        var tileSize = new System.Drawing.Size(w, h);
        var renderCtx = new Maps.Rendering.RenderContext(rm, selector, tileRect, scale, options, styles, tileSize);

        using var bitmap = new SkiaSharp.SKBitmap(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            using var graphics = new Maps.Graphics.BitmapGraphics(canvas);
            renderCtx.Render(graphics);
        }
        using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return Results.Bytes(encoded.ToArray(), "image/png");
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});
app.MapMethods("/api/poster",  ["GET","POST"], (HttpContext ctx) => RenderPoster(ctx, null, null, null));
app.MapMethods("/api/poster/{sector}", ["GET","POST"], (HttpContext ctx, string sector) => RenderPoster(ctx, sector, null, null));
app.MapMethods("/api/poster/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}", ["GET","POST"],
    (HttpContext ctx, string sector, string quadrant) => RenderPoster(ctx, sector, quadrant, null));
app.MapMethods("/api/poster/{sector}/{subsector}", ["GET","POST"],
    (HttpContext ctx, string sector, string subsector) => RenderPoster(ctx, sector, null, subsector));
app.MapGet("/api/tile", (HttpContext ctx) =>
{
    double x     = double.TryParse(ctx.Request.Query["x"],     out double xv) ? xv : 0;
    double y     = double.TryParse(ctx.Request.Query["y"],     out double yv) ? yv : 0;
    double scale = double.TryParse(ctx.Request.Query["scale"], out double sv) ? sv : 64;
    int    w     = int.TryParse(ctx.Request.Query["w"],        out int    wv) ? wv : 256;
    int    h     = int.TryParse(ctx.Request.Query["h"],        out int    hv) ? hv : 256;
    string? milieu = ctx.Request.Query["milieu"];

    scale = Math.Clamp(scale, Maps.API.ImageHandlerBase.MinScale, Maps.API.ImageHandlerBase.MaxScale);
    w = Math.Clamp(w, 1, 2048);
    h = Math.Clamp(h, 1, 2048);

    try
    {
        var resourceManager = Maps.ResourceManager.GetInstance();
        var tileRect = new System.Drawing.RectangleF
        {
            X      = (float)(x * w / (scale * Maps.Astrometrics.ParsecScaleX)),
            Y      = (float)(y * h / (scale * Maps.Astrometrics.ParsecScaleY)),
            Width  = (float)(w / (scale * Maps.Astrometrics.ParsecScaleX)),
            Height = (float)(h / (scale * Maps.Astrometrics.ParsecScaleY))
        };

        var options  = Maps.Rendering.MapOptions.SectorGrid | Maps.Rendering.MapOptions.BordersMajor
                     | Maps.Rendering.MapOptions.NamesMajor | Maps.Rendering.MapOptions.NamesMinor;
        var style    = Maps.Rendering.Style.Poster;
        var styles   = new Maps.Rendering.Stylesheet(scale, options, style);
        var selector = new Maps.RectSelector(
            Maps.SectorMap.ForMilieu(milieu), resourceManager, tileRect);
        var renderCtx = new Maps.Rendering.RenderContext(
            resourceManager, selector, tileRect, scale, options, styles, new System.Drawing.Size(w, h))
        {
            ClipOutsectorBorders = true
        };

        using var bitmap = new SkiaSharp.SKBitmap(w, h,
            SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            using var graphics = new Maps.Graphics.BitmapGraphics(canvas);
            graphics.MultiplyTransform(Maps.Graphics.AbstractMatrix.Identity);
            renderCtx.Render(graphics);
        }

        using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        byte[] bytes = encoded.ToArray();
        return Results.Bytes(bytes, "image/png");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

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

// ── Poster rendering helper ──────────────────────────────────────────────────

IResult RenderPoster(HttpContext ctx, string? sectorName, string? quadrant, string? subsector)
{
    sectorName ??= ctx.Request.Query["sector"];
    string? milieu = ctx.Request.Query["milieu"];
    double scale   = double.TryParse(ctx.Request.Query["scale"], out double sv) ? sv : 64;
    string? accept = ctx.Request.Query["accept"].ToString();
    if (string.IsNullOrEmpty(accept))
        accept = ctx.Request.Headers["Accept"].ToString().Split(',')[0].Trim();
    if (string.IsNullOrEmpty(accept))
        accept = Maps.Utilities.ContentTypes.Image.Png;
    // URL '+' decodes to space; restore it in MIME types.
    accept = accept.Replace("svg xml", "svg+xml").Replace("svg%2Bxml", "svg+xml");

    if (string.IsNullOrEmpty(sectorName))
        return Results.BadRequest("sector required");

    try
    {
        var rm  = Maps.ResourceManager.GetInstance();
        var map = Maps.SectorMap.ForMilieu(milieu);

        var sector = map.FromName(sectorName);
        if (sector == null) return Results.NotFound($"Sector '{sectorName}' not found.");

        System.Drawing.RectangleF tileRect;
        Maps.Selector selector;
        var options = Maps.Rendering.MapOptions.SectorGrid | Maps.Rendering.MapOptions.SubsectorGrid
                    | Maps.Rendering.MapOptions.BordersMajor | Maps.Rendering.MapOptions.BordersMinor
                    | Maps.Rendering.MapOptions.NamesMajor | Maps.Rendering.MapOptions.NamesMinor
                    | Maps.Rendering.MapOptions.WorldsCapitals | Maps.Rendering.MapOptions.WorldsHomeworlds;
        string title = sector.Names[0].Text;

        if (subsector != null)
        {
            int idx = sector.SubsectorIndexFor(subsector);
            if (idx == -1) return Results.NotFound($"Subsector '{subsector}' not found.");
            selector = new Maps.SubsectorSelector(rm, sector, idx);
            tileRect = (System.Drawing.RectangleF)sector.SubsectorBounds(idx);
            title += $" - Subsector {(char)('A' + idx)}";
        }
        else
        {
            selector = new Maps.SectorSelector(rm, sector);
            tileRect = (System.Drawing.RectangleF)sector.Bounds;
            tileRect.Height += 0.5f;
            tileRect.Inflate(0.25f, 0.10f);
        }

        var styles   = new Maps.Rendering.Stylesheet(scale, options, Maps.Rendering.Style.Poster);
        int w = (int)Math.Floor(tileRect.Width * scale * Maps.Astrometrics.ParsecScaleX);
        int h = (int)Math.Floor(tileRect.Height * scale * Maps.Astrometrics.ParsecScaleY);
        var tileSize = new System.Drawing.Size(w, h);
        var renderCtx = new Maps.Rendering.RenderContext(rm, selector, tileRect, scale, options, styles, tileSize);

        if (accept == Maps.Utilities.ContentTypes.Application.Pdf)
        {
            using var doc = new PdfSharp.Pdf.PdfDocument();
            doc.Info.Title = title;
            var page = doc.AddPage();
            page.Width  = PdfSharp.Drawing.XUnit.FromPoint(w);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(h);
            using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            using var pdfGraphics = new Maps.Graphics.PdfSharpGraphics(gfx);
            renderCtx.Render(pdfGraphics);
            using var ms = new System.IO.MemoryStream();
            doc.Save(ms, closeStream: false);
            return Results.Bytes(ms.ToArray(), "application/pdf");
        }

        if (accept == Maps.Utilities.ContentTypes.Image.Svg)
        {
            using var svg = new Maps.Graphics.SVGGraphics(w, h);
            renderCtx.Render(svg);
            using var ms = new System.IO.MemoryStream();
            svg.Serialize(new System.IO.StreamWriter(ms));
            return Results.Bytes(ms.ToArray(), "image/svg+xml");
        }

        // Default: PNG
        using var bitmap = new SkiaSharp.SKBitmap(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            using var graphics = new Maps.Graphics.BitmapGraphics(canvas);
            renderCtx.Render(graphics);
        }
        using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return Results.Bytes(encoded.ToArray(), "image/png");
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}
