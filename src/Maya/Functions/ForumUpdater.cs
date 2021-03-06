﻿using Discord;
using Discord.WebSocket;
using Maya.Controllers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Maya.Modules.Functions
{
    public class ForumUpdater
    {
        private MainHandler MainHandler;
        private Timer timer;
        private string wgstaffLastUrl;
        private string recentLastUrl;

        public ForumUpdater(MainHandler MainHandler)
        {
            this.MainHandler = MainHandler;
            wgstaffLastUrl = recentLastUrl = null;
        }

        public Task Initialize()
        {
            timer = new Timer(UpdateTime, null, 3000, 300000);
            return Task.CompletedTask;
        }

        public async void UpdateTime(object state)
        {
            try
            {
                string html;
                using (var httpClient = new HttpClient())
                {
                    string link = "http://forum.worldofwarships.com/";
                    var res = await httpClient.GetAsync(link);
                    if (!res.IsSuccessStatusCode)
                        return;
                    html = await res.Content.ReadAsStringAsync();
                }
                html = html.Split(new string[] { "<h3>WG staff posts</h3>" }, StringSplitOptions.RemoveEmptyEntries)[1];
                string[] divs = html.Split(new string[] { "<div class='left'>" }, StringSplitOptions.RemoveEmptyEntries);
                List<EmbedBuilder> list = new List<EmbedBuilder>();
                if (wgstaffLastUrl != null)
                {
                    for (int i = 1; i < 11; i++)
                    {
                        EmbedBuilder now = await HtmlToEmbedBuilderAsync("WG staff posts", divs[i]);
                        if (wgstaffLastUrl.Equals(now.Url))
                            break;
                        else
                            list.Add(now);
                    }
                    if (list.Count() != 0)
                    {
                        wgstaffLastUrl = list.First().Url;
                        if (list.Count() == 10)
                            list.Clear();
                    }
                }
                else
                    wgstaffLastUrl = (await HtmlToEmbedBuilderAsync("WG staff posts", divs[1])).Url;
                if (recentLastUrl != null)
                {
                    int c = list.Count();
                    for (int i = 11; i < 21; i++)
                    {
                        EmbedBuilder now = await HtmlToEmbedBuilderAsync("Recent Topics", divs[i]);
                        if (recentLastUrl.Equals(now.Url))
                            break;
                        else
                            list.Add(now);
                    }
                    if (list.Count() != c)
                    {
                        recentLastUrl = list.ElementAt(c).Url;
                        if (list.Count() == c + 10)
                            list.RemoveRange(c, list.Count() - c);
                    }
                }
                else
                    recentLastUrl = (await HtmlToEmbedBuilderAsync("Recent Topics", divs[11])).Url;
                if (list.Count != 0)
                    foreach (SocketGuild guild in MainHandler.Client.Guilds)
                    {
                        var w = MainHandler.GuildConfigHandler(guild).GetForumUpdates();
                        if (w.IsEnabled)
                        {
                            ITextChannel tc = guild.FindTextChannel(w.TextChannel);
                            if (tc != null)
                                for (int i = list.Count - 1; i >= 0; i--)
                                    await MainHandler.ExceptionHandler.SendMessageAsyncEx("ForumUpdaterMessage", () => tc.SendMessageAsync("", false, list.ElementAt(i)));
                        }
                    }
            }
            catch(Exception e)
            {
                await MainHandler.ExceptionHandler.WriteException(e);
            }
        }

        private async Task<EmbedBuilder> HtmlToEmbedBuilderAsync(string place, string str)
        {
            str = WebUtility.HtmlDecode(str);

            string[] url = str.Split(new string[] { "<a href=\"" }, StringSplitOptions.None)[1].Split(new char[] { '"' }, 2);
            string[] info = str.Split(new string[] { "ipsType_smaller'>" }, StringSplitOptions.None)[1].Split(new string[] { "</p>" }, StringSplitOptions.None)[0].Trim().Split('-');

            EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
            eab.Url = url[0];
            eab.Name = info[0].Trim();
            eab.IconUrl = str.Split(new string[] { "<img src='" }, StringSplitOptions.None)[1].Split('\'')[0];

            EmbedBuilder eb = new EmbedBuilder();
            eb.Author = eab;
            eb.Url = url[0];
            eb.Color = Utils.GetRandomColor();
            string text="";
            try { text=await GetForumThreadTextAsync(url[0]); } catch (Exception ex) { Console.WriteLine(ex.ToString()); text = ""; }
            eb.Description = $"[{url[1].Split(new string[] { "'>" }, StringSplitOptions.None)[1].Split(new string[] { "</a>" }, StringSplitOptions.None)[0]}]({url[0]})" + text;
            eb.Footer = new EmbedFooterBuilder().WithText(place);
            info[1] = info[1].Trim();
            Nullable<DateTime> dt = null;
            if (info[1].StartsWith("Today, ")|| info[1].StartsWith("Yesterday, "))
            {
                DateTime now = DateTime.UtcNow;
                if (info[1].StartsWith("Yesterday, "))
                    now = now.AddDays(-1);
                string[] hour_full = info[1].Split(' ');
                dt = DateTime.ParseExact($"{now.ToString("dd/MM/yyyy")} {hour_full[1]} {hour_full[2]}", "dd/MM/yyyy hh:mm tt", CultureInfo.InvariantCulture);
            }
            else
            {
                try
                {
                    dt = DateTime.ParseExact(info[1], "MMM dd yyyy hh:mm tt", CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    dt = DateTime.Parse(info[1]);
                }
            }
            eb.Timestamp = dt;

            return eb;
        }

        private async Task<string> GetForumThreadTextAsync(string link)
        {
            try
            {
                string html;
                using (var httpClient = new HttpClient())
                {
                    var res = await httpClient.GetAsync(link);
                    if (!res.IsSuccessStatusCode)
                        return "";
                    html = await res.Content.ReadAsStringAsync();
                }
                string text;
                if (link.Contains("&p="))
                    text = Regex.Replace(WebUtility.HtmlDecode(Utils.StripTags(html.Split(new string[] { @"#entry" + link.Split(new string[] { "&p=" }, StringSplitOptions.None)[1] + "' rel='bookmark'" }, StringSplitOptions.None)[1].Split(new string[] { @"post entry-content '>" }, StringSplitOptions.None)[1].Split(new string[] { "<script type='text/" }, StringSplitOptions.None)[0]).Trim()), @"\s+", " ");
                else
                    text = Regex.Replace(WebUtility.HtmlDecode(html.Split(new string[] { "<meta name=\"description\" content=\"" }, StringSplitOptions.None)[1].Split(new string[] { "\" />" }, StringSplitOptions.None)[0]).Split(new char[] { ':' }, 2)[1].Trim(), @"\s+", " ");
                text = "\n" + (text.Length > 200 ? text.Substring(0, 200) + "..." : text);
                return text;
            }
            catch(Exception e)
            {
                await MainHandler.ExceptionHandler.WriteException(e);
                return "";
            }
        }
    }
}
