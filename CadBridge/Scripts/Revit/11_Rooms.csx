// Revit STEP 11: creates the ROOMS in every closed area made by the walls, and writes the finish text of each room.
// Run 08_Walls (and 10_Doors_Windows if you want openings) first. The walls must form closed loops.
// The finish texts are what 21_Takeoff_Architecture uses to measure floors, skirting, wall finishes and ceilings.
//   FromLevel / ToLevel : rooms are created on every level from FromLevel to ToLevel (ToLevel = FromLevel -> one level).
//   RoomName    : the name; a number is added after it (Room 1, Room 2 ...).  Numbers are 101, 102 ... on the first level, 201 ... on the next.
//   FloorFinish / WallFinish / CeilingFinish / BaseFinish : text written in the room (they appear in the room schedule and in the BOQ).
//   UpperLimit  : NextLevel = the room goes up to the next level (minus Deduct),  Height = a fixed room height in mm.
//   Height      : used when UpperLimit = Height.
//   Deduct      : mm taken off the storey height when UpperLimit = NextLevel (slab thickness, for example 200).
// A floor-plan view is made for a level that has none (Revit needs one to create rooms).
// Rooms that already exist are left alone: only new closed areas get a room.
// Available variables: uiapp, doc, OutDir, Log(...)

string FromLevel = "{{FromLevel=@levels}}";              // list from Revit
string ToLevel = "{{ToLevel=@levels}}";                  // list from Revit
string RoomName = "{{RoomName=Room}}";
string FloorFinish = "{{FloorFinish=Ceramic tiles}}";
string WallFinish = "{{WallFinish=Plaster and paint}}";
string CeilingFinish = "{{CeilingFinish=Plaster and paint}}";
string BaseFinish = "{{BaseFinish=Ceramic skirting}}";
string UpperLimit = "{{UpperLimit=NextLevel|Height}}";
double Height = {{Height=3000}};
double Deduct = {{Deduct=200}};

List<Level> levels = Cb.Levels(doc);
if (levels.Count == 0) throw new Exception("No level found in this project. Run 02_Levels first.");
int LevelIdx(string name, string what)
{
    int found = levels.FindIndex(q => q.Name == (name ?? "").Trim());
    if (found < 0)
        throw new Exception(what + " not found: '" + name + "'. Press Connect to refresh the lists. Available: " + string.Join(", ", levels.Select(q => q.Name)));
    return found;
}
int i0 = LevelIdx(FromLevel, "FromLevel");
int i1 = Math.Max(i0, LevelIdx(ToLevel, "ToLevel"));
bool toNext = UpperLimit.Trim().StartsWith("N", StringComparison.OrdinalIgnoreCase);

void SetText(Element e, BuiltInParameter bip, string text)
{
    Parameter p = e.get_Parameter(bip);
    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(text ?? "");
}

int created = 0, failedLevels = 0, viewsMade = 0;

using (Transaction t = new Transaction(doc, "CAD Bridge: rooms"))
{
    t.Start();

    for (int lvIndex = i0; lvIndex <= i1; lvIndex++)
    {
        Level lv = levels[lvIndex];

        // Revit creates rooms from a floor plan: make one when this level has none
        bool hasPlan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Any(q => !q.IsTemplate && q.ViewType == ViewType.FloorPlan && q.GenLevel != null && q.GenLevel.Id.Equals(lv.Id));
        if (!hasPlan)
        {
            ViewFamilyType vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(q => q.ViewFamily == ViewFamily.FloorPlan);
            if (vft != null) { ViewPlan.Create(doc, vft.Id, lv.Id); viewsMade++; }
        }

        ICollection<ElementId> madeIds = null;
        try { madeIds = doc.Create.NewRooms2(lv); }
        catch (Exception ex)
        {
            failedLevels++;
            Log("Level " + lv.Name + ": Revit could not create rooms (" + ex.Message + ").");
            continue;
        }
        if (madeIds == null || madeIds.Count == 0)
        {
            Log("Level " + lv.Name + ": no new closed area found. Check that the walls form closed loops and are room bounding.");
            continue;
        }

        int n = 0;
        foreach (ElementId id in madeIds)
        {
            Autodesk.Revit.DB.Architecture.Room rm = doc.GetElement(id) as Autodesk.Revit.DB.Architecture.Room;
            if (rm == null) continue;
            n++;
            SetText(rm, BuiltInParameter.ROOM_NAME, RoomName.Trim() + " " + n);
            SetText(rm, BuiltInParameter.ROOM_NUMBER, ((lvIndex + 1) * 100 + n).ToString());
            SetText(rm, BuiltInParameter.ROOM_FINISH_FLOOR, FloorFinish);
            SetText(rm, BuiltInParameter.ROOM_FINISH_WALL, WallFinish);
            SetText(rm, BuiltInParameter.ROOM_FINISH_CEILING, CeilingFinish);
            SetText(rm, BuiltInParameter.ROOM_FINISH_BASE, BaseFinish);

            // room height: up to the next level, or a fixed height above this level
            Parameter pUpper = rm.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
            Parameter pOffset = rm.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
            if (pUpper != null && pOffset != null && !pUpper.IsReadOnly && !pOffset.IsReadOnly)
            {
                if (toNext && lvIndex + 1 < levels.Count)
                {
                    pUpper.Set(levels[lvIndex + 1].Id);
                    pOffset.Set(-Cb.Ft(Deduct));
                }
                else
                {
                    pUpper.Set(lv.Id);
                    pOffset.Set(Cb.Ft(Height));
                }
            }
        }
        created += n;
        Log("Level " + lv.Name + ": " + n + " room(s) created.");
    }

    t.Commit();
}

Log("Created " + created + " room(s) on " + (i1 - i0 + 1) + " level(s)." + (viewsMade > 0 ? " Floor plan views made: " + viewsMade + "." : ""));
if (created == 0 && failedLevels == 0) Log("No room was created. Rooms need closed loops of room-bounding walls (08_Walls with Place = Perimeter or AllGrids).");
