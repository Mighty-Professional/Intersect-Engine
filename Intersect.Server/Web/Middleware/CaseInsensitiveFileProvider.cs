using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Intersect.Server.Web.Middleware;

/// <summary>
/// Wraps a PhysicalFileProvider with case-insensitive file lookups for Linux.
/// On Linux, file paths are case-sensitive, but game asset names may not match
/// the exact casing on disk (e.g., server sends "overworld.png" but file is "Overworld.png").
/// </summary>
public class CaseInsensitiveFileProvider : IFileProvider
{
    private readonly PhysicalFileProvider _inner;
    private readonly string _root;

    public CaseInsensitiveFileProvider(string root)
    {
        _root = root;
        _inner = new PhysicalFileProvider(root, Microsoft.Extensions.FileProviders.Physical.ExclusionFilters.Sensitive);
    }

    private int _logCount;

    public IFileInfo GetFileInfo(string subpath)
    {
        // Try exact match first (fast path)
        var result = _inner.GetFileInfo(subpath);
        if (result.Exists)
        {
            return result;
        }

        // Case-insensitive fallback: search the directory for a matching filename
        var resolvedPath = ResolveCaseInsensitive(subpath);
        if (resolvedPath != null)
        {
            var resolved = _inner.GetFileInfo(resolvedPath);
            if (resolved.Exists && _logCount < 100)
            {
                _logCount++;
                Console.WriteLine($"[FILESERVE] Case-insensitive resolve: '{subpath}' -> '{resolvedPath}' ({resolved.Length} bytes)");
            }
            return resolved;
        }

        if (_logCount < 100)
        {
            _logCount++;
            Console.WriteLine($"[FILESERVE] NOT FOUND (even case-insensitive): '{subpath}'");
        }
        return result; // Return the not-found result
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        return _inner.GetDirectoryContents(subpath);
    }

    public IChangeToken Watch(string filter)
    {
        return _inner.Watch(filter);
    }

    private string? ResolveCaseInsensitive(string subpath)
    {
        // Normalize path separators and split into segments
        var normalized = subpath.TrimStart('/').Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var currentDir = _root;
        var resolvedParts = new List<string>();

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var isLast = i == segments.Length - 1;

            try
            {
                var entries = isLast
                    ? Directory.GetFiles(currentDir)
                    : Directory.GetDirectories(currentDir);

                var match = entries
                    .Select(Path.GetFileName)
                    .FirstOrDefault(name => string.Equals(name, segment, StringComparison.OrdinalIgnoreCase));

                if (match == null) return null;

                resolvedParts.Add(match);
                currentDir = Path.Combine(currentDir, match);
            }
            catch
            {
                return null;
            }
        }

        return "/" + string.Join("/", resolvedParts);
    }
}
