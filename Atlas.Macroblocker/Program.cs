using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Script;

Gbx.LZO = new GBX.NET.LZO.Lzo();

if (args.Length < 2)
{
    Console.WriteLine("Usage: Atlas.Macroblocker <item-folder> <macroblock-folder> [prepend-path]");
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey(true);
    return;
}

var itemFolder = args[0];
var macroblockFolder = args[1];
var prependPath = args.Length > 2 ? args[2] : "";

foreach (var filePath in Directory.GetFiles(itemFolder, "*.Item.Gbx", SearchOption.AllDirectories))
{
    var item = Gbx.ParseNode<CGameItemModel>(filePath);

    var relativeItemPath = Path.GetRelativePath(itemFolder, filePath);

    var macroblock = new CGameCtnMacroBlockInfo
    {
        Ident = item.Ident,
        Name = item.Name,
        Icon = item.Icon,
        ProdState = item.ProdState,
        Flags = item.Flags,
        IconUseAutoRender = item.IconUseAutoRender,
        
        ObjectSpawns = [
            new CGameCtnMacroBlockInfo.ObjectSpawn
            {
                ItemModel = new Ident(Path.Combine(prependPath, relativeItemPath).Replace("/", "\\"), item.Ident.Collection, item.Ident.Author),
                AbsolutePositionInMap = new Vec3(
                    -(item.DefaultPlacement?.GridSnapHOffset ?? 0), 
                    item.DefaultPlacement?.GridSnapVOffset ?? 0, 
                    -(item.DefaultPlacement?.GridSnapHOffset ?? 0)),
                U03 = 1,
                Version = 9
            }
        ],
        ScriptMetadata = new CScriptTraitsMetadata()
    };
    macroblock.CreateChunk<CGameCtnCollector.HeaderChunk2E001003>().Version = 8;
    macroblock.CreateChunk<CGameCtnCollector.HeaderChunk2E001004>().U01 = 1;
    macroblock.CreateChunk<CGameCtnCollector.HeaderChunk2E001006>();
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E001009>();
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E00100B>();
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E00100C>();
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E00100D>();
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E00100E>();
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E001010>().Version = 2;
    macroblock.CreateChunk<CGameCtnCollector.Chunk2E001011>().Version = 1;
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D000>();
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D001>();
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D002>();
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D006>().U01 = 2;
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D008>().U02 = 2;
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D00B>();
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D00C>().Version = 2;
    macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D00E>().Version = 2;
    var chunk00F = macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D00F>();
    chunk00F.U01 = new Int3(-1, 0, 0);
    chunk00F.U02 = new Int3(3, 1, 3);
    var chunk011 = macroblock.CreateChunk<CGameCtnMacroBlockInfo.Chunk0310D011>();
    chunk011.U02 = new Int3(-1, 0, 0);
    chunk011.U03 = new Int3(3, 1, 3);
    macroblock.ScriptMetadata.CreateChunk<CScriptTraitsMetadata.Chunk11002000>().Version = 5;

    var macroblockFilePath = filePath.Replace(itemFolder, macroblockFolder).Replace(".Item.Gbx", ".Macroblock.Gbx", StringComparison.OrdinalIgnoreCase);
    Directory.CreateDirectory(Path.GetDirectoryName(macroblockFilePath)!);

    macroblock.Save(macroblockFilePath);
}
