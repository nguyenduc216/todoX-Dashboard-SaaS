using TodoX.EditVideoTool.Mcp;

namespace TodoX.EditVideoTool.Tests;

public class InternalNamesTests
{
    [Theory]
    [InlineData("Bộ xử lý video báo lỗi: [libx264 @ 0x1] ffmpeg: Invalid argument")]
    [InlineData("Dựng video thất bại: Error at node_modules/@remotion/renderer/dist/index.js (chrome-headless-shell)")]
    [InlineData("whisper-cli.exe lỗi: ggml_init failed")]
    [InlineData("ffprobe version n8.1 Copyright (c) 2007-2026\nbuilt with gcc 14\nInvalid data found")]
    public void Program_names_never_reach_the_assistant(string text)
    {
        var s = InternalNames.Scrub(text);
        Assert.DoesNotMatch("(?i)ffmpeg|ffprobe|remotion|whisper|ggml|libx264|chrome|node_modules|gcc", s);
    }

    [Fact]
    public void Component_paths_are_hidden_but_customer_text_stays()
    {
        InternalNames.UseComponentsDir(@"C:\Users\A\AppData\Local\TodoX\EditVideoTool\components");
        try
        {
            var s = InternalNames.Scrub(@"{""err"":""C:\\Users\\A\\AppData\\Local\\TodoX\\EditVideoTool\\components\\bo-xu-ly-video\\bin\\x.exe failed"",""file"":""D:\\Videos\\node trên cây.mp4"",""version"":""1.2.11""}");
            Assert.DoesNotContain("components", s);
            Assert.Contains(@"D:\\Videos\\node trên cây.mp4", s);
            Assert.Contains("1.2.11", s);
        }
        finally
        {
            InternalNames.UseComponentsDir(null);
        }
    }
}
