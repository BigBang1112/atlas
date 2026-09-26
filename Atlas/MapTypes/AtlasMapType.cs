using Atlas.Libs;

namespace Atlas.MapTypes;

public class AtlasMapType : CMapType, IContext
{
    private readonly AtlasEngine atlas = new();
    private bool panelHovered;
    
    public void Main()
    {
        // Paths are relative to ManiaPlanetUserData\Blocks. Dock folders match the freeform
        // 1x1 piece indices, with their files as subvariants; the final entry is the filler.
        var dock = "Lagoon\\Z_Bay\\Z_BayDock\\Z_BayDock\\";
        var bayBase = "Lagoon\\Z_Bay\\Z_BayDock\\A_BayBase\\";
        atlas.SetNoItemBlockName(bayBase + "BaySeaBase.Macroblock.Gbx");
        atlas.SetItemBlockGroup("BayDocks", [
            [], // No air variants.
            [
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "X_BayDocksBase1\\BayDockBase1A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "X_BayDocksBase1\\BayDockBase1B.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "X_BayDocksBase1\\BayDockBase1C.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "X_BayDocksBase1\\BayDockBase1D.Macroblock.Gbx" }
                ], DirectionOffset = 3 },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "V_BayDocksBase3\\BayDockBase3A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "V_BayDocksBase3\\BayDockBase3B.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "V_BayDocksBase3\\BayDockBase3C.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "V_BayDocksBase3\\BayDockBase3D.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "W_BayDocksBase5\\BayDockBase5A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "W_BayDocksBase5\\BayDockBase5B.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "S_BayDocksBase7\\BayDockBase7A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "S_BayDocksBase7\\BayDockBase7B.Macroblock.Gbx" }
                ], DirectionOffset = 2 },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "P_BayDocksBase15\\BayDockBase15A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "P_BayDocksBase15\\BayDockBase15B.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "P_BayDocksBase15\\BayDockBase15C.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "P_BayDocksBase15\\BayDockBase15D.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Z_BayDocksDeadend\\BayDockDeadendA.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Z_BayDocksDeadend\\BayDockDeadendB.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Z_BayDocksDeadend\\BayDockDeadendC.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Z_BayDocksDeadend\\BayDockDeadendD.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "U_BayDocksDeadend4\\BayDockDeadend4A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "U_BayDocksDeadend4\\BayDockDeadend4B.Macroblock.Gbx" }
                ], DirectionOffset = 2 },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "T_BayDocksDeadend8\\BayDockDeadend8A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "T_BayDocksDeadend8\\BayDockDeadend8B.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "O_BayDocksDeadend12\\BayDockDeadend12A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "O_BayDocksDeadend12\\BayDockDeadend12B.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "O_BayDocksDeadend12\\BayDockDeadend12C.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "O_BayDocksDeadend12\\BayDockDeadend12D.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Y_BayDocksCorner\\BayDockCornerA.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Y_BayDocksCorner\\BayDockCornerB.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Y_BayDocksCorner\\BayDockCornerC.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Y_BayDocksCorner\\BayDockCornerD.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Q_BayDocksCorner8\\BayDockCorner8A.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Q_BayDocksCorner8\\BayDockCorner8B.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Q_BayDocksCorner8\\BayDockCorner8C.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "Q_BayDocksCorner8\\BayDockCorner8D.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "R_BayDocksStraight\\BayDockStraightA.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "R_BayDocksStraight\\BayDockStraightB.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "R_BayDocksStraight\\BayDockStraightC.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "R_BayDocksStraight\\BayDockStraightD.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "N_BayDocksTShaped\\BayDockTShapedA.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "N_BayDocksTShaped\\BayDockTShapedB.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "N_BayDocksTShaped\\BayDockTShapedC.Macroblock.Gbx" },
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "N_BayDocksTShaped\\BayDockTShapedD.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = dock + "M_BayDocksCross\\BayDockCross.Macroblock.Gbx" }
                ] },
                new AtlasEngine.ItemBlockVariant { Subvariants = [
                    new AtlasEngine.ItemBlockSubvariant { MacroblockName = bayBase + "BayHarborBase.Macroblock.Gbx" }
                ] }, // Optional interior filler.
            ]
        ]);

        // These example names are for Lagoon. Water modes require valid block names.
        atlas.SetRemoveWaterBlockMapping(new Dictionary<string, string>
        {
            ["LagoonVoid"] = "Beach",
            ["Land"] = "LagoonGrassVoid",
            ["Beach"] = "LagoonBeachVoid"
        });
        atlas.SetRestoreWaterBlockMapping(
            new List<string> { "LagoonGrassVoid", "LagoonBeachVoid" }, "LagoonVoid");

        atlas.Initialize();
        atlas.SetSelectionChangeEventsEnabled(false);
        atlas.SetSelectionMode(AtlasEngine.SelectionMode.Ground2D);
        atlas.SetFreeformPlacementMode(AtlasEngine.FreeformPlacementMode.ConnectExisting);
        ManialinkText = """
            <manialink version="3">
              <frame pos="91 76">
                                <quad pos="0 0" z-index="0" size="65 85" bgcolor="1112" />
                <label pos="3 -2" z-index="2" size="59 5" text="ATLAS TOOLS" textsize="2" textcolor="fff" />
                <label pos="3 -8" z-index="2" size="59 4" text="Select a mode, then drag in the map" textsize="1" textcolor="ccc" />

                <quad pos="3 -14" z-index="1" size="28 8" bgcolor="4a80" />
                <label id="AtlasGround" pos="4 -15" z-index="2" size="26 6" text="Ground docks" textsize="2" textcolor="fff" scriptevents="1" />
                <quad pos="34 -14" z-index="1" size="28 8" bgcolor="4a80" />
                <label id="AtlasPlane" pos="35 -15" z-index="2" size="26 6" text="2D plane" textsize="2" textcolor="fff" scriptevents="1" />

                <quad pos="3 -24" z-index="1" size="28 8" bgcolor="4a80" />
                <label id="AtlasBox" pos="4 -25" z-index="2" size="26 6" text="3D box" textsize="2" textcolor="fff" scriptevents="1" />
                <quad pos="34 -24" z-index="1" size="28 8" bgcolor="4a80" />
                <label id="AtlasLine" pos="35 -25" z-index="2" size="26 6" text="Line" textsize="2" textcolor="fff" scriptevents="1" />

                <quad pos="3 -34" z-index="1" size="28 8" bgcolor="3670" />
                <label id="AtlasRemoveWater" pos="4 -35" z-index="2" size="26 6" text="Remove water" textsize="2" textcolor="fff" scriptevents="1" />
                <quad pos="34 -34" z-index="1" size="28 8" bgcolor="3670" />
                <label id="AtlasRestoreWater" pos="35 -35" z-index="2" size="26 6" text="Restore water" textsize="2" textcolor="fff" scriptevents="1" />

                <quad pos="3 -46" z-index="1" size="59 8" bgcolor="4a80" />
                <label id="AtlasNone" pos="4 -47" z-index="2" size="57 6" text="No selection" textsize="2" textcolor="fff" scriptevents="1" />

                <quad pos="3 -56" z-index="1" size="28 8" bgcolor="7540" />
                <label id="AtlasUndo" pos="4 -57" z-index="2" size="26 6" text="Undo item" textsize="2" textcolor="fff" scriptevents="1" />
                <quad pos="34 -56" z-index="1" size="28 8" bgcolor="7540" />
                <label id="AtlasRedo" pos="35 -57" z-index="2" size="26 6" text="Redo item" textsize="2" textcolor="fff" scriptevents="1" />

                <quad pos="3 -66" z-index="1" size="59 8" bgcolor="2860" />
                <label id="AtlasConnections" pos="4 -67" z-index="2" size="57 6" text="Connections: Existing" textsize="2" textcolor="fff" scriptevents="1" />

                <label id="AtlasStatus" pos="3 -78" z-index="2" size="59 5" text="Mode: Ground docks" textsize="1" textcolor="ccc" />
              </frame>
              <script><!--
                main() {
                  declare Status <=> (Page.GetFirstChild("AtlasStatus") as CMlLabel);
                                    declare Connections <=> (Page.GetFirstChild("AtlasConnections") as CMlLabel);
                                    declare Boolean ConnectExisting = True;
                  declare Boolean WasPointerOverPanel = False;
                  while (True) {
                    yield;
                    declare Boolean PointerOverPanel = MouseX >= 91. && MouseX <= 156. && MouseY <= 76. && MouseY >= -9.;
                    if (PointerOverPanel != WasPointerOverPanel) {
                      if (PointerOverPanel) SendCustomEvent("AtlasPanelHover", ["1"]);
                      else SendCustomEvent("AtlasPanelHover", ["0"]);
                      WasPointerOverPanel = PointerOverPanel;
                    }
                    foreach (Event in PendingEvents) {
                      if (Event.Type != CMlScriptEvent::Type::MouseClick) continue;
                      if (Event.ControlId == "AtlasGround") Status.Value = "Mode: Ground docks";
                      else if (Event.ControlId == "AtlasPlane") Status.Value = "Mode: 2D plane";
                      else if (Event.ControlId == "AtlasBox") Status.Value = "Mode: 3D box";
                      else if (Event.ControlId == "AtlasLine") Status.Value = "Mode: Line";
                      else if (Event.ControlId == "AtlasRemoveWater") Status.Value = "Mode: Remove water";
                      else if (Event.ControlId == "AtlasRestoreWater") Status.Value = "Mode: Restore water";
                      else if (Event.ControlId == "AtlasNone") Status.Value = "Mode: No selection";
                                            else if (Event.ControlId == "AtlasConnections") {
                                                ConnectExisting = !ConnectExisting;
                                                if (ConnectExisting) Connections.Value = "Connections: Existing";
                                                else Connections.Value = "Connections: Selection only";
                                            }
                      SendCustomEvent("AtlasPanel", [Event.ControlId]);
                    }
                  }
                }
              --></script>
            </manialink>
            """;
        Log("Atlas BayDocks ready: drag on the ground to place docks.");
    }

    public void Loop()
    {
        // Update once per editor frame, then consume event queues so they do not accumulate.
        var panelClicked = false;
        foreach (var evt in PendingEvents)
        {
            if (evt.Type != CMapEditorPluginEvent.EType.LayerCustomEvent || evt.CustomEventData.Count == 0)
                continue;
            if (evt.CustomEventType == "AtlasPanelHover")
            {
                panelHovered = evt.CustomEventData[0] == "1";
                continue;
            }
            if (evt.CustomEventType != "AtlasPanel") continue;

            panelClicked = true;
            var action = evt.CustomEventData[0];
            if (action == "AtlasGround") atlas.SetSelectionMode(AtlasEngine.SelectionMode.Ground2D);
            else if (action == "AtlasPlane") atlas.SetSelectionMode(AtlasEngine.SelectionMode.Plane2D);
            else if (action == "AtlasBox") atlas.SetSelectionMode(AtlasEngine.SelectionMode.Box3D);
            else if (action == "AtlasLine") atlas.SetSelectionMode(AtlasEngine.SelectionMode.Line1D);
            else if (action == "AtlasRemoveWater") atlas.SetSelectionMode(AtlasEngine.SelectionMode.RemoveWater);
            else if (action == "AtlasRestoreWater") atlas.SetSelectionMode(AtlasEngine.SelectionMode.RestoreWater);
            else if (action == "AtlasNone") atlas.SetSelectionMode(AtlasEngine.SelectionMode.None);
            else if (action == "AtlasConnections")
            {
                if (atlas.FreeformMode == AtlasEngine.FreeformPlacementMode.ConnectExisting)
                    atlas.SetFreeformPlacementMode(AtlasEngine.FreeformPlacementMode.SelectionOnly);
                else atlas.SetFreeformPlacementMode(AtlasEngine.FreeformPlacementMode.ConnectExisting);
            }
            else if (action == "AtlasUndo")
            {
                if (!atlas.CanUndoAtlasEdit) Log("No item edit to undo.");
                else if (!atlas.UndoAtlasEdit()) Log("Atlas undo failed.");
            }
            else if (action == "AtlasRedo")
            {
                if (!atlas.CanRedoAtlasEdit) Log("No item edit to redo.");
                else if (!atlas.RedoAtlasEdit()) Log("Atlas redo failed.");
            }
        }

        atlas.SetSelectionInputEnabled(!panelHovered && !panelClicked);
        atlas.Update();

        foreach (var change in atlas.SelectionConfirmed)
        {
            if (atlas.Mode == AtlasEngine.SelectionMode.Ground2D)
            {
                if (!atlas.PlaceFreeform1x1(change.Coords, "BayDocks"))
                    Log("BayDocks placement failed.");
            }
            else if (atlas.Mode != AtlasEngine.SelectionMode.RemoveWater &&
                     atlas.Mode != AtlasEngine.SelectionMode.RestoreWater)
                atlas.SetCurrentSelection(change.Coords, true);
        }

        foreach (var removal in atlas.ItemRemovals)
            Log($"Item removed at {removal.Position}");
    }
}
