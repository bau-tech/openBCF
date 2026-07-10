using System.Xml.Linq;
using OpenBcf.Core.Model;

namespace OpenBcf.Core.Serialization;

public static class BcfVersionSerializer
{
    public static BcfVersion Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("version.bcf is empty.");

        var versionId = root.Attribute("VersionId")?.Value
            ?? throw new InvalidDataException("version.bcf is missing VersionId.");

        return new BcfVersion(versionId, root.Element("DetailedVersion")?.Value);
    }

    public static void Write(BcfVersion version, Stream stream)
    {
        var root = new XElement("Version", new XAttribute("VersionId", version.VersionId));
        if (version.DetailedVersion is not null)
            root.Add(new XElement("DetailedVersion", version.DetailedVersion));

        new XDocument(root).Save(stream);
    }
}
