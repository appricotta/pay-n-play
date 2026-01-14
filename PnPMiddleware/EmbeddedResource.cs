using System.Reflection;

namespace PnPMiddleware;

public class EmbeddedResource
{
    private readonly string _name;
    private readonly string? _assemblyName;

    public EmbeddedResource(string name, string? assemblyName = null)
    {
        _name = name;
        _assemblyName = assemblyName;
    }

    public string GetText()
    {
        return GetText(_name, _assemblyName);
    }

    public Stream GetStream()
    {
        return GetStream(_name, _assemblyName);
    }

    public static string GetText(string name, string? assemblyName = null)
    {
        using Stream stream = GetStream(name, assemblyName);
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Stream GetStream(string name, string? assemblyName = null)
    {
        var source = GetResourceSource(name, assemblyName);
        var stream = source.Assembly.GetManifestResourceStream(source.ResourcePath);
        if (stream == null)
        {
            throw new FileNotFoundException("Embedded resource is not found.");
        }
        return stream;
    }

    private static ResourceSource GetResourceSource(string name, string? assemblyName)
    {
        var assembly = assemblyName == null
            ? Assembly.GetExecutingAssembly()
            : AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == assemblyName);
        var resourcePath = assembly?.GetManifestResourceNames().Single(str => str.EndsWith(name));
        if (assembly == null || resourcePath == null)
        {
            throw new FileNotFoundException();
        }

        return new ResourceSource { Assembly = assembly, ResourcePath = resourcePath };
    }

    private struct ResourceSource
    {
        public Assembly Assembly { get; set; }
        public string ResourcePath { get; set; }
    }
}