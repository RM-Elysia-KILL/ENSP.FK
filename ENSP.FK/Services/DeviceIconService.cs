using System.Collections.Concurrent;
using System.IO;
using System.Xml.Linq;

namespace ENSP.ZD.Services;

/// <summary>
/// Resolves eNSP device model names to topology icon paths by parsing
/// eNSP's items.xml. Falls back to type-based default icons.
/// </summary>
public class DeviceIconService
{
    private readonly string _enspRoot;
    private readonly string _templatesDir;
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DeviceIconService(string enspRoot)
    {
        _enspRoot = enspRoot;
        _templatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ENSP.ZD", "templates");
    }

    /// <summary>
    /// Directory where user-captured template images are stored (one per model, e.g. AR1220.png).
    /// </summary>
    public string TemplatesDir => _templatesDir;

    /// <summary>
    /// Resolve a toolbar button template (e.g. "start_all" → templates/start_all.png).
    /// </summary>
    public string? ResolveToolbarIconPath(string templateName)
    {
        var path = Path.Combine(_templatesDir, $"{templateName}.png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Remove cached icon path for a model so the next lookup picks up a newly captured template.
    /// </summary>
    public void InvalidateCache(string model)
    {
        _cache.TryRemove(model, out _);
    }

    /// <summary>
    /// Clear all cached icon paths and template bitmaps.
    /// </summary>
    public void InvalidateAll()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Returns the full path to the topology PNG icon for a device model.
    /// Example: model="AR1220" → "C:\Program Files\Huawei\eNSP\res\DeviceType\iRouter.png"
    /// </summary>
    public string? ResolveIconPath(string model)
    {
        if (string.IsNullOrEmpty(model))
            return null;

        return _cache.GetOrAdd(model, m =>
        {
            // 1. Custom user-captured template
            var customPath = Path.Combine(_templatesDir, $"{m}.png");
            if (File.Exists(customPath))
                return customPath;

            // 2. eNSP items.xml icon
            var pathFromXml = LookupIconFromItemsXml(m);
            if (pathFromXml != null && File.Exists(pathFromXml))
                return pathFromXml;

            // Fallback: try res/Device/{model}_all.png
            var fallback = Path.Combine(_enspRoot, "res", "Device", $"{m}_all.png");
            if (File.Exists(fallback))
                return fallback;

            // Fallback: use type-based default
            return GetDefaultIcon(m);
        });
    }

    private string? LookupIconFromItemsXml(string model)
    {
        try
        {
            var itemsPath = Path.Combine(_enspRoot, "res", "items.xml");
            if (!File.Exists(itemsPath))
                return null;

            var doc = XDocument.Load(itemsPath);
            if (doc.Root == null) return null;

            // Search: <model name="AR1220"> → <topoIcon path="..."/>
            foreach (var item in doc.Root.Elements("Item"))
            {
                foreach (var modelEl in item.Elements("model"))
                {
                    if (string.Equals(modelEl.Attribute("name")?.Value, model, StringComparison.OrdinalIgnoreCase))
                    {
                        var iconEl = modelEl.Element("topoIcon");
                        var path = iconEl?.Attribute("path")?.Value;
                        if (path != null)
                        {
                            var fullPath = Path.Combine(_enspRoot, path.Replace('/', '\\'));
                            if (File.Exists(fullPath))
                                return fullPath;
                        }
                        // If icon not found, break to use type default
                        break;
                    }
                }
            }
        }
        catch
        {
            // items.xml may be locked or corrupt — fall through to defaults
        }

        return null;
    }

    private string? GetDefaultIcon(string model)
    {
        var t = model.ToLower();
        string iconFile;
        if (t.Contains("ar") || t.Contains("router") || t.Contains("ne") || t.Contains("cx"))
            iconFile = "iRouter.png";
        else if (t.Contains("lsw") || t.Contains("s57") || t.Contains("s37") || t.Contains("switch") || t.Contains("ce"))
            iconFile = "iCorelsw.png";
        else if (t.Contains("fw") || t.Contains("usg") || t.Contains("firewall"))
            iconFile = "iFW.png";
        else if (t.StartsWith("pc") || t.Contains("client") || t.Contains("pc-"))
            iconFile = "iPC.png";
        else if (t.Contains("server"))
            iconFile = "iServer.png";
        else
            iconFile = "iRouter.png"; // default

        var path = Path.Combine(_enspRoot, "res", "DeviceType", iconFile);
        return File.Exists(path) ? path : null;
    }
}
