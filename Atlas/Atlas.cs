using ManiaScriptSharp;

namespace Atlas;

public class Atlas : ILib<CMapEditorPlugin>
{
    public required CMapEditorPlugin Context { get; init; }

    private bool previousLeftMouse;
    private bool isDragging;
    private Int3 dragStartCoord;

    public string Greet()
    {
        return "Hello from Atlas!";
    }

    /// <summary>
    /// Places terrain and void blocks over the ground covered by the given selection.
    /// </summary>
    public void ConfirmGroundSelection(Int3 startCoord, Int3 endCoord)
    {
        var voidBlockModel = Context.GetBlockModelFromName("LagoonGrassVoid");
        var edgeVoidBlockModel = Context.GetBlockModelFromName("LagoonBeachVoid");

        if (voidBlockModel == null || edgeVoidBlockModel == null)
        {
            return;
        }

        Context.PlaceTerrainBlocks(Context.TerrainBlockModels[0], startCoord, endCoord);

        var minX = startCoord.X;
        var maxX = endCoord.X;
        var minZ = startCoord.Z;
        var maxZ = endCoord.Z;
        if (minX > maxX)
        {
            var swapX = minX;
            minX = maxX;
            maxX = swapX;
        }
        if (minZ > maxZ)
        {
            var swapZ = minZ;
            minZ = maxZ;
            maxZ = swapZ;
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                var coord = new Int3(x, Context.GetGroundHeight(x, z), z);
                var block = Context.GetBlock(coord);

                if (block == null)
                {
                    continue;
                }
                else if (block.BlockModel.Name == "Beach")
                {
                    Context.PlaceBlock(edgeVoidBlockModel, coord, CMapEditorPlugin.CardinalDirections.North);
                }
                else
                {
                    Context.PlaceBlock(voidBlockModel, coord, CMapEditorPlugin.CardinalDirections.North);
                }
            }
        }
    }

    /// <summary>
    /// Handles the ground drag selection for one frame: draws the custom selection
    /// overlay, tracks an in-progress drag and confirms the selection on mouse release.
    /// All drag state is tracked in global variables between calls, so this can simply
    /// be called once per frame.
    /// </summary>
    public void UpdateGroundSelection()
    {
        Context.Cursor.Brightness = 0f;

        if (!isDragging)
        {
            Context.CustomSelectionRGB = new Vec3(0f, 0.65f, 1f);
            Context.CustomSelectionCoords.Clear();
            Context.CustomSelectionCoords.Add(Context.GetMouseCoordOnGround());
        }

        if (Context.Input.MouseLeftButton && !previousLeftMouse)
        {
            dragStartCoord = Context.GetMouseCoordOnGround();
            isDragging = true;
        }

        if (isDragging && Context.Input.MouseLeftButton)
        {
            var dragEndCoord = Context.GetMouseCoordOnGround();
            var minX = dragStartCoord.X;
            var maxX = dragEndCoord.X;
            var minZ = dragStartCoord.Z;
            var maxZ = dragEndCoord.Z;
            if (minX > maxX)
            {
                (maxX, minX) = (minX, maxX);
            }
            if (minZ > maxZ)
            {
                (maxZ, minZ) = (minZ, maxZ);
            }

            Context.CustomSelectionRGB = new Vec3(0f, 0.65f, 1f);
            Context.CustomSelectionCoords.Clear();
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    Context.CustomSelectionCoords.Add(new Int3(x, Context.GetGroundHeight(x, z), z));
                }
            }
        }

        if (!Context.Input.MouseLeftButton && previousLeftMouse && isDragging)
        {
            ConfirmGroundSelection(dragStartCoord, Context.GetMouseCoordOnGround());
            isDragging = false;
            Context.CustomSelectionCoords.Clear();
        }

        previousLeftMouse = Context.Input.MouseLeftButton;
    }
}
