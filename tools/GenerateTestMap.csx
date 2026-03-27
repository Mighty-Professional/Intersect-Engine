#!/usr/bin/env dotnet-script
// Generate test map tile data for the Intersect Engine web client
// Usage: dotnet script tools/GenerateTestMap.csx

#r "nuget: K4os.Compression.LZ4, 1.3.8"
#r "nuget: Newtonsoft.Json, 13.0.3"
#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using K4os.Compression.LZ4;
using Newtonsoft.Json;
using Microsoft.Data.Sqlite;
using System.Text;

// Map config (must match server Options)
const int MapWidth = 32;
const int MapHeight = 26;

// Ground.png tileset GUID
var groundTilesetId = Guid.Parse("9B784A67-8C34-44BF-B85C-C77795A616FE");
// Overworld.png for variety
var overworldTilesetId = Guid.Parse("43929737-31DA-4D54-88FC-7831EFED4B51");

// Tile struct matching the engine format
var layers = new Dictionary<string, object[,]>();

string[] layerNames = { "Ground", "Mask 1", "Mask 2", "Fringe 1", "Fringe 2" };

foreach (var layerName in layerNames)
{
    var tiles = new object[MapWidth, MapHeight];
    for (int x = 0; x < MapWidth; x++)
    {
        for (int y = 0; y < MapHeight; y++)
        {
            if (layerName == "Ground")
            {
                // Fill ground with grass tiles from Ground.png
                // Tile X=0, Y=0 is the top-left tile in the tileset (usually grass)
                tiles[x, y] = new
                {
                    TilesetId = groundTilesetId,
                    X = 0,
                    Y = 0,
                    Autotile = (byte)0
                };
            }
            else
            {
                // Empty tile for other layers
                tiles[x, y] = new
                {
                    TilesetId = Guid.Empty,
                    X = 0,
                    Y = 0,
                    Autotile = (byte)0
                };
            }
        }
    }
    layers[layerName] = tiles;
}

// Serialize to JSON
var json = JsonConvert.SerializeObject(layers, Formatting.None);
Console.WriteLine($"Tile JSON length: {json.Length} chars");
Console.WriteLine($"First 200 chars: {json.Substring(0, Math.Min(200, json.Length))}");

// LZ4 compress
var jsonBytes = Encoding.UTF8.GetBytes(json);
var compressed = LZ4Pickler.Pickle(jsonBytes, LZ4Level.L12_MAX);
Console.WriteLine($"Compressed: {compressed.Length} bytes");

// Also generate empty attributes
var emptyAttrs = new object[MapWidth, MapHeight];
var attrsJson = JsonConvert.SerializeObject(emptyAttrs, Formatting.None);
var attrsCompressed = LZ4Pickler.Pickle(Encoding.UTF8.GetBytes(attrsJson), LZ4Level.L12_MAX);

// Write to database
var mapId = "2D9A9B7A-0373-4708-B535-5572024B8E94";
var dbPath = "Intersect.Client.Web/wwwroot/resources/gamedata.db";

// Also update the server's copy
var serverDbPath = "Intersect.Server/resources/gamedata.db";

foreach (var path in new[] { dbPath, serverDbPath })
{
    var fullPath = Path.GetFullPath(path);
    if (!File.Exists(fullPath))
    {
        Console.WriteLine($"SKIP: {fullPath} not found");
        continue;
    }

    using var conn = new SqliteConnection($"Data Source={fullPath}");
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "UPDATE Maps SET TileData = @tileData, Attributes = @attrs, Revision = Revision + 1 WHERE Id = @id";
    cmd.Parameters.AddWithValue("@tileData", compressed);
    cmd.Parameters.AddWithValue("@attrs", attrsCompressed);
    cmd.Parameters.AddWithValue("@id", mapId);

    var rows = cmd.ExecuteNonQuery();
    Console.WriteLine($"Updated {rows} row(s) in {path}");

    // Verify
    using var verify = conn.CreateCommand();
    verify.CommandText = "SELECT length(TileData), Revision FROM Maps WHERE Id = @id";
    verify.Parameters.AddWithValue("@id", mapId);
    using var reader = verify.ExecuteReader();
    if (reader.Read())
    {
        Console.WriteLine($"  TileData: {reader.GetInt64(0)} bytes, Revision: {reader.GetInt32(1)}");
    }
}

Console.WriteLine("Done! Restart the server to load the new map data.");
