﻿using Maps.Rendering;
using PdfSharp.Drawing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Web;

namespace Maps.API
{
    internal class JumpMapHandler : ImageHandlerBase
    {
        protected override string ServiceName { get { return "jumpmap"; } }

        protected override DataResponder GetResponder(HttpContext context)
        {
            return new Responder(context);
        }

        private class Responder : ImageResponder
        {
            public Responder(HttpContext context) : base(context) { }
            public override void Process()
            {
                // NOTE: This (re)initializes a static data structure used for 
                // resolving names into sector locations, so needs to be run
                // before any other objects (e.g. Worlds) are loaded.
                ResourceManager resourceManager = new ResourceManager(context.Server);

                //
                // Jump
                //
                int jump = Util.Clamp(GetIntOption("jump", 6), 0, 12);

                //
                // Content & Coordinates
                //
                Selector selector;
                Location loc;
                if (context.Request.HttpMethod == "POST")
                {
                    Sector sector;
                    try
                    {
                        bool lint = GetBoolOption("lint", defaultValue: false);
                        ErrorLogger errors = new ErrorLogger();
                        sector = GetPostedSector(context.Request, errors);
                        if (lint && !errors.Empty)
                        {
                            SendError(400, "Bad Request", errors.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        SendError(400, "Bad Request", ex.Message);
                        return;
                    }

                    if (sector == null)
                    {
                        SendError(400, "Bad Request", "Either file or data must be supplied in the POST data.");
                        return;
                    }

                    int hex = GetIntOption("hex", Astrometrics.SectorCentralHex);
                    loc = new Location(new Point(0, 0), hex);
                    selector = new HexSectorSelector(resourceManager, sector, loc.Hex, jump);
                }
                else
                {
                    SectorMap map = SectorMap.FromName(SectorMap.DefaultSetting, resourceManager);

                    if (HasOption("sector") && HasOption("hex"))
                    {
                        string sectorName = GetStringOption("sector");
                        int hex = GetIntOption("hex", 0);
                        Sector sector = map.FromName(sectorName);
                        if (sector == null)
                        {
                            SendError(404, "Not Found", string.Format("The specified sector '{0}' was not found.", sectorName));
                            return;
                        }

                        loc = new Location(sector.Location, hex);
                    }
                    else if (HasLocation())
                    {
                        loc = GetLocation();
                    }
                    else
                    {
                        loc = new Location(map.FromName("Spinward Marches").Location, 1910);
                    }
                    selector = new HexSelector(map, resourceManager, loc, jump);
                }


                //
                // Scale
                //
                double scale = Util.Clamp(GetDoubleOption("scale", 64), MinScale, MaxScale);

                //
                // Options & Style
                //
                MapOptions options = MapOptions.BordersMajor | MapOptions.BordersMinor | MapOptions.ForceHexes;
                Stylesheet.Style style = Stylesheet.Style.Poster;
                ParseOptions(ref options, ref style);

                //
                // Border
                //
                bool border = GetBoolOption("border", defaultValue: true);

                //
                // Clip
                //
                bool clip = GetBoolOption("clip", defaultValue: true);

                //
                // What to render
                //

                RectangleF tileRect = new RectangleF();

                Point coords = Astrometrics.LocationToCoordinates(loc);
                tileRect.X = coords.X - jump - 1;
                tileRect.Width = jump + 1 + jump;
                tileRect.Y = coords.Y - jump - 1;
                tileRect.Height = jump + 1 + jump;

                // Account for jagged hexes
                tileRect.Y += (coords.X % 2 == 0) ? 0 : 0.5f;
                tileRect.Inflate(0.35f, 0.15f);

                Size tileSize = new Size((int)Math.Floor(tileRect.Width * scale * Astrometrics.ParsecScaleX), (int)Math.Floor(tileRect.Height * scale * Astrometrics.ParsecScaleY));


                // Construct clipping path
                List<Point> clipPath = new List<Point>(jump * 6 + 1);
                Point cur = coords;
                for (int i = 0; i < jump; ++i)
                {
                    // Move J parsecs to the upper-left (start of border path logic)
                    cur = Astrometrics.HexNeighbor(cur, 1);
                }
                clipPath.Add(cur);
                for (int dir = 0; dir < 6; ++dir)
                {
                    for (int i = 0; i < jump; ++i)
                    {
                        cur = Astrometrics.HexNeighbor(cur, (dir + 3) % 6); // Clockwise from upper left
                        clipPath.Add(cur);
                    }
                }

                Stylesheet styles = new Stylesheet(scale, options, style);

                // If any names are showing, show them all
                if (styles.worldDetails.HasFlag(WorldDetails.KeyNames))
                    styles.worldDetails |= WorldDetails.AllNames;

                // Compute path
                float[] edgeX, edgeY;
                RenderUtil.HexEdges(styles.hexStyle == HexStyle.Square ? PathUtil.PathType.Square : PathUtil.PathType.Hex,
                    out edgeX, out edgeY);
                PointF[] boundingPathCoords;
                byte[] boundingPathTypes;
                PathUtil.ComputeBorderPath(clipPath, edgeX, edgeY, out boundingPathCoords, out boundingPathTypes);

                Render.RenderContext ctx = new Render.RenderContext();
                ctx.resourceManager = resourceManager;
                ctx.selector = selector;
                ctx.tileRect = tileRect;
                ctx.scale = scale;
                ctx.options = options;
                ctx.styles = styles;
                ctx.tileSize = tileSize;
                ctx.border = border;
                ctx.clipOutsectorBorders = true;

                // TODO: Widen path to allow for single-pixel border
                ctx.clipPath = clip ? new XGraphicsPath(boundingPathCoords, boundingPathTypes, XFillMode.Alternate) : null;
                ProduceResponse(context, "Jump Map", ctx, tileSize, transparent: clip);
            }
        }
    }
}
