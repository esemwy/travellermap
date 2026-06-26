// ASP.NET Core host for Traveller Map.
// Replaces Global.asax.cs and IIS/Web.config hosting.
using Maps.Database;
using Maps.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);


// Port 8080 is set via ASPNETCORE_URLS env var in the container (issue #9).
// For local dev: set ASPNETCORE_URLS=http://+:8080 or use the default port.

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// The original API used PascalCase property names; preserve that in Results.Json().
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
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

// Rewrite extensionless paths to .html if the file exists (e.g. /doc/about → /doc/about.html).
app.Use(async (context, next) =>
{
    string path = context.Request.Path.Value ?? "";
    if (!System.IO.Path.HasExtension(path) && !path.EndsWith("/") && path.Length > 1)
    {
        string candidate = System.IO.Path.Combine(webRootPath,
            path.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar) + ".html");
        if (System.IO.File.Exists(candidate))
            context.Request.Path = path + ".html";
    }
    await next(context);
});

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
app.MapMethods("/admin/reindex", ["GET","POST"], () =>
{
    if (DBUtil.Factory == null)
        return Results.Problem("Database not configured.", statusCode: 503);
    var lines = new System.Collections.Generic.List<string>();
    Maps.Search.SearchEngine.PopulateDatabase(Maps.ResourceManager.GetDedicatedInstance(), s => lines.Add(s));
    return Results.Text(string.Join("\n", lines));
});
app.MapMethods("/admin/profile", ["GET","POST"], () =>
{
    var p = System.Diagnostics.Process.GetCurrentProcess();
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"Process.Id: {p.Id}");
    sb.AppendLine($"Process.StartTime: {p.StartTime}");
    sb.AppendLine($"Process.WorkingSet64: {p.WorkingSet64:#,0}");
    sb.AppendLine($"Process.PeakWorkingSet64: {p.PeakWorkingSet64:#,0}");
    sb.AppendLine($"Process.PrivateMemorySize64: {p.PrivateMemorySize64:#,0}");
    sb.AppendLine($"Process.VirtualMemorySize64: {p.VirtualMemorySize64:#,0}");
    sb.AppendLine($"Process.Threads.Count: {p.Threads.Count}");
    return Results.Text(sb.ToString());
});
app.MapMethods("/admin/uptime",  ["GET","POST"], () =>
{
    var uptime = DateTime.Now - Maps.GlobalAsax.startup_time;
    return Results.Text($"Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
});
app.MapGet("/admin/codes", (HttpContext ctx) =>
{
    try
    {
        string? sectorName = Qs(ctx, "sector");
        string? type       = Qs(ctx, "type");
        string? regex      = Qs(ctx, "regex");
        string? milieu     = Qs(ctx, "milieu");

        Maps.SectorMap.Flush();
        var map = Maps.SectorMap.GetInstance();
        var rm  = Maps.ResourceManager.GetDedicatedInstance();
        try
        {
            var filter = new System.Text.RegularExpressions.Regex(regex ?? ".*");
            var knownCodes = BuildKnownCodesMap();
            var codes = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.SortedSet<string>>();

            var sectorQuery = map.Sectors
                .Where(s => (sectorName == null || s.Names[0].Text.StartsWith(sectorName, StringComparison.OrdinalIgnoreCase))
                         && s.DataFile != null
                         && (type == null || s.DataFile.Type == type)
                         && !s.Tags.Contains("ZCR")
                         && !s.Tags.Contains("meta")
                         && (milieu == null || s.CanonicalMilieu == milieu))
                .OrderBy(s => s.Names[0].Text);

            foreach (var sector in sectorQuery)
            {
                var worlds = sector.GetWorlds(rm, cacheResults: false);
                if (worlds == null) continue;
                foreach (var code in worlds.SelectMany(w => w.Codes)
                    .Where(c => filter.IsMatch(c) && !knownCodes.IsMatch(c)))
                {
                    if (!codes.ContainsKey(code)) codes[code] = new System.Collections.Generic.SortedSet<string>();
                    codes[code].Add($"{sector.Names[0].Text} [{sector.CanonicalMilieu}]");
                }
            }

            var sb = new System.Text.StringBuilder();
            foreach (var (code, sectors) in codes)
                sb.AppendLine($"{code} - {string.Join(" ", sectors)}");
            return Results.Text(sb.ToString());
        }
        finally { Maps.SectorMap.Flush(); rm.Flush(); }
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});
app.MapGet("/admin/routes",   (HttpContext ctx) =>
{
    try
    {
        var map = Maps.SectorMap.GetInstance();
        var sb = new System.Text.StringBuilder("Allegiance\tType\tCount\n");
        foreach (var sector in map.Sectors)
        {
            foreach (var route in sector.Routes)
            {
                string alleg = route.Allegiance ?? "(none)";
                string type  = route.Type ?? "standard";
                sb.AppendLine($"{alleg}\t{type}\t1");
            }
        }
        return Results.Text(sb.ToString());
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});
app.MapGet("/admin/dump", (HttpContext ctx) =>
{
    try
    {
        var rm  = Maps.ResourceManager.GetDedicatedInstance();
        var map = Maps.SectorMap.GetInstance();
        var sb  = new System.Text.StringBuilder();
        sb.AppendLine("Sector\tSX\tSY\tName\tHex\tUWP");
        foreach (var sector in map.Sectors.Where(s => s.Tags.Contains("OTU")).Take(10))
        {
            var worlds = sector.GetWorlds(rm, cacheResults: false);
            if (worlds == null) continue;
            foreach (var w in worlds.Take(5))
                sb.AppendLine($"{sector.Names.FirstOrDefault()?.Text}\t{sector.X}\t{sector.Y}\t{w.Name}\t{w.Hex}\t{w.UWP}");
        }
        return Results.Text(sb.ToString());
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});
app.MapGet("/admin/errors", (HttpContext ctx) =>
{
    try
    {
        string? sectorName = Qs(ctx, "sector");
        string? type       = Qs(ctx, "type");
        string? milieu     = Qs(ctx, "milieu");
        string? tag        = Qs(ctx, "tag");

        Maps.SectorMap.Flush();
        var map = Maps.SectorMap.GetInstance();
        var rm  = Maps.ResourceManager.GetDedicatedInstance();
        try
        {
            var sectorQuery = map.Sectors
                .Where(s => (sectorName == null || s.Names[0].Text.StartsWith(sectorName, StringComparison.OrdinalIgnoreCase))
                         && s.DataFile != null
                         && (type == null || s.DataFile.Type == type)
                         && (milieu == null || s.CanonicalMilieu == milieu)
                         && (tag == null || s.Tags.Contains(tag))
                         && (s.Tags.Contains("OTU") || s.Tags.Contains("Apocryphal") || s.Tags.Contains("Faraway")))
                .OrderBy(s => s.Names[0].Text);

            var sb = new System.Text.StringBuilder();
            foreach (var sector in sectorQuery)
            {
                sb.AppendLine($"{sector.Names[0].Text} - {sector.Milieu}");
                try
                {
                    var worlds = sector.GetWorlds(rm, cacheResults: false);
                    if (worlds != null)
                    {
                        sb.AppendLine($"  {worlds.Count()} world(s)");
                        foreach (var world in worlds)
                        {
                            if (!string.IsNullOrEmpty(world.Allegiance) &&
                                sector.GetAllegianceFromCode(world.Allegiance) == null)
                                sb.AppendLine($"  Undefined allegiance: {world.Allegiance} ({world.Name} {world.Hex})");
                        }
                    }
                    else { sb.AppendLine("  0 world(s)"); }
                }
                catch (Exception ex) { sb.AppendLine($"  Bad data file: {ex.Message}"); }
                sb.AppendLine();
            }
            return Results.Text(sb.ToString());
        }
        finally { Maps.SectorMap.Flush(); rm.Flush(); }
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});
app.MapGet("/admin/overview", (HttpContext ctx) =>
{
    try
    {
        string? milieu = Qs(ctx, "milieu");
        var rm = Maps.ResourceManager.GetInstance();
        var options = Maps.Rendering.MapOptions.SectorGrid | Maps.Rendering.MapOptions.FilledBorders;
        float scale = 2f;
        var tileSize = new System.Drawing.Size(1000, 1000);
        var tileRect = new System.Drawing.RectangleF
        {
            X      = -0.5f * tileSize.Width  / (scale * Maps.Astrometrics.ParsecScaleX),
            Y      = -0.5f * tileSize.Height / (scale * Maps.Astrometrics.ParsecScaleY),
            Width  = (float)tileSize.Width   / (scale * Maps.Astrometrics.ParsecScaleX),
            Height = (float)tileSize.Height  / (scale * Maps.Astrometrics.ParsecScaleY),
        };
        var selector = new Maps.RectSelector(Maps.SectorMap.ForMilieu(milieu), rm, tileRect);
        var styles   = new Maps.Rendering.Stylesheet(scale, options, Maps.Rendering.Style.Poster);
        styles.microRoutes.visible      = true;
        styles.macroRoutes.visible      = false;
        styles.macroBorders.visible     = false;
        styles.microBorders.visible     = true;
        styles.capitals.visible         = false;
        styles.worlds.visible           = true;
        styles.worldDetails             = Maps.Rendering.WorldDetails.Dotmap;
        styles.showAllSectorNames       = false;
        styles.showSomeSectorNames      = false;
        styles.macroNames.visible       = false;
        styles.pseudoRandomStars.visible = false;
        styles.fillMicroBorders         = true;
        var renderCtx = new Maps.Rendering.RenderContext(rm, selector, tileRect, scale, options, styles, tileSize)
        { ClipOutsectorBorders = true };
        using var bitmap = new SkiaSharp.SKBitmap(tileSize.Width, tileSize.Height,
            SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
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

// ── Shared query-param helpers ───────────────────────────────────────────────

static string? Qs(HttpContext ctx, string name)
{
    var v = ctx.Request.Query[name].ToString();
    return string.IsNullOrEmpty(v) ? null : v;
}
static int Qi(HttpContext ctx, string name, int def = 0) =>
    int.TryParse(Qs(ctx, name), out int v) ? v : def;
static bool Qb(HttpContext ctx, string name, bool def = false) =>
    Qs(ctx, name) is string s ? s is "1" or "true" : def;
static bool HasQ(HttpContext ctx, string name) => !string.IsNullOrEmpty(Qs(ctx, name));

static Maps.Sector? GetSector(HttpContext ctx, Maps.SectorMap.Milieu map, string paramName = "sector")
{
    string? name = Qs(ctx, paramName);
    if (name != null) return map.FromName(name);
    if (HasQ(ctx, "sx") && HasQ(ctx, "sy")) return map.FromLocation(Qi(ctx, "sx"), Qi(ctx, "sy"));
    return null;
}

static Maps.Location GetLocation(HttpContext ctx, Maps.SectorMap.Milieu map)
{
    if (HasQ(ctx, "sector"))
    {
        var sec = map.FromName(Qs(ctx, "sector")!)
            ?? throw new InvalidOperationException("Sector not found.");
        return new Maps.Location(sec.Location, Qi(ctx, "hex", 0));
    }
    if (HasQ(ctx, "sx") && HasQ(ctx, "sy"))
        return new Maps.Location(
            new Maps.Point(Qi(ctx, "sx"), Qi(ctx, "sy")),
            new Maps.Hex((byte)Qi(ctx, "hx"), (byte)Qi(ctx, "hy")));
    if (HasQ(ctx, "x") && HasQ(ctx, "y"))
        return Maps.Astrometrics.CoordinatesToLocation(Qi(ctx, "x"), Qi(ctx, "y"));
    throw new InvalidOperationException("Location not specified.");
}

static IResult XmlResult<T>(T obj)
{
    using var ms = new System.IO.MemoryStream();
    new System.Xml.Serialization.XmlSerializer(typeof(T)).Serialize(ms, obj);
    return Results.Bytes(ms.ToArray(), "text/xml");
}

// ── Search / route ──────────────────────────────────────────────────────────

app.MapGet("/api/search", (HttpContext ctx) =>
{
    string? q = Qs(ctx, "q");

    // Special tour searches: "(Grand Tour)", "(Arrival Vengeance)", etc. map to static JSON files.
    if (q != null)
    {
        var specialMatch = System.Text.RegularExpressions.Regex.Match(q, @"\(([A-Za-z0-9 ]+)\)");
        if (specialMatch.Success)
        {
            string fileName = specialMatch.Groups[1].Value.Replace(" ", "") + ".json";
            string filePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "res", "search", fileName);
            if (System.IO.File.Exists(filePath))
                return Results.Content(System.IO.File.ReadAllText(filePath), "application/json");
        }
    }

    if (DBUtil.Factory == null)
        return Results.Problem("Database not configured. Set ConnectionString in configuration.", statusCode: 503);

    string? milieu = Qs(ctx, "milieu");
    int limit = Qi(ctx, "limit", 160);

    try
    {
        string? milieuStr = milieu ?? Maps.SectorMap.DEFAULT_MILIEU;
        var map = Maps.SectorMap.ForMilieu(milieuStr);
        var rm  = Maps.ResourceManager.GetInstance();

        IEnumerable<SearchResult> rawResults;
        int numResults;

        if (q == "(random world)")
        {
            numResults = 1;
            rawResults = SearchEngine.PerformSearch(milieuStr, null, SearchEngine.SearchResultsType.Worlds, 1, random: true);
        }
        else
        {
            string? query = q;
            if (query != null)
            {
                query = query.Replace('*', '%').Replace('?', '_');
                if (System.Text.RegularExpressions.Regex.IsMatch(query, @"^\w{7}-\w$"))
                    query = "uwp:" + query;
            }
            numResults = limit;
            rawResults = SearchEngine.PerformSearch(milieuStr, query, SearchEngine.SearchResultsType.Default, numResults);
        }

        var items = rawResults
            .Select(r => Maps.API.Results.SearchResults.SearchResultToItem(map, rm, r))
            .OfType<Maps.API.Results.SearchResults.Item>()
            .OrderByDescending(item => item.Importance)
            .Take(numResults)
            .Select(item => item switch
            {
                Maps.API.Results.SearchResults.WorldResult w => (object)new
                {
                    World = new { w.SectorX, w.SectorY, w.HexX, w.HexY, w.Name, w.Sector, w.Uwp, w.SectorTags }
                },
                Maps.API.Results.SearchResults.SubsectorResult ss => (object)new
                {
                    Subsector = new { ss.SectorX, ss.SectorY, ss.Name, ss.Sector, ss.Index, ss.SectorTags }
                },
                Maps.API.Results.SearchResults.SectorResult s => (object)new
                {
                    Sector = new { s.SectorX, s.SectorY, s.Name, s.SectorTags }
                },
                Maps.API.Results.SearchResults.LabelResult lab => (object)new
                {
                    Label = new { lab.SectorX, lab.SectorY, lab.HexX, lab.HexY, Scale = lab.Scale, lab.Name, lab.SectorTags }
                },
                _ => null
            })
            .Where(x => x != null)
            .ToList();

        return Results.Json(new { Results = new { Count = items.Count, Items = items } });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});
app.MapGet("/api/route", (HttpContext ctx) =>
{
    try
    {
        var rm   = Maps.ResourceManager.GetInstance();
        var map  = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        int jump = Math.Clamp(Qi(ctx, "jump", 2), 0, 12);
        bool wild  = Qb(ctx, "wild");
        bool im    = Qb(ctx, "im");
        bool nored = Qb(ctx, "nored");
        bool aok   = Qb(ctx, "aok");

        Maps.World? ResolveWorld(string paramName)
        {
            string? val = Qs(ctx, paramName);
            if (val == null) return null;
            var m = System.Text.RegularExpressions.Regex.Match(val, @"^(.+?)\s+(\d{4})$");
            if (m.Success)
            {
                var sec = map.FromName(m.Groups[1].Value);
                int h = int.Parse(m.Groups[2].Value);
                return sec?.GetWorlds(rm, cacheResults: true)?[h];
            }
            var r = Maps.Search.SearchEngine.FindNearestWorldMatch(
                val, Maps.SectorMap.DEFAULT_MILIEU, 0, 0);
            if (r == null) return null;
            return map.FromLocation(r.Sector.X, r.Sector.Y)?
                .GetWorlds(rm, cacheResults: true)?
                .FirstOrDefault(w => w.X == r.Hex.X && w.Y == r.Hex.Y);
        }

        var startWorld = ResolveWorld("start");
        var endWorld   = ResolveWorld("end");
        if (startWorld == null || endWorld == null)
            return Results.BadRequest("start and end parameters required (format: 'Sector HHHH').");

        // Use PathFinder with an inline IMap implementation.
        Maps.World? routeEnd = endWorld;
        var imap = new RouteMap(rm, map, jump, wild, im, nored, aok, routeEnd);
        var route = Maps.Utilities.PathFinder.FindPath<Maps.World>(imap, startWorld, endWorld);
        if (route == null) return Results.NotFound("No route found.");
        return Results.Json(route.Select(w => new {
            Sector = w.SectorName, SectorX = w.Sector.X, SectorY = w.Sector.Y,
            Name = w.Name, Hex = w.Hex, HexX = w.X, HexY = w.Y,
            UWP = w.UWP, Zone = w.Zone
        }).ToArray());
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

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
    double dpr   = double.TryParse(ctx.Request.Query["dpr"],   out double dv) ? dv : 1.0;

    // Default options match map.js Defaults.options
    var options = Maps.Rendering.MapOptions.SectorGrid | Maps.Rendering.MapOptions.SubsectorGrid
                | Maps.Rendering.MapOptions.SectorsSelected
                | Maps.Rendering.MapOptions.BordersMajor | Maps.Rendering.MapOptions.BordersMinor
                | Maps.Rendering.MapOptions.NamesMajor
                | Maps.Rendering.MapOptions.WorldsCapitals | Maps.Rendering.MapOptions.WorldsHomeworlds;
    if (int.TryParse(ctx.Request.Query["options"], out int optv))
        options = (Maps.Rendering.MapOptions)optv;

    var style = Maps.Rendering.Style.Poster;
    string? styleStr = ctx.Request.Query["style"].ToString();
    if (!string.IsNullOrEmpty(styleStr))
        style = styleStr.ToLowerInvariant() switch {
            "atlas"  => Maps.Rendering.Style.Atlas,
            "print"  => Maps.Rendering.Style.Print,
            "candy"  => Maps.Rendering.Style.Candy,
            _        => Maps.Rendering.Style.Poster,
        };

    scale = Math.Clamp(scale, Maps.API.ImageHandlerBase.MinScale, Maps.API.ImageHandlerBase.MaxScale);
    w = Math.Clamp(w, 1, 2048);
    h = Math.Clamp(h, 1, 2048);
    dpr = Math.Clamp(Math.Round(dpr, 1), 1.0, 2.0);

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

        var styles   = new Maps.Rendering.Stylesheet(scale, options, style);

        // Apply per-parameter stylesheet overrides (mirrors ImageHandlerBase behavior)
        if (!string.IsNullOrEmpty(milieu) && milieu != Maps.SectorMap.DEFAULT_MILIEU)
        {
            if (styles.macroBorders.visible) { styles.macroBorders.visible = false; styles.microBorders.visible = true; }
            styles.macroNames.visible  = false;
            styles.macroRoutes.visible = false;
        }
        if (ctx.Request.Query["routes"] == "0") { styles.macroRoutes.visible = false; styles.microRoutes.visible = false; }
        if (ctx.Request.Query["rifts"]  == "0") styles.showRiftOverlay = false;
        if (ctx.Request.Query["po"]     == "1") styles.populationOverlay.visible = true;
        if (ctx.Request.Query["im"]     == "1") styles.importanceOverlay.visible = true;
        if (ctx.Request.Query["cp"]     == "1") styles.capitalOverlay.visible    = true;
        if (ctx.Request.Query["dimunofficial"] == "1") styles.dimUnofficialSectors = true;

        var selector  = new Maps.RectSelector(Maps.SectorMap.ForMilieu(milieu), resourceManager, tileRect);
        var renderCtx = new Maps.Rendering.RenderContext(
            resourceManager, selector, tileRect, scale, options, styles, new System.Drawing.Size(w, h))
        {
            ClipOutsectorBorders = true
        };

        int bw = (int)Math.Floor(w * dpr);
        int bh = (int)Math.Floor(h * dpr);
        using var bitmap = new SkiaSharp.SKBitmap(bw, bh,
            SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            using var graphics = new Maps.Graphics.BitmapGraphics(canvas);
            graphics.ScaleTransform((float)dpr);
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

app.MapGet("/api/coordinates", (HttpContext ctx) =>
{
    try
    {
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        var loc = GetLocation(ctx, map);
        if (loc.Hex.IsEmpty) loc.Hex = Maps.Astrometrics.SectorCenter;
        var pt = Maps.Astrometrics.LocationToCoordinates(loc);
        return Results.Json(new { sx = loc.Sector.X, sy = loc.Sector.Y,
            hx = loc.Hex.X, hy = loc.Hex.Y, x = pt.X, y = pt.Y });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 400); }
});

app.MapGet("/api/credits", (HttpContext ctx) =>
{
    try
    {
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        Maps.Location loc;
        try { loc = GetLocation(ctx, map); }
        catch { loc = Maps.Location.Empty; }
        if (loc.Hex.IsEmpty) loc.Hex = Maps.Astrometrics.SectorCenter;
        var sector = map.FromLocation(loc.Sector.X, loc.Sector.Y);
        if (sector == null) return Results.NotFound("Sector not found.");
        return Results.Json(new
        {
            SectorX = sector.X, SectorY = sector.Y,
            SectorName = sector.Names.FirstOrDefault()?.Text,
            Credits = sector.Credits?.Trim(),
            SectorAuthor = sector.DataFile?.Author ?? sector.Author,
            SectorPublisher = sector.DataFile?.Publisher ?? sector.Publisher,
            SectorRef = sector.DataFile?.Ref ?? sector.Ref,
            SectorTags = sector.TagString,
            SectorMilieu = sector.CanonicalMilieu,
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

app.MapGet("/api/jumpworlds", (HttpContext ctx) =>
{
    try
    {
        var rm  = Maps.ResourceManager.GetInstance();
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        int jump = Math.Clamp(Qi(ctx, "jump", 6), 0, 12);
        var loc = GetLocation(ctx, map);
        var selector = new Maps.HexSelector(map, rm, loc, jump);
        return Results.Json(new { Worlds = selector.Worlds.Select(w => new {
            Sector = w.SectorName, SectorX = w.Sector.X, SectorY = w.Sector.Y,
            Name = w.Name, Hex = w.Hex, HexX = w.X, HexY = w.Y,
            UWP = w.UWP, Zone = w.Zone
        }).ToArray() });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

// ── Data retrieval (API-centric) ────────────────────────────────────────────

app.MapGet("/api/universe", (HttpContext ctx) =>
{
    try
    {
        var map = Maps.SectorMap.GetInstance();
        string? milieu = Qs(ctx, "milieu") ?? Qs(ctx, "era");
        bool requireData = Qb(ctx, "requireData");
        var tags = Qs(ctx, "tag")?.Split('|');
        var sectors = map.Sectors
            .Where(s => !(s.Tags.Contains("meta") && !(tags?.Contains("meta") ?? false)))
            .Where(s => milieu == null || s.CanonicalMilieu == milieu)
            .Where(s => !requireData || s.DataFile != null)
            .Where(s => tags == null || tags.Any(t => s.Tags.Contains(t)))
            .Select(s => new {
                X = s.X, Y = s.Y, Milieu = s.CanonicalMilieu,
                Abbreviation = s.Abbreviation, Tags = s.TagString,
                Names = s.Names.Select(n => n.Text).ToArray()
            }).ToArray();
        return Results.Json(new { Sectors = sectors });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

app.MapGet("/api/milieux", (HttpContext ctx) =>
{
    try
    {
        var map = Maps.SectorMap.GetInstance();
        var milieux = map.GetMilieux().OrderBy(m => m).ToArray();
        return Results.Json(new { Milieux = milieux });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

app.MapGet("/api/sec", (HttpContext ctx) => RenderSEC(ctx));
app.MapGet("/api/sec/{sector}", (HttpContext ctx, string sector) => RenderSEC(ctx, sector: sector));
app.MapGet("/api/sec/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}",
    (HttpContext ctx, string sector, string quadrant) => RenderSEC(ctx, sector: sector, quadrant: quadrant));
app.MapGet("/api/sec/{sector}/{subsector}",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector));

app.MapGet("/api/metadata", (HttpContext ctx) => RenderMetadata(ctx));
app.MapGet("/api/metadata/{sector}", (HttpContext ctx, string sector) => RenderMetadata(ctx, sector: sector));

app.MapGet("/api/msec", (HttpContext ctx) => RenderMSEC(ctx));
app.MapGet("/api/msec/{sector}", (HttpContext ctx, string sector) => RenderMSEC(ctx, sector: sector));

// ── Data retrieval (RESTful /data) ──────────────────────────────────────────

app.MapGet("/data", (HttpContext ctx) =>
{
    try
    {
        var map = Maps.SectorMap.GetInstance();
        string? milieu = Qs(ctx, "milieu") ?? Qs(ctx, "era");
        bool requireData = Qb(ctx, "requireData");
        var tags = Qs(ctx, "tag")?.Split('|');
        var data = new Maps.API.Results.UniverseResult();
        foreach (var sector in map.Sectors)
        {
            if (requireData && sector.DataFile == null) continue;
            if (sector.Tags.Contains("meta") && !(tags?.Contains("meta") ?? false)) continue;
            if (milieu != null && sector.CanonicalMilieu != milieu) continue;
            if (tags != null && !tags.Any(t => sector.Tags.Contains(t))) continue;
            data.Sectors.Add(new Maps.API.Results.UniverseResult.SectorResult(sector));
        }
        return XmlResult(data);
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
});

// Sector – /data/{sector}/...
app.MapGet("/data/{sector}",            (HttpContext ctx, string sector) => RenderSEC(ctx, sector: sector));
app.MapGet("/data/{sector}/sec",        (HttpContext ctx, string sector) => RenderSEC(ctx, sector: sector));
app.MapGet("/data/{sector}/tab",        (HttpContext ctx, string sector) => RenderSEC(ctx, sector: sector, type: "TabDelimited"));
app.MapGet("/data/{sector}/coordinates",(HttpContext ctx, string sector) => RenderCoordinates(ctx, sector: sector));
app.MapGet("/data/{sector}/credits",    (HttpContext ctx, string sector) => RenderCredits(ctx, sector: sector));
app.MapGet("/data/{sector}/metadata",   (HttpContext ctx, string sector) => RenderMetadata(ctx, sector: sector));
app.MapGet("/data/{sector}/msec",       (HttpContext ctx, string sector) => RenderMSEC(ctx, sector: sector));
app.MapMethods("/data/{sector}/image",  ["GET","POST"], (HttpContext ctx, string sector) => RenderPoster(ctx, sector, null, null));
app.MapGet("/data/{sector}/booklet",    (string sector) =>
    Results.Redirect($"/make/booklet?sector={sector}", permanent: false));

// Quadrant
app.MapGet("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}",
    (HttpContext ctx, string sector, string quadrant) => RenderSEC(ctx, sector: sector, quadrant: quadrant));
app.MapGet("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}/sec",
    (HttpContext ctx, string sector, string quadrant) => RenderSEC(ctx, sector: sector, quadrant: quadrant));
app.MapGet("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}/tab",
    (HttpContext ctx, string sector, string quadrant) => RenderSEC(ctx, sector: sector, quadrant: quadrant, type: "TabDelimited"));
app.MapMethods("/data/{sector}/{quadrant:regex(^(alpha|beta|gamma|delta)$)}/image", ["GET","POST"],
    (HttpContext ctx, string sector, string quadrant) => RenderPoster(ctx, sector, quadrant, null));

// Subsector by single letter (A-P)
app.MapGet("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector));
app.MapGet("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}/sec",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector));
app.MapGet("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}/tab",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector, type: "TabDelimited"));
app.MapMethods("/data/{sector}/{subsector:regex(^[A-Pa-p]$)}/image", ["GET","POST"],
    (HttpContext ctx, string sector, string subsector) => RenderPoster(ctx, sector, null, subsector));

// World (4-digit hex)
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}",
    (HttpContext ctx, string sector, string hex) => RenderJumpWorlds(ctx, sector, hex, 0));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/coordinates",
    (HttpContext ctx, string sector, string hex) => RenderCoordinates(ctx, sector: sector, hexStr: hex));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/credits",
    (HttpContext ctx, string sector, string hex) => RenderCredits(ctx, sector: sector, hexStr: hex));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/jump/{jump:int}",
    (HttpContext ctx, string sector, string hex, int jump) => RenderJumpWorlds(ctx, sector, hex, jump));
app.MapMethods("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/image", ["GET","POST"],
    (HttpContext ctx, string sector, string hex) => RenderWorldJumpMap(ctx, sector, hex, 0));
app.MapMethods("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/jump/{jump:int}/image", ["GET","POST"],
    (HttpContext ctx, string sector, string hex, int jump) => RenderWorldJumpMap(ctx, sector, hex, jump));
app.MapGet("/data/{sector}/{hex:regex(^[0-9][0-9][0-9][0-9]$)}/sheet",
    (string sector, string hex) =>
        Results.Redirect($"/print/world?sector={sector}&hex={hex}", permanent: false));

// Subsector by name (catch-all, must come after hex and single-letter patterns)
app.MapGet("/data/{sector}/{subsector}",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector));
app.MapGet("/data/{sector}/{subsector}/sec",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector));
app.MapGet("/data/{sector}/{subsector}/tab",
    (HttpContext ctx, string sector, string subsector) => RenderSEC(ctx, sector: sector, subsector: subsector, type: "TabDelimited"));
app.MapMethods("/data/{sector}/{subsector}/image", ["GET","POST"],
    (HttpContext ctx, string sector, string subsector) => RenderPoster(ctx, sector, null, subsector));

// ── T5SS reference data ─────────────────────────────────────────────────────

app.MapGet("/t5ss/allegiances", () =>
{
    var codes = Maps.SecondSurvey.AllegianceCodes.OrderBy(c => c).Select(code => {
        var a = Maps.SecondSurvey.GetStockAllegianceFromCode(code);
        return new { Code = code, LegacyCode = a?.LegacyCode, Name = a?.Name, Location = a?.Location };
    }).ToArray();
    return Results.Json(new { Allegiances = codes });
});

app.MapGet("/t5ss/sophonts", () =>
{
    var codes = Maps.SecondSurvey.SophontCodes.OrderBy(c => c).Select(code => {
        var s = Maps.SecondSurvey.SophontForCode(code);
        return new { Code = code, Name = s?.Name, Location = s?.Location };
    }).ToArray();
    return Results.Json(new { Sophonts = codes });
});

// ───────────────────────────────────────────────────────────────────────────

app.Run();

// ── SEC / MSEC / Metadata rendering helpers ──────────────────────────────────

IResult RenderSEC(HttpContext ctx, string? sector = null, string? quadrant = null,
    string? subsector = null, string? type = null)
{
    sector   ??= Qs(ctx, "sector");
    quadrant ??= Qs(ctx, "quadrant");
    subsector ??= Qs(ctx, "subsector");
    type     ??= Qs(ctx, "type") ?? "SecondSurvey";
    string? milieu = Qs(ctx, "milieu");
    try
    {
        var rm  = Maps.ResourceManager.GetInstance();
        var map = Maps.SectorMap.ForMilieu(milieu);
        Maps.Sector? sec = sector != null ? map.FromName(sector)
                         : HasQ(ctx, "sx") && HasQ(ctx, "sy") ? map.FromLocation(Qi(ctx, "sx"), Qi(ctx, "sy"))
                         : null;
        if (sec == null) return Results.NotFound("Sector not found.");

        var options = new Maps.Serialization.SectorSerializeOptions
        {
            sscoords = Qb(ctx, "sscoords"),
            includeMetadata = Qb(ctx, "metadata", true),
            includeHeader   = Qb(ctx, "header", true),
            includeRoutes   = Qb(ctx, "routes"),
        };
        if (quadrant != null)
        {
            int qi = Maps.Sector.QuadrantIndexFor(quadrant);
            if (qi == -1) return Results.BadRequest($"Invalid quadrant '{quadrant}'.");
            options.filter = w => w.Quadrant == qi;
        }
        else if (subsector != null && subsector.Length == 1)
        {
            int si = subsector[0] >= 'A' && subsector[0] <= 'P' ? subsector[0] - 'A'
                   : subsector[0] >= 'a' && subsector[0] <= 'p' ? subsector[0] - 'a' : -1;
            if (si >= 0) options.filter = w => w.Subsector == si;
        }
        else if (subsector != null)
        {
            int si = sec.SubsectorIndexFor(subsector);
            if (si >= 0) options.filter = w => w.Subsector == si;
        }

        using var writer = new System.IO.StringWriter();
        sec.Serialize(rm, writer, type, options);
        return Results.Text(writer.ToString(), "text/plain");
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

IResult RenderMSEC(HttpContext ctx, string? sector = null)
{
    sector ??= Qs(ctx, "sector");
    try
    {
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        Maps.Sector? sec = sector != null ? map.FromName(sector)
                         : HasQ(ctx, "sx") && HasQ(ctx, "sy") ? map.FromLocation(Qi(ctx, "sx"), Qi(ctx, "sy"))
                         : null;
        if (sec == null) return Results.NotFound("Sector not found.");
        using var writer = new System.IO.StringWriter();
        new Maps.Serialization.MSECSerializer().Serialize(writer, sec);
        return Results.Text(writer.ToString(), "text/plain");
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

IResult RenderMetadata(HttpContext ctx, string? sector = null)
{
    sector ??= Qs(ctx, "sector");
    try
    {
        var rm  = Maps.ResourceManager.GetInstance();
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        Maps.Sector? sec = sector != null ? map.FromName(sector)
                         : HasQ(ctx, "sx") && HasQ(ctx, "sy") ? map.FromLocation(Qi(ctx, "sx"), Qi(ctx, "sy"))
                         : null;
        if (sec == null) return Results.NotFound("Sector not found.");
        var result = new Maps.API.Results.SectorMetadata(sec, sec.GetWorlds(rm, cacheResults: true), null);
        return XmlResult(result);
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

IResult RenderCoordinates(HttpContext ctx, string? sector = null, string? hexStr = null)
{
    try
    {
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        Maps.Location loc;
        if (sector != null)
        {
            var sec = map.FromName(sector);
            if (sec == null) return Results.NotFound($"Sector '{sector}' not found.");
            int h = hexStr != null && int.TryParse(hexStr, out int hv) ? hv : Maps.Astrometrics.SectorCentralHex;
            loc = new Maps.Location(sec.Location, h);
        }
        else { loc = GetLocation(ctx, map); }
        if (loc.Hex.IsEmpty) loc.Hex = Maps.Astrometrics.SectorCenter;
        var pt = Maps.Astrometrics.LocationToCoordinates(loc);
        return Results.Json(new { sx = loc.Sector.X, sy = loc.Sector.Y,
            hx = loc.Hex.X, hy = loc.Hex.Y, x = pt.X, y = pt.Y });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 400); }
}

IResult RenderCredits(HttpContext ctx, string? sector = null, string? hexStr = null)
{
    try
    {
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        Maps.Sector? sec;
        if (sector != null)
        {
            sec = map.FromName(sector);
            if (sec == null) return Results.NotFound($"Sector '{sector}' not found.");
        }
        else
        {
            Maps.Location loc;
            try { loc = GetLocation(ctx, map); }
            catch { loc = Maps.Location.Empty; }
            sec = map.FromLocation(loc.Sector.X, loc.Sector.Y);
        }
        if (sec == null) return Results.NotFound("Sector not found.");
        return Results.Json(new
        {
            SectorX = sec.X, SectorY = sec.Y,
            SectorName = sec.Names.FirstOrDefault()?.Text,
            Credits = sec.Credits?.Trim(),
            SectorAuthor    = sec.DataFile?.Author    ?? sec.Author,
            SectorPublisher = sec.DataFile?.Publisher ?? sec.Publisher,
            SectorRef       = sec.DataFile?.Ref       ?? sec.Ref,
            SectorTags      = sec.TagString,
            SectorMilieu    = sec.CanonicalMilieu,
        });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

IResult RenderJumpWorlds(HttpContext ctx, string sectorName, string hexStr, int jump)
{
    try
    {
        var rm  = Maps.ResourceManager.GetInstance();
        var map = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        var sec = map.FromName(sectorName);
        if (sec == null) return Results.NotFound($"Sector '{sectorName}' not found.");
        int hex = int.TryParse(hexStr, out int hv) ? hv : 0;
        var loc = new Maps.Location(sec.Location, hex);
        var selector = new Maps.HexSelector(map, rm, loc, Math.Clamp(jump, 0, 12));
        return Results.Json(new { Worlds = selector.Worlds.Select(w => new {
            Sector = w.SectorName, SectorX = w.Sector.X, SectorY = w.Sector.Y,
            Name = w.Name, Hex = w.Hex, HexX = w.X, HexY = w.Y,
            UWP = w.UWP, Zone = w.Zone
        }).ToArray() });
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

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

// ── World jump-map image helper ───────────────────────────────────────────────

IResult RenderWorldJumpMap(HttpContext ctx, string sectorName, string hexStr, int jump)
{
    try
    {
        var rm     = Maps.ResourceManager.GetInstance();
        var map    = Maps.SectorMap.ForMilieu(Qs(ctx, "milieu"));
        var sector = map.FromName(sectorName);
        if (sector == null) return Results.NotFound($"Sector '{sectorName}' not found.");
        jump = Math.Clamp(jump, 0, 12);
        double scale = 64;
        var tileRect = (System.Drawing.RectangleF)sector.Bounds;
        tileRect.Height += 0.5f;
        tileRect.Inflate(0.25f, 0.10f);
        var options  = Maps.Rendering.MapOptions.SectorGrid | Maps.Rendering.MapOptions.BordersMajor
                     | Maps.Rendering.MapOptions.NamesMajor;
        var styles   = new Maps.Rendering.Stylesheet(scale, options, Maps.Rendering.Style.Poster);
        var selector = new Maps.SectorSelector(rm, sector);
        int w = (int)Math.Floor(tileRect.Width  * scale * Maps.Astrometrics.ParsecScaleX);
        int h = (int)Math.Floor(tileRect.Height * scale * Maps.Astrometrics.ParsecScaleY);
        var renderCtx = new Maps.Rendering.RenderContext(rm, selector, tileRect, scale, options, styles,
            new System.Drawing.Size(w, h));
        using var wjmBitmap = new SkiaSharp.SKBitmap(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using (var wjmCanvas = new SkiaSharp.SKCanvas(wjmBitmap))
        {
            wjmCanvas.Clear(SkiaSharp.SKColors.White);
            using var wjmGraphics = new Maps.Graphics.BitmapGraphics(wjmCanvas);
            renderCtx.Render(wjmGraphics);
        }
        using var wjmEncoded = wjmBitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return Results.Bytes(wjmEncoded.ToArray(), "image/png");
    }
    catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
}

// ── /admin/codes known-codes regex map ───────────────────────────────────────

Maps.Utilities.RegexMap<string> BuildKnownCodesMap()
{
    var legacySophont = new[] { "A","C","D","F","H","I","M","V","X","Z" };
    return new Maps.Utilities.RegexMap<string>
    {
        { @"^Rs[ABGDEZHT]$", "Rs" },
        { @"^O:[0-9]{4}(-\w+)?$", "O:nnnn" },
        { @"^O:[A-Za-z]{3,4}-[0-9]{4}$", "O:nnnn (outsector)" },
        { @"^C:[0-9]{4}(-\w+)?$", "C:nnnn" },
        { @"^C:[A-Za-z]{3,4}-[0-9]{4}$", "C:nnnn (outsector)" },
        "Ag", "As", "Ba", "De", "Fa", "Fl", "Hi", "Ic", "In", "Lo", "Na", "Nh",
        "Ni", "Nk", "Po", "Ri", "St", "Va", "Wa", "An", "Cf", "Cm", "Cp",
        "Cs", "Cx", "Ex", "Mr", "Pr", "Rs",
        { @"^(" + string.Join("|", legacySophont) + @"):?(\d|w)$", "(sophont)" },
        "Tp", "Tn", "Lt", "Ht", "Xb",
        { @"^Rw:?[0-9VZ]$", "Rw#" },
        "Hp", "Hn",
        { @"^S[0-9A-F]{1,2}$", "S##" },
        "Rn", "Rv",
        "Di", "Ga", "He", "Oc", "Ph", "Pa", "Pi", "Fr", "Ho", "Co", "Lk",
        "Tr", "Tu", "Tz", "Mi", "Px", "Pe", "Re", "Sa", "Fo", "Pz", "Da", "Ab",
        { @"^\[.*?\][0-9?]?$", "(major race homeworld)" },
        { @"^\(.*?\)[0-9?]?$", "(minor race homeworld)" },
        { @"^Di\(.*?\)$", "(extinct minor race homeworld)" },
        { @"^(" + string.Join("|", Maps.SecondSurvey.SophontCodes) + @")(\d|W|\?)$", "(sophont)" },
        { @"^Mr\((" + string.Join("|", Maps.SecondSurvey.AllegianceCodes) + @")\)$", "(military rule)" },
        { @"^\{.*\}$", "(comment)" },
    };
}

// Type declaration must follow all top-level statements (local functions included).

/// <summary>A* IMap implementation for world-to-world route finding.</summary>
sealed class RouteMap : Maps.Utilities.PathFinder.IMap<Maps.World>
{
    readonly Maps.ResourceManager _rm;
    readonly Maps.SectorMap.Milieu _map;
    readonly int _jump;
    readonly bool _wild, _im, _nored, _aok;
    readonly Maps.World _end;

    public RouteMap(Maps.ResourceManager rm, Maps.SectorMap.Milieu map, int jump,
        bool wild, bool im, bool nored, bool aok, Maps.World end)
    {
        _rm = rm; _map = map; _jump = jump;
        _wild = wild; _im = im; _nored = nored; _aok = aok; _end = end;
    }

    public IEnumerable<Maps.World> Neighbors(Maps.World world)
    {
        var loc = Maps.Astrometrics.CoordinatesToLocation(world.Coordinates);
        foreach (Maps.World w in new Maps.HexSelector(_map, _rm, loc, _jump).Worlds)
        {
            if (w != _end)
            {
                if (!_aok && w.IsAnomaly) continue;
                if (_wild && w.GasGiants == 0 && !w.WaterPresent) continue;
                if (_nored && w.IsRed) continue;
                if (_im && !Maps.SecondSurvey.IsDefaultAllegiance(w.Allegiance)) continue;
            }
            yield return w;
        }
    }

    public double CostEstimate(Maps.World a, Maps.World b) =>
        Math.Ceiling(Maps.Astrometrics.HexDistance(a.Coordinates, b.Coordinates) / (double)_jump);

    public double EdgeWeight(Maps.World a, Maps.World b) =>
        1 + Maps.Astrometrics.HexDistance(a.Coordinates, b.Coordinates) / 36.0;
}
