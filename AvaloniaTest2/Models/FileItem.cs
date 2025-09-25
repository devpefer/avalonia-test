namespace AvaloniaTest2.Models;

public class FileItem
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Name => System.IO.Path.GetFileName(Path);
}
