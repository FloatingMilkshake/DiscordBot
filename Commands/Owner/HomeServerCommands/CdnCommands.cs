﻿namespace MechanicalMilkshake.Commands.Owner.HomeServerCommands;

public partial class CdnCommands : ApplicationCommandModule
{
    [SlashCommandGroup("cdn", "Manage files uploaded to Amazon S3-compatible cloud storage.")]
    public partial class Cdn
    {
        [SlashCommand("upload", "Upload a file to Amazon S3-compatible cloud storage.")]
        public static async Task Upload(InteractionContext ctx,
            [Option("name", "The name for the uploaded file.")]
            string name = "preserve",
            [Option("link", "A link to a file to upload.")]
            string link = null,
            [Option("file", "A direct file to upload. This will override a link if both are provided!")]
            DiscordAttachment file = null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            if (Program.DisabledCommands.Contains("cdn"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, true);
                return;
            }

            if (file is null && link is null)
            {
                await ctx.FollowUpAsync(
                    new DiscordFollowupMessageBuilder().WithContent("You must provide a link or file to upload!"));
                return;
            }

            if (file is not null) link = file.Url;

            link = link.Replace("<", "");
            link = link.Replace(">", "");

            string fileName;

            // Get file, where 'link' is the URL
            MemoryStream memStream = new(await Program.HttpClient.GetByteArrayAsync(link));

            try
            {
                var bucket = Program.ConfigJson.S3.Bucket;

                // Strip the URL down to just the file name

                // Regex partially taken from https://stackoverflow.com/a/26253039
                var fileNamePattern = FileNamePattern();

                var fileNameAndExtension = fileNamePattern.Match(link).Value;

                // From here on out we can be sure that 'fileNameAndExtension' is in the format 'example.png'.

                var extension = Path.GetExtension(fileNameAndExtension);

                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

                fileName = name switch
                {
                    "random" or "generate" => new string(Enumerable.Repeat(chars, 10)
                        .Select(s => s[Program.Random.Next(s.Length)])
                        .ToArray()) + extension,
                    "preserve" => fileNameAndExtension,
                    _ => name + extension
                };

                var args = new PutObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(fileName)
                    .WithStreamData(memStream)
                    .WithObjectSize(memStream.Length)
                    .WithContentType(MimeTypeMap.GetMimeType(extension));

                await Program.Minio.PutObjectAsync(args);
            }
            catch (MinioException e)
            {
                await ctx.FollowUpAsync(
                    new DiscordFollowupMessageBuilder().WithContent(
                        $"An API error occured while uploading!```\n{e.Message}```"));
                return;
            }
            catch (Exception e)
            {
                await ctx.FollowUpAsync(
                    new DiscordFollowupMessageBuilder().WithContent(
                        $"An unexpected error occured while uploading!```\n{e.Message}```"));
                return;
            }

            await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                $"Upload successful!\n<{Program.ConfigJson.S3.CdnBaseUrl}/{fileName}>"));
        }

        [SlashCommand("delete", "Delete a file from Amazon S3-compatible cloud storage.")]
        public static async Task DeleteUpload(InteractionContext ctx,
            [Option("file", "The file to delete.")]
            string fileToDelete)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            if (Program.DisabledCommands.Contains("cdn"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, true);
                return;
            }

            fileToDelete = fileToDelete.Replace("<", "").Replace(">", "");

            var fileName = fileToDelete.Replace($"{Program.ConfigJson.S3.CdnBaseUrl}/", "");

            try
            {
                var args = new RemoveObjectArgs()
                    .WithBucket(Program.ConfigJson.S3.Bucket)
                    .WithObject(fileName);

                await Program.Minio.RemoveObjectAsync(args);
            }
            catch (MinioException e)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"An API error occured while attempting to delete the file!```\n{e.Message}```"));
                return;
            }
            catch (Exception e)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"An unexpected error occured while attempting to delete the file!```\n{e.Message}```"));
                return;
            }

            await ctx.FollowUpAsync(
                new DiscordFollowupMessageBuilder().WithContent(
                    "File deleted successfully!\nAttempting to purge Cloudflare cache..."));

            var cloudflareUrlPrefix = Program.ConfigJson.Cloudflare.UrlPrefix;

            // This code is (mostly) taken from https://github.com/Sankra/cloudflare-cache-purger/blob/master/main.csx#L113.
            // (Note that I originally found it here: https://github.com/Erisa/Lykos/blob/1f32e03/src/Modules/Owner.cs#L232)

            CloudflareContent content = new([cloudflareUrlPrefix + fileName]);
            var cloudflareContentString = JsonConvert.SerializeObject(content);
            try
            {
                using HttpRequestMessage request =
                    new(HttpMethod.Delete, $"https://api.cloudflare.com/client/v4/zones/{Program.ConfigJson.Cloudflare.ZoneId}/purge_cache/files");
                request.Content = new StringContent(cloudflareContentString, Encoding.UTF8, "application/json");
                request.Headers.Add("Authorization", $"Bearer {Program.ConfigJson.Cloudflare.Token}");

                var response = await Program.HttpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"File deleted successfully!\nSuccessfully purged the Cloudflare cache for `{fileName}`!"));
                else
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        $"File deleted successfully!\nAn API error occured when purging the Cloudflare cache: ```json\n{responseText}```"));
            }
            catch (Exception e)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"File deleted successfully!\nAn unexpected error occured when purging the Cloudflare cache: ```json\n{e.Message}```"));
            }
        }

        [SlashCommand("check", "Check whether a file exists in your S3 bucket. Uses the S3 API to avoid caching.")]
        public static async Task CdnPreview(InteractionContext ctx,
            [Option("name", "The name (or link) of the file to check.")]
            string name)
        {
            if (Program.DisabledCommands.Contains("cdn"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, false);
                return;
            }
            
            if (name.Contains(Program.ConfigJson.S3.CdnBaseUrl))
                name = name.Replace(Program.ConfigJson.S3.CdnBaseUrl, "").Trim('/');

            try
            {
                await Program.Minio.GetObjectAsync(new GetObjectArgs().WithBucket(Program.ConfigJson.S3.Bucket)
                    .WithObject(name).WithFile(name));
            }
            catch (ObjectNotFoundException)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().WithContent("That file doesn't exist!"));
                return;
            }
            catch (Exception ex)
            {
                await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().WithContent(
                        $"I ran into an error trying to check for that file! {ex.GetType()}: {ex.Message}"));
                return;
            }

            await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent("That file exists!"));
        }

        [GeneratedRegex(@"[^/\\&\?#]+\.\w*(?=([\?&#].*$|$))")]
        private static partial Regex FileNamePattern();
    }

    // This code is taken from https://github.com/Sankra/cloudflare-cache-purger/blob/master/main.csx#L197,
    // minus some minor changes.
    // (Note that I originally found it here: https://github.com/Erisa/Lykos/blob/3335c38/src/Modules/Owner.cs#L313)
    private readonly struct CloudflareContent(List<string> urls)
    {
        public List<string> Files { get; } = urls;
    }
}