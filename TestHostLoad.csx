#r "Newtonsoft.Json.dll"
using Newtonsoft.Json;
using System.IO;

// Simulate the C# model minimally
public enum HostMessageDirection { Send, Receive }
public enum HostFieldType { ASCII, Binary, Boolean, U1, U2, U4, U8, I1, I2, I4, I8, F4, F8, Object, ArrayList, String, Integer }
public class HostFieldTemplate {
    public string Name { get; set; } = "";
    public HostFieldType Type { get; set; }
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Required { get; set; }
    public List<HostFieldTemplate> Children { get; set; } = new();
}
public class HostMessageTemplate {
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public HostMessageDirection Direction { get; set; }
    public List<HostFieldTemplate> Fields { get; set; } = new();
}
public class HostMessageTemplateCollection {
    public List<HostMessageTemplate> Templates { get; set; } = new();
}

var json = File.ReadAllText(@"D:\Python\Object\EAPSimulator\host_message_templates.json");
var coll = JsonConvert.DeserializeObject<HostMessageTemplateCollection>(json);
Console.WriteLine($"Loaded: {coll.Templates.Count} templates");
foreach (var t in coll.Templates.Take(3))
{
    int nc = t.Fields.Count;
    int nested = t.Fields.Count(f => f.Children.Count > 0);
    int ncChildren = t.Fields.Where(f => f.Children.Count > 0).Sum(f => f.Children.Count);
    Console.WriteLine($"  {t.Name} | {t.Direction} | {nc}f ({nested}n/{ncChildren}c)");
}
