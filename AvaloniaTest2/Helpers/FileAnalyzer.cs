using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTest2.Helpers;

public class FileAnalyzer
{
    /// <summary>
    /// Obtiene los archivos más grandes de un directorio (recursivo).
    /// </summary>
    /// <param name="rootPath">Carpeta raíz donde buscar</param>
    /// <param name="topCount">Número de archivos más grandes a devolver</param>
    /// <returns>Lista de tuplas (ruta, tamaño en bytes)</returns>
    public static List<(string Path, long Size)> GetLargestFiles(string rootPath, int topCount = 10)
    {
        var files = new List<(string Path, long Size)>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(rootPath))
            {
                try
                {
                    var info = new FileInfo(file);
                    files.Add((info.FullName, info.Length));
                }
                catch { /* Ignorar ficheros inaccesibles */ }
            }

            foreach (var dir in Directory.EnumerateDirectories(rootPath))
            {
                try
                {
                    files.AddRange(GetLargestFiles(dir, topCount)); // recursión
                }
                catch { /* Ignorar carpetas inaccesibles */ }
            }
        }
        catch { /* Ignorar rootPath inaccesible */ }

        // Ordenar por tamaño descendente y devolver solo los topCount
        return files.OrderByDescending(f => f.Size)
            .Take(topCount)
            .ToList();
    }
    
    public static async Task GetLargestFilesIncremental(
        string root, int count, Action<FileInfo> onFound, Action<string>? onError = null)
    {
        var topFiles = new List<FileInfo>();
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        long SafeLength(FileInfo fi)
        {
            try { return fi?.Length ?? 0; }
            catch { return 0; }
        }

        void AddFile(FileInfo f)
        {
            lock (topFiles)
            {
                topFiles.Add(f);
                topFiles.Sort((a, b) => SafeLength(b).CompareTo(SafeLength(a)));
                if (topFiles.Count > count)
                    topFiles.RemoveAt(topFiles.Count - 1);
            }
            onFound?.Invoke(f);
        }

        void Scan(string dir)
        {
            try
            {
                // evita bucles de enlaces simbólicos
                if (!visitedDirs.Add(dir))
                    return;

                foreach (var file in Directory.GetFiles(dir))
                {
                    try { AddFile(new FileInfo(file)); }
                    catch (Exception ex) { onError?.Invoke($"Archivo {file}: {ex.Message}"); }
                }

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    // ignora pseudofs comunes
                    if (subDir.StartsWith("/proc") || subDir.StartsWith("/sys") ||
                        subDir.StartsWith("/dev")  || subDir.StartsWith("/run"))
                        continue;

                    Scan(subDir);
                }
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Dir {dir}: {ex.Message}");
            }
        }

        await Task.Run(() =>
        {
            try { Scan(root); }
            catch (Exception ex) { onError?.Invoke($"Root {root}: {ex.Message}"); }
        });
    }

    
    public static async IAsyncEnumerable<FileInfo> GetLargestFilesAsyncOLD(string path, int count)
    {
        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Length);

        int i = 0;
        foreach (var f in files)
        {
            if (i++ >= count) yield break;
            yield return f;
            await Task.Delay(1); // para que ceda el control al UI
        }
    }

}