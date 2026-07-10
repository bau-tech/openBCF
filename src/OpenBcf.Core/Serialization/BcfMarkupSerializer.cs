using System.Globalization;
using System.Xml.Linq;
using OpenBcf.Core.Model;

namespace OpenBcf.Core.Serialization;

public static class BcfMarkupSerializer
{
    public static BcfMarkup Read(Stream stream)
    {
        var root = XDocument.Load(stream).Root
            ?? throw new InvalidDataException("markup.bcf is empty.");

        var topicElement = root.Element("Topic")
            ?? throw new InvalidDataException("markup.bcf is missing the Topic element.");

        return new BcfMarkup(ReadTopic(topicElement))
        {
            Files = root.Element("Header")?.Elements("File").Select(ReadFile).ToList() ?? new List<BcfFileReference>(),
            Comments = root.Elements("Comment").Select(ReadComment).ToList(),
            Viewpoints = root.Elements("Viewpoints").Select(ReadViewpointReference).ToList(),
        };
    }

    public static void Write(BcfMarkup markup, Stream stream)
    {
        var root = new XElement("Markup");

        if (markup.Files.Count > 0)
            root.Add(new XElement("Header", markup.Files.Select(WriteFile)));

        root.Add(WriteTopic(markup.Topic));
        root.Add(markup.Comments.Select(WriteComment));
        root.Add(markup.Viewpoints.Select(WriteViewpointReference));

        new XDocument(root).Save(stream);
    }

    private static BcfFileReference ReadFile(XElement element) => new(
        IfcProject: element.Attribute("IfcProject")?.Value,
        IfcSpatialStructureElement: element.Attribute("IfcSpatialStructureElement")?.Value,
        Filename: element.Element("Filename")?.Value,
        Date: ParseDate(element.Element("Date")?.Value),
        Reference: element.Element("Reference")?.Value,
        IsExternal: ParseBool(element.Attribute("isExternal")?.Value));

    private static XElement WriteFile(BcfFileReference file)
    {
        var element = new XElement("File", new XAttribute("isExternal", file.IsExternal));

        if (file.IfcProject is not null) element.Add(new XAttribute("IfcProject", file.IfcProject));
        if (file.IfcSpatialStructureElement is not null) element.Add(new XAttribute("IfcSpatialStructureElement", file.IfcSpatialStructureElement));
        if (file.Filename is not null) element.Add(new XElement("Filename", file.Filename));
        if (file.Date is not null) element.Add(new XElement("Date", FormatDate(file.Date.Value)));
        if (file.Reference is not null) element.Add(new XElement("Reference", file.Reference));

        return element;
    }

    private static BcfTopic ReadTopic(XElement element)
    {
        var guid = Guid.Parse(element.Attribute("Guid")?.Value
            ?? throw new InvalidDataException("Topic element is missing Guid."));

        var title = element.Element("Title")?.Value
            ?? throw new InvalidDataException("Topic element is missing Title.");

        var bimSnippet = element.Element("BimSnippet") is { } snippet
            ? new BcfBimSnippet(
                SnippetType: snippet.Attribute("SnippetType")?.Value ?? string.Empty,
                Reference: snippet.Element("Reference")?.Value,
                ReferenceSchema: snippet.Element("ReferenceSchema")?.Value,
                IsExternal: ParseBool(snippet.Attribute("isExternal")?.Value))
            : null;

        return new BcfTopic(
            Guid: guid,
            Title: title,
            TopicType: element.Attribute("TopicType")?.Value,
            TopicStatus: element.Attribute("TopicStatus")?.Value,
            Priority: element.Element("Priority")?.Value,
            Index: ParseInt(element.Element("Index")?.Value),
            CreationDate: ParseDate(element.Element("CreationDate")?.Value),
            CreationAuthor: element.Element("CreationAuthor")?.Value,
            ModifiedDate: ParseDate(element.Element("ModifiedDate")?.Value),
            ModifiedAuthor: element.Element("ModifiedAuthor")?.Value,
            DueDate: ParseDate(element.Element("DueDate")?.Value),
            AssignedTo: element.Element("AssignedTo")?.Value,
            Stage: element.Element("Stage")?.Value,
            Description: element.Element("Description")?.Value,
            BimSnippet: bimSnippet)
        {
            Labels = element.Elements("Labels").Select(e => e.Value).ToList(),
            ReferenceLinks = element.Elements("ReferenceLink").Select(e => e.Value).ToList(),
            DocumentReferences = element.Elements("DocumentReference").Select(ReadDocumentReference).ToList(),
            RelatedTopics = element.Elements("RelatedTopic").Select(e => Guid.Parse(e.Attribute("Guid")!.Value)).ToList(),
        };
    }

    private static XElement WriteTopic(BcfTopic topic)
    {
        var element = new XElement("Topic", new XAttribute("Guid", topic.Guid));

        if (topic.TopicType is not null) element.Add(new XAttribute("TopicType", topic.TopicType));
        if (topic.TopicStatus is not null) element.Add(new XAttribute("TopicStatus", topic.TopicStatus));

        element.Add(topic.ReferenceLinks.Select(link => new XElement("ReferenceLink", link)));
        element.Add(new XElement("Title", topic.Title));
        if (topic.Priority is not null) element.Add(new XElement("Priority", topic.Priority));
        if (topic.Index is not null) element.Add(new XElement("Index", topic.Index));
        element.Add(topic.Labels.Select(label => new XElement("Labels", label)));
        if (topic.CreationDate is not null) element.Add(new XElement("CreationDate", FormatDate(topic.CreationDate.Value)));
        if (topic.CreationAuthor is not null) element.Add(new XElement("CreationAuthor", topic.CreationAuthor));
        if (topic.ModifiedDate is not null) element.Add(new XElement("ModifiedDate", FormatDate(topic.ModifiedDate.Value)));
        if (topic.ModifiedAuthor is not null) element.Add(new XElement("ModifiedAuthor", topic.ModifiedAuthor));
        if (topic.DueDate is not null) element.Add(new XElement("DueDate", FormatDate(topic.DueDate.Value)));
        if (topic.AssignedTo is not null) element.Add(new XElement("AssignedTo", topic.AssignedTo));
        if (topic.Stage is not null) element.Add(new XElement("Stage", topic.Stage));
        if (topic.Description is not null) element.Add(new XElement("Description", topic.Description));

        if (topic.BimSnippet is { } snippet)
        {
            var snippetElement = new XElement("BimSnippet",
                new XAttribute("SnippetType", snippet.SnippetType),
                new XAttribute("isExternal", snippet.IsExternal));
            if (snippet.Reference is not null) snippetElement.Add(new XElement("Reference", snippet.Reference));
            if (snippet.ReferenceSchema is not null) snippetElement.Add(new XElement("ReferenceSchema", snippet.ReferenceSchema));
            element.Add(snippetElement);
        }

        element.Add(topic.DocumentReferences.Select(WriteDocumentReference));
        element.Add(topic.RelatedTopics.Select(guid => new XElement("RelatedTopic", new XAttribute("Guid", guid))));

        return element;
    }

    private static BcfDocumentReference ReadDocumentReference(XElement element) => new(
        Guid: Guid.Parse(element.Attribute("Guid")?.Value
            ?? throw new InvalidDataException("DocumentReference element is missing Guid.")),
        ReferencedDocument: element.Element("ReferencedDocument")?.Value,
        Description: element.Element("Description")?.Value,
        IsExternal: ParseBool(element.Attribute("isExternal")?.Value));

    private static XElement WriteDocumentReference(BcfDocumentReference reference)
    {
        var element = new XElement("DocumentReference",
            new XAttribute("Guid", reference.Guid),
            new XAttribute("isExternal", reference.IsExternal));

        if (reference.ReferencedDocument is not null) element.Add(new XElement("ReferencedDocument", reference.ReferencedDocument));
        if (reference.Description is not null) element.Add(new XElement("Description", reference.Description));

        return element;
    }

    private static BcfComment ReadComment(XElement element) => new(
        Guid: Guid.Parse(element.Attribute("Guid")?.Value
            ?? throw new InvalidDataException("Comment element is missing Guid.")),
        Date: ParseDate(element.Element("Date")?.Value)
            ?? throw new InvalidDataException("Comment element is missing Date."),
        Author: element.Element("Author")?.Value
            ?? throw new InvalidDataException("Comment element is missing Author."),
        Comment: element.Element("Comment")?.Value ?? string.Empty,
        ViewpointGuid: element.Element("Viewpoint")?.Attribute("Guid")?.Value is { } viewpointGuid ? Guid.Parse(viewpointGuid) : null,
        ModifiedDate: ParseDate(element.Element("ModifiedDate")?.Value),
        ModifiedAuthor: element.Element("ModifiedAuthor")?.Value);

    private static XElement WriteComment(BcfComment comment)
    {
        var element = new XElement("Comment",
            new XAttribute("Guid", comment.Guid),
            new XElement("Date", FormatDate(comment.Date)),
            new XElement("Author", comment.Author));

        if (comment.ViewpointGuid is { } viewpointGuid)
            element.Add(new XElement("Viewpoint", new XAttribute("Guid", viewpointGuid)));

        element.Add(new XElement("Comment", comment.Comment));

        if (comment.ModifiedDate is { } modifiedDate) element.Add(new XElement("ModifiedDate", FormatDate(modifiedDate)));
        if (comment.ModifiedAuthor is not null) element.Add(new XElement("ModifiedAuthor", comment.ModifiedAuthor));

        return element;
    }

    private static BcfViewpointReference ReadViewpointReference(XElement element) => new(
        Guid: Guid.Parse(element.Attribute("Guid")?.Value
            ?? throw new InvalidDataException("Viewpoints element is missing Guid.")),
        ViewpointFile: element.Element("Viewpoint")?.Value,
        SnapshotFile: element.Element("Snapshot")?.Value,
        Index: ParseInt(element.Element("Index")?.Value));

    private static XElement WriteViewpointReference(BcfViewpointReference reference)
    {
        var element = new XElement("Viewpoints", new XAttribute("Guid", reference.Guid));

        if (reference.ViewpointFile is not null) element.Add(new XElement("Viewpoint", reference.ViewpointFile));
        if (reference.SnapshotFile is not null) element.Add(new XElement("Snapshot", reference.SnapshotFile));
        if (reference.Index is not null) element.Add(new XElement("Index", reference.Index));

        return element;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);

    private static int? ParseInt(string? value) =>
        value is null ? null : int.Parse(value, CultureInfo.InvariantCulture);

    private static bool ParseBool(string? value) =>
        value is not null && bool.Parse(value);
}
