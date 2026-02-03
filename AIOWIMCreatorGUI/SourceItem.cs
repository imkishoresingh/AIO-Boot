using System.Collections.Generic;

public class SourceItem
{
    public string Path { get; set; } = "";
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Error { get; set; } = "";
}

public class ImageInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Selected { get; set; }
}