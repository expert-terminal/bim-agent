using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using WinForms = System.Windows.Forms;

namespace BimAgent.AutoCAD;

public sealed class BridgeApp : IExtensionApplication
{
    private CancellationTokenSource? _stop;
    private readonly object _gate = new();
    private (JsonElement Request, TaskCompletionSource<string> Completion)? _pending;

    public void Initialize()
    {
        _stop = new CancellationTokenSource();
        CadApp.Idle += OnIdle;
        _ = Task.Run(() => ServeAsync(_stop.Token));
    }

    public void Terminate()
    {
        CadApp.Idle -= OnIdle;
        _stop?.Cancel();
        _stop?.Dispose();
    }

    private async Task ServeAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream("BimAgent-AutoCAD-2026", PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                while (!stop.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(stop);
                    if (line is null) break;
                    string response;
                    try
                    {
                        using var parsed = JsonDocument.Parse(line);
                        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                        lock (_gate)
                        {
                            if (_pending is not null) throw new InvalidOperationException("AutoCAD is processing another request.");
                            _pending = (parsed.RootElement.Clone(), completion);
                        }
                        response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(120), stop);
                    }
                    catch (System.Exception ex) { response = JsonSerializer.Serialize(new { ok = false, error = ex.Message }); }
                    await writer.WriteLineAsync(response);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
            catch (System.Exception) { await Task.Delay(500, stop); }
        }
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        (JsonElement Request, TaskCompletionSource<string> Completion)? job;
        lock (_gate) { job = _pending; _pending = null; }
        if (job is null) return;
        try { job.Value.Completion.TrySetResult(JsonSerializer.Serialize(new { ok = true, result = Handle(job.Value.Request) })); }
        catch (System.Exception ex) { job.Value.Completion.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = ex.Message })); }
    }

    private static object Handle(JsonElement request)
    {
        var doc = CadApp.DocumentManager.MdiActiveDocument ?? throw new InvalidOperationException("Open an AutoCAD drawing first.");
        var action = request.GetProperty("action").GetString();
        using var documentLock = doc.LockDocument();
        return action switch
        {
            "status" => new { name = doc.Name, units = doc.Database.Insunits.ToString(), version = CadApp.Version.ToString() },
            "layers" => ListLayers(doc),
            "entities" => ListEntities(doc, request),
            "create_lines" => CreateLines(doc, request),
            "create_polylines" => CreatePolylines(doc, request),
            "create_dimension" => CreateDimension(doc, request),
            "create_text" => CreateText(doc, request),
            "create_mtext" => CreateMText(doc, request),
            "create_circles" => CreateCircles(doc, request),
            "create_arcs" => CreateArcs(doc, request),
            "create_hatches" => CreateHatches(doc, request),
            "move_entities" => MoveEntities(doc, request),
            "copy_entities" => CopyEntities(doc, request),
            "rotate_entities" => RotateEntities(doc, request),
            "delete_entities" => DeleteEntities(doc, request),
            "create_layer" => CreateLayer(doc, request),
            "create_box" => CreateBox(doc, request),
            _ => throw new ArgumentException($"Unknown action: {action}")
        };
    }

    private static object ListLayers(Document doc)
    {
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (LayerTable)tx.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        return table.Cast<ObjectId>().Select(id =>
        {
            var layer = (LayerTableRecord)tx.GetObject(id, OpenMode.ForRead);
            return new { name = layer.Name, frozen = layer.IsFrozen, locked = layer.IsLocked };
        }).ToArray();
    }

    private static object ListEntities(Document doc, JsonElement request)
    {
        var limit = request.TryGetProperty("limit", out var v) ? Math.Clamp(v.GetInt32(), 1, 200) : 50;
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        return model.Cast<ObjectId>().Take(limit).Select(id =>
        {
            var entity = (Entity)tx.GetObject(id, OpenMode.ForRead);
            return new { handle = entity.Handle.ToString(), type = entity.GetType().Name, layer = entity.Layer };
        }).ToArray();
    }

    private static object CreateLines(Document doc, JsonElement request)
    {
        var lines = request.GetProperty("lines").EnumerateArray().ToArray();
        if (lines.Length is < 1 or > 100) throw new ArgumentException("Provide 1 to 100 lines.");
        var specs = lines.Select(item => (Start: Point(item.GetProperty("start")), End: Point(item.GetProperty("end")),
            Layer: item.TryGetProperty("layer", out var layer) ? layer.GetString() : null)).ToArray();
        if (specs.Any(x => x.Start.DistanceTo(x.End) < 0.000001)) throw new ArgumentException("Zero-length line is not allowed.");
        if (!Confirm($"Create {specs.Length} line(s) in {doc.Name}? Coordinates are drawing units."))
            return new { approved = false, handles = Array.Empty<string>() };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var layers = (LayerTable)tx.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        var handles = new List<string>();
        foreach (var spec in specs)
        {
            if (spec.Layer is not null && !layers.Has(spec.Layer)) throw new ArgumentException($"Layer '{spec.Layer}' does not exist.");
            var line = new Line(spec.Start, spec.End);
            if (spec.Layer is not null) line.Layer = spec.Layer;
            model.AppendEntity(line);
            tx.AddNewlyCreatedDBObject(line, true);
            handles.Add(line.Handle.ToString());
        }
        tx.Commit();
        return new { approved = true, handles };
    }

    private static object CreateLayer(Document doc, JsonElement request)
    {
        var name = request.GetProperty("name").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("Invalid layer name.");
        if (!Confirm($"Create layer '{name}' in {doc.Name}?")) return new { approved = false, name = (string?)null };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var layers = (LayerTable)tx.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name)) throw new ArgumentException("Layer already exists.");
        layers.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name };
        layers.Add(layer);
        tx.AddNewlyCreatedDBObject(layer, true);
        tx.Commit();
        return new { approved = true, name };
    }

    private static object CreatePolylines(Document doc, JsonElement request)
    {
        var items = request.GetProperty("polylines").EnumerateArray().ToArray();
        if (items.Length is < 1 or > 50) throw new ArgumentException("Provide 1 to 50 polylines.");
        var specs = items.Select(item =>
        {
            var vertices = item.GetProperty("points").EnumerateArray().Select(Point).ToArray();
            var closed = item.TryGetProperty("closed", out var c) && c.GetBoolean();
            if (vertices.Length < (closed ? 3 : 2) || vertices.Length > 200) throw new ArgumentException("Polyline needs 2–200 vertices (3 minimum when closed).");
            if (vertices.Select(p => Math.Round(p.Z, 6)).Distinct().Count() != 1) throw new ArgumentException("Each 2D polyline must use one Z elevation.");
            var layer = item.TryGetProperty("layer", out var l) ? l.GetString() : null;
            return (Vertices: vertices, Closed: closed, Layer: layer);
        }).ToArray();
        if (!Confirm($"Create {specs.Length} polyline(s) in {doc.Name}? Coordinates use native drawing units."))
            return new { approved = false, handles = Array.Empty<string>() };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var layers = (LayerTable)tx.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        var handles = new List<string>();
        foreach (var spec in specs)
        {
            if (spec.Layer is not null && !layers.Has(spec.Layer)) throw new ArgumentException($"Layer '{spec.Layer}' does not exist.");
            var polyline = new Polyline { Closed = spec.Closed, Elevation = spec.Vertices[0].Z };
            for (var i = 0; i < spec.Vertices.Length; i++)
                polyline.AddVertexAt(i, new Point2d(spec.Vertices[i].X, spec.Vertices[i].Y), 0, 0, 0);
            if (spec.Layer is not null) polyline.Layer = spec.Layer;
            model.AppendEntity(polyline);
            tx.AddNewlyCreatedDBObject(polyline, true);
            handles.Add(polyline.Handle.ToString());
        }
        tx.Commit();
        return new { approved = true, handles };
    }

    private static object CreateDimension(Document doc, JsonElement request)
    {
        var a = Point(request.GetProperty("start"));
        var b = Point(request.GetProperty("end"));
        var offset = request.GetProperty("offset").GetDouble();
        if (Math.Abs(a.Z - b.Z) > 1e-6) throw new ArgumentException("Dimension endpoints must share one Z elevation.");
        if (a.DistanceTo(b) < 1e-6 || !double.IsFinite(offset) || Math.Abs(offset) < 1e-6 || Math.Abs(offset) > 1_000_000)
            throw new ArgumentException("Dimension endpoints and offset must be non-zero finite values within range.");
        if (!Confirm($"Create a linear dimension between the supplied points in {doc.Name}? Drawing units are native."))
            return new { approved = false, handle = (string?)null };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var midpoint = new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, a.Z);
        var dimPoint = new Point3d(midpoint.X - Math.Sin(angle) * offset, midpoint.Y + Math.Cos(angle) * offset, midpoint.Z);
        var dimension = new RotatedDimension(angle, a, b, dimPoint, string.Empty, doc.Database.Dimstyle);
        model.AppendEntity(dimension);
        tx.AddNewlyCreatedDBObject(dimension, true);
        var handle = dimension.Handle.ToString();
        tx.Commit();
        return new { approved = true, handle };
    }

    private static object CreateText(Document doc, JsonElement request)
    {
        var position = Point(request.GetProperty("position"));
        var value = request.GetProperty("text").GetString();
        var height = Positive(request.GetProperty("height").GetDouble());
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2000) throw new ArgumentException("Text must contain 1–2000 characters.");
        if (!Confirm($"Create text '{value[..Math.Min(value.Length, 80)]}' in {doc.Name}? Text height uses native drawing units."))
            return new { approved = false, handle = (string?)null };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var textEntity = new DBText { Position = position, Height = height, TextString = value };
        model.AppendEntity(textEntity);
        tx.AddNewlyCreatedDBObject(textEntity, true);
        var handle = textEntity.Handle.ToString();
        tx.Commit();
        return new { approved = true, handle };
    }

    private static object CreateCircles(Document doc, JsonElement request)
    {
        var specs = request.GetProperty("circles").EnumerateArray().Select(item =>
        {
            var center = Point(item.GetProperty("center"));
            var radius = Positive(item.GetProperty("radius").GetDouble());
            if (radius > 1_000_000) throw new ArgumentException("Circle radius is over the 1,000,000 drawing-unit limit.");
            var layer = item.TryGetProperty("layer", out var l) ? l.GetString() : null;
            return (Center: center, Radius: radius, Layer: layer);
        }).ToArray();
        if (specs.Length is < 1 or > 100) throw new ArgumentException("Provide 1 to 100 circles.");
        if (!Confirm($"Create {specs.Length} circle(s) in {doc.Name}? Coordinates use native drawing units."))
            return new { approved = false, handles = Array.Empty<string>() };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var layers = (LayerTable)tx.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        var handles = new List<string>();
        foreach (var spec in specs)
        {
            if (spec.Layer is not null && !layers.Has(spec.Layer)) throw new ArgumentException($"Layer '{spec.Layer}' does not exist.");
            var circle = new Circle(spec.Center, Vector3d.ZAxis, spec.Radius);
            if (spec.Layer is not null) circle.Layer = spec.Layer;
            model.AppendEntity(circle);
            tx.AddNewlyCreatedDBObject(circle, true);
            handles.Add(circle.Handle.ToString());
        }
        tx.Commit();
        return new { approved = true, handles };
    }

    private static object CreateMText(Document doc, JsonElement request)
    {
        var position = Point(request.GetProperty("position"));
        var content = request.GetProperty("text").GetString();
        var height = Positive(request.GetProperty("height").GetDouble());
        var width = Positive(request.GetProperty("width").GetDouble());
        if (string.IsNullOrWhiteSpace(content) || content.Length > 8000) throw new ArgumentException("Multiline text must contain 1–8000 characters.");
        if (!Confirm($"Create multiline text at the supplied point in {doc.Name}? Width and height use native drawing units."))
            return new { approved = false, handle = (string?)null };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var mtext = new MText { Location = position, TextHeight = height, Width = width, Contents = content };
        model.AppendEntity(mtext);
        tx.AddNewlyCreatedDBObject(mtext, true);
        var handle = mtext.Handle.ToString();
        tx.Commit();
        return new { approved = true, handle };
    }

    private static object CreateHatches(Document doc, JsonElement request)
    {
        var specs = request.GetProperty("hatches").EnumerateArray().ToArray();
        if (specs.Length is < 1 or > 50) throw new ArgumentException("Provide 1 to 50 hatch specifications.");
        var data = specs.Select(item =>
        {
            var handle = item.GetProperty("boundaryHandle").GetString();
            var pattern = item.GetProperty("pattern").GetString();
            if (string.IsNullOrWhiteSpace(handle) || handle.Length > 16) throw new ArgumentException("Invalid boundary handle.");
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 64) throw new ArgumentException("Invalid hatch pattern name.");
            return (Handle: handle, Pattern: pattern);
        }).ToArray();
        if (!Confirm($"Create {data.Length} associative hatch(es) in {doc.Name}? Each boundary must be a closed polyline."))
            return new { approved = false, handles = Array.Empty<string>() };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var database = doc.Database;
        var blockTable = (BlockTable)tx.GetObject(database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var handles = new List<string>();
        foreach (var spec in data)
        {
            if (!long.TryParse(spec.Handle, System.Globalization.NumberStyles.HexNumber, null, out var value)) throw new ArgumentException($"Invalid boundary handle '{spec.Handle}'.");
            var boundaryId = database.GetObjectId(false, new Handle(value), 0);
            if (!boundaryId.IsValid || tx.GetObject(boundaryId, OpenMode.ForRead) is not Polyline { Closed: true })
                throw new ArgumentException($"Boundary handle '{spec.Handle}' does not identify a closed polyline.");
            var hatch = new Hatch();
            model.AppendEntity(hatch);
            tx.AddNewlyCreatedDBObject(hatch, true);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, spec.Pattern);
            hatch.Associative = true;
            var loop = new ObjectIdCollection { boundaryId };
            hatch.AppendLoop(HatchLoopTypes.External, loop);
            hatch.EvaluateHatch(true);
            handles.Add(hatch.Handle.ToString());
        }
        tx.Commit();
        return new { approved = true, handles };
    }

    private static object MoveEntities(Document doc, JsonElement request)
    {
        var raw = request.GetProperty("handles").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (raw.Length is < 1 or > 100 || raw.Distinct(StringComparer.OrdinalIgnoreCase).Count() != raw.Length) throw new ArgumentException("Provide 1 to 100 distinct entity handles.");
        var delta = Point(request.GetProperty("delta"));
        if (delta.DistanceTo(Point3d.Origin) < 1e-9) throw new ArgumentException("Move displacement must be non-zero.");
        if (!Confirm($"Move {raw.Length} entity/entities in {doc.Name} by the supplied native-unit vector?"))
            return new { approved = false, handles = raw };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var ids = raw.Select(handle =>
        {
            if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, null, out var value)) throw new ArgumentException($"Invalid entity handle '{handle}'.");
            var id = doc.Database.GetObjectId(false, new Handle(value), 0);
            if (!id.IsValid || tx.GetObject(id, OpenMode.ForRead) is not Entity) throw new ArgumentException($"Handle '{handle}' does not identify an entity.");
            return id;
        }).ToArray();
        var transform = Matrix3d.Displacement(new Vector3d(delta.X, delta.Y, delta.Z));
        foreach (var id in ids) ((Entity)tx.GetObject(id, OpenMode.ForWrite)).TransformBy(transform);
        tx.Commit();
        return new { approved = true, handles = raw };
    }

    private static object CopyEntities(Document doc, JsonElement request)
    {
        var raw = request.GetProperty("handles").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (raw.Length is < 1 or > 100 || raw.Distinct(StringComparer.OrdinalIgnoreCase).Count() != raw.Length) throw new ArgumentException("Provide 1 to 100 distinct entity handles.");
        var delta = Point(request.GetProperty("delta"));
        if (delta.DistanceTo(Point3d.Origin) < 1e-9) throw new ArgumentException("Copy displacement must be non-zero.");
        if (!Confirm($"Copy {raw.Length} entity/entities in {doc.Name} by the supplied native-unit vector?"))
            return new { approved = false, handles = Array.Empty<string>() };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var transform = Matrix3d.Displacement(new Vector3d(delta.X, delta.Y, delta.Z));
        var created = new List<string>();
        foreach (var handle in raw)
        {
            if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, null, out var value)) throw new ArgumentException($"Invalid entity handle '{handle}'.");
            var id = doc.Database.GetObjectId(false, new Handle(value), 0);
            if (!id.IsValid || tx.GetObject(id, OpenMode.ForRead) is not Entity entity) throw new ArgumentException($"Handle '{handle}' does not identify an entity.");
            var copy = entity.GetTransformedCopy(transform);
            model.AppendEntity(copy);
            tx.AddNewlyCreatedDBObject(copy, true);
            created.Add(copy.Handle.ToString());
        }
        tx.Commit();
        return new { approved = true, handles = created };
    }

    private static object RotateEntities(Document doc, JsonElement request)
    {
        var raw = request.GetProperty("handles").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (raw.Length is < 1 or > 100 || raw.Distinct(StringComparer.OrdinalIgnoreCase).Count() != raw.Length) throw new ArgumentException("Provide 1 to 100 distinct entity handles.");
        var pivot = Point(request.GetProperty("pivot"));
        var degrees = request.GetProperty("degrees").GetDouble();
        if (!double.IsFinite(degrees) || Math.Abs(degrees) > 360_000) throw new ArgumentException("degrees must be finite and within ±360000.");
        if (Math.Abs(degrees) < 1e-9) throw new ArgumentException("Rotation must be non-zero.");
        if (!Confirm($"Rotate {raw.Length} entity/entities around the WCS Z axis by {degrees}° in {doc.Name}?"))
            return new { approved = false, handles = raw };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var ids = raw.Select(handle =>
        {
            if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, null, out var value)) throw new ArgumentException($"Invalid entity handle '{handle}'.");
            var id = doc.Database.GetObjectId(false, new Handle(value), 0);
            if (!id.IsValid || tx.GetObject(id, OpenMode.ForRead) is not Entity) throw new ArgumentException($"Handle '{handle}' does not identify an entity.");
            return id;
        }).ToArray();
        var transform = Matrix3d.Rotation(degrees * Math.PI / 180.0, Vector3d.ZAxis, pivot);
        foreach (var id in ids) ((Entity)tx.GetObject(id, OpenMode.ForWrite)).TransformBy(transform);
        tx.Commit();
        return new { approved = true, handles = raw };
    }

    private static object DeleteEntities(Document doc, JsonElement request)
    {
        var raw = request.GetProperty("handles").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (raw.Length is < 1 or > 100 || raw.Distinct(StringComparer.OrdinalIgnoreCase).Count() != raw.Length) throw new ArgumentException("Provide 1 to 100 distinct entity handles.");
        if (!Confirm($"Delete {raw.Length} entity/entities from {doc.Name}? AutoCAD Undo can restore committed deletions."))
            return new { approved = false, handles = raw };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        foreach (var handle in raw)
        {
            if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, null, out var value)) throw new ArgumentException($"Invalid entity handle '{handle}'.");
            var id = doc.Database.GetObjectId(false, new Handle(value), 0);
            if (!id.IsValid || tx.GetObject(id, OpenMode.ForRead) is not Entity entity) throw new ArgumentException($"Handle '{handle}' does not identify an entity.");
            entity.UpgradeOpen();
            entity.Erase();
        }
        tx.Commit();
        return new { approved = true, deletedHandles = raw };
    }

    private static object CreateArcs(Document doc, JsonElement request)
    {
        var specs = request.GetProperty("arcs").EnumerateArray().Select(item =>
        {
            var center = Point(item.GetProperty("center"));
            var radius = Positive(item.GetProperty("radius").GetDouble());
            var start = item.GetProperty("startRadians").GetDouble();
            var end = item.GetProperty("endRadians").GetDouble();
            if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start || end - start >= 2 * Math.PI)
                throw new ArgumentException("Arc angles must be finite, increasing, and span less than 2π radians.");
            return (Center: center, Radius: radius, Start: start, End: end);
        }).ToArray();
        if (specs.Length is < 1 or > 100) throw new ArgumentException("Provide 1 to 100 arcs.");
        if (!Confirm($"Create {specs.Length} arc(s) in {doc.Name}? Angles are radians; coordinates use native drawing units."))
            return new { approved = false, handles = Array.Empty<string>() };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var handles = new List<string>();
        foreach (var spec in specs)
        {
            var arc = new Arc(spec.Center, spec.Radius, spec.Start, spec.End);
            model.AppendEntity(arc);
            tx.AddNewlyCreatedDBObject(arc, true);
            handles.Add(arc.Handle.ToString());
        }
        tx.Commit();
        return new { approved = true, handles };
    }

    private static object CreateBox(Document doc, JsonElement request)
    {
        var origin = Point(request.GetProperty("origin"));
        var width = Positive(request.GetProperty("width").GetDouble());
        var depth = Positive(request.GetProperty("depth").GetDouble());
        var height = Positive(request.GetProperty("height").GetDouble());
        if (!Confirm($"Create a {width} × {depth} × {height} 3D solid box in {doc.Name}? Dimensions are drawing units."))
            return new { approved = false, handle = (string?)null };
        using var tx = doc.Database.TransactionManager.StartTransaction();
        var table = (BlockTable)tx.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var solid = new Solid3d();
        solid.CreateBox(width, depth, height);
        solid.TransformBy(Matrix3d.Displacement(origin - Point3d.Origin));
        model.AppendEntity(solid);
        tx.AddNewlyCreatedDBObject(solid, true);
        var handle = solid.Handle.ToString();
        tx.Commit();
        return new { approved = true, handle };
    }

    private static Point3d Point(JsonElement item)
    {
        if (item.GetArrayLength() != 3) throw new ArgumentException("Points must be [x,y,z].");
        var values = item.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (values.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1_000_000)) throw new ArgumentException("Invalid coordinate.");
        return new Point3d(values[0], values[1], values[2]);
    }

    private static double Positive(double value) => double.IsFinite(value) && value > 0 && value <= 1_000_000
        ? value : throw new ArgumentException("Dimension must be finite, positive and at most 1,000,000 drawing units.");

    private static bool Confirm(string message) => WinForms.MessageBox.Show(message, "BIM-agent AutoCAD",
        WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question) == WinForms.DialogResult.Yes;
}
