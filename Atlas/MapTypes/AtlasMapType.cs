using Atlas.Libs;

namespace Atlas.MapTypes;

public class AtlasMapType : CMapType, IContext
{
    private readonly AtlasEngine atlas = new();
    
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
        atlas.SetFreeformPlacementMode(AtlasEngine.FreeformPlacementMode.SelectionOnly);
        Log("Atlas BayDocks ready: drag on the ground to place docks.");
    }

    public void Loop()
    {
        // Update once per editor frame, then consume event queues so they do not accumulate.
        atlas.Update();

        foreach (var change in atlas.SelectionConfirmed)
        {
            if (!atlas.PlaceFreeform1x1(change.Coords, "BayDocks"))
                Log("BayDocks placement failed.");
        }

        foreach (var removal in atlas.ItemRemovals)
            Log($"Item removed at {removal.Position}");
    }
}
