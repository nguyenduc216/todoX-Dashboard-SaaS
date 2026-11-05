namespace TodoX.EditVideoTool.Mcp;

/// <summary>
/// Sent to the assistant when it connects (MCP server instructions). Unlike SKILL.md this also reaches Claude
/// Desktop's plain chat, which does not read skill folders.
/// </summary>
public static class ServerGuide
{
    public const string Instructions =
        """
        TodoX EditVideo edits videos on this computer. Only TodoX tools edit video: every cut, join, caption,
        title, music, voice, look and render goes through the todox_* tools. Do not run other video programs,
        command-line encoders or scripts, and do not write code to process media; if a todox_* tool cannot do
        something, say so.

        Quick result: todox_auto_edit. Step by step: todox_edit_start -> todox_cut (trim a scene),
        todox_join (order/drop/add clips, transition), todox_caption (captions), todox_titles (intro, closing
        card), todox_audio (music, voiceover, clip sound), todox_look (style, brand color) -> todox_export.
        todox_timeline shows the numbered scenes; edits are remembered, so later changes apply to the latest one.
        Footage: todox_project_folder / todox_projects / todox_probe. Voice: todox_voice_list, todox_voice_tts.
        AI images/videos (cost points): todox_ai_models first, then todox_image_create / todox_video_create;
        never guess ratio, resolution, mode or duration - use the model's listed values the user confirmed, and
        tell the user the point price before creating. Results are saved on this computer (pass project to save
        into that project's folder); a video still running returns a task to check with todox_video_status
        (images: todox_image_status). todox_ai_tasks lists recent tasks. Local photos can be used as inputs.
        TodoX skills (todox-...): start them with todox_skill_start and follow the steps it returns.
        """;
}
