﻿namespace MechanicalMilkshake.Commands.Owner.HomeServerCommands;

public class LinkCommands : ApplicationCommandModule
{
    [SlashCommandGroup("link", "Set, update, or delete a short link with Cloudflare worker-links.")]
    public class Link
    {
        [SlashCommand("set", "Set or update a short link with Cloudflare worker-links.")]
        public static async Task SetWorkerLink(InteractionContext ctx,
            [Option("key", "Set a custom key for the short link.")]
            string key,
            [Option("url", "The URL the short link should point to.")]
            string url)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder());
        
            if (Program.DisabledCommands.Contains("wl"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, true);
                return;
            }

            if (url.Contains('<')) url = url.Replace("<", "");

            if (url.Contains('>')) url = url.Replace(">", "");

            if (Program.ConfigJson.WorkerLinks.BaseUrl is null)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    "Error: No base URL provided! Make sure the baseUrl field under workerLinks in your config.json file is set."));
                return;
            }

            var baseUrl = Program.ConfigJson.WorkerLinks.BaseUrl;

            using HttpClient httpClient = new();
            httpClient.BaseAddress = new Uri(baseUrl);

            var request = key is "null" or "random" or "rand"
                ? new HttpRequestMessage(HttpMethod.Post, "")
                : new HttpRequestMessage(HttpMethod.Put, key);

            if (Program.ConfigJson.WorkerLinks.Secret is null)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    "Error: No secret provided! Make sure the secret field under workerLinks in your config.json file is set."));
                return;
            }

            var secret = Program.ConfigJson.WorkerLinks.Secret;

            request.Headers.Add("Authorization", secret);
            request.Headers.Add("URL", url);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"An exception occurred while trying to send the request! `{ex.GetType()}: {ex.Message}`"));
                return;
            }
            
            var httpStatusCode = (int)response.StatusCode;
            var httpStatus = response.StatusCode.ToString();
            var responseText = await response.Content.ReadAsStringAsync();
            if (responseText.Length > 1947)
            {
                var hasteUrl = await HastebinHelpers.UploadToHastebinAsync(responseText);
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"Worker responded with code: `{httpStatusCode}`...but the full response is too long to post here. It was uploaded to Hastebin here: {hasteUrl}"));
                return;
            }

            await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                $"Worker responded with code: `{httpStatusCode}` (`{httpStatus}`)\n```json\n{responseText}\n```"));
        }

        [SlashCommand("delete", "Delete a short link with Cloudflare worker-links.")]
        public static async Task DeleteWorkerLink(InteractionContext ctx,
            [Option("link", "The key or URL of the short link to delete.")]
            string url)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
            
            if (Program.DisabledCommands.Contains("wl"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, true);
                return;
            }

            var baseUrl = Program.ConfigJson.WorkerLinks.BaseUrl;

            using HttpClient httpClient = new();
            httpClient.BaseAddress = new Uri(baseUrl);

            if (!url.Contains(baseUrl)) url = $"{baseUrl}/{url}";

            if (Program.ConfigJson.WorkerLinks.Secret is null)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    "Error: No secret provided! Make sure the secret field under workerLinks in your config.json file is set."));
                return;
            }

            var secret = Program.ConfigJson.WorkerLinks.Secret;

            HttpRequestMessage request = new(HttpMethod.Delete, url);
            request.Headers.Add("Authorization", secret);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"An exception occurred while trying to send the request! `{ex.GetType()}: {ex.Message}`"));
                return;
            }
            
            var httpStatusCode = (int)response.StatusCode;
            var httpStatus = response.StatusCode.ToString();
            var responseText = await response.Content.ReadAsStringAsync();
            if (responseText.Length > 1947)
            {
                var hasteUrl = await HastebinHelpers.UploadToHastebinAsync(responseText);
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"Worker responded with code: `{httpStatusCode}`...but the full response is too long to post here. It was uploaded to Hastebin here: {hasteUrl}"));
                return;
            }

            await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                $"Worker responded with code: `{httpStatusCode}` (`{httpStatus}`)\n```json\n{responseText}\n```"));
        }

        [SlashCommand("list", "List all short links configured with Cloudflare worker-links.")]
        public static async Task ListWorkerLinks(InteractionContext ctx)
        {
            await ctx.CreateResponseAsync(new DiscordInteractionResponseBuilder().WithContent("Working on it..."));
            
            if (Program.DisabledCommands.Contains("wl"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, true);
                return;
            }

            var requestUri =
                $"https://api.cloudflare.com/client/v4/accounts/{Program.ConfigJson.WorkerLinks.AccountId}/storage/kv/namespaces/{Program.ConfigJson.WorkerLinks.NamespaceId}/keys";
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);

            request.Headers.Add("X-Auth-Key", Program.ConfigJson.WorkerLinks.ApiKey);
            request.Headers.Add("X-Auth-Email", Program.ConfigJson.WorkerLinks.Email);
            var response = await Program.HttpClient.SendAsync(request);

            var responseText = await response.Content.ReadAsStringAsync();

            var parsedResponse = JsonConvert.DeserializeObject<CloudflareResponse>(responseText);

            var kvListResponse = "";

            foreach (var item in parsedResponse.Result)
            {
                var key = item.Name.Replace("/", "%2F");

                var valueRequestUri =
                    $"https://api.cloudflare.com/client/v4/accounts/{Program.ConfigJson.WorkerLinks.AccountId}/storage/kv/namespaces/{Program.ConfigJson.WorkerLinks.NamespaceId}/values/{key}";
                HttpRequestMessage valueRequest = new(HttpMethod.Get, valueRequestUri);

                valueRequest.Headers.Add("X-Auth-Key", Program.ConfigJson.WorkerLinks.ApiKey);
                valueRequest.Headers.Add("X-Auth-Email", Program.ConfigJson.WorkerLinks.Email);
                var valueResponse = await Program.HttpClient.SendAsync(valueRequest);

                var value = await valueResponse.Content.ReadAsStringAsync();
                value = value.Replace(value, $"<{value}>");
                kvListResponse += $"**{item.Name}**: {value}\n\n";
            }

            DiscordEmbedBuilder embed = new()
            {
                Title = "Link List"
            };
            try
            {
                var pages = Program.Discord.GetInteractivity()
                    .GeneratePagesInEmbed(kvListResponse, SplitType.Line, embed);

                var leftSkip = new DiscordButtonComponent(ButtonStyle.Primary, "leftskip", "<<<");
                var left = new DiscordButtonComponent(ButtonStyle.Primary, "left", "<");
                var right = new DiscordButtonComponent(ButtonStyle.Primary, "right", ">");
                var rightSkip = new DiscordButtonComponent(ButtonStyle.Primary, "rightskip", ">>>");
                var stop = new DiscordButtonComponent(ButtonStyle.Danger, "stop", "Stop");

                await ctx.Interaction.SendPaginatedResponseAsync(false, ctx.User, pages,
                    new PaginationButtons
                        { SkipLeft = leftSkip, Left = left, Right = right, SkipRight = rightSkip, Stop = stop },
                    deletion: ButtonPaginationBehavior.DeleteMessage,
                    asEditResponse: true);
            }
            catch (Exception ex)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"Hmm, I couldn't send the list of links here!" +
                    $" You can see the full list on Cloudflare's website [here](https://dash.cloudflare.com/" +
                    $"{Program.ConfigJson.WorkerLinks.AccountId}/workers/kv/namespaces/{Program.ConfigJson.WorkerLinks.NamespaceId})." +
                    $" Exception details are below.\n```\n{ex.GetType()}: {ex.Message}\n```"));
            }
        }

        [SlashCommand("get", "Get the long URL for a short link.")]
        public static async Task GetWorkerLink(InteractionContext ctx,
            [Option("link", "The key or URL of the short link to get.")]
            string url)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);
            
            if (Program.DisabledCommands.Contains("wl"))
            {
                await CommandHandlerHelpers.FailOnMissingInfo(ctx, true);
                return;
            }

            var key = url.Contains($"{Program.ConfigJson.WorkerLinks.BaseUrl}/")
                ? url.Replace($"{Program.ConfigJson.WorkerLinks.BaseUrl}/", "")
                : url;

            if (!key.StartsWith('/') && !key.StartsWith("%2F")) key = $"%2F{key}";
            if (key.StartsWith('/')) key = key.Replace("/", "%2F");

            var requestUri =
                $"https://api.cloudflare.com/client/v4/accounts/{Program.ConfigJson.WorkerLinks.AccountId}/storage/kv/namespaces/{Program.ConfigJson.WorkerLinks.NamespaceId}/values/{key}";
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);

            request.Headers.Add("X-Auth-Key", Program.ConfigJson.WorkerLinks.ApiKey);
            request.Headers.Add("X-Auth-Email", Program.ConfigJson.WorkerLinks.Email);

            HttpResponseMessage response;
            try
            {
                response = await Program.HttpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                    $"An exception occurred while trying to send the request! `{ex.GetType()}: {ex.Message}`"));
                return;
            }
            
            var responseData = await response.Content.ReadAsStringAsync();
            if (responseData.Contains("not found") || responseData.Contains("not_found"))
            {
                await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent("That link doesn't exist!"));
                return;
            }
            
            if (!url.StartsWith(Program.ConfigJson.WorkerLinks.BaseUrl) && !url.StartsWith('/')) url = $"/{url}";
            if (!url.StartsWith(Program.ConfigJson.WorkerLinks.BaseUrl)) url = $"{Program.ConfigJson.WorkerLinks.BaseUrl}{url}";
            
            await ctx.FollowUpAsync(new DiscordFollowupMessageBuilder().WithContent(
                $"<{url}>\npoints to:\n<{responseData}>"));
        }

        public class CloudflareResponse
        {
            [JsonProperty("result")] public List<KvEntry> Result { get; set; }
        }

        public class KvEntry
        {
            [JsonProperty("name")] public string Name { get; set; }
        }
    }
}