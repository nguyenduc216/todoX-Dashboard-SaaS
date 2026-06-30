using Microsoft.EntityFrameworkCore;
using TodoX.Dashboard.Models;
using TodoX.Dashboard.Services;

namespace TodoX.Dashboard.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, PasswordService passwords, IConfiguration config)
    {
        var adminUser = config["TodoX:SeedAdminUser"] ?? "admin";
        var adminPassword = config["TodoX:SeedAdminPassword"] ?? "toDOx@123#2026";
        var adminEmail = config["TodoX:SeedAdminEmail"] ?? "admin@todox.local";

        if (!await db.SystemUsers.AnyAsync(x => x.UserName == adminUser))
        {
            db.SystemUsers.Add(new SystemUser
            {
                UserName = adminUser,
                Email = adminEmail,
                DisplayName = "TodoX Administrator",
                PasswordHash = passwords.Hash(adminPassword),
                Role = "Admin"
            });
        }

        if (!await db.PricingRules.AnyAsync())
        {
            db.PricingRules.AddRange(
                new PricingRule { ServiceCode = "IMAGE_BASIC", ServiceName = "Render hình ảnh cơ bản", BaseToken = 10, PerSecondToken = 0 },
                new PricingRule { ServiceCode = "VIDEO_STANDARD", ServiceName = "Render video tiêu chuẩn", BaseToken = 30, PerSecondToken = 2, VoiceEnabled = true, CaptionEnabled = true },
                new PricingRule { ServiceCode = "PROMPT_OPTIMIZE", ServiceName = "Tối ưu prompt", BaseToken = 5, PerSecondToken = 0 }
            );
        }

        if (!await db.SystemSettings.AnyAsync())
        {
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "Render.MinioBucket", Value = "todox-render", Description = "Bucket lưu file render" },
                new SystemSetting { Key = "Billing.DefaultCurrency", Value = "VND", Description = "Đơn vị tiền tệ mặc định" }
            );
        }

        await db.SaveChangesAsync();
    }
}
