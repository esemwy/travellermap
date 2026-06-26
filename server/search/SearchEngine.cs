#nullable enable
using Dapper;
using Maps.Database;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace Maps.Search
{
    // Holds the configured database connection factory.
    // Set once at application startup from Program.cs.
    internal static class DBUtil
    {
        public static IDbConnectionFactory? Factory { get; set; }

        public static IDbConnection MakeConnection()
        {
            if (Factory == null)
                throw new InvalidOperationException(
                    "Database not configured. Set DBUtil.Factory in Program.cs.");
            return Factory.CreateConnection();
        }

        public static ISqlDialect Dialect =>
            Factory?.Dialect ?? throw new InvalidOperationException("Database not configured.");
    }

    internal static class SearchEngine
    {
        [Flags]
        public enum SearchResultsType : int
        {
            Sectors    = 1 << 0,
            Subsectors = 1 << 1,
            Worlds     = 1 << 2,
            Labels     = 1 << 3,
            Default    = Sectors | Subsectors | Worlds | Labels
        }

        private static readonly object s_lock = new object();

        private static string SanifyLabel(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

        private static readonly string[] SECTORS_COLUMNS_NAMES =
            { "milieu", "x", "y", "name" };
        private static readonly string[] SUBSECTORS_COLUMNS_NAMES =
            { "milieu", "sector_x", "sector_y", "subsector_index", "name" };
        private static readonly string[] WORLDS_COLUMNS_NAMES = {
            "milieu", "x", "y", "sector_x", "sector_y", "hex_x", "hex_y",
            "name", "uwp", "remarks", "pbg", "zone", "alleg", "stellar",
            "ix", "ex", "cx", "sector_name"
        };
        private static readonly string[] LABELS_COLUMNS_NAMES =
            { "milieu", "x", "y", "radius", "name" };

        private static string MakeColumnDef(ISqlDialect d, string colName)
        {
            // Map column names to their types using the dialect.
            return colName switch
            {
                "milieu"           => $"milieu {d.VarCharType(12)} NULL",
                "x"                => "x int NOT NULL",
                "y"                => "y int NOT NULL",
                "sector_x"         => "sector_x int NOT NULL",
                "sector_y"         => "sector_y int NOT NULL",
                "hex_x"            => "hex_x int NOT NULL",
                "hex_y"            => "hex_y int NOT NULL",
                "radius"           => "radius int NOT NULL",
                "ix"               => "ix int NOT NULL",
                "subsector_index"  => $"subsector_index {d.CharType(1)} NOT NULL",
                "name"             => $"name {d.VarCharType(100)} NULL",
                "uwp"              => $"uwp {d.CharType(9)} NULL",
                "pbg"              => $"pbg {d.CharType(3)} NULL",
                "zone"             => $"zone {d.CharType(1)} NULL",
                "alleg"            => $"alleg {d.CharType(4)} NULL",
                "ex"               => $"ex {d.CharType(5)} NULL",
                "cx"               => $"cx {d.CharType(4)} NULL",
                "remarks"          => $"remarks {d.VarCharType(50)} NULL",
                "stellar"          => $"stellar {d.VarCharType(30)} NULL",
                "sector_name"      => $"sector_name {d.VarCharType(50)} NULL",
                _                  => throw new ArgumentException($"Unknown column: {colName}")
            };
        }

        public static void PopulateDatabase(ResourceManager resourceManager, Action<string> statusCallback)
        {
            lock (s_lock)
            {
                SectorMap map = SectorMap.GetInstance();
                var d = DBUtil.Dialect;

                // Collect all data in memory first.
                var rows_sectors    = new List<object?[]>();
                var rows_subsectors = new List<object?[]>();
                var rows_worlds     = new List<object?[]>();
                var rows_labels_raw = new Dictionary<Tuple<string, string>, List<Point>>();

                void AddLabel(string milieu, string text, Point coords)
                {
                    if (text == null) return;
                    text = SanifyLabel(text);
                    var key = Tuple.Create(milieu, text);
                    if (!rows_labels_raw.ContainsKey(key))
                        rows_labels_raw.Add(key, new List<Point>());
                    rows_labels_raw[key].Add(coords);
                }

                statusCallback("Parsing data...");
                foreach (Sector sector in map.Sectors)
                {
                    if (!sector.Tags.Contains("OTU") && !sector.Tags.Contains("Faraway"))
                        continue;

                    string suffix = sector.Tags.Contains("Apocryphal") ? " (Apocryphal)" :
                                    sector.Tags.Contains("Alternate")  ? " (Alternate)"  : "";

                    foreach (Name name in sector.Names)
                        rows_sectors.Add(new object?[] { sector.CanonicalMilieu, sector.X, sector.Y, name.Text });

                    if (!string.IsNullOrEmpty(sector.Abbreviation))
                        rows_sectors.Add(new object?[] { sector.CanonicalMilieu, sector.X, sector.Y, sector.Abbreviation! });

                    foreach (Subsector subsector in sector.Subsectors)
                        rows_subsectors.Add(new object?[] { sector.CanonicalMilieu, sector.X, sector.Y, subsector.Index, subsector.Name });

                    foreach (Border border in sector.BordersAndRegions.Where(b => b.ShowLabel))
                        AddLabel(sector.CanonicalMilieu, border.GetLabel(sector) + suffix,
                            Astrometrics.LocationToCoordinates(new Location(sector.Location, border.LabelPosition)));

                    foreach (Label label in sector.Labels)
                        AddLabel(sector.CanonicalMilieu, label.Text + suffix,
                            Astrometrics.LocationToCoordinates(new Location(sector.Location, label.Hex)));

#if DEBUG
                    if (!sector.Selected) continue;
#endif
                    WorldCollection? worlds = sector.GetWorlds(resourceManager, cacheResults: false);
                    if (worlds == null) continue;

                    foreach (World world in worlds.Where(w => !w.IsPlaceholder))
                    {
                        rows_worlds.Add(new object?[] {
                            sector.CanonicalMilieu,
                            world.Coordinates.X,
                            world.Coordinates.Y,
                            sector.X, sector.Y,
                            world.X, world.Y,
                            string.IsNullOrEmpty(world.Name) ? null : (object?)world.Name,
                            world.UWP,
                            world.Remarks,
                            world.PBG,
                            string.IsNullOrEmpty(world.Zone) ? "G" : world.Zone,
                            world.Allegiance,
                            world.Stellar,
                            world.CalculatedImportance,
                            StripBrackets(world.Economic),
                            StripBrackets(world.Cultural),
                            sector.Names.Count > 0 ? (object?)sector.Names[0].Text : null
                        });
                    }
                }

                var rows_labels = new List<object?[]>();
                foreach (var (key, points) in rows_labels_raw)
                {
                    var (milieu, name) = key;
                    int avgX   = (int)Math.Round(points.Average(p => p.X));
                    int avgY   = (int)Math.Round(points.Average(p => p.Y));
                    int radius = Math.Max(points.Max(p => p.X) - points.Min(p => p.X),
                                         points.Max(p => p.Y) - points.Min(p => p.Y));
                    rows_labels.Add(new object?[] { milieu, avgX, avgY, radius, name });
                }

                // Rebuild schema and insert data.
                using var connection = DBUtil.MakeConnection();

                statusCallback("Rebuilding schema...");
                void Exec(string sql)
                {
                    connection.Execute(sql);
                }

                void RebuildTable(string tableName, string[] columns, (string name, string col)[] indexes)
                {
                    Exec(d.DropTableIfExists(tableName));
                    Exec($"CREATE TABLE {tableName} ({string.Join(", ", columns.Select(c => MakeColumnDef(d, c)))})");
                    foreach (var (idxName, col) in indexes)
                        Exec(d.CreateIndex(idxName, tableName, col));
                }

                RebuildTable("sectors",    SECTORS_COLUMNS_NAMES,    new[] { ("sector_name", "name"), ("sector_milieu", "milieu") });
                RebuildTable("subsectors", SUBSECTORS_COLUMNS_NAMES, new[] { ("subsector_name", "name"), ("subsector_milieu", "milieu") });
                RebuildTable("worlds",     WORLDS_COLUMNS_NAMES,     new[] {
                    ("world_name", "name"), ("world_uwp", "uwp"), ("world_pbg", "pbg"),
                    ("world_alleg", "alleg"), ("world_stellar", "stellar"),
                    ("world_sector_name", "sector_name"), ("world_milieu", "milieu")
                });
                RebuildTable("labels",     LABELS_COLUMNS_NAMES,     new[] { ("label_name", "name"), ("label_milieu", "milieu") });

                statusCallback($"Writing {rows_sectors.Count} sectors...");
                d.BulkInsert(connection, "sectors", SECTORS_COLUMNS_NAMES, rows_sectors);

                statusCallback($"Writing {rows_subsectors.Count} subsectors...");
                d.BulkInsert(connection, "subsectors", SUBSECTORS_COLUMNS_NAMES, rows_subsectors);

                statusCallback($"Writing {rows_worlds.Count} worlds...");
                d.BulkInsert(connection, "worlds", WORLDS_COLUMNS_NAMES, rows_worlds);

                statusCallback($"Writing {rows_labels.Count} labels...");
                d.BulkInsert(connection, "labels", LABELS_COLUMNS_NAMES, rows_labels);

                statusCallback("Complete!");
            }
        }

        public static IEnumerable<SearchResult> PerformSearch(string? milieu, string? query,
            SearchResultsType types, int maxResultsPerType, bool random = false)
        {
            var results = new List<SearchResult>();
            var d = DBUtil.Dialect;

            types = ParseQuery(query, types, out var clauses, out var terms);
            if (!clauses.Any() && !random)
                return results;

            clauses.Insert(0, "milieu = @term");
            terms.Insert(0, milieu ?? SectorMap.DEFAULT_MILIEU);

            string where = string.Join(" AND ",
                clauses.Select((clause, i) => "(" + clause.Replace("@term", $"@term{i}") + ")"));

            string orderBy = random ? $"ORDER BY {d.RandomOrder}" : "";

            using var connection = DBUtil.MakeConnection();

            DynamicParameters BuildParams(List<string> t)
            {
                var p = new DynamicParameters();
                for (int i = 0; i < t.Count; i++)
                    p.Add($"@term{i}", t[i]);
                return p;
            }

            if (types.HasFlag(SearchResultsType.Sectors))
            {
                string sql = d.FormatDistinctTopQuery("TT.x, TT.y", "x, y", "sectors", where, orderBy, maxResultsPerType);
                foreach (var row in connection.Query(sql, BuildParams(terms)))
                    results.Add(new SectorResult(row.x, row.y));
            }

            if (types.HasFlag(SearchResultsType.Subsectors))
            {
                string sql = d.FormatDistinctTopQuery(
                    "TT.sector_x, TT.sector_y, TT.subsector_index",
                    "sector_x, sector_y, subsector_index",
                    "subsectors", where, orderBy, maxResultsPerType);
                foreach (var row in connection.Query(sql, BuildParams(terms)))
                    results.Add(new SubsectorResult(row.sector_x, row.sector_y, (char)row.subsector_index[0]));
            }

            if (types.HasFlag(SearchResultsType.Worlds))
            {
                string sql = d.FormatDistinctTopQuery(
                    "TT.sector_x, TT.sector_y, TT.hex_x, TT.hex_y",
                    "sector_x, sector_y, hex_x, hex_y",
                    "worlds", where, orderBy, maxResultsPerType);
                foreach (var row in connection.Query(sql, BuildParams(terms)))
                    results.Add(new WorldResult(row.sector_x, row.sector_y, (byte)row.hex_x, (byte)row.hex_y));
            }

            if (types.HasFlag(SearchResultsType.Labels))
            {
                string sql = d.FormatDistinctTopQuery(
                    "TT.x, TT.y, TT.radius, TT.name",
                    "x, y, radius, name",
                    "labels", where, orderBy, maxResultsPerType);
                foreach (var row in connection.Query(sql, BuildParams(terms)))
                    results.Add(new LabelResult(row.name, new Point(row.x, row.y), row.radius));
            }

            return results;
        }

        public static WorldResult? FindNearestWorldMatch(string name, string milieu, int x, int y)
        {
            const string sql =
                "SELECT sector_x, sector_y, hex_x, hex_y, " +
                "((@x - x) * (@x - x) + (@y - y) * (@y - y)) AS distance " +
                "FROM worlds WHERE name = @name AND milieu = @milieu " +
                "ORDER BY distance ASC LIMIT 1";

            milieu ??= SectorMap.DEFAULT_MILIEU;
            using var connection = DBUtil.MakeConnection();
            var row = connection.QueryFirstOrDefault(sql,
                new { x, y, milieu, name });
            if (row == null) return null;
            return new WorldResult(row.sector_x, row.sector_y, (byte)row.hex_x, (byte)row.hex_y);
        }

        private static readonly string[] OPS = {
            "uwp:", "pbg:", "zone:", "alleg:", "stellar:", "remark:",
            "exact:", "like:", "in:", "ix:", "ex:", "cx:"
        };

        private static readonly Regex RE_TERMS = new Regex(
            "(" + string.Join("|", OPS) + ")?(\"[^\"]+\"|\\S+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static IEnumerable<string> ParseTerms(string q) =>
            RE_TERMS.Matches(q).Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value).Where(s => !string.IsNullOrWhiteSpace(s));

        private static SearchResultsType ParseQuery(string? query, SearchResultsType types,
            out List<string> clauses, out List<string> terms)
        {
            clauses = new List<string>();
            terms   = new List<string>();
            if (string.IsNullOrWhiteSpace(query))
                return types;
            query = query!.Trim().ToLowerInvariant();

            var m = Regex.Match(query,
                @"^(?<sector>[A-Za-z0-9!' ]{3,}) (?<hex>\d{4})$",
                RegexOptions.CultureInvariant);
            if (m.Success)
            {
                int hex = int.Parse(m.Groups["hex"].Value);
                clauses.Add("sector_name LIKE CONCAT(@term, '%')");
                terms.Add(m.Groups["sector"].Value);
                clauses.Add("hex_x = @term");
                terms.Add((hex / 100).ToString());
                clauses.Add("hex_y = @term");
                terms.Add((hex % 100).ToString());
                return SearchResultsType.Worlds;
            }

            foreach (string t in ParseTerms(query))
            {
                string term = t;
                string? op = null;
                bool quoted = false;

                foreach (var o in OPS)
                {
                    if (term.StartsWith(o)) { op = o; term = term[o.Length..]; break; }
                }

                if (term.StartsWith("\"") && (!term.EndsWith("\"") || term.Length == 1))
                    term += '"';
                if (term.Length >= 2 && term.StartsWith("\"") && term.EndsWith("\""))
                {
                    quoted = true;
                    term = term[1..^1];
                }
                if (term.Length == 0) continue;

                string clause;
                if      (op == "uwp:")    { clause = "uwp LIKE @term";                                            types = SearchResultsType.Worlds; }
                else if (op == "pbg:")    { clause = "pbg LIKE @term";                                            types = SearchResultsType.Worlds; }
                else if (op == "ix:")     { clause = "ix = @term";                                                types = SearchResultsType.Worlds; }
                else if (op == "ex:")     { clause = "ex LIKE @term";                                             types = SearchResultsType.Worlds; }
                else if (op == "cx:")     { clause = "cx LIKE @term";                                             types = SearchResultsType.Worlds; }
                else if (op == "zone:")   { clause = "zone LIKE @term";                                           types = SearchResultsType.Worlds; }
                else if (op == "alleg:")  { clause = "alleg LIKE @term";                                          types = SearchResultsType.Worlds; }
                else if (op == "stellar:"){ clause = "CONCAT(' ', stellar, ' ') LIKE CONCAT('% ', @term, ' %')";  types = SearchResultsType.Worlds; }
                else if (op == "remark:") { clause = "CONCAT(' ', remarks, ' ') LIKE CONCAT('% ', @term, ' %')"; types = SearchResultsType.Worlds; }
                else if (op == "in:")     { clause = "sector_name LIKE CONCAT('%', @term, '%')";                  types = SearchResultsType.Worlds; }
                else if (op == "exact:")  { clause = "name LIKE @term"; }
                else if (op == "like:")   { clause = "SOUNDEX(name) = SOUNDEX(@term)"; }
                else if (quoted)          { clause = "name LIKE @term"; }
                else if (term.Contains('%') || term.Contains('_')) { clause = "name LIKE @term"; }
                else if (term.Equals("sector",    StringComparison.InvariantCultureIgnoreCase)) { types = SearchResultsType.Sectors;    continue; }
                else if (term.Equals("subsector", StringComparison.InvariantCultureIgnoreCase)) { types = SearchResultsType.Subsectors; continue; }
                else if (term.Equals("world",     StringComparison.InvariantCultureIgnoreCase)) { types = SearchResultsType.Worlds;     continue; }
                else    { clause = "name LIKE CONCAT(@term, '%') OR name LIKE CONCAT('% ', @term, '%')"; }

                clauses.Add(clause);
                terms.Add(term);
            }
            return types;
        }

        private static string? StripBrackets(string? input) =>
            input == null ? null : string.Join("", input.Split('(', ')', '[', ']', '{', '}'));
    }
}
