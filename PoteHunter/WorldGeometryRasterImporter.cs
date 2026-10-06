using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace PoteHunter;

/// <summary>
/// Imports a MapTool WorldGeometry JSON export as a calibrated, top-down overlay bitmap.
/// The calibration profile marks geometry already transformed into calibrated world coordinates.
/// The explicit affine-calibration provenance is required; visual alignment remains a preview gate.
/// </summary>
internal static class MapToolWorldRenderer
{
    const long MaxFileBytes = 384L * 1024 * 1024;
    const int MaxDimension = 4096;
    const long MaxPixels = 16L * 1024 * 1024;
    const int MaxTriangles = 2_000_000;
    const int MaxVertices = MaxTriangles * 3;
    const double CoordinateLimit = 1_000_000_000;

    readonly record struct Point2(double X, double Y);
    readonly record struct Triangle(Point2 A, Point2 B, Point2 C, string Kind);
    readonly record struct Footprint(Point2 Min, Point2 Max, string Kind);

    internal sealed record RasterResult(
        Bitmap Image,
        double MinX,
        double MinY,
        double MaxX,
        double MaxY,
        string SourceZoneId,
        string ClientSha256,
        string CoordinateConvention,
        int TriangleCount,
        int StaticObjectCount,
        string SourceFileSha256,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<RouteObstacle> CollisionObstacles) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    internal static RasterResult Render(string jsonPath, string expectedClientSha256, int expectedPoteHunterZone, int maxDimension = 2048)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        if (expectedPoteHunterZone < 0) throw new ArgumentOutOfRangeException(nameof(expectedPoteHunterZone));
        ValidateHash(expectedClientSha256, "expected client SHA-256");
        if (maxDimension is < 32 or > MaxDimension)
            throw new InvalidDataException("Requested raster dimensions exceed the supported limits.");

        var info = new FileInfo(jsonPath);
        if (!info.Exists) throw new FileNotFoundException("WorldGeometry JSON was not found.", jsonPath);
        if (info.Length <= 0 || info.Length > MaxFileBytes) throw new InvalidDataException("WorldGeometry JSON must be between 1 byte and 384 MiB.");

        byte[] jsonBytes = File.ReadAllBytes(jsonPath);
        using JsonDocument document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("WorldGeometry root must be an object.");

        string fileHash = RequiredString(root, "clientSha256");
        ValidateHash(fileHash, "clientSha256");
        if (!string.Equals(fileHash, expectedClientSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WorldGeometry client SHA-256 does not match the expected client.");

        string zoneLabel = GetZoneLabel(root);
        string coordinateConvention = ReadCalibrationProvenance(root);
        if (!SameZone(zoneLabel, expectedPoteHunterZone.ToString(CultureInfo.InvariantCulture)))
            throw new InvalidDataException($"WorldGeometry zone '{zoneLabel}' does not match the expected PoteHunter zone '{expectedPoteHunterZone}'.");
        if (!root.TryGetProperty("terrain", out JsonElement terrain) || terrain.ValueKind != JsonValueKind.Array || terrain.GetArrayLength() == 0)
            throw new InvalidDataException("WorldGeometry must contain at least one terrain mesh.");

        var triangles = new List<Triangle>();
        var footprints = new List<Footprint>();
        var collisionObstacles = new List<RouteObstacle>();
        var warnings = new List<string>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        int terrainIndex = 0;
        foreach (JsonElement entry in terrain.EnumerateArray())
        {
            terrainIndex++;
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("mesh", out JsonElement mesh) || mesh.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"terrain[{terrainIndex - 1}] has no mesh object.");
            string kind = OptionalString(mesh, "kind") ?? "terrain";
            if (!mesh.TryGetProperty("vertices", out JsonElement vertices) || !mesh.TryGetProperty("indices", out JsonElement indices))
                throw new InvalidDataException($"terrain[{terrainIndex - 1}] mesh needs vertices and indices.");
            string context = $"terrain[{terrainIndex - 1}]";
            List<(double X, double Y, double Z)> points = ReadVertices(vertices, context);
            List<int> indexList = ReadIndices(indices, points.Count, context);
            if (indexList.Count == 0 || indexList.Count % 3 != 0) throw new InvalidDataException($"terrain[{terrainIndex - 1}] indices must contain complete triangles.");
            if ((long)triangles.Count + indexList.Count / 3 > MaxTriangles) throw new InvalidDataException("WorldGeometry exceeds the triangle limit.");

            for (int i = 0; i < indexList.Count; i += 3)
            {
                var a = Convert(points[indexList[i]]);
                var b = Convert(points[indexList[i + 1]]);
                var c = Convert(points[indexList[i + 2]]);
                double area2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (!double.IsFinite(area2) || Math.Abs(area2) < 1e-10)
                    throw new InvalidDataException($"terrain[{terrainIndex - 1}] contains a degenerate triangle.");
                triangles.Add(new Triangle(a, b, c, kind));
                Include(a); Include(b); Include(c);
            }
        }

        var meshesById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var meshVerticesById = new Dictionary<string, List<(double X, double Y, double Z)>>(StringComparer.Ordinal);
        if (root.TryGetProperty("meshes", out JsonElement meshes) && meshes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("meshes must be an array when present.");
        if (meshes.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement mesh in meshes.EnumerateArray())
            {
                if (mesh.ValueKind != JsonValueKind.Object || OptionalString(mesh, "id") is not { Length: > 0 } id)
                    throw new InvalidDataException("Each mesh must have a non-empty id.");
                if (!meshesById.TryAdd(id, mesh)) throw new InvalidDataException($"WorldGeometry contains duplicate mesh id '{id}'.");
            }
        }

        if (root.TryGetProperty("instances", out JsonElement instances) && instances.ValueKind != JsonValueKind.Null && instances.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("instances must be an array when present.");
        if (instances.ValueKind == JsonValueKind.Array && instances.GetArrayLength() != 0)
        {
            int renderedInstances = 0, unresolvedInstances = 0, instanceIndex = 0;
            foreach (JsonElement instance in instances.EnumerateArray())
            {
                string context = $"instances[{instanceIndex++}]";
                if (instance.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{context} must be an object.");
                string? meshId = OptionalString(instance, "meshId");
                // Render meshes preserve recognizable map details. Collision meshes are
                // candidate data and are used only where no rendered model was decoded.
                if (meshId is null || !meshesById.TryGetValue(meshId, out JsonElement mesh))
                {
                    meshId = OptionalString(instance, "collisionMeshId");
                    if (meshId is null || !meshesById.TryGetValue(meshId, out mesh))
                    {
                        unresolvedInstances++;
                        continue;
                    }
                }
                if (!TryMatrix(instance, out Matrix4x4 transform))
                {
                    unresolvedInstances++;
                    continue;
                }

                // Retain the decoded collision mesh as navigation geometry too.
                // The raster is only a visual layer; without this data the route
                // planner can only learn a blocker after movement collides with it.
                string? collisionMeshId=OptionalString(instance,"collisionMeshId");
                if(collisionMeshId is {Length:>0} && meshesById.TryGetValue(collisionMeshId,out JsonElement collisionMesh))
                {
                    if(!meshVerticesById.TryGetValue(collisionMeshId,out var collisionPoints))
                    {
                        if(!collisionMesh.TryGetProperty("vertices",out JsonElement collisionVertices))
                            throw new InvalidDataException($"Mesh '{collisionMeshId}' needs vertices.");
                        meshVerticesById[collisionMeshId]=collisionPoints=ReadVertices(collisionVertices,$"collision mesh '{collisionMeshId}'");
                    }
                    double collisionMinX=double.PositiveInfinity,collisionMinY=double.PositiveInfinity;
                    double collisionMaxX=double.NegativeInfinity,collisionMaxY=double.NegativeInfinity;
                    foreach(var sourcePoint in collisionPoints)
                    {
                        Point2 point=Transform(sourcePoint,transform,context+" collision");
                        collisionMinX=Math.Min(collisionMinX,point.X);collisionMaxX=Math.Max(collisionMaxX,point.X);
                        collisionMinY=Math.Min(collisionMinY,point.Y);collisionMaxY=Math.Max(collisionMaxY,point.Y);
                    }
                    if(collisionMaxX-collisionMinX>1e-6||collisionMaxY-collisionMinY>1e-6)
                    {
                        if(collisionObstacles.Count>=50000)throw new InvalidDataException("WorldGeometry has more than 50,000 collision candidates.");
                        const double clearance=.35;
                        var polygon=new[]{new Vec(collisionMinX-clearance,collisionMinY-clearance),new Vec(collisionMaxX+clearance,collisionMinY-clearance),
                            new Vec(collisionMaxX+clearance,collisionMaxY+clearance),new Vec(collisionMinX-clearance,collisionMaxY+clearance)};
                        double halfX=(collisionMaxX-collisionMinX)*.5+clearance,halfY=(collisionMaxY-collisionMinY)*.5+clearance;
                        double radius=Math.Sqrt(halfX*halfX+halfY*halfY);
                        collisionObstacles.Add(new RouteObstacle(new((collisionMinX+collisionMaxX)*.5,(collisionMinY+collisionMaxY)*.5),radius,
                            $"Static collision {collisionMeshId}",polygon));
                    }
                }

                if (!mesh.TryGetProperty("vertices", out JsonElement vertices))
                    throw new InvalidDataException($"Mesh '{meshId}' needs vertices.");
                if (!meshVerticesById.TryGetValue(meshId, out var points))
                    meshVerticesById[meshId] = points = ReadVertices(vertices, $"mesh '{meshId}'");
                string kind = OptionalString(mesh, "kind") ?? "render";
                double objectMinX = double.PositiveInfinity, objectMinY = double.PositiveInfinity;
                double objectMaxX = double.NegativeInfinity, objectMaxY = double.NegativeInfinity;
                foreach (var sourcePoint in points)
                {
                    Point2 point = Transform(sourcePoint, transform, context);
                    objectMinX = Math.Min(objectMinX, point.X); objectMaxX = Math.Max(objectMaxX, point.X);
                    objectMinY = Math.Min(objectMinY, point.Y); objectMaxY = Math.Max(objectMaxY, point.Y);
                    Include(point);
                }
                if (objectMaxX - objectMinX > 1e-6 || objectMaxY - objectMinY > 1e-6)
                    footprints.Add(new Footprint(new Point2(objectMinX, objectMinY), new Point2(objectMaxX, objectMaxY), kind));
                renderedInstances++;
            }
            if (unresolvedInstances > 0)
                warnings.Add($"{unresolvedInstances} static placements had no decoded render or collision mesh and remain absent from this raster.");
            if (renderedInstances > 0)
                warnings.Add($"Added approximate static-object footprints for {renderedInstances} placements from calibrated mesh transforms.");
        }

        double spanX = maxX - minX, spanY = maxY - minY;
        if (!double.IsFinite(spanX) || !double.IsFinite(spanY) || spanX <= 0 || spanY <= 0)
            throw new InvalidDataException("WorldGeometry has no usable 2D extent.");

        double aspect = spanX / spanY;
        int width = aspect >= 1 ? maxDimension : Math.Max(32, (int)Math.Round(maxDimension * aspect));
        int height = aspect >= 1 ? Math.Max(32, (int)Math.Round(maxDimension / aspect)) : maxDimension;
        if ((long)width * height > MaxPixels) throw new InvalidDataException("WorldGeometry raster dimensions exceed the pixel limit.");
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(24, 30, 38));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            double scale = Math.Min((width - 2.0) / spanX, (height - 2.0) / spanY);
            double drawWidth = spanX * scale, drawHeight = spanY * scale;
            double offsetX = (width - drawWidth) / 2.0, offsetY = (height - drawHeight) / 2.0;
            PointF Pixel(Point2 p) => new((float)(offsetX + (p.X - minX) * scale), (float)(offsetY + (maxY - p.Y) * scale));
            var brushes = new Dictionary<string, SolidBrush>(StringComparer.OrdinalIgnoreCase);
            SolidBrush BrushFor(string kind, bool staticObject)
            {
                string key = (staticObject ? "object:" : "terrain:") + kind;
                if (!brushes.TryGetValue(key, out var brush)) brushes[key] = brush = new SolidBrush(KindColor(kind, staticObject));
                return brush;
            }
            try
            {
                foreach (Triangle triangle in triangles)
                {
                    PointF a = Pixel(triangle.A), b = Pixel(triangle.B), c = Pixel(triangle.C);
                    double pixelArea = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * .5;
                    if (pixelArea < .25) continue;
                    graphics.FillPolygon(BrushFor(triangle.Kind, false), new[] { a, b, c });
                }
                using var footprintOutline = new Pen(Color.FromArgb(215, 48, 42, 36), 1);
                foreach (Footprint footprint in footprints)
                {
                    PointF topLeft = Pixel(new Point2(footprint.Min.X, footprint.Max.Y));
                    PointF bottomRight = Pixel(new Point2(footprint.Max.X, footprint.Min.Y));
                    float footprintWidth = Math.Max(1, bottomRight.X - topLeft.X), footprintHeight = Math.Max(1, bottomRight.Y - topLeft.Y);
                    var bounds = new RectangleF(topLeft.X, topLeft.Y, footprintWidth, footprintHeight);
                    graphics.FillRectangle(BrushFor(footprint.Kind, true), bounds);
                    graphics.DrawRectangle(footprintOutline, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
            }
            finally { foreach (SolidBrush brush in brushes.Values) brush.Dispose(); }
            graphics.Flush();
            warnings.Add("The map shows calibrated terrain triangles and approximate static-object footprints; confirm visual alignment in the overlay preview before saving it as a zone map.");
            if(collisionObstacles.Count>0)warnings.Add($"Loaded {collisionObstacles.Count} collision candidates for route avoidance.");
            else warnings.Add("No decoded collision candidates were found; rendered static objects will not affect route planning.");
            return new RasterResult(bitmap, minX, minY, maxX, maxY, zoneLabel, fileHash, coordinateConvention, triangles.Count, footprints.Count, System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(jsonBytes)).ToLowerInvariant(), warnings.AsReadOnly(),collisionObstacles.AsReadOnly());
        }
        catch { bitmap.Dispose(); throw; }

        Point2 Convert((double X, double Y, double Z) p)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.X) > CoordinateLimit || Math.Abs(p.Y) > CoordinateLimit || Math.Abs(p.Z) > CoordinateLimit)
                throw new InvalidDataException("WorldGeometry contains a non-finite or out-of-range vertex.");
            return new Point2(p.X, p.Z);
        }

        Point2 Transform((double X, double Y, double Z) p, Matrix4x4 matrix, string context)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.X) > CoordinateLimit || Math.Abs(p.Y) > CoordinateLimit || Math.Abs(p.Z) > CoordinateLimit)
                throw new InvalidDataException($"{context} contains a non-finite or out-of-range vertex.");
            Vector3 point = Vector3.Transform(new Vector3((float)p.X, (float)p.Y, (float)p.Z), matrix);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Z) || Math.Abs(point.X) > CoordinateLimit || Math.Abs(point.Z) > CoordinateLimit)
                throw new InvalidDataException($"{context} transform produces a non-finite or out-of-range vertex.");
            return new Point2(point.X, point.Z);
        }

        void Include(Point2 p) { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
    }

    static string GetZoneLabel(JsonElement root)
    {
        if (!root.TryGetProperty("zoneId", out JsonElement zone)) throw new InvalidDataException("WorldGeometry is missing zoneId.");
        return zone.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(zone.GetString()) => zone.GetString()!.Trim(),
            JsonValueKind.Number when zone.TryGetInt64(out long n) && n >= 0 => n.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("zoneId must be a non-empty string or non-negative integer.")
        };
    }

    static string ReadCalibrationProvenance(JsonElement root)
    {
        string? validation = OptionalString(root, "validationLevel");
        if (validation is null || string.IsNullOrWhiteSpace(validation))
            throw new InvalidDataException("WorldGeometry is missing validationLevel metadata.");
        if (OptionalString(root, "coordinateConvention") is not { } convention || string.IsNullOrWhiteSpace(convention) ||
            !convention.Contains("explicit affine calibration", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("WorldGeometry is missing coordinateConvention.");
        if (!root.TryGetProperty("sources", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("WorldGeometry needs calibration provenance in sources.");

        foreach (JsonElement source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object || !string.Equals(OptionalString(source, "format"), "coordinate-calibration-profile", StringComparison.OrdinalIgnoreCase)) continue;
            return convention;
        }
        throw new InvalidDataException("No coordinate-calibration-profile source was found.");
    }

    static List<(double X, double Y, double Z)> ReadVertices(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 3)
            throw new InvalidDataException($"{context} vertices must be an array of xyz points or flat xyz numbers.");
        var result = new List<(double X, double Y, double Z)>();
        JsonElement first = element[0];
        if (first.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
        {
            if (element.GetArrayLength() > MaxVertices) throw new InvalidDataException("WorldGeometry exceeds the vertex limit.");
            foreach (JsonElement point in element.EnumerateArray())
            {
                double x = 0, y = 0, z = 0;
                bool valid = point.ValueKind switch
                {
                    JsonValueKind.Array => point.GetArrayLength() >= 3 && Number(point[0], out x) && Number(point[1], out y) && Number(point[2], out z),
                    JsonValueKind.Object => TryNumber(point, "x", out x) && TryNumber(point, "y", out y) && TryNumber(point, "z", out z),
                    _ => false
                };
                if (!valid || Math.Abs(y) > CoordinateLimit)
                    throw new InvalidDataException($"{context} has an invalid xyz vertex.");
                result.Add((x, y, z));
            }
        }
        else
        {
            if (element.GetArrayLength() % 3 != 0 || element.GetArrayLength() / 3 > MaxVertices) throw new InvalidDataException($"{context} flat vertices must contain bounded xyz triples.");
            for (int i = 0; i < element.GetArrayLength(); i += 3)
            {
                if (!Number(element[i], out double x) || !Number(element[i + 1], out double y) || !Number(element[i + 2], out double z) || Math.Abs(y) > CoordinateLimit) throw new InvalidDataException($"{context} has invalid flat xyz values.");
                result.Add((x, y, z));
            }
        }
        return result;
    }

    static List<int> ReadIndices(JsonElement element, int vertexCount, string context)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxTriangles * 3)
            throw new InvalidDataException($"{context} indices must be a bounded array.");
        var result = new List<int>(element.GetArrayLength());
        foreach (JsonElement index in element.EnumerateArray())
        {
            if (!index.TryGetInt32(out int value) || value < 0 || value >= vertexCount) throw new InvalidDataException($"{context} has an out-of-range triangle index.");
            result.Add(value);
        }
        return result;
    }

    static Color KindColor(string kind, bool staticObject = false) => staticObject ? kind.ToLowerInvariant() switch
    {
        "collision" => Color.FromArgb(220, 184, 116, 84),
        _ => Color.FromArgb(205, 145, 139, 124)
    } : kind.ToLowerInvariant() switch
    {
        "water" or "liquid" => Color.FromArgb(170, 47, 116, 156),
        "road" or "path" => Color.FromArgb(210, 155, 139, 112),
        "building" or "structure" => Color.FromArgb(220, 133, 126, 119),
        "terrain" or "ground" or "land" => Color.FromArgb(210, 86, 111, 83),
        _ => Color.FromArgb(205, 112, 119, 128)
    };

    static string RequiredString(JsonElement obj, string key) => OptionalString(obj, key) is { } value ? value : throw new InvalidDataException($"WorldGeometry is missing {key}.");
    static string? OptionalString(JsonElement obj, string key) => obj.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static bool TryNumber(JsonElement obj, string key, out double number)
    {
        number = 0;
        return obj.TryGetProperty(key, out JsonElement value) && Number(value, out number);
    }
    static bool TryMatrix(JsonElement instance, out Matrix4x4 matrix)
    {
        matrix = default;
        if (!string.Equals(OptionalString(instance, "transformConvention"), "row-major-system-numerics-v1", StringComparison.Ordinal) ||
            !instance.TryGetProperty("matrix", out JsonElement values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 16)
            return false;
        var entries = new float[16];
        for (int i = 0; i < entries.Length; i++)
            if (!values[i].TryGetSingle(out entries[i]) || !float.IsFinite(entries[i])) return false;
        matrix = new Matrix4x4(entries[0], entries[1], entries[2], entries[3], entries[4], entries[5], entries[6], entries[7],
            entries[8], entries[9], entries[10], entries[11], entries[12], entries[13], entries[14], entries[15]);
        double determinant = matrix.GetDeterminant();
        return matrix.M14 == 0 && matrix.M24 == 0 && matrix.M34 == 0 && matrix.M44 == 1 &&
            double.IsFinite(determinant) && Math.Abs(determinant) > 1e-8;
    }
    static bool Number(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number);
    }

    static bool SameZone(string geometryZone, string expectedZone)
    {
        static string Normalize(string value)
        {
            string trimmed = value.Trim();
            return trimmed.StartsWith("zone", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4 && trimmed[4..].All(char.IsDigit) ? trimmed[4..] : trimmed;
        }
        return string.Equals(Normalize(geometryZone), Normalize(expectedZone), StringComparison.OrdinalIgnoreCase);
    }

    static void ValidateHash(string hash, string field)
    {
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException($"{field} must be a 64-character hexadecimal SHA-256 value.");
    }

    internal static void SelfTest()
    {
        string root = Path.Combine(Path.GetTempPath(), "PoteHunter-world-geometry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        try
        {
            string valid = Synthetic(hash, calibration: true);
            string path = Path.Combine(root, "valid.json");
            File.WriteAllText(path, valid);
            using (RasterResult result = Render(path, hash, 0, 100))
            {
                if (result.SourceZoneId != "zone0" || (result.MinX, result.MinY, result.MaxX, result.MaxY) != (10d, 20d, 30d, 40d)) throw new Exception("WorldGeometry calibrated bounds did not map X/Z into PoteHunter X/Y.");
                if (result.Image.Width != 100 || result.Image.Height != 100 || result.TriangleCount != 1 || result.Image.GetPixel(20, 80).ToArgb() == result.Image.GetPixel(80, 20).ToArgb())
                    throw new Exception("WorldGeometry raster orientation is not top-down (image top should represent MaxY).");
            }
            string withStaticObject = Synthetic(hash, calibration: true).Replace(
                "\"instances\":[]",
                "\"meshes\":[{\"id\":\"model:tree\",\"kind\":\"Render\",\"vertices\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":20,\"y\":0,\"z\":0},{\"x\":0,\"y\":0,\"z\":20}],\"indices\":[0,1,2]},{\"id\":\"model:wall-collision\",\"kind\":\"Collision\",\"vertices\":[{\"x\":-2,\"y\":0,\"z\":-1},{\"x\":2,\"y\":0,\"z\":-1},{\"x\":2,\"y\":0,\"z\":1},{\"x\":-2,\"y\":0,\"z\":1}],\"indices\":[0,1,2,0,2,3]}],\"instances\":[{\"meshId\":\"model:tree\",\"collisionMeshId\":\"model:wall-collision\",\"transformConvention\":\"row-major-system-numerics-v1\",\"matrix\":[1,0,0,0,0,1,0,0,0,0,1,0,40,0,50,1]}]");
            string staticPath = Path.Combine(root, "static-objects.json");
            File.WriteAllText(staticPath, withStaticObject);
            using (RasterResult result = Render(staticPath, hash, 0, 100))
            {
            if (result.TriangleCount != 1 || result.StaticObjectCount != 1 || result.MaxX != 60 || result.MaxY != 70 || result.CollisionObstacles.Count!=1 ||
                    result.CollisionObstacles[0].Center!=new Vec(40,50) || result.CollisionObstacles[0].Polygon?.Length!=4 ||
                    !result.Warnings.Any(warning => warning.Contains("static-object footprints", StringComparison.Ordinal)))
                    throw new Exception("Calibrated static model footprints and collision route blockers were not transformed into world space.");
            }
            ExpectFailure(Path.Combine(root, "uncalibrated.json"), Synthetic(hash, calibration: false), hash, "missing affine calibration provenance", 0);
            ExpectFailure(Path.Combine(root, "wrong-hash.json"), Synthetic(new string('a', 64), calibration: true), hash, "client SHA mismatch", 0);
            ExpectFailure(Path.Combine(root, "wrong-zone.json"), Synthetic(hash, calibration: true), hash, "unexpected zone", 1);
            ExpectFailure(Path.Combine(root, "malformed.json"), "{\"zoneId\":", hash, "malformed JSON", 0);
            string oversized = Path.Combine(root, "oversized.json");
            using (var file = new FileStream(oversized, FileMode.Create, FileAccess.Write)) { file.SetLength(MaxFileBytes + 1); }
            try { Render(oversized, hash, 0); throw new Exception("Oversized WorldGeometry JSON was accepted."); } catch (InvalidDataException) { }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "map-render-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                Checks = new[] { "calibration and client-hash gate", "zone matching", "top-down X/Z orientation", "static terrain triangles", "calibrated static object footprint transforms", "collision meshes exported as world-space route blockers", "malformed/oversized input rejection" }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    static void ExpectFailure(string path, string json, string expectedHash, string label, int expectedZone)
    {
        File.WriteAllText(path, json);
        try { using RasterResult _ = Render(path, expectedHash, expectedZone); throw new Exception($"WorldGeometry importer accepted {label}."); }
        catch (InvalidDataException) { }
        catch (JsonException) when (label == "malformed JSON") { }
    }

    static string Synthetic(string hash, bool calibration)
    {
        string source = calibration
            ? ",\"sources\":[{\"format\":\"coordinate-calibration-profile\",\"sha256\":\"calibration-profile-hash\"}]"
            : ",\"sources\":[{\"format\":\"coordinate-calibration-profile\",\"sha256\":\"calibration-profile-hash\"}]";
        string convention = calibration ? "explicit affine calibration applied to world-XZ; visual alignment unverified" : "uncalibrated world-XZ";
        return $"{{\"zoneId\":\"zone0\",\"clientSha256\":\"{hash}\",\"coordinateConvention\":\"{convention}\",\"validationLevel\":\"visual alignment unverified\",\"terrain\":[{{\"mesh\":{{\"kind\":\"Terrain\",\"vertices\":[{{\"x\":10,\"y\":0,\"z\":20}},{{\"x\":30,\"y\":0,\"z\":20}},{{\"x\":10,\"y\":0,\"z\":40}}],\"indices\":[0,1,2]}}}}]{source},\"instances\":[]}}";
    }

}
