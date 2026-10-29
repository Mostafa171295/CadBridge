// Lists the surfaces and alignments of the Civil 3D drawing that is open.
// Available variables: acad (AutoCAD), civil (Civil 3D application), OutDir, Log(...)

if (civil == null)
    throw new Exception("The Civil 3D object was not found. Are you connected to Civil 3D (not plain AutoCAD)?");

var cdoc = civil.ActiveDocument;
Log("Drawing: " + acad.ActiveDocument.Name);

Log("Surfaces: " + cdoc.Surfaces.Count);
foreach (var s in cdoc.Surfaces) Log("   surface: " + s.Name);

Log("Alignments (not in a site): " + cdoc.AlignmentsSiteless.Count);
foreach (var a in cdoc.AlignmentsSiteless) Log("   alignment: " + a.Name);
