using System.Text;
using K4os.Compression.LZ4;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

// Map config (must match server Options)
const int MapWidth = 32;
const int MapHeight = 26;

// Tileset GUIDs from the database
var groundTilesetId = Guid.Parse("9B784A67-8C34-44BF-B85C-C77795A616FE"); // Ground.png

// Build tile layers as the engine expects: Dictionary<string, Tile[,]>
// Each Tile has: TilesetId (Guid), X (int), Y (int), Autotile (byte)
var layers = new Dictionary<string, object[,]>();
string[] layerNames = ["Ground", "Mask 1", "Mask 2", "Fringe 1", "Fringe 2"];

foreach (var layerName in layerNames)
{
    var tiles = new object[MapWidth, MapHeight];
    for (int x = 0; x < MapWidth; x++)
    {
        for (int y = 0; y < MapHeight; y++)
        {
            if (layerName == "Ground")
            {
                // Fill with the top-left tile from Ground.png (tile position 0,0 in the tileset)
                tiles[x, y] = new { TilesetId = groundTilesetId, X = 0, Y = 0, Autotile = (byte)0 };
            }
            else
            {
                // Empty tile
                tiles[x, y] = new { TilesetId = Guid.Empty, X = 0, Y = 0, Autotile = (byte)0 };
            }
        }
    }
    layers[layerName] = tiles;
}

// Serialize and LZ4 compress
var tileJson = JsonConvert.SerializeObject(layers, Formatting.None);
var tileCompressed = LZ4Pickler.Pickle(Encoding.UTF8.GetBytes(tileJson), LZ4Level.L12_MAX);
Console.WriteLine($"TileData: {tileJson.Length} chars JSON -> {tileCompressed.Length} bytes compressed");

// Empty attributes
var attrs = new object?[MapWidth, MapHeight];
var attrsJson = JsonConvert.SerializeObject(attrs, Formatting.None);
var attrsCompressed = LZ4Pickler.Pickle(Encoding.UTF8.GetBytes(attrsJson), LZ4Level.L12_MAX);

// Update the database
var mapId = "2D9A9B7A-0373-4708-B535-5572024B8E94";
var dbPath = Path.GetFullPath(args.Length > 0 ? args[0] : "Intersect.Server/resources/gamedata.db");

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 1;
}

Console.WriteLine($"Updating map {mapId} in {dbPath}");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = "UPDATE Maps SET TileData = @tileData, Attributes = @attrs, Revision = Revision + 1 WHERE Id = @id";
cmd.Parameters.AddWithValue("@tileData", tileCompressed);
cmd.Parameters.AddWithValue("@attrs", attrsCompressed);
cmd.Parameters.AddWithValue("@id", mapId);

var rows = cmd.ExecuteNonQuery();
Console.WriteLine($"Updated {rows} row(s)");

// Verify
using var verify = conn.CreateCommand();
verify.CommandText = "SELECT length(TileData), Revision FROM Maps WHERE Id = @id";
verify.Parameters.AddWithValue("@id", mapId);
using var reader = verify.ExecuteReader();
if (reader.Read())
{
    Console.WriteLine($"TileData: {reader.GetInt64(0)} bytes, Revision: {reader.GetInt32(1)}");
}

Console.WriteLine("Done! Restart the server to load the new map data.");
return 0;
