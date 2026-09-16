namespace StickApp.Models;

public class NoteImage
{
    public string FileName { get; set; } = "";
    public double Size { get; set; } = 60;
}

public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 220;
    public string Color { get; set; } = "#FFF6A6";
    public string Text { get; set; } = "";
    public List<NoteImage> Images { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}
