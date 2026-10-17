using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
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
            "floor_types" => new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                .OrderBy(x => x.Name).Select(x => new { id = x.Id.Value, name = x.Name, family = x.FamilyName }).ToArray(),
            "ceiling_types" => new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>()
                .OrderBy(x => x.Name).Select(x => new { id = x.Id.Value, name = x.Name, family = x.FamilyName }).ToArray(),
            "roof_types" => new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>()
                .OrderBy(x => x.Name).Select(x => new { id = x.Id.Value, name = x.Name, family = x.FamilyName }).ToArray(),
            "view_types" => new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .OrderBy(x => x.ViewFamily.ToString()).ThenBy(x => x.Name)
                .Select(x => new { id = x.Id.Value, name = x.Name, viewFamily = x.ViewFamily.ToString() }).ToArray(),
            "titleblock_types" => new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().Cast<FamilySymbol>().OrderBy(x => x.FamilyName).ThenBy(x => x.Name)
                .Select(x => new { id = x.Id.Value, family = x.FamilyName, name = x.Name }).ToArray(),
            "rooms" => ListRooms(doc, request),
            "schedule_categories" => ScheduleCategories(doc),
            "schedule_fields" => ScheduleFields(doc, request),
            "create_schedule" => CreateSchedule(doc, request),
            "materials" => new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .OrderBy(x => x.Name).Take(Limit(request)).Select(x => new { id = x.Id.Value, name = x.Name, color = new[] { x.Color.Red, x.Color.Green, x.Color.Blue } }).ToArray(),
            "model_health" => ModelHealth(doc, request),
            "views" => ListViews(doc, request),
            "view_templates" => ListViewTemplates(doc, request),
            "text_note_types" => ListTextNoteTypes(doc, request),
            "sheets" => ListSheets(doc, request),
            "family_types" => ListFamilyTypes(doc, request),
            "walls" => ListWalls(doc, request),
            "elements" => ListElements(doc, request),
            "element" => InspectElement(doc, request),
            "create_walls" => CreateWalls(doc, request),
            "load_family_type" => LoadFamilyType(doc, request),
            "create_floors" => CreateFloors(doc, request),
            "create_ceilings" => CreateCeilings(doc, request),
            "create_roof" => CreateRoof(doc, request),
            "place_family_instances" => PlaceFamilyInstances(doc, request),
            "create_plan_view" => CreatePlanView(doc, request),
            "create_drafting_view" => CreateDraftingView(doc, request),
            "create_section_view" => CreateSectionView(doc, request),
            "create_elevation_view" => CreateElevationView(doc, request),
            "create_detail_lines" => CreateDetailLines(doc, request),
            "create_text_notes" => CreateTextNotes(doc, request),
            "apply_view_template" => ApplyViewTemplate(doc, request),
            "dimension_grids" => DimensionGrids(doc, request),
            "create_group" => CreateGroup(doc, request),
            "ungroup_elements" => UngroupElements(doc, request),
            "place_view_on_sheet" => PlaceViewOnSheet(doc, request),
            "create_3d_view" => Create3DView(doc, request),
            "set_view_scale" => SetViewScale(doc, request),
            "set_view_visibility" => SetViewVisibility(doc, request),
            "tag_elements" => TagElements(doc, request),
            "duplicate_view" => DuplicateView(doc, request),
            "move_elements" => MoveElements(doc, request),
            "change_element_type" => ChangeElementType(doc, request),
            "rotate_elements" => RotateElements(doc, request),
            "copy_elements" => CopyElements(doc, request),
            "create_room" => CreateRoom(doc, request),
            "export_sheets_pdf" => ExportSheetsPdf(doc, request),
            "export_views_dwg" => ExportViewsDwg(doc, request),
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

    private static object ListRooms(Document doc, JsonElement request) =>
        new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
            .Cast<SpatialElement>().Take(Limit(request)).Select(room => new
            {
                id = room.Id.Value, name = room.Name,
                number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(),
                level = doc.GetElement(room.LevelId)?.Name,
                areaSquareMetres = Math.Round(UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters), 3),
                locationMm = (room.Location as LocationPoint) is { } location
                    ? new[] { Math.Round(ToMm(location.Point.X), 1), Math.Round(ToMm(location.Point.Y), 1), Math.Round(ToMm(location.Point.Z), 1) }
                    : null
            }).ToArray();

    private static object ModelHealth(Document doc, JsonElement request)
    {
        var limit = request.TryGetProperty("limit", out var value) ? Math.Clamp(value.GetInt32(), 1, 100) : 50;
        var warnings = doc.GetWarnings().Take(limit).Select(w => new
        {
            description = w.GetDescriptionText(),
            elementIds = w.GetFailingElements().Take(20).Select(id => id.Value).ToArray()
        }).ToArray();
        return new { warningCount = doc.GetWarnings().Count, warnings };
    }

    private static object ScheduleCategories(Document doc) => doc.Settings.Categories.Cast<Category>()
        .Where(c => c.AllowsBoundParameters && ViewSchedule.IsValidCategoryForSchedule(c.Id))
        .OrderBy(c => c.Name).Select(c => new { id = c.Id.Value, name = c.Name }).ToArray();

    private static object ScheduleFields(Document doc, JsonElement request)
    {
        var categoryId = new ElementId(request.GetProperty("categoryId").GetInt64());
        if (!ViewSchedule.IsValidCategoryForSchedule(categoryId)) throw new ArgumentException("categoryId is not schedulable.");
        using var transaction = new Transaction(doc, "Agent: inspect schedule fields");
        if (transaction.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start field inspection.");
        try
        {
            var schedule = ViewSchedule.CreateSchedule(doc, categoryId);
            var fields = schedule.Definition.GetSchedulableFields().Select(field => new
            {
                name = field.GetName(doc), parameterId = field.ParameterId.Value, fieldType = field.FieldType.ToString()
            }).OrderBy(field => field.name).ToArray();
            transaction.RollBack();
            return new { categoryId = categoryId.Value, fields };
        }
        catch { if (transaction.HasStarted()) transaction.RollBack(); throw; }
    }

    private static object CreateSchedule(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var categoryId = new ElementId(request.GetProperty("categoryId").GetInt64());
        var name = RequiredName(request, "name", 100);
        var fieldNames = request.GetProperty("fieldNames").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (fieldNames.Length is < 1 or > 30 || fieldNames.Any(string.IsNullOrWhiteSpace) || fieldNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fieldNames.Length)
            throw new ArgumentException("Provide 1 to 30 distinct non-empty schedule field names.");
        if (!ViewSchedule.IsValidCategoryForSchedule(categoryId)) throw new ArgumentException("categoryId is not schedulable.");
        if (!Confirm(doc, $"Create schedule '{name}' with {fieldNames.Length} fields for category ID {categoryId.Value}?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create schedule");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var schedule = ViewSchedule.CreateSchedule(doc, categoryId);
            var available = schedule.Definition.GetSchedulableFields();
            foreach (var fieldName in fieldNames)
            {
                var matches = available.Where(f => string.Equals(f.GetName(doc), fieldName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new ArgumentException($"Expected one schedulable field '{fieldName}', found {matches.Length}.");
                schedule.Definition.AddField(matches[0]);
            }
            schedule.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the schedule.");
            return new { approved = true, createdId = (long?)schedule.Id.Value, name = schedule.Name };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static int Limit(JsonElement request) => request.TryGetProperty("limit", out var value)
        ? Math.Clamp(value.GetInt32(), 1, 200) : 50;

    private static object ListViews(Document doc, JsonElement request) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v is not ViewSheet)
            .OrderBy(v => v.Name).Take(Limit(request))
            .Select(v => new { id = v.Id.Value, name = v.Name, viewType = v.ViewType.ToString(), scale = v.Scale }).ToArray();

    private static object ListViewTemplates(Document doc, JsonElement request) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => v.IsTemplate).OrderBy(v => v.Name).Take(Limit(request))
            .Select(v => new { id = v.Id.Value, name = v.Name, viewType = v.ViewType.ToString() }).ToArray();

    private static object ListTextNoteTypes(Document doc, JsonElement request) =>
        new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
            .OrderBy(t => t.Name).Take(Limit(request))
            .Select(t => new { id = t.Id.Value, name = t.Name, family = t.FamilyName }).ToArray();

    private static object ApplyViewTemplate(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var viewId = new ElementId(request.GetProperty("viewId").GetInt64());
        var templateId = new ElementId(request.GetProperty("templateId").GetInt64());
        if (doc.GetElement(viewId) is not View view || view.IsTemplate || view is ViewSheet)
            throw new ArgumentException("viewId must identify a non-template project view.");
        if (doc.GetElement(templateId) is not View template || !template.IsTemplate)
            throw new ArgumentException("templateId must identify a view template.");
        if (!view.IsValidViewTemplate(templateId)) throw new ArgumentException("This view cannot use the requested template.");
        if (!Confirm(doc, $"Apply view template '{template.Name}' to '{view.Name}'? Controlled visibility, scale, and graphics may change."))
            return new { approved = false, viewId = view.Id.Value, templateId = view.ViewTemplateId.Value };
        using var tx = new Transaction(doc, "Agent: apply view template");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            view.ViewTemplateId = templateId;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the view template change.");
            return new { approved = true, viewId = view.Id.Value, templateId = view.ViewTemplateId.Value, templateName = template.Name };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateTextNotes(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var viewId = new ElementId(request.GetProperty("viewId").GetInt64());
        var typeId = new ElementId(request.GetProperty("textNoteTypeId").GetInt64());
        if (doc.GetElement(viewId) is not View view || view.IsTemplate || view is ViewSheet || !view.CanBePrinted)
            throw new ArgumentException("viewId must identify a printable, non-template project view.");
        if (doc.GetElement(typeId) is not TextNoteType) throw new ArgumentException("textNoteTypeId must identify a text note type.");
        var notes = request.GetProperty("notes").EnumerateArray().ToArray();
        if (notes.Length is < 1 or > 50) throw new ArgumentException("Provide 1 to 50 text notes.");
        var specs = notes.Select(n =>
        {
            var content = n.GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 2000) throw new ArgumentException("Text note content must contain 1–2000 characters.");
            return (Content: content, Point: Point3(n.GetProperty("pointMm")));
        }).ToArray();
        if (!Confirm(doc, $"Create {specs.Length} text note(s) in '{view.Name}' using type '{doc.GetElement(typeId)!.Name}'?"))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: create text notes");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var options = new TextNoteOptions(typeId);
            var created = specs.Select(s => TextNote.Create(doc, viewId, s.Point, s.Content, options)).ToArray();
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit text notes.");
            return new { approved = true, createdIds = created.Select(n => n.Id.Value).ToArray(), count = created.Length };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

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
        var titleBlockId = request.TryGetProperty("titleBlockTypeId", out var titleBlockJson)
            ? new ElementId(titleBlockJson.GetInt64()) : ElementId.InvalidElementId;
        if (titleBlockId != ElementId.InvalidElementId &&
            (doc.GetElement(titleBlockId) is not FamilySymbol titleBlock || titleBlock.Category?.Id.Value != (long)BuiltInCategory.OST_TitleBlocks))
            throw new ArgumentException("titleBlockTypeId must identify a title block family type.");
        if (new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Any(s => s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A sheet with that number already exists.");
        if (!Confirm(doc, titleBlockId == ElementId.InvalidElementId
                ? $"Create sheet {number} — {name} without a title block?"
                : $"Create sheet {number} — {name} with title block type {titleBlockId.Value}?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create sheet");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var sheet = ViewSheet.Create(doc, titleBlockId);
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
        if (parameter.StorageType == StorageType.None)
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
            StorageType.ElementId => SetElementIdParameter(doc, parameter, value),
                _ => false
            };
            if (!changed) throw new InvalidOperationException("Revit rejected the parameter value.");
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the parameter change.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, elementId = id.Value, parameterName = name, value = RawParameterValue(parameter) };
    }

    private static bool SetElementIdParameter(Document doc, Parameter parameter, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var rawId))
            throw new ArgumentException("Element ID parameters require an integer project element ID.");
        var id = new ElementId(rawId);
        if (rawId >= 0 && doc.GetElement(id) is null) throw new ArgumentException($"Referenced element ID {rawId} does not exist.");
        return parameter.Set(id);
    }

    private static object DeleteElements(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var ids = request.GetProperty("elementIds").EnumerateArray().Select(x => new ElementId(x.GetInt64())).ToArray();
        if (ids.Length is < 1 or > 20 || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Provide 1 to 20 distinct element IDs.");
        foreach (var id in ids) if (doc.GetElement(id) is null) throw new ArgumentException($"Element {id.Value} not found.");
        ICollection<ElementId> previewIds;
        using (var preview = new Transaction(doc, "Agent: preview deletion"))
        {
            if (preview.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not preview deletion.");
            try
            {
                previewIds = doc.Delete(ids);
                if (preview.RollBack() != TransactionStatus.RolledBack)
                    throw new InvalidOperationException("Revit could not roll back the deletion preview.");
            }
            catch { if (preview.HasStarted()) preview.RollBack(); throw; }
        }
        var requested = ids.Select(x => x.Value).ToHashSet();
        var dependents = previewIds.Where(x => !requested.Contains(x.Value)).ToArray();
        var details = dependents.Take(8).Select(id =>
        {
            var element = doc.GetElement(id);
            return $"{id.Value}: {element?.Category?.Name ?? "Element"} {element?.Name ?? ""}";
        });
        var summary = $"Delete {ids.Length} requested element(s) from '{doc.Title}'?\n" +
            $"Revit would also delete {dependents.Length} dependent element(s).\n\n" +
            "Requested IDs: " + string.Join(", ", ids.Select(x => x.Value)) + "\n" +
            string.Join("\n", details) + (dependents.Length > 8 ? $"\n... and {dependents.Length - 8} more" : "") +
            "\n\nThis operation creates one undo entry.";
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

    private static object CreateFloors(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var items = request.GetProperty("floors").EnumerateArray().ToArray();
        if (items.Length is < 1 or > 20) throw new ArgumentException("Provide 1 to 20 floor boundaries.");
        var specs = new List<(string Name, ElementId Type, ElementId Level, List<XYZ> Points, double OffsetMm)>();
        foreach (var item in items)
        {
            var typeId = new ElementId(item.GetProperty("floorTypeId").GetInt64());
            var levelId = new ElementId(item.GetProperty("levelId").GetInt64());
            if (doc.GetElement(typeId) is not FloorType) throw new ArgumentException($"Invalid floor type ID {typeId.Value}.");
            if (doc.GetElement(levelId) is not Level level) throw new ArgumentException($"Invalid level ID {levelId.Value}.");
            var points = item.GetProperty("pointsMm").EnumerateArray().Select(Point).ToList();
            if (points.Count is < 3 or > 100) throw new ArgumentException("Each floor needs 3 to 100 XY boundary points.");
            if (points[0].DistanceTo(points[^1]) < FromMm(1)) points.RemoveAt(points.Count - 1);
            if (points.Count < 3 || points.DistinctBy(p => (Math.Round(p.X, 7), Math.Round(p.Y, 7))).Count() < 3)
                throw new ArgumentException("Floor boundary must contain at least 3 unique points.");
            points = points.Select(p => new XYZ(p.X, p.Y, level.Elevation)).ToList();
            var offsetMm = item.TryGetProperty("offsetMm", out var offset) ? offset.GetDouble() : 0;
            if (!double.IsFinite(offsetMm) || Math.Abs(offsetMm) > 100_000) throw new ArgumentException("Floor offset must be finite and within ±100,000 mm.");
            specs.Add((item.TryGetProperty("name", out var name) ? name.GetString() ?? "Floor" : "Floor", typeId, levelId, points, offsetMm));
        }
        if (!Confirm(doc, $"Create {specs.Count} floor(s) from closed polygon boundaries? Area and bounds will be reported after creation."))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: create floors");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        var created = new List<(Floor Floor, string Name)>();
        try
        {
            foreach (var spec in specs)
            {
                var loop = new CurveLoop();
                for (var i = 0; i < spec.Points.Count; i++)
                    loop.Append(Line.CreateBound(spec.Points[i], spec.Points[(i + 1) % spec.Points.Count]));
                var floor = Floor.Create(doc, new List<CurveLoop> { loop }, spec.Type, spec.Level);
                var offsetParameter = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (offsetParameter is { IsReadOnly: false }) offsetParameter.Set(FromMm(spec.OffsetMm));
                created.Add((floor, spec.Name));
            }
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the floors.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, floors = created.Select(x => new
        {
            id = x.Floor.Id.Value, name = x.Name,
            areaSquareMetres = Math.Round(UnitUtils.ConvertFromInternalUnits(x.Floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0, UnitTypeId.SquareMeters), 3)
        }).ToArray() };
    }

    private static object CreateCeilings(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var items = request.GetProperty("ceilings").EnumerateArray().ToArray();
        if (items.Length is < 1 or > 20) throw new ArgumentException("Provide 1 to 20 ceiling boundaries.");
        var specs = items.Select(item =>
        {
            var typeId = new ElementId(item.GetProperty("ceilingTypeId").GetInt64());
            var levelId = new ElementId(item.GetProperty("levelId").GetInt64());
            if (doc.GetElement(typeId) is not CeilingType) throw new ArgumentException($"Invalid ceiling type ID {typeId.Value}.");
            if (doc.GetElement(levelId) is not Level level) throw new ArgumentException($"Invalid level ID {levelId.Value}.");
            var points = item.GetProperty("pointsMm").EnumerateArray().Select(Point).ToList();
            if (points.Count is < 3 or > 100) throw new ArgumentException("Each ceiling needs 3 to 100 XY boundary points.");
            if (points[0].DistanceTo(points[^1]) < FromMm(1)) points.RemoveAt(points.Count - 1);
            if (points.Count < 3) throw new ArgumentException("Ceiling boundary requires at least 3 unique points.");
            var heightMm = item.TryGetProperty("heightAboveLevelMm", out var height) ? height.GetDouble() : 0;
            if (!double.IsFinite(heightMm) || Math.Abs(heightMm) > 100_000) throw new ArgumentException("Ceiling height offset must be finite and within ±100,000 mm.");
            return (Type: typeId, Level: levelId, Elevation: level.Elevation, Points: points, Height: heightMm);
        }).ToArray();
        if (!Confirm(doc, $"Create {specs.Length} ceiling(s) from closed XY polygons? Ceiling height offsets default to zero."))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: create ceilings");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        var ids = new List<long>();
        try
        {
            foreach (var spec in specs)
            {
                var pts = spec.Points.Select(p => new XYZ(p.X, p.Y, spec.Elevation)).ToList();
                var loop = new CurveLoop();
                for (var i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
                var ceiling = Ceiling.Create(doc, new List<CurveLoop> { loop }, spec.Type, spec.Level);
                var heightParameter = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                if (heightParameter is { IsReadOnly: false }) heightParameter.Set(FromMm(spec.Height));
                ids.Add(ceiling.Id.Value);
            }
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the ceilings.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, createdIds = ids };
    }

    private static object CreateRoof(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var typeId = new ElementId(request.GetProperty("roofTypeId").GetInt64());
        var levelId = new ElementId(request.GetProperty("levelId").GetInt64());
        var type = doc.GetElement(typeId) as RoofType ?? throw new ArgumentException("roofTypeId must identify a footprint roof type.");
        var level = doc.GetElement(levelId) as Level ?? throw new ArgumentException("Invalid levelId.");
        var points = request.GetProperty("pointsMm").EnumerateArray().Select(Point).ToList();
        if (points.Count is < 3 or > 100) throw new ArgumentException("Roof footprint needs 3 to 100 XY boundary points.");
        if (points[0].DistanceTo(points[^1]) < FromMm(1)) points.RemoveAt(points.Count - 1);
        var slopeDegrees = request.GetProperty("slopeDegrees").GetDouble();
        if (!double.IsFinite(slopeDegrees) || slopeDegrees < 0 || slopeDegrees > 89) throw new ArgumentException("slopeDegrees must be between 0 and 89.");
        if (points.Count < 3) throw new ArgumentException("Roof footprint must have at least 3 unique points.");
        if (!Confirm(doc, $"Create a footprint roof on '{level.Name}' using '{type.Name}' with {slopeDegrees}° slope on each edge?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create footprint roof");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var footprint = doc.Application.Create.NewCurveArray();
            for (var i = 0; i < points.Count; i++)
                footprint.Append(Line.CreateBound(new XYZ(points[i].X, points[i].Y, level.Elevation), new XYZ(points[(i + 1) % points.Count].X, points[(i + 1) % points.Count].Y, level.Elevation)));
            var roof = doc.Create.NewFootPrintRoof(footprint, level, type, out var mappings);
            var iterator = mappings.ForwardIterator();
            while (iterator.MoveNext())
            {
                if (iterator.Current is ModelCurve modelCurve)
                {
                    roof.set_DefinesSlope(modelCurve, true);
                    roof.set_SlopeAngle(modelCurve, slopeDegrees * Math.PI / 180.0);
                }
            }
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the roof.");
            return new { approved = true, createdId = (long?)roof.Id.Value, slopeDegrees };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object PlaceFamilyInstances(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var items = request.GetProperty("instances").EnumerateArray().ToArray();
        if (items.Length is < 1 or > 50) throw new ArgumentException("Provide 1 to 50 family instances.");
        var specs = items.Select(item =>
        {
            var typeId = new ElementId(item.GetProperty("familyTypeId").GetInt64());
            var levelId = new ElementId(item.GetProperty("levelId").GetInt64());
            var symbol = doc.GetElement(typeId) as FamilySymbol ?? throw new ArgumentException($"Invalid family type ID {typeId.Value}.");
            if (doc.GetElement(levelId) is not Level) throw new ArgumentException($"Invalid level ID {levelId.Value}.");
            var point = item.TryGetProperty("pointMm", out var p) ? Point3(p) : throw new ArgumentException("Each instance needs pointMm [x,y,z].");
            Element? host = null;
            if (item.TryGetProperty("hostWallId", out var hostJson) && hostJson.ValueKind == JsonValueKind.Number)
                host = doc.GetElement(new ElementId(hostJson.GetInt64())) is Wall wall ? wall : throw new ArgumentException("hostWallId must identify a wall.");
            return (Symbol: symbol, Level: levelId, Point: point, Host: host);
        }).ToArray();
        var summary = string.Join("\n", specs.Take(12).Select((x, i) =>
            $"{i + 1}. {x.Symbol.FamilyName}: {x.Symbol.Name} at ({ToMm(x.Point.X):0}, {ToMm(x.Point.Y):0}, {ToMm(x.Point.Z):0}) mm"));
        if (specs.Length > 12) summary += $"\n... and {specs.Length - 12} more";
        if (!Confirm(doc, $"Place {specs.Length} family instance(s)?\n{summary}"))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: place family instances");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        var ids = new List<long>();
        try
        {
            foreach (var spec in specs)
            {
                if (!spec.Symbol.IsActive) spec.Symbol.Activate();
                FamilyInstance instance = spec.Host is Wall wall
                    ? doc.Create.NewFamilyInstance(spec.Point, spec.Symbol, wall, (Level)doc.GetElement(spec.Level)!, StructuralType.NonStructural)
                    : doc.Create.NewFamilyInstance(spec.Point, spec.Symbol, (Level)doc.GetElement(spec.Level)!, StructuralType.NonStructural);
                ids.Add(instance.Id.Value);
            }
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the family instances.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, createdIds = ids };
    }

    private static object LoadFamilyType(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        if (doc.IsModifiable) throw new InvalidOperationException("Family loading requires no open Revit transaction.");
        var path = Path.GetFullPath(request.GetProperty("path").GetString() ?? "");
        var typeName = RequiredName(request, "typeName", 100);
        if (!path.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("path must identify an existing Revit family (.rfa) file.");
        if (!Confirm(doc, $"Load family type '{typeName}' from:\n{path}? This adds the family content to the project."))
            return new { approved = false, familyTypeId = (long?)null };
        if (!doc.LoadFamilySymbol(path, typeName, out var symbol) || symbol is null)
            throw new InvalidOperationException($"Revit could not load type '{typeName}' from the selected family file.");
        return new { approved = true, family = symbol.FamilyName, type = symbol.Name, familyTypeId = (long?)symbol.Id.Value };
    }

    private static object CreatePlanView(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var typeId = new ElementId(request.GetProperty("viewFamilyTypeId").GetInt64());
        var levelId = new ElementId(request.GetProperty("levelId").GetInt64());
        var name = RequiredName(request, "name", 100);
        if (doc.GetElement(typeId) is not ViewFamilyType type || type.ViewFamily is not (ViewFamily.FloorPlan or ViewFamily.CeilingPlan or ViewFamily.StructuralPlan))
            throw new ArgumentException("viewFamilyTypeId must be a floor, ceiling, or structural plan view type.");
        if (doc.GetElement(levelId) is not Level) throw new ArgumentException("Invalid level ID.");
        if (!Confirm(doc, $"Create {type.ViewFamily} view '{name}' on level {doc.GetElement(levelId)!.Name}?")) return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create plan view");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var view = ViewPlan.Create(doc, typeId, levelId);
            view.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the view.");
            return new { approved = true, createdId = (long?)view.Id.Value, name = view.Name, viewType = view.ViewType.ToString() };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateDraftingView(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var typeId = new ElementId(request.GetProperty("viewFamilyTypeId").GetInt64());
        var name = RequiredName(request, "name", 100);
        if (doc.GetElement(typeId) is not ViewFamilyType type || type.ViewFamily != ViewFamily.Drafting)
            throw new ArgumentException("viewFamilyTypeId must identify a drafting view family type.");
        if (!Confirm(doc, $"Create drafting view '{name}' using type '{type.Name}'?")) return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create drafting view");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var view = ViewDrafting.Create(doc, typeId);
            view.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the drafting view.");
            return new { approved = true, createdId = (long?)view.Id.Value, name = view.Name };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateSectionView(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var typeId = new ElementId(request.GetProperty("viewFamilyTypeId").GetInt64());
        if (doc.GetElement(typeId) is not ViewFamilyType type || type.ViewFamily != ViewFamily.Section)
            throw new ArgumentException("viewFamilyTypeId must identify a section view family type.");
        var name = RequiredName(request, "name", 100);
        var axis = request.GetProperty("axis").GetString()?.ToLowerInvariant();
        if (axis is not ("x" or "y")) throw new ArgumentException("axis must be 'x' or 'y'.");
        var origin = PointMm(request.GetProperty("originMm"));
        var width = BoundedPositive(request.GetProperty("widthMm").GetDouble(), 10, 500_000, "widthMm");
        var height = BoundedPositive(request.GetProperty("heightMm").GetDouble(), 10, 500_000, "heightMm");
        var depth = BoundedPositive(request.GetProperty("depthMm").GetDouble(), 10, 500_000, "depthMm");
        var scale = request.TryGetProperty("scale", out var scaleNode) ? scaleNode.GetInt32() : 100;
        if (scale is < 1 or > 10000) throw new ArgumentException("scale must be between 1 and 10000.");
        var right = axis == "x" ? XYZ.BasisY : XYZ.BasisX;
        var up = XYZ.BasisZ;
        var direction = axis == "x" ? XYZ.BasisX : -XYZ.BasisY;
        if (!Confirm(doc, $"Create section '{name}' along world {axis.ToUpperInvariant()} at ({request.GetProperty("originMm")[0].GetDouble()}, {request.GetProperty("originMm")[1].GetDouble()}, {request.GetProperty("originMm")[2].GetDouble()}) mm; extents {width} × {height} × {depth} mm?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create section view");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = direction;
            var box = new BoundingBoxXYZ
            {
                Transform = transform,
                Enabled = true,
                Min = new XYZ(-FromMm(width / 2), 0, -FromMm(depth / 2)),
                Max = new XYZ(FromMm(width / 2), FromMm(height), FromMm(depth / 2))
            };
            for (var bound = 0; bound < 3; bound++)
            {
                box.set_MinEnabled(bound, true);
                box.set_MaxEnabled(bound, true);
            }
            var view = ViewSection.CreateSection(doc, typeId, box);
            view.Name = name;
            view.Scale = scale;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the section view.");
            return new { approved = true, createdId = (long?)view.Id.Value, name = view.Name, scale = view.Scale };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateElevationView(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var typeId = new ElementId(request.GetProperty("viewFamilyTypeId").GetInt64());
        var planId = new ElementId(request.GetProperty("planViewId").GetInt64());
        if (doc.GetElement(typeId) is not ViewFamilyType type || type.ViewFamily != ViewFamily.Elevation)
            throw new ArgumentException("viewFamilyTypeId must identify an elevation view family type.");
        if (doc.GetElement(planId) is not ViewPlan plan || plan.IsTemplate)
            throw new ArgumentException("planViewId must identify a non-template plan view.");
        var index = request.GetProperty("directionIndex").GetInt32();
        if (index is < 0 or > 3) throw new ArgumentException("directionIndex must be 0, 1, 2, or 3.");
        var name = RequiredName(request, "name", 100);
        var origin = PointMm(request.GetProperty("originMm"));
        var scale = request.TryGetProperty("scale", out var scaleNode) ? scaleNode.GetInt32() : 100;
        if (scale is < 1 or > 10000) throw new ArgumentException("scale must be between 1 and 10000.");
        if (!Confirm(doc, $"Create elevation '{name}' from plan '{plan.Name}' at direction index {index}?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create elevation view");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var marker = ElevationMarker.CreateElevationMarker(doc, typeId, origin, scale);
            var view = marker.CreateElevation(doc, planId, index);
            view.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the elevation view.");
            return new { approved = true, createdId = (long?)view.Id.Value, name = view.Name, scale = view.Scale, directionIndex = index };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateDetailLines(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var viewId = new ElementId(request.GetProperty("viewId").GetInt64());
        if (doc.GetElement(viewId) is not View view || view.IsTemplate || view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.Schedule)
            throw new ArgumentException("viewId must identify a non-template 2D view.");
        var lines = request.GetProperty("lines").EnumerateArray().ToArray();
        if (lines.Length is < 1 or > 100) throw new ArgumentException("Provide 1 to 100 detail lines.");
        var curves = lines.Select(item =>
        {
            var start = PointMm(item.GetProperty("startMm"));
            var end = PointMm(item.GetProperty("endMm"));
            if (start.DistanceTo(end) < 1e-6) throw new ArgumentException("Detail line endpoints must differ.");
            return Line.CreateBound(start, end);
        }).ToArray();
        if (!Confirm(doc, $"Create {curves.Length} detail line(s) in view '{view.Name}'? Coordinates are absolute model millimetres."))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: create detail lines");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var created = curves.Select(curve => doc.Create.NewDetailCurve(view, curve).Id.Value).ToArray();
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit detail lines.");
            return new { approved = true, viewId = view.Id.Value, createdIds = created };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object DimensionGrids(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var viewId = new ElementId(request.GetProperty("viewId").GetInt64());
        if (doc.GetElement(viewId) is not ViewPlan view || view.IsTemplate) throw new ArgumentException("viewId must identify a non-template plan view.");
        var rawIds = request.GetProperty("gridIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 2 or > 30 || rawIds.Distinct().Count() != rawIds.Length) throw new ArgumentException("Provide 2 to 30 distinct grid IDs.");
        var grids = rawIds.Select(x => doc.GetElement(new ElementId(x)) as Grid ?? throw new ArgumentException($"Element {x} is not a grid.")).ToArray();
        var lines = grids.Select(g => g.Curve as Line ?? throw new ArgumentException("Only straight grids can be dimensioned by this tool.")).ToArray();
        var direction = lines[0].Direction.Normalize();
        if (lines.Skip(1).Any(line => Math.Abs(direction.DotProduct(line.Direction.Normalize())) < 0.9999))
            throw new ArgumentException("All selected grids must be parallel.");
        var offset = request.GetProperty("offsetMm").GetDouble();
        if (!double.IsFinite(offset) || Math.Abs(offset) > 100_000) throw new ArgumentException("offsetMm must be finite and within ±100000 mm.");
        var normal = new XYZ(-direction.Y, direction.X, 0).Normalize();
        var anchor = lines[0].Evaluate(0.5, true);
        var scalars = lines.Select(line => line.Evaluate(0.5, true).DotProduct(normal)).ToArray();
        if (scalars.Max() - scalars.Min() < FromMm(1)) throw new ArgumentException("The selected grids are too close together to dimension.");
        var z = view.GenLevel?.Elevation ?? anchor.Z;
        var shifted = anchor + direction * FromMm(offset);
        var start = new XYZ(shifted.X + normal.X * (scalars.Min() - shifted.DotProduct(normal)), shifted.Y + normal.Y * (scalars.Min() - shifted.DotProduct(normal)), z);
        var end = new XYZ(shifted.X + normal.X * (scalars.Max() - shifted.DotProduct(normal)), shifted.Y + normal.Y * (scalars.Max() - shifted.DotProduct(normal)), z);
        if (!Confirm(doc, $"Dimension {grids.Length} parallel grids in view '{view.Name}' with a {offset} mm offset?"))
            return new { approved = false, dimensionId = (long?)null };
        using var tx = new Transaction(doc, "Agent: dimension grids");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var refs = new ReferenceArray();
            foreach (var grid in grids.Zip(scalars).OrderBy(pair => pair.Second).Select(pair => pair.First)) refs.Append(new Reference(grid));
            var dimension = doc.Create.NewDimension(view, Line.CreateBound(start, end), refs);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the grid dimension.");
            return new { approved = true, dimensionId = (long?)dimension.Id.Value, gridIds = rawIds };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object CreateGroup(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var rawIds = request.GetProperty("elementIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 2 or > 100 || rawIds.Distinct().Count() != rawIds.Length) throw new ArgumentException("Provide 2 to 100 distinct element IDs.");
        foreach (var id in rawIds) if (doc.GetElement(new ElementId(id)) is null) throw new ArgumentException($"Element {id} not found.");
        var name = request.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String ? nameNode.GetString() : null;
        if (name is { Length: > 100 }) throw new ArgumentException("Group name must be 100 characters or fewer.");
        if (!Confirm(doc, $"Create a group from {rawIds.Length} elements{(string.IsNullOrWhiteSpace(name) ? "" : $" named '{name}'")}?"))
            return new { approved = false, groupId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create model group");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var group = doc.Create.NewGroup(rawIds.Select(x => new ElementId(x)).ToList());
            if (!string.IsNullOrWhiteSpace(name)) group.GroupType.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the group.");
            return new { approved = true, groupId = (long?)group.Id.Value, typeId = group.GroupType.Id.Value, name = group.GroupType.Name };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object UngroupElements(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var groupId = new ElementId(request.GetProperty("groupId").GetInt64());
        if (doc.GetElement(groupId) is not Group group) throw new ArgumentException("groupId must identify a model group.");
        if (!Confirm(doc, $"Ungroup '{group.GroupType.Name}'? This removes the group container and keeps its members."))
            return new { approved = false, elementIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: ungroup model group");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var members = group.UngroupMembers().Select(x => x.Value).ToArray();
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the ungroup operation.");
            return new { approved = true, elementIds = members };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object PlaceViewOnSheet(Document doc, JsonElement request)
    {
        var sheetId = new ElementId(request.GetProperty("sheetId").GetInt64());
        var viewId = new ElementId(request.GetProperty("viewId").GetInt64());
        var sheet = doc.GetElement(sheetId) as ViewSheet ?? throw new ArgumentException("Invalid sheetId.");
        var view = doc.GetElement(viewId) as View ?? throw new ArgumentException("Invalid viewId.");
        if (!Viewport.CanAddViewToSheet(doc, sheetId, viewId)) throw new ArgumentException("This view cannot be placed on that sheet (it may already be placed or be a schedule).");
        var x = request.GetProperty("xMm").GetDouble(); var y = request.GetProperty("yMm").GetDouble();
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 5000 || Math.Abs(y) > 5000) throw new ArgumentException("Sheet coordinates must be finite and within ±5000 mm.");
        if (!Confirm(doc, $"Place view '{view.Name}' on sheet {sheet.SheetNumber} at ({x}, {y}) mm from the sheet origin?")) return new { approved = false, viewportId = (long?)null };
        using var tx = new Transaction(doc, "Agent: place view on sheet");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var viewport = Viewport.Create(doc, sheetId, viewId, new XYZ(FromMm(x), FromMm(y), 0));
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the viewport.");
            return new { approved = true, viewportId = (long?)viewport.Id.Value, sheetId = sheetId.Value, viewId = viewId.Value };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object Create3DView(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var typeId = new ElementId(request.GetProperty("viewFamilyTypeId").GetInt64());
        var name = RequiredName(request, "name", 100);
        if (doc.GetElement(typeId) is not ViewFamilyType type || type.ViewFamily != ViewFamily.ThreeDimensional)
            throw new ArgumentException("viewFamilyTypeId must identify a 3D view family type.");
        if (!Confirm(doc, $"Create isometric 3D view '{name}' using type '{type.Name}'?"))
            return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create 3D view");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var view = View3D.CreateIsometric(doc, typeId);
            view.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the 3D view.");
            return new { approved = true, createdId = (long?)view.Id.Value, name = view.Name };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object SetViewScale(Document doc, JsonElement request)
    {
        var view = doc.GetElement(new ElementId(request.GetProperty("viewId").GetInt64())) as View
            ?? throw new ArgumentException("Invalid viewId.");
        var scale = request.GetProperty("scale").GetInt32();
        if (view.IsTemplate || scale is < 1 or > 10000) throw new ArgumentException("Choose a non-template view and a scale from 1 to 10000.");
        if (!Confirm(doc, $"Set view '{view.Name}' scale to 1:{scale}?"))
            return new { approved = false, viewId = view.Id.Value };
        using var tx = new Transaction(doc, "Agent: set view scale");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            view.Scale = scale;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the view scale.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, viewId = view.Id.Value, scale = view.Scale };
    }

    private static object SetViewVisibility(Document doc, JsonElement request)
    {
        var view = doc.GetElement(new ElementId(request.GetProperty("viewId").GetInt64())) as View
            ?? throw new ArgumentException("Invalid viewId.");
        if (view.IsTemplate) throw new ArgumentException("Choose a view, not a view template.");
        var rawIds = request.GetProperty("elementIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 1 or > 100 || rawIds.Distinct().Count() != rawIds.Length) throw new ArgumentException("Provide 1 to 100 distinct element IDs.");
        var ids = rawIds.Select(x => new ElementId(x)).ToList();
        foreach (var id in ids) if (doc.GetElement(id) is null) throw new ArgumentException($"Element {id.Value} not found.");
        var hide = request.GetProperty("hidden").GetBoolean();
        if (!Confirm(doc, $"{(hide ? "Hide" : "Show")} {ids.Count} element(s) in view '{view.Name}'?"))
            return new { approved = false, viewId = view.Id.Value };
        using var tx = new Transaction(doc, "Agent: set view element visibility");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            if (hide) view.HideElements(ids); else view.UnhideElements(ids);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit view visibility.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, viewId = view.Id.Value, hidden = hide, elementIds = rawIds };
    }

    private static object TagElements(Document doc, JsonElement request)
    {
        var view = doc.GetElement(new ElementId(request.GetProperty("viewId").GetInt64())) as View
            ?? throw new ArgumentException("Invalid viewId.");
        if (view.IsTemplate || (view is View3D view3d && !view3d.IsLocked)) throw new ArgumentException("Choose a non-template 2D view or a locked 3D view.");
        var tagTypeId = new ElementId(request.GetProperty("tagTypeId").GetInt64());
        var tagType = doc.GetElement(tagTypeId) as FamilySymbol ?? throw new ArgumentException("tagTypeId must identify a tag family type.");
        var items = request.GetProperty("tags").EnumerateArray().ToArray();
        if (items.Length is < 1 or > 50) throw new ArgumentException("Provide 1 to 50 tags.");
        var specs = items.Select(item =>
        {
            var id = new ElementId(item.GetProperty("elementId").GetInt64());
            var element = doc.GetElement(id) ?? throw new ArgumentException($"Element {id.Value} not found.");
            var point = Point3(item.GetProperty("headMm"));
            return (Element: element, Point: point);
        }).ToArray();
        if (!Confirm(doc, $"Place {specs.Length} '{tagType.FamilyName}: {tagType.Name}' tag(s) in view '{view.Name}'?"))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: tag elements");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        var created = new List<long>();
        try
        {
            foreach (var spec in specs)
            {
                var tag = IndependentTag.Create(doc, tagTypeId, view.Id, new Reference(spec.Element), true, TagOrientation.Horizontal, spec.Point);
                created.Add(tag.Id.Value);
            }
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the tags.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, createdIds = created };
    }

    private static object DuplicateView(Document doc, JsonElement request)
    {
        var id = new ElementId(request.GetProperty("viewId").GetInt64());
        var source = doc.GetElement(id) as View ?? throw new ArgumentException("Invalid viewId.");
        var name = RequiredName(request, "name", 100);
        var option = request.TryGetProperty("withDetailing", out var detailed) && detailed.GetBoolean()
            ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;
        if (!source.CanViewBeDuplicated(option)) throw new ArgumentException("This view cannot be duplicated with the requested option.");
        if (!Confirm(doc, $"Duplicate view '{source.Name}' as '{name}'?")) return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: duplicate view");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var copy = (View)doc.GetElement(source.Duplicate(option))!;
            copy.Name = name;
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the duplicated view.");
            return new { approved = true, createdId = (long?)copy.Id.Value, name = copy.Name };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object MoveElements(Document doc, JsonElement request)
    {
        var raw = request.GetProperty("elementIds").EnumerateArray().Select(v => v.GetInt64()).ToArray();
        if (raw.Length is < 1 or > 50 || raw.Distinct().Count() != raw.Length) throw new ArgumentException("Provide 1 to 50 distinct element IDs.");
        var ids = raw.Select(x => new ElementId(x)).ToArray();
        foreach (var id in ids) if (doc.GetElement(id) is null) throw new ArgumentException($"Element {id.Value} not found.");
        var delta = Point3(request.GetProperty("deltaMm"));
        if (delta.GetLength() < FromMm(0.1)) throw new ArgumentException("Movement must be at least 0.1 mm.");
        if (!Confirm(doc, $"Move {ids.Length} element(s) by ({ToMm(delta.X):0.##}, {ToMm(delta.Y):0.##}, {ToMm(delta.Z):0.##}) mm?"))
            return new { approved = false, movedIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: move elements");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            ElementTransformUtils.MoveElements(doc, ids, delta);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the movement.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, movedIds = raw };
    }

    private static object RotateElements(Document doc, JsonElement request)
    {
        var rawIds = request.GetProperty("elementIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 1 or > 50 || rawIds.Distinct().Count() != rawIds.Length) throw new ArgumentException("Provide 1 to 50 distinct element IDs.");
        var ids = rawIds.Select(x => new ElementId(x)).ToList();
        foreach (var id in ids) if (doc.GetElement(id) is null) throw new ArgumentException($"Element {id.Value} not found.");
        var pivot = Point3(request.GetProperty("pivotMm"));
        var degrees = request.GetProperty("degrees").GetDouble();
        if (!double.IsFinite(degrees) || Math.Abs(degrees) > 36000) throw new ArgumentException("Rotation must be finite and within ±36,000 degrees.");
        if (Math.Abs(degrees) < 0.001) throw new ArgumentException("Rotation must be at least 0.001 degrees.");
        if (!Confirm(doc, $"Rotate {ids.Count} element(s) around vertical axis at ({ToMm(pivot.X):0}, {ToMm(pivot.Y):0}, {ToMm(pivot.Z):0}) mm by {degrees}°?"))
            return new { approved = false, rotatedIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: rotate elements");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var axis = Line.CreateBound(pivot, pivot + XYZ.BasisZ);
            ElementTransformUtils.RotateElements(doc, ids, axis, degrees * Math.PI / 180.0);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the rotation.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, rotatedIds = rawIds };
    }

    private static object CopyElements(Document doc, JsonElement request)
    {
        var rawIds = request.GetProperty("elementIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 1 or > 50 || rawIds.Distinct().Count() != rawIds.Length) throw new ArgumentException("Provide 1 to 50 distinct element IDs.");
        var ids = rawIds.Select(x => new ElementId(x)).ToList();
        foreach (var id in ids) if (doc.GetElement(id) is null) throw new ArgumentException($"Element {id.Value} not found.");
        var delta = Point3(request.GetProperty("deltaMm"));
        if (delta.GetLength() < FromMm(0.1)) throw new ArgumentException("Copy displacement must be at least 0.1 mm.");
        if (!Confirm(doc, $"Copy {ids.Count} element(s) by ({ToMm(delta.X):0.##}, {ToMm(delta.Y):0.##}, {ToMm(delta.Z):0.##}) mm?"))
            return new { approved = false, createdIds = Array.Empty<long>() };
        using var tx = new Transaction(doc, "Agent: copy elements");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        ICollection<ElementId> created;
        try
        {
            created = ElementTransformUtils.CopyElements(doc, ids, delta);
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the copy.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, sourceIds = rawIds, createdIds = created.Select(x => x.Value).ToArray() };
    }

    private static object ChangeElementType(Document doc, JsonElement request)
    {
        var id = new ElementId(request.GetProperty("elementId").GetInt64());
        var typeId = new ElementId(request.GetProperty("typeId").GetInt64());
        var element = doc.GetElement(id) ?? throw new ArgumentException("Element not found.");
        if (doc.GetElement(typeId) is not ElementType type) throw new ArgumentException("typeId must identify an element type.");
        if (!Confirm(doc, $"Change element {id.Value} ({element.Category?.Name}) to type '{type.Name}'?")) return new { approved = false, elementId = id.Value };
        using var tx = new Transaction(doc, "Agent: change element type");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        ElementId oldType;
        try
        {
            oldType = element.GetTypeId();
            var rejected = element.ChangeTypeId(typeId);
            if (rejected != ElementId.InvalidElementId) throw new InvalidOperationException($"Revit rejected the type change; replacement element ID {rejected.Value} was returned.");
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the type change.");
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
        return new { approved = true, elementId = id.Value, previousTypeId = oldType.Value, typeId = typeId.Value };
    }

    private static object CreateRoom(Document doc, JsonElement request)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("The active document is read-only.");
        var levelId = new ElementId(request.GetProperty("levelId").GetInt64());
        var level = doc.GetElement(levelId) as Level ?? throw new ArgumentException("Invalid levelId.");
        var u = request.GetProperty("pointMm")[0].GetDouble(); var v = request.GetProperty("pointMm")[1].GetDouble();
        if (!double.IsFinite(u) || !double.IsFinite(v) || Math.Abs(u) > 1_000_000 || Math.Abs(v) > 1_000_000) throw new ArgumentException("Room point must be finite and within ±1,000,000 mm.");
        if (!Confirm(doc, $"Place a room on level '{level.Name}' at ({u}, {v}) mm? Revit must find an enclosed room boundary.")) return new { approved = false, createdId = (long?)null };
        using var tx = new Transaction(doc, "Agent: create room");
        if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Could not start a Revit transaction.");
        try
        {
            var room = doc.Create.NewRoom(level, new UV(FromMm(u), FromMm(v)));
            if (room is null) throw new InvalidOperationException("Revit could not create a room at that point. Confirm an enclosed boundary exists.");
            if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit did not commit the room.");
            return new { approved = true, createdId = (long?)room.Id.Value, number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString(), areaSquareMetres = Math.Round(UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters), 3) };
        }
        catch { if (tx.HasStarted()) tx.RollBack(); throw; }
    }

    private static object ExportSheetsPdf(Document doc, JsonElement request)
    {
        var rawIds = request.GetProperty("sheetIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 1 or > 100 || rawIds.Distinct().Count() != rawIds.Length)
            throw new ArgumentException("Provide 1 to 100 distinct sheet IDs.");
        var ids = rawIds.Select(x => new ElementId(x)).ToList();
        foreach (var id in ids) if (doc.GetElement(id) is not ViewSheet) throw new ArgumentException($"Element {id.Value} is not a sheet.");
        var folder = Path.GetFullPath(request.GetProperty("folder").GetString() ?? "");
        if (!Directory.Exists(folder)) throw new ArgumentException("The output folder must already exist.");
        var fileName = RequiredName(request, "fileName", 160);
        fileName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("fileName must be a valid file name.");
        var output = Path.Combine(folder, fileName + ".pdf");
        var overwrite = File.Exists(output);
        if (!Confirm(doc, $"Export {ids.Count} sheet(s) to one PDF?\n{output}" +
            (overwrite ? "\nAn existing file will be replaced." : "") +
            "\nPDF export is an external file operation and is not part of Revit Undo."))
            return new { approved = false, outputPath = (string?)null };
        using var options = new PDFExportOptions { Combine = true, FileName = fileName };
        if (!doc.Export(folder, ids, options)) throw new InvalidOperationException("Revit reported that one or more sheets failed to export.");
        return new { approved = true, outputPath = output, sheetIds = rawIds };
    }

    private static object ExportViewsDwg(Document doc, JsonElement request)
    {
        var rawIds = request.GetProperty("viewIds").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        if (rawIds.Length is < 1 or > 100 || rawIds.Distinct().Count() != rawIds.Length)
            throw new ArgumentException("Provide 1 to 100 distinct view IDs.");
        var ids = rawIds.Select(x => new ElementId(x)).ToList();
        foreach (var id in ids) if (doc.GetElement(id) is not View view || view.IsTemplate) throw new ArgumentException($"Element {id.Value} is not an exportable view.");
        var folder = Path.GetFullPath(request.GetProperty("folder").GetString() ?? "");
        if (!Directory.Exists(folder)) throw new ArgumentException("The output folder must already exist.");
        var prefix = Path.GetFileNameWithoutExtension(RequiredName(request, "filePrefix", 120));
        if (string.IsNullOrWhiteSpace(prefix) || prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid filePrefix.");
        if (!Confirm(doc, $"Export {ids.Count} view(s) as DWG into '{folder}' using prefix '{prefix}'? Export files are external and cannot be undone through Revit."))
            return new { approved = false, folder = (string?)null };
        using var options = new DWGExportOptions();
        if (!doc.Export(folder, prefix, ids, options)) throw new InvalidOperationException("Revit reported that one or more views failed to export.");
        return new { approved = true, folder, filePrefix = prefix, viewIds = rawIds };
    }

    private static XYZ Point(JsonElement point)
    {
        if (point.GetArrayLength() != 2) throw new ArgumentException("A point must contain [xMm, yMm].");
        var x = point[0].GetDouble(); var y = point[1].GetDouble();
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000)
            throw new ArgumentException("Coordinates must be finite and within ±1,000,000 mm.");
        return new XYZ(FromMm(x), FromMm(y), 0);
    }

    private static XYZ Point3(JsonElement point)
    {
        if (point.GetArrayLength() != 3) throw new ArgumentException("A point must contain [xMm, yMm, zMm].");
        var values = point.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (values.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1_000_000)) throw new ArgumentException("Coordinates must be finite and within ±1,000,000 mm.");
        return new XYZ(FromMm(values[0]), FromMm(values[1]), FromMm(values[2]));
    }

    private static XYZ PointMm(JsonElement point) => Point3(point);

    private static double BoundedPositive(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name} must be finite and between {minimum} and {maximum} mm.");
        return value;
    }

    private static double FromMm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
}
