// CAD Bridge - Revit: build a structure from A to Z.  Run the numbered scripts in this order
// (double-click one in the Script library, set the values, press Run code).
// This script only prints the list - it changes nothing.

Log("STEP 1  01_Grids     the axes: vertical 1, 2, 3 ... and horizontal A, B, C ...");
Log("STEP 2  02_Levels    foundation level, ground, storeys and the roof level (+ plan views)");
Log("STEP 3  03_Footings  isolated footings under the grid intersections   (family folder button)");
Log("STEP 4  04_TieBeams  tie beams between the footings                    (family folder button)");
Log("STEP 5  05_Columns   columns on every intersection, storey by storey   (family folder button)");
Log("STEP 6  06_Beams     floor beams between the columns, for each level   (family folder button)");
Log("STEP 7  07_Slabs     floor slabs on each level");
Log("STEP 8  08_Walls     walls / shear walls along the grid lines");
Log("STEP 9  09_Roof      the roof on the top level");
Log("STEP 10 10_Doors_Windows  doors / windows in the middle of the bays (run it once for windows, once for doors)");
Log("STEP 11 11_Rooms          rooms in every closed area + finish texts (floor / wall / ceiling / skirting)");
Log("STEP 12 12_Stairs         real Revit stairs between levels: Straight / UTurn / LTurn + the opening in the slab above");
Log("STEP 13 13_Slab_Openings  shaft openings (lift, duct, stairwell) that cut the slabs between two levels");
Log("STEP 14 14_Railings       railings around the slab edge, around slab openings, or around the grid area");
Log("STEP 15 15_Fence_Walls    site boundary wall (سور) or roof parapet around the grid area (gate sides can be left open)");
Log("STEP 16 16_Edit_Selected  edit what you SELECTED in Revit: change type / set parameter / move / rotate / copy / delete (or a whole category)");
Log("        10_Doors_Windows now has Host = SelectedWalls: select walls in Revit, then add doors / windows only in those walls");
Log("QUANTITIES (Excel, the model is not changed):");
Log("        20_Takeoff_Structure     concrete, formwork, steel, plain concrete, excavation, backfill  -> BOQ_Structure_*.xlsx");
Log("        21_Takeoff_Architecture  masonry, plaster, finishes by room, doors / windows / lintels, roofs -> BOQ_Architecture_*.xlsx");
Log("        22_Takeoff_Selected      quantities (Excel) of ONLY the elements you select in Revit, grouped by category and type");
Log("Tip: press Connect again after you change anything in Revit, so the lists (levels, types, materials) are refreshed.");
Log("Tip: running a script twice does not duplicate what already exists (same place + same level = skipped).");
