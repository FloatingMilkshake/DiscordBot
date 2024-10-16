﻿using DSharpPlus.Commands.EventArgs;

namespace MechanicalMilkshake.Events;

public class InteractionEvents
{
    public static async Task CommandExecuted(CommandsExtension _, CommandExecutedEventArgs e)
    {
        await LogCmdUsage(e.Context);
    }

    private static async Task LogCmdUsage(CommandContext context)
    {
        try
        {
            // Ignore home server, excluded servers, and authorized users
            if (context.Guild is not null && (context.Guild.Id == Program.HomeServer.Id ||
                Program.ConfigJson.Logs.SlashCommands.CmdLogExcludedGuilds.Contains(context.Guild.Id.ToString())) ||
                Program.ConfigJson.Base.AuthorizedUsers.Contains(context.User.Id.ToString()))
                return;

            // Increment count
            if (await Program.Db.HashExistsAsync("commandCounts", context.Command.Name))
                await Program.Db.HashIncrementAsync("commandCounts", context.Command.Name);
            else
                await Program.Db.HashSetAsync("commandCounts", context.Command.Name, 1);

            // Log to log channel if configured
            if (Program.ConfigJson.Logs.SlashCommands.LogChannel is not null)
            {
                var description = context.Channel.IsPrivate
                    ? $"{context.User.Username} (`{context.User.Id}`) used {SlashCmdMentionHelpers.GetSlashCmdMention(context.Command.Name)} in DMs."
                    : $"{context.User.Username} (`{context.User.Id}`) used {SlashCmdMentionHelpers.GetSlashCmdMention(context.Command.Name)} in `{context.Channel.Name}` (`{context.Channel.Id}`) in \"{context.Guild.Name}\" (`{context.Guild.Id}`).";
                
                var embed = new DiscordEmbedBuilder()
                    .WithColor(Program.BotColor)
                    .WithAuthor(context.User.Username, null, context.User.AvatarUrl)
                    .WithDescription(description)
                    .WithTimestamp(DateTime.Now);

                try
                {
                    await (await context.Client.GetChannelAsync(
                        Convert.ToUInt64(Program.ConfigJson.Logs.SlashCommands.LogChannel))).SendMessageAsync(embed);
                }
                catch (Exception ex) when (ex is UnauthorizedException or NotFoundException)
                {
                    Program.Discord.Logger.LogError(Program.BotEventId,
                        "{User} used {Command} in {Guild} but it could not be logged because the log channel cannot be accessed",
                        context.User.Id, context.Command.Name, context.Guild.Id);
                }
                catch (FormatException)
                {
                    Program.Discord.Logger.LogError(Program.BotEventId,
                        "{User} used {Command} in {Guild} but it could not be logged because the log channel ID is invalid",
                        context.User.Id, context.Command.Name, context.Guild.Id);
                }
            }
        }
        catch (Exception ex)
        {
            DiscordEmbedBuilder embed = new()
            {
                Title = "An exception was thrown when logging a slash command",
                Description =
                    $"An exception was thrown when {context.User.Mention} used `/{context.Command.Name}`. Details are below.",
                Color = DiscordColor.Red
            };
            embed.AddField("Exception Details",
                $"```{ex.GetType()}: {ex.Message}:\n{ex.StackTrace}".Truncate(1020) + "\n```");
            
            await Program.HomeChannel.SendMessageAsync(embed);
        }
        
    }
}