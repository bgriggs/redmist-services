using BigMission.Streaming.Shared.Models;

namespace BigMission.Streaming.NginxClient.Tests;

[TestClass]
public sealed class NginxConfigurationTests
{
    // ****** Make sure to set the nginx.conf file to LF line endings (bottom right of the VS editor page in the file). ******

    [TestMethod]
    public void NoDestination_Test()
    {
        var conf = File.ReadAllText("nginx.conf");
        var updatedConf = NginxConfiguration.SetStreamDestinations(conf, []);

        var expectedLines = File.ReadAllLines("nginx.conf").ToList();
        expectedLines.RemoveRange(73, 2);
        var expected = string.Join('\n', expectedLines); // LF/Linux line endings

        //for (int i = 0; i < expected.Length; i++)
        //{
        //    if (expected[i] != updatedConf[i])
        //    {
        //        Console.WriteLine($"Expected: {expected[i]}");
        //        Console.WriteLine($"Actual: {updatedConf[i]}");
        //        Console.WriteLine($"Index: {i}");
        //        break;
        //    }
        //}

        Assert.AreEqual(expected, updatedConf);
    }

    [TestMethod]
    public void YouTubeDestination_Test()
    {
        var conf = File.ReadAllText("nginx.conf");
        var yt = new NginxStreamPush
        {
            Platform = Platform.YouTube,
            StreamKey = "fh1p-3r7j-s2ed-mfjh-c261"
        };
        var updatedConf = NginxConfiguration.SetStreamDestinations(conf, [yt]);

        var expectedLines = File.ReadAllLines("nginx.conf").ToList();
        expectedLines.RemoveRange(74, 1);
        var expected = string.Join('\n', expectedLines); // LF/Linux line endings

        Assert.AreEqual(expected, updatedConf);
    }

    [TestMethod]
    public void FacebookDestination_Test()
    {
        var conf = File.ReadAllText("nginx.conf");
        var fb = new NginxStreamPush
        {
            Platform = Platform.Facebook,
            StreamKey = "FB-111610481641111-0-Abz-iS3v16pgdU8D"
        };
        var updatedConf = NginxConfiguration.SetStreamDestinations(conf, [fb]);

        var expectedLines = File.ReadAllLines("nginx.conf").ToList();
        expectedLines.RemoveRange(73, 1);
        var expected = string.Join('\n', expectedLines); // LF/Linux line endings

        Assert.AreEqual(expected, updatedConf);
    }

    [TestMethod]
    [ExpectedException(typeof(Exception))]
    public void MultipleFacebookDestination_Error_Test()
    {
        var conf = File.ReadAllText("nginx.conf");
        var fb = new NginxStreamPush
        {
            Platform = Platform.Facebook,
            StreamKey = "FB-111610481641111-0-Abz-iS3v16pgdU8D"
        };
        NginxConfiguration.SetStreamDestinations(conf, [fb, fb]);
    }

    [TestMethod]
    public void MultipleDestination_Test()
    {
        var conf = File.ReadAllText("nginx.conf");
        var yt = new NginxStreamPush
        {
            Platform = Platform.YouTube,
            StreamKey = "fh1p-3r7j-s2ed-mfjh-c261"
        };
        var fb = new NginxStreamPush
        {
            Platform = Platform.Facebook,
            StreamKey = "FB-111610481641111-0-Abz-iS3v16pgdU8D"
        };
        var updatedConf = NginxConfiguration.SetStreamDestinations(conf, [yt, fb]);

        var expected = File.ReadAllText("nginx.conf");

        Assert.AreEqual(expected, updatedConf);
    }

    [TestMethod]
    public void GetStreams_Test()
    {
        var conf = File.ReadAllText("nginx.conf");
        var streams = NginxConfiguration.GetStreams(conf);
        Assert.IsNotNull(streams);
        Assert.AreEqual(2, streams.Length);
        Assert.AreEqual(Platform.YouTube, streams[0].Platform);
        Assert.AreEqual("fh1p-3r7j-s2ed-mfjh-c261", streams[0].StreamKey);
        Assert.AreEqual(Platform.Facebook, streams[1].Platform);
        Assert.AreEqual("FB-111610481641111-0-Abz-iS3v16pgdU8D", streams[1].StreamKey);
    }

}
