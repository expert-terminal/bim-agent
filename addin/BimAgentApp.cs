using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BimAgent;

public sealed class BridgeApp : IExternalApplication
{
    private CancellationTokenSource? _stop;
    private Task? _server;
    private BridgeHandler? _handler;
    private ExternalEvent? _externalEvent;

    public Result OnStartup(UIControlledApplication application)
    {
        _handler = new BridgeHandler();
        _externalEvent = ExternalEvent.Create(_handler);
        _stop = new CancellationTokenSource();
        _server = Task.Run(() => ServeAsync(_stop.Token));
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _stop?.Cancel();
        _externalEvent?.Dispose();
        _stop?.Dispose();
        return Result.Succeeded;
    }

    private async Task ServeAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    "BimAgent-2026", PipeDirection.InOut, 1,
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
                        using var request = JsonDocument.Parse(line);
                        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                        if (!_handler!.TryQueue(request.RootElement.Clone(), completion))
                            throw new InvalidOperationException("Revit is processing another request.");
                        var result = _externalEvent!.Raise();
                        if (result != Autodesk.Revit.UI.ExternalEventRequest.Accepted)
                        {
                            _handler.CancelQueued();
                            throw new InvalidOperationException($"Revit did not accept the event: {result}");
                        }
                        response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(120), stop);
                    }
                    catch (Exception ex)
                    {
                        response = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
                    }
                    await writer.WriteLineAsync(response);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
            catch (Exception) { await Task.Delay(500, stop); }
        }
    }
}

internal sealed class BridgeHandler : IExternalEventHandler
{
    private readonly object _gate = new();
    private (JsonElement Request, TaskCompletionSource<string> Completion)? _pending;

    public string GetName() => "BIM-agent request";

    public bool TryQueue(JsonElement request, TaskCompletionSource<string> completion)
    {
        lock (_gate)
        {
            if (_pending is not null) return false;
            _pending = (request, completion);
            return true;
        }
    }

    public void CancelQueued()
    {
        lock (_gate) _pending = null;
    }

    public void Execute(UIApplication uiapp)
    {
        (JsonElement Request, TaskCompletionSource<string> Completion)? job;
        lock (_gate) { job = _pending; _pending = null; }
        if (job is null) return;
        try
        {
            var result = Handle(uiapp, job.Value.Request);
            job.Value.Completion.TrySetResult(JsonSerializer.Serialize(new { ok = true, result }));
        }
        catch (Exception ex)
        {
            job.Value.Completion.TrySetResult(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
        }
    }

    private static object Handle(UIApplication uiapp, JsonElement request)
    {
        var action = request.GetProperty("action").GetString();
        var doc = uiapp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a Revit project first.");
        return action switch
        {
            "status" => new { name = doc.Title, path = doc.PathName, version = uiapp.Application.VersionNumber, readOnly = doc.IsReadOnly },
            "levels" => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(x => x.Elevation).Select(x => new { id = x.Id.Value, name = x.Name, elevationMm = Math.Round(ToMm(x.Elevation), 2) }).ToArray(),
            "wall_types" => new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                .OrderBy(x => x.Name).Select(x => new { id = x.Id.Value, name = x.Name, kind = x.Kind.ToString() }).ToArray(),
            "views" => ListViews(doc, request),
            "sheets" => ListSheets(doc, request),
            "family_types" => ListFamilyTypes(doc, request),
            "walls" => ListWalls(doc, request),
            "elements" => ListElements(doc, request),
            "element" => InspectElement(doc, request),
            "create_walls" => CreateWalls(doc, request),
            "create_level" => CreateLevel(doc, request),
            "create_grid" => CreateGrid(doc, request),
            "create_sheet" => CreateSheet(doc, request),
            "set_parameter" => SetParameter(doc, request),
            "delete_elements" => DeleteElements(doc, request),
            _ => throw new ArgumentException($"Unknown action: {action}")
        };
    }

    private static object ListWalls(Document doc, JsonElement request)
    {
        var limit = request.TryGetProperty("limit", out var value) ? Math.Clamp(value.GetInt32(), 1, 200) : 50;
        var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>();
        return walls.Take(limit).Select(w => new { id = w.Id.Value, name = w.Name, typeId = w.GetTypeId().Value,
            levelId = w.LevelId.Value, lengthMm = Math.Round(ToMm(w.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0), 2) }).ToArray();
    }

    private static int Limit(JsonElement request) => request.TryGetProperty("limit", out var value)
        ? Math.Clamp(value.GetInt32(), 1, 200) : 50;

    private static object ListViews(Document doc, JsonElement request) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v is not ViewSheet)
            .OrderBy(v => v.Name).Take(Limit(request))
            .Select(v => new { id = v.Id.Value, name = v.Name, viewType = v.ViewType.ToString(), scale = v.Scale }).ToArray();

    private static object ListSheets(Document doc, JsonElement request) =>
        new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .OrderBy(s => s.SheetNumber).Take(Limit(request))
            .Select(s => new { id = s.Id.Value, number = s.SheetNumber, name = s.Name, placeholder = s.IsPlaceholder }).ToArray();

    private static object ListFamilyTypes(Document doc, JsonElement request)
    {
        var category = request.TryGetProperty("category", out var c) ? c.GetString() : null;
        return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(s => string.IsNullOrWhiteSpace(category) || string.Equals(s.Category?.Name, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.FamilyName).ThenBy(s => s.Name).Take(Limit(request))
            .Select(s => new { id = s.Id.Value, family = s.FamilyName, name = s.Name, category = s.Category?.Name, active = s.IsActive }).ToArray();
    }

    private static object CreateLevel(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var name = RequiredName(request, "name", 100);
        var elevationMm = request.GetProperty("elevationMm").GetDouble();
        if (!double.IsFinite(elevationMm) || Math.Abs(elevationMm) > 100_000)
            throw new ArgumentException("Elevation must be finite and within ±100,000 mm.");
        if (new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A level with that name already exists.");
        if (!Confirm(doc, $"Create level '{name}' at {elevationMm} mm?")) return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create level");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var level = Level.Create(doc, FromMm(elevationMm));
            level.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the level.");
            return new { approved = true, createdId = (long?)level.Id.Value };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateGrid(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var name = RequiredName(request, "name", 100);
        var a = Point(request.GetProperty("startMm"));
        var b = Point(request.GetProperty("endMm"));
        if (a.DistanceTo(b) < FromMm(100)) throw new ArgumentException("Grid length must be at least 100 mm.");
        if (new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A grid with that name already exists.");
        if (!Confirm(doc, $"Create grid '{name}' from ({ToMm(a.X):0}, {ToMm(a.Y):0}) to ({ToMm(b.X):0}, {ToMm(b.Y):0}) mm?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create grid");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var grid = Grid.Create(doc, Line.CreateBound(a, b));
            grid.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the grid.");
            return new { approved = true, createdId = (long?)grid.Id.Value };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateSheet(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var number = RequiredName(request, "number", 50);
        var name = RequiredName(request, "name", 100);
        if (new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Any(s => s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A sheet with that number already exists.");
        if (!Confirm(doc, $"Create sheet {number} — {name} without a title block?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create sheet");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
            sheet.SheetNumber = number;
            sheet.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the sheet.");
            return new { approved = true, createdId = (long?)sheet.Id.Value };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static string RequiredName(JsonElement request, string key, int max)
    {
        var value = request.GetProperty(key).GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Contains('\n'))
            throw new ArgumentException($"{key} must be 1–{max} characters without a newline.");
        return value;
    }

    private static bool Confirm(Document doc, string action) => TaskDialog.Show("BIM-agent",
        $"{action}\n\nProject: {doc.Title}", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes;

    private static object ListElements(Document doc, JsonElement request)
    {
        var limit = request.TryGetProperty("limit", out var value) ? Math.Clamp(value.GetInt32(), 1, 200) : 50;
        var category = request.TryGetProperty("category", out var c) ? c.GetString() : null;
        if (category is { Length: > 100 }) throw new ArgumentException("Category name is too long.");
        var elements = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements()
            .Where(e => e.Category is not null).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category))
            elements = elements.Where(e => string.Equals(e.Category?.Name, category, StringComparison.OrdinalIgnoreCase));
        return elements.Take(limit).Select(e => new { id = e.Id.Value, name = e.Name, category = e.Category?.Name,
            typeId = e.GetTypeId().Value, uniqueId = e.UniqueId }).ToArray();
    }

    private static object InspectElement(Document doc, JsonElement request)
    {
        var id = new ElementId(request.GetProperty("elementId").GetInt64());
        var element = doc.GetElement(id) ?? throw new ArgumentException($"Element {id.Value} not found.");
        var parameters = element.Parameters.Cast<Parameter>()
            .Where(p => p.Definition is not null)
            .OrderBy(p => p.Definition.Name)
            .Take(300)
            .Select(p => new { name = p.Definition.Name, storageType = p.StorageType.ToString(), readOnly = p.IsReadOnly,
                value = p.AsValueString() ?? (p.StorageType == StorageType.String ? p.AsString() : null),
                raw = RawParameterValue(p) }).ToArray();
        return new { id = element.Id.Value, uniqueId = element.UniqueId, name = element.Name,
            category = element.Category?.Name, typeId = element.GetTypeId().Value, parameters };
    }

    private static object? RawParameterValue(Parameter parameter) => parameter.StorageType switch
    {
        StorageType.String => parameter.AsString(),
        StorageType.Integer => parameter.AsInteger(),
        StorageType.Double => parameter.AsDouble(),
        StorageType.ElementId => parameter.AsElementId().Value,
        _ => null
    };

    private static object SetParameter(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var id = new ElementId(request.GetProperty("elementId").GetInt64());
        var element = doc.GetElement(id) ?? throw new ArgumentException($"Element {id.Value} not found.");
        var name = request.GetProperty("parameterName").GetString();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new ArgumentException("Invalid parameter name.");
        var matches = element.Parameters.Cast<Parameter>().Where(p => p.Definition?.Name == name).ToArray();
        if (matches.Length != 1) throw new ArgumentException($"Expected one parameter named '{name}', found {matches.Length}. Use an unambiguous parameter.");
        var parameter = matches[0];
        if (parameter.IsReadOnly) throw new InvalidOperationException("The parameter is read-only.");
        var value = request.GetProperty("value");
        if (parameter.StorageType == StorageType.ElementId || parameter.StorageType == StorageType.None)
            throw new ArgumentException("This parameter storage type is not supported for editing.");
        if (parameter.StorageType == StorageType.String && value.ValueKind != JsonValueKind.String ||
            parameter.StorageType == StorageType.Integer && value.ValueKind != JsonValueKind.Number ||
            parameter.StorageType == StorageType.Double && value.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Value type does not match the Revit parameter storage type.");
        if (TaskDialog.Show("BIM-agent", $"Set '{name}' on {element.Category?.Name ?? "element"} {id.Value} in '{doc.Title}'?",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
            return new { approved = false, elementId = id.Value };
        using var tx = new Transaction(doc, $"Agent: set {name}");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var changed = parameter.StorageType switch
            {
                StorageType.String => parameter.Set(value.GetString()),
                StorageType.Integer => parameter.Set(value.GetInt32()),
                StorageType.Double => parameter.SetValueString(value.GetString()),
                _ => false
            };
            if (!changed) throw new InvalidOperationException("Revit rejected the parameter value.");
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the parameter change.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, elementId = id.Value, parameterName = name, value = RawParameterValue(parameter) };
    }

    private static object DeleteElements(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var ids = request.GetProperty("elementIds").EnumerateArray().Select(x => new ElementId(x.GetInt64())).ToArray();
        if (ids.Length is < 1 or > 20 || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Provide 1 to 20 distinct element IDs.");
        foreach (var id in ids) if (doc.GetElement(id) is null) throw new ArgumentException($"Element {id.Value} not found.");
        var summary = $"Delete {ids.Length} requested element(s) from '{doc.Title}'?\n\n" +
            "Revit may also delete dependent elements. This operation creates one undo entry.\n\nIDs: " + string.Join(", ", ids.Select(x => x.Value));
        if (TaskDialog.Show("BIM-agent", summary, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
            return new { approved = false, deletedIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: delete elements");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        ICollection<ElementId> deleted;
        try
        {
            deleted = doc.Delete(ids);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the deletion.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, requestedIds = ids.Select(x => x.Value).ToArray(), deletedIds = deleted.Select(x => x.Value).ToArray() };
    }

    private static object CreateWalls(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var items = request.GetProperty("walls").EnumerateArray().ToArray();
        if (items.Length is < 1 or > 50) throw new ArgumentException("Provide 1 to 50 walls.");
        var specs = new List<(XYZ A, XYZ B, ElementId Level, ElementId Type, double Height)>();
        foreach (var item in items)
        {
            var a = Point(item.GetProperty("startMm"));
            var b = Point(item.GetProperty("endMm"));
            var level = new ElementId(item.GetProperty("levelId").GetInt64());
            var type = new ElementId(item.GetProperty("wallTypeId").GetInt64());
            var heightMm = item.GetProperty("heightMm").GetDouble();
            if (!double.IsFinite(heightMm) || heightMm > 100_000) throw new ArgumentException("Wall height must be finite and at most 100,000 mm.");
            var height = FromMm(heightMm);
            if (a.DistanceTo(b) < FromMm(10)) throw new ArgumentException("Wall length must be at least 10 mm.");
            if (height < FromMm(100)) throw new ArgumentException("Wall height must be at least 100 mm.");
            if (doc.GetElement(level) is not Level selectedLevel) throw new ArgumentException($"Invalid level ID {level.Value}.");
            if (doc.GetElement(type) is not WallType wt || wt.Kind != WallKind.Basic)
                throw new ArgumentException($"Wall type {type.Value} must be a basic wall type.");
            specs.Add((new XYZ(a.X, a.Y, selectedLevel.Elevation), new XYZ(b.X, b.Y, selectedLevel.Elevation), level, type, height));
        }
        var summary = $"Create {specs.Count} straight wall(s) in '{doc.Title}'?\n\n" +
            string.Join("\n", specs.Take(8).Select((s, i) => $"{i + 1}. {Math.Round(ToMm(s.A.DistanceTo(s.B)))} mm long × {Math.Round(ToMm(s.Height))} mm high")) +
            (specs.Count > 8 ? $"\n... and {specs.Count - 8} more" : "");
        if (TaskDialog.Show("BIM-agent", summary, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
            return new { approved = false, createdIds = Array.Empty<long>() };
        var ids = new List<long>();
        using var tx = new Transaction(doc, "Agent: create walls");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            foreach (var spec in specs)
            {
                var wall = Wall.Create(doc, Line.CreateBound(spec.A, spec.B), spec.Type, spec.Level, spec.Height, 0, false, false);
                ids.Add(wall.Id.Value);
            }
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the wall transaction.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, createdIds = ids };
    }

    private static XYZ Point(JsonElement point)
    {
        if (point.GetArrayLength() != 2) throw new ArgumentException("A point must contain [xMm, yMm].");
        var x = point[0].GetDouble(); var y = point[1].GetDouble();
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000)
            throw new ArgumentException("Coordinates must be finite and within ±1,000,000 mm.");
        return new XYZ(FromMm(x), FromMm(y), 0);
    }

    private static double FromMm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
