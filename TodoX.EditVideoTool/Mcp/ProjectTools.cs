using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Projects;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class ProjectTools
{
    private readonly ProjectFolders _projects;

    public ProjectTools(ProjectFolders projects)
    {
        _projects = projects;
    }

    [McpServerTool(Name = "todox_projects", ReadOnly = true, Idempotent = true)]
    [Description("List the customer's video projects in the TodoX resource folder (one sub-folder per project) with how many photos, videos and audio files each holds.")]
    public string Projects() => JsonSerializer.Serialize(new
    {
        resourceFolder = _projects.Root,
        projects = _projects.List().Select(Shape)
    }, StatusTools.Json);

    [McpServerTool(Name = "todox_project_folder", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find (or create) the resource folder of a project, e.g. \"dự án An Đông\", and open it in Windows Explorer. Every project has the same sub-folders (01-anh photos, 02-video clips, 03-am-thanh music/voice, 04-kich-ban brief and scripts, 05-thuong-hieu logo and brand, 06-ai AI results, output renders) and a CLAUDE.md with the project brief and rules: read it, and keep its log up to date. Matching ignores case and Vietnamese accents. If the folder has no footage, ask the user to copy files into the right sub-folders and tell you when done, then use todox_probe on the folder.")]
    public string ProjectFolder(
        [Description("Project name as the user says it, e.g. \"An Đông\" or \"dự án An Đông\".")] string project,
        [Description("Open the folder in Windows Explorer (default true).")] bool open = true,
        [Description("Create the folder when no project matches (default true).")] bool create = true)
    {
        var (folder, created) = _projects.Find(project, create);
        if (folder is null)
        {
            return JsonSerializer.Serialize(new
            {
                found = false,
                message = $"Chưa có thư mục dự án \"{project}\".",
                resourceFolder = _projects.Root,
                projects = _projects.List().Select(p => p.Name)
            }, StatusTools.Json);
        }

        var opened = open && ProjectFolders.Open(folder.Path);
        return JsonSerializer.Serialize(new
        {
            found = true,
            created,
            opened,
            project = Shape(folder),
            next = folder.Images + folder.Videos == 0
                ? "The folder is empty: ask the user to copy the project's photos/videos into it and tell you when done."
                : "Use todox_probe on this folder to see the footage."
        }, StatusTools.Json);
    }

    private static object Shape(ProjectFolder p) => new
    {
        name = p.Name,
        path = p.Path,
        images = p.Images,
        videos = p.Videos,
        audio = p.Audio,
        other = p.Other,
        lastChanged = p.LastChanged?.ToString("yyyy-MM-dd HH:mm"),
        brief = Path.Combine(p.Path, ProjectFolders.ClaudeFile),
        folders = ProjectFolders.Layout.ToDictionary(f => f.Name, f => f.Holds)
    };
}
