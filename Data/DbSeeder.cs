using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data;

public static class DbSeeder
{
    public const string BotUserName = "ChessBot";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        var config = services.GetRequiredService<IConfiguration>();
        var adminEmail = config["Admin:Email"] ?? "admin@chessarena.app";
        var adminPassword = config["Admin:Password"] ?? "Admin123!";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                DisplayName = "Admin",
                Rating = 1200
            };
            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }
        else if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        var bot = await userManager.FindByNameAsync(BotUserName);
        if (bot is null)
        {
            bot = new ApplicationUser
            {
                UserName = BotUserName,
                Email = "bot@chessarena.app",
                EmailConfirmed = true,
                DisplayName = BotUserName,
                Rating = 1500,
                IsBot = true
            };
            var botResult = await userManager.CreateAsync(bot, Guid.NewGuid().ToString("N") + "Aa1!");
            if (!botResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to seed bot user: " + string.Join("; ", botResult.Errors.Select(e => e.Description)));
            }
        }
    }
}