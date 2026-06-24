using System.IO.Compression;
using BCFree.Core.Model;

namespace BCFree.Core.Serialization;

/// <summary>
/// Reads and writes complete .bcfzip archives: the root bcf.version and project.bcf files,
/// plus one folder per topic (named after the topic's GUID by convention) containing
/// markup.bcf, any *.bcfv viewpoint files, and attachments such as snapshots.
/// </summary>
public static class BcfArchive
{
    private const string VersionEntryName = "bcf.version";
    private const string ProjectEntryName = "project.bcf";
    private const string MarkupEntryName = "markup.bcf";

    public static BcfDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static BcfDocument Read(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var versionEntry = archive.Entries.FirstOrDefault(e => IsRootEntry(e, VersionEntryName))
            ?? throw new InvalidDataException("The archive is missing bcf.version.");
        var version = ReadXml(versionEntry, BcfVersionSerializer.Read);

        var projectEntry = archive.Entries.FirstOrDefault(e => IsRootEntry(e, ProjectEntryName));
        var project = projectEntry is null ? null : ReadXml(projectEntry, BcfProjectSerializer.Read);

        var topics = archive.Entries
            .Where(e => e.FullName.Contains('/'))
            .GroupBy(e => RootFolder(e.FullName))
            .Where(g => g.Any(e => string.Equals(EntryRelativeName(e, g.Key), MarkupEntryName, StringComparison.OrdinalIgnoreCase)))
            .Select(ReadTopicFolder)
            .ToList();

        return new BcfDocument(version) { Project = project, Topics = topics };
    }

    public static void Write(BcfDocument document, string path)
    {
        using var stream = File.Create(path);
        Write(document, stream);
    }

    public static void Write(BcfDocument document, Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        WriteXml(archive, VersionEntryName, document.Version, BcfVersionSerializer.Write);

        if (document.Project is { } project)
            WriteXml(archive, ProjectEntryName, project, BcfProjectSerializer.Write);

        foreach (var topic in document.Topics)
        {
            var folder = topic.Markup.Topic.Guid.ToString();

            WriteXml(archive, $"{folder}/{MarkupEntryName}", topic.Markup, BcfMarkupSerializer.Write);

            foreach (var viewpointEntry in topic.Viewpoints)
                WriteXml(archive, $"{folder}/{SafeRelativeEntryName(viewpointEntry.Key)}", viewpointEntry.Value, BcfVisualizationSerializer.Write);

            foreach (var attachmentEntry in topic.Attachments)
                WriteBytes(archive, $"{folder}/{SafeRelativeEntryName(attachmentEntry.Key)}", attachmentEntry.Value);
        }
    }

    private static BcfTopicFolder ReadTopicFolder(IGrouping<string, ZipArchiveEntry> folder)
    {
        var markupEntry = folder.FirstOrDefault(e => string.Equals(EntryRelativeName(e, folder.Key), MarkupEntryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Topic folder '{folder.Key}' is missing markup.bcf.");

        var topicFolder = new BcfTopicFolder(ReadXml(markupEntry, BcfMarkupSerializer.Read));

        foreach (var entry in folder)
        {
            if (entry == markupEntry)
                continue;

            var fileName = EntryRelativeName(entry, folder.Key);
            if (fileName.Length == 0)
                continue;

            if (fileName.EndsWith(".bcfv", StringComparison.OrdinalIgnoreCase))
                topicFolder.Viewpoints[fileName] = ReadXml(entry, BcfVisualizationSerializer.Read);
            else
                topicFolder.Attachments[fileName] = ReadBytes(entry);
        }

        return topicFolder;
    }

    private static bool IsRootEntry(ZipArchiveEntry entry, string name) =>
        !entry.FullName.Contains('/') && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase);

    private static string RootFolder(string entryName) => entryName.Substring(0, entryName.IndexOf('/'));

    private static string EntryRelativeName(ZipArchiveEntry entry, string rootFolder)
    {
        var prefix = rootFolder + "/";
        return entry.FullName.StartsWith(prefix, StringComparison.Ordinal)
            ? entry.FullName.Substring(prefix.Length)
            : entry.Name;
    }

    private static string SafeRelativeEntryName(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
            throw new InvalidDataException($"Invalid BCF archive entry name '{entryName}'.");

        return normalized;
    }

    private static T ReadXml<T>(ZipArchiveEntry entry, Func<Stream, T> read)
    {
        using var entryStream = entry.Open();
        return read(entryStream);
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static void WriteXml<T>(ZipArchive archive, string entryName, T value, Action<T, Stream> write)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        write(value, entryStream);
    }

    private static void WriteBytes(ZipArchive archive, string entryName, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }
}
