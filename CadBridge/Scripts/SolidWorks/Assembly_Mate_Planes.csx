// Adds a COINCIDENT mate between the same-named plane of two components of the ACTIVE assembly.
// Open your assembly first. Component names are the ones in the feature tree, e.g. Piston-1.
// Run it again with other values for more mates.
// Available variables: swApp, OutDir, Log(...)

string CompA = "{{CompA=Piston-1}}";
string CompB = "{{CompB=Rod-1}}";
string Plane = "{{Plane=Front Plane}}";        // Front Plane, Top Plane or Right Plane

ModelDoc2 model = (ModelDoc2)swApp.ActiveDoc;
if (model == null || !(model is AssemblyDoc))
    throw new Exception("Open an assembly first (it must be the active document).");
AssemblyDoc asm = (AssemblyDoc)model;

string title = Path.GetFileNameWithoutExtension(model.GetPathName());
if (title == "") title = model.GetTitle();

model.ClearSelection2(true);
bool a = model.Extension.SelectByID2(Plane + "@" + CompA + "@" + title, "PLANE", 0, 0, 0, false, 1, null, 0);
bool b = model.Extension.SelectByID2(Plane + "@" + CompB + "@" + title, "PLANE", 0, 0, 0, true, 1, null, 0);
if (!a || !b)
    throw new Exception("Could not select the planes. Check the component names in the feature tree and the plane name.");

int err;
object mate = asm.AddMate5((int)swMateType_e.swMateCOINCIDENT, (int)swMateAlign_e.swMateAlignALIGNED,
    false, 0, 0, 0, 0, 0, 0, 0, 0, false, false, 0, out err);

Log(mate != null ? "Mate added: " + Plane + " of " + CompA + " = " + Plane + " of " + CompB
                 : "Mate failed (error " + err + ").");
