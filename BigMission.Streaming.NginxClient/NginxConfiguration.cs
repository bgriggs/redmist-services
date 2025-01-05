using BigMission.Streaming.Shared.Models;
using NLog;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace BigMission.Streaming.NginxClient;

public partial class NginxConfiguration
{
    const string YOU_TUBE_CONFIG = "                        push rtmp://a.rtmp.youtube.com/live2/{0};\n";
    const string FACEBOOK_CONFIG = "                        push rtmp://127.0.0.1:1936/rtmp/{0};\n";
    const string CONFIG_STREAM_START = "                        # Start stream destinations";
    const string CONFIG_STREAM_END = "                        # End stream destinations";

    public static string SetStreamDestinations(string config, ImmutableArray<NginxStreamPush> streamDestinations)
    {
        var sb = new StringBuilder();
        bool hasFb = false;
        foreach (var destination in streamDestinations)
        {
            switch (destination.Platform)
            {
                case Platform.YouTube:
                    sb.Append(string.Format(YOU_TUBE_CONFIG, destination.StreamKey));
                    break;
                case Platform.Facebook:
                    if (hasFb)
                    {
                        throw new Exception("Only one Facebook stream is permitted.");
                    }
                    sb.Append(string.Format(FACEBOOK_CONFIG, destination.StreamKey));
                    hasFb = true;
                    break;
                default:
                    throw new NotSupportedException($"Platform {destination.Platform} is not supported.");
            }
        }

        config = RemoveStreamDestinations(config);
        config = InsertStreamDestinations(config, sb.ToString());

        return config;
    }

    private static string RemoveStreamDestinations(string config)
    {
        // Find index of end of stream destinations comment plus the LF character
        var start = config.IndexOf(CONFIG_STREAM_START) + CONFIG_STREAM_START.Length + 1;
        var end = config.IndexOf(CONFIG_STREAM_END);
        if (start == -1 || end == -1)
        {
            throw new Exception("Stream destinations not found in config.");
        }
        return config.Remove(start, end - start);
    }

    private static string InsertStreamDestinations(string config, string destinations)
    {
        // Find index of end of stream destinations comment plus the LF character
        var start = config.IndexOf(CONFIG_STREAM_START) + CONFIG_STREAM_START.Length + 1;
        if (start == -1)
        {
            throw new Exception("Stream destinations not found in config.");
        }
        return config.Insert(start, destinations);
    }

    public static ImmutableArray<NginxStreamPush> GetStreams(string config)
    {
        var streams = new List<NginxStreamPush>();
        var start = config.IndexOf(CONFIG_STREAM_START) + CONFIG_STREAM_START.Length + 1;
        var end = config.IndexOf(CONFIG_STREAM_END);
        if (start == -1 || end == -1)
        {
            throw new Exception("Stream destinations not found in config.");
        }

        var contents = config[start..end];
        var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var stream = ParseStream(line);
            if (stream != null)
            {
                streams.Add(stream);
            }
        }

        return [.. streams];
    }

    private static NginxStreamPush? ParseStream(string line)
    {
        var ytRegex = YouTubeStreamRegex();
        var ytMatch = ytRegex.Match(line);
        if (ytMatch.Success)
        {
            return new NginxStreamPush
            {
                Platform = Platform.YouTube,
                StreamKey = ytMatch.Groups["key"].Value
            };
        }

        var fbRegex = FacebookStreamRegex();
        var fbMatch = fbRegex.Match(line);
        if (fbMatch.Success)
        {
            return new NginxStreamPush
            {
                Platform = Platform.Facebook,
                StreamKey = fbMatch.Groups["key"].Value
            };
        }

        return null;
    }

    [GeneratedRegex(@".*a.rtmp.youtube.com/live2/(?'key'.+);")]
    private static partial Regex YouTubeStreamRegex();

    [GeneratedRegex(@".*rtmp://127.0.0.1:1936/rtmp/(?'key'.+);")]
    private static partial Regex FacebookStreamRegex();
}
