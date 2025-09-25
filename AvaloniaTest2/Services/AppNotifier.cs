using System;

namespace AvaloniaTest2.Services;

public class AppNotifier
{
    public string Title { get; set; }
    public string Message { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);
}