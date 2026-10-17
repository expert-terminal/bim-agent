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
