// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kan.Core.Score
{
    /// <summary>
    /// Who made the cover a level is built on: levels
    /// often come from covers that people record for the love of it,
    /// sometimes together, and upload. The song's artist stays in the
    /// level's metadata; this credits the performers and links to their
    /// pages, so players who like it can go and thank and support them.
    /// Stored as <see cref="FILE_NAME"/> inside the level's files, so it
    /// travels with the level.
    /// </summary>
    public class KanCredits
    {
        public const string FILE_NAME = @"kan.credits.json";

        public const int VERSION = 1;

        public int Version { get; set; } = VERSION;

        /// <summary>Where the cover was published (e.g. its YouTube video).</summary>
        public string CoverUrl { get; set; } = string.Empty;

        /// <summary>The people who made the cover, in the order they are credited.</summary>
        public List<CoverPerformer> Performers { get; set; } = new List<CoverPerformer>();

        /// <summary>A few words of thanks or context, shown with the credits.</summary>
        public string Note { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(CoverUrl) && string.IsNullOrWhiteSpace(Note) && Performers.All(p => p.IsEmpty);

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>The file's content, without empty performers or links.</summary>
        public string ToJson()
        {
            var clean = new KanCredits
            {
                CoverUrl = CoverUrl.Trim(),
                Note = Note.Trim(),
                Performers = Performers.Where(p => !p.IsEmpty).Select(p => new CoverPerformer
                {
                    Name = p.Name.Trim(),
                    Role = p.Role.Trim(),
                    Links = p.Links.Select(l => l.Trim()).Where(l => l.Length > 0).ToList(),
                }).ToList(),
            };

            return JsonSerializer.Serialize(clean, options);
        }

        /// <summary>Null when the text is not a credits file.</summary>
        public static KanCredits? FromJson(string json)
        {
            try
            {
                var credits = JsonSerializer.Deserialize<KanCredits>(json, options);
                if (credits == null) return null;

                credits.CoverUrl ??= string.Empty;
                credits.Note ??= string.Empty;
                credits.Performers ??= new List<CoverPerformer>();

                foreach (var p in credits.Performers)
                {
                    p.Name ??= string.Empty;
                    p.Role ??= string.Empty;
                    p.Links ??= new List<string>();
                }

                return credits;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// A link as typed, made openable: "youtube.com/@x" gets "https://".
        /// Null when it is not a web address (only http/https are opened).
        /// </summary>
        public static string? NormaliseLink(string link)
        {
            link = link.Trim();
            if (link.Length == 0 || link.Any(char.IsWhiteSpace)) return null;

            if (!link.Contains(@"://", StringComparison.Ordinal))
                link = @"https://" + link;

            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
            if (!uri.Host.Contains('.')) return null;

            return uri.ToString();
        }

        /// <summary>The site a link points to, in plain words ("YouTube", "Instagram"…), or its host.</summary>
        public static string SiteName(string link)
        {
            string? url = NormaliseLink(link);
            if (url == null) return string.Empty;

            string host = new Uri(url).Host.ToLowerInvariant();
            if (host.StartsWith(@"www.", StringComparison.Ordinal)) host = host[4..];

            foreach (var (domain, name) in known_sites)
            {
                if (host == domain || host.EndsWith(@"." + domain, StringComparison.Ordinal))
                    return name;
            }

            return host;
        }

        private static readonly (string domain, string name)[] known_sites =
        {
            (@"youtube.com", @"YouTube"), (@"youtu.be", @"YouTube"),
            (@"instagram.com", @"Instagram"), (@"tiktok.com", @"TikTok"),
            (@"x.com", @"X"), (@"twitter.com", @"X"),
            (@"facebook.com", @"Facebook"), (@"twitch.tv", @"Twitch"),
            (@"soundcloud.com", @"SoundCloud"), (@"bandcamp.com", @"Bandcamp"),
            (@"spotify.com", @"Spotify"), (@"music.apple.com", @"Apple Music"),
            (@"patreon.com", @"Patreon"), (@"ko-fi.com", @"Ko-fi"),
            (@"buymeacoffee.com", @"Buy Me a Coffee"), (@"paypal.me", @"PayPal"),
            (@"bsky.app", @"Bluesky"), (@"linktr.ee", @"Linktree"),
            (@"nicovideo.jp", @"niconico"), (@"bilibili.com", @"bilibili"),
        };
    }

    /// <summary>One person who made the cover.</summary>
    public class CoverPerformer
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>What they did: "ocarina", "vocals", "arrangement", "mixing"…</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>Their pages: channel, social networks, a place to support them.</summary>
        public List<string> Links { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Role) && Links.All(string.IsNullOrWhiteSpace);
    }
}
