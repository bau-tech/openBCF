using System.Xml.Linq;
using BCFree.Core.Model;

namespace BCFree.Core.Serialization;

public static class BcfProjectSerializer
{
    public static BcfProject Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("project.bcf is empty.");

        var project = root.Element("Project")
            ?? throw new InvalidDataException("project.bcf is missing the Project element.");

        var projectId = project.Attribute("ProjectId")?.Value
            ?? throw new InvalidDataException("project.bcf Project element is missing ProjectId.");

        return new BcfProject(projectId, project.Element("Name")?.Value);
    }

    public static void Write(BcfProject project, Stream stream)
    {
        var projectElement = new XElement("Project", new XAttribute("ProjectId", project.ProjectId));
        if (project.Name is not null)
            projectElement.Add(new XElement("Name", project.Name));

        var root = new XElement("ProjectExtension", projectElement);
        new XDocument(root).Save(stream);
    }
}
