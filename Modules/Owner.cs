using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Minio.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBot.Modules
{
    public class Owner : BaseCommandModule
    {
        [Command("shutdown")]
        [Description("Shuts down the bot.")]
        [RequireOwner]
        public async Task Shutdown(CommandContext ctx, [Description("This must be \"I am sure\" for the command to run."), RemainingText] string areYouSure)
        {
            if (areYouSure == "I am sure")
            {
                var msg = await ctx.RespondAsync("**Warning**: The bot is now shutting down. This action is permanent.");
                await ctx.Client.DisconnectAsync();
                Environment.Exit(0);
            }
            else
            {
                await ctx.RespondAsync("Are you sure?");
            }
        }

        [Command("link")]
        [Aliases("wl", "links")]
        [Description("Set/update/delete a short link with Cloudflare worker-links.")]
        public async Task Link(CommandContext ctx, [Description("(Optional) Set a custom key for the short link.")] string key, [Description("The URL the short link should point to.")] string url)
        {
            if (url.Contains("<"))
            {
                url = url.Replace("<", "");
            }
            if (url.Contains(">"))
            {
                url = url.Replace(">", "");
            }

            string baseUrl;
            if (Environment.GetEnvironmentVariable("WORKER_LINKS_BASE_URL") == null)
            {
                await ctx.RespondAsync("Error: No base URL provided! Make sure the environment variable `WORKER_LINKS_BASE_URL` is set.");
                return;
            }
            else
            {
                baseUrl = Environment.GetEnvironmentVariable("WORKER_LINKS_BASE_URL");
            }

            using HttpClient httpClient = new()
            {
                BaseAddress = new Uri(baseUrl)
            };

            HttpRequestMessage request;

            if (key == "null" || key == "random")
            {
                request = new HttpRequestMessage(HttpMethod.Post, "") { };
            }
            else if (key == "delete" || key == "del")
            {
                request = new HttpRequestMessage(HttpMethod.Delete, url) { };
            }
            else
            {
                request = new HttpRequestMessage(HttpMethod.Put, key) { };
            }

            string secret;
            if (Environment.GetEnvironmentVariable("WORKER_LINKS_SECRET") == null)
            {
                await ctx.RespondAsync("Error: No secret provided! Make sure the environment variable `WORKER_LINKS_secret` is set.");
                return;
            }
            else
            {
                secret = Environment.GetEnvironmentVariable("WORKER_LINKS_SECRET");
            }

            request.Headers.Add("Authorization", secret);
            request.Headers.Add("URL", url);

            HttpResponseMessage response = await httpClient.SendAsync(request);
            int httpStatusCode = (int)response.StatusCode;
            string httpStatus = response.StatusCode.ToString();
            string responseText = await response.Content.ReadAsStringAsync();
            if (responseText.Length > 1940)
            {
                await ctx.Channel.SendMessageAsync($"Worker responded with code: `{httpStatusCode}`...but the full response is too long to post here. Think about connecting this to a pastebin-like service.");
            }
            await ctx.Channel.SendMessageAsync($"Worker responded with code: `{httpStatusCode}` (`{httpStatus}`)\n```json\n{responseText}\n```");
        }

        [Command("upload")]
        [Description("Upload a file to Amazon S3-compatible cloud storage. Accepts an uploaded file.")]
        public async Task Upload(CommandContext ctx, [Description("(Optional) A link to a file to upload. This will take priority over a file upload!")] string link = null)
        {
            var msg = await ctx.RespondAsync("Uploading...");

            if (ctx.Message.Attachments.Count == 0 && link == null)
            {
                await msg.ModifyAsync("Please attach a file to upload!");
                return;
            }

            string linkToFile;
            if (link != null)
            {
                linkToFile = link;
            }
            else
            {
                linkToFile = ctx.Message.Attachments[0].Url;
            }

            string fileName;
            string extension;

            MemoryStream memStream;
            using (WebClient client = new())
            {
                memStream = new MemoryStream(client.DownloadData(linkToFile));
            }

            try
            {
                Dictionary<string, string> meta = new() { };

                meta["x-amz-acl"] = "public-read";

                string bucket = null;
                if (Environment.GetEnvironmentVariable("S3_BUCKET") == null)
                {
                    await msg.ModifyAsync("Error: S3 bucket info missing! Please check the `S3_BUCKET` environment variable.");
                    return;
                }
                else
                {
                    bucket = Environment.GetEnvironmentVariable("S3_BUCKET");
                }

                if (ctx.Message.Attachments[0].FileName.Contains(".png"))
                {
                    extension = "png";
                }
                else if (ctx.Message.Attachments[0].FileName.Contains(".jpg"))
                {
                    extension = "jpg";
                }
                else if (ctx.Message.Attachments[0].FileName.Contains(".gif"))
                {
                    extension = "gif";
                }
                else if (ctx.Message.Attachments[0].FileName.Contains(".mov"))
                {
                    extension = "mov";
                }
                else
                {
                    await msg.ModifyAsync("File extension not supported! Please add to `Owner.cs`.");
                    return;
                }

                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
                fileName = new string(Enumerable.Repeat(chars, 10).Select(s => s[Program.random.Next(s.Length)]).ToArray()) + "." + extension;

                await Program.minio.PutObjectAsync(bucket, fileName, memStream, memStream.Length, $"image/{extension}", meta);
            }
            catch (MinioException e)
            {
                await msg.ModifyAsync($"An API error occured while uploading!```\n{e.Message}```");
                return;
            }
            catch (Exception e)
            {
                await msg.ModifyAsync($"An unexpected error occured while uploading!```\n{e.Message}```");
                return;
            }

            string cdnUrl;
            if (Environment.GetEnvironmentVariable("CDN_BASE_URL") == null)
            {
                await msg.ModifyAsync($"Upload successful!\nThere's no CDN URL set in your environment file, so I can't give you a link. But your file was uploaded as {fileName}.");
            }
            else
            {
                cdnUrl = Environment.GetEnvironmentVariable("CDN_BASE_URL");
                await msg.ModifyAsync($"Upload successful!\n<{cdnUrl}/{fileName}>");
            }
        }

        [Command("deleteupload")]
        [Description("Delete a file uploaded to Amazon S3-compatible cloud storage.")]
        [Aliases("delupload", "delfile", "deletefile")]
        public async Task DeleteUpload(CommandContext ctx, string link)
        {
            if (link.Contains("<"))
            {
                link = link.Replace("<", "");
            }
            if (link.Contains(">"))
            {
                link = link.Replace(">", "");
            }

            var msg = await ctx.RespondAsync("Working on it...");

            if (!link.Contains("https://cdn.floatingmilkshake.com/"))
            {
                await msg.ModifyAsync("Please include the full URL to the file to delete!");
                return;
            }

            string bucket;
            if (Environment.GetEnvironmentVariable("S3_BUCKET") == null)
            {
                await msg.ModifyAsync("Error: S3 bucket info missing! Please check the `S3_BUCKET` environment variable.");
                return;
            }
            else
            {
                bucket = Environment.GetEnvironmentVariable("S3_BUCKET");
            }

            string fileName = link.Replace("https://cdn.floatingmilkshake.com/", "");

            try
            {
                await Program.minio.RemoveObjectAsync(bucket, fileName);
            }
            catch (MinioException e)
            {
                await msg.ModifyAsync($"An API error occured while attempting to delete the file!```\n{e.Message}```");
                return;
            }
            catch (Exception e)
            {
                await msg.ModifyAsync($"An unexpected error occured while attempting to delete the file!```\n{e.Message}```");
                return;
            }

            await msg.ModifyAsync("File deleted successfully!\nAttempting to purge Cloudflare cache...");

            string cloudflareUrlPrefix;
            if (Environment.GetEnvironmentVariable("CLOUDFLARE_URL_PREFIX") != null)
            {
                cloudflareUrlPrefix = Environment.GetEnvironmentVariable("CLOUDFLARE_URL_PREFIX");
            }
            else
            {
                await msg.ModifyAsync("File deleted successfully!\nError: missing Zone ID for Cloudflare. Unable to purge cache! Check the `CLOUDFLARE_URL_PREFIX` environment variable.");
                return;
            }

            // https://github.com/Sankra/cloudflare-cache-purger/blob/master/main.csx#L113 (https://github.com/Erisa/Lykos/blob/main/src/Modules/Owner.cs#L227)
            CloudflareContent content = new(new List<string>() { cloudflareUrlPrefix + fileName });
            string cloudflareContentString = JsonConvert.SerializeObject(content);
            try
            {
                using HttpClient httpClient = new()
                {
                    BaseAddress = new Uri("https://api.cloudflare.com/")
                };

                string zoneId;
                if (Environment.GetEnvironmentVariable("CLOUDFLARE_ZONE_ID") != null)
                {
                    zoneId = Environment.GetEnvironmentVariable("CLOUDFLARE_ZONE_ID");
                }
                else
                {
                    await msg.ModifyAsync("File deleted successfully!\nError: missing Zone ID for Cloudflare. Unable to purge cache! Check the `CLOUDFLARE_ZONE_ID` environment variable.");
                    return;
                }

                string cloudflareToken;
                if (Environment.GetEnvironmentVariable("CLOUDFLARE_TOKEN") != null)
                {
                    cloudflareToken = Environment.GetEnvironmentVariable("CLOUDFLARE_TOKEN");
                }
                else
                {
                    await msg.ModifyAsync("File deleted successfully!\nError: missing token for Cloudflare. Unable to purge cache! Check the `CLOUDFLARE_TOKEN` environment variable.");
                    return;
                }

                HttpRequestMessage request = new(HttpMethod.Delete, "client/v4/zones/" + zoneId + "/purge_cache")
                {
                    Content = new StringContent(cloudflareContentString, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bearer {cloudflareToken}");

                HttpResponseMessage response = await httpClient.SendAsync(request);
                string responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    await msg.ModifyAsync($"File Deleted successfully!\nSuccesssfully purged the Cloudflare cache for `{fileName}`!");
                }
                else
                {
                    await msg.ModifyAsync($"File deleted successfully!\nAn API error occured when purging the Cloudflare cache: ```json\n{responseText}```");
                }
            }
            catch (Exception e)
            {
                await msg.ModifyAsync($"File deleted successfully!\nAn unexpected error occured when purging the Cloudflare cache: ```json\n{e.Message}```");
            }
        }

        // https://github.com/Sankra/cloudflare-cache-purger/blob/master/main.csx#L197 (https://github.com/Erisa/Lykos/blob/3335c38a52d28820a935f99c53f030805d4da607/src/Modules/Owner.cs#L313)
        readonly struct CloudflareContent
        {
            public CloudflareContent(List<string> urls)
            {
                Files = urls;
            }

            public List<string> Files { get; }
        }
    }
}
