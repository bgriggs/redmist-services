using BigMission.Cache;
using BigMission.DeviceApp.Shared;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using NLog;
using NUglify.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Processes application status from the in car apps. (not channel status)
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly SemaphoreSlim lapCheckLock = new SemaphoreSlim(1, 1);
        private readonly IDataContext dataContext;
        private readonly ITimerHelper eventSubTimer;
        private readonly ITimerHelper lapCheckTimer;
        private readonly IFuelRangeContext fuelRangeContext;

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking, IDataContext dataContext, 
            ITimerHelper eventSubTimer, ITimerHelper lapCheckTimer, IFuelRangeContext fuelRangeContext)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            this.dataContext = dataContext;
            this.eventSubTimer = eventSubTimer;
            this.lapCheckTimer = lapCheckTimer;
            this.fuelRangeContext = fuelRangeContext;
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            //InitializeEvents();

            //LoadFullTestLaps();
            //var c = events[28141].Cars["134"];

            //foreach (var p in c.Pits)
            //{
            //    Console.WriteLine($"Time={p.EstPitStopSecs} TS={p.EndPitTime} RefLap={p.RefLapTimeSecs}");
            //    Console.WriteLine($"\t{p.Comments}");
            //    foreach (var l in ((PitStop)p).Laps)
            //    {
            //        Console.WriteLine($"\tLap={l.Value.CurrentLap} TS={l.Value.Timestamp} LastPitLap={l.Value.LastPitLap} LapTime={l.Value.LastLapTimeSeconds}");
            //    }
            //}


            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
        }

      
        


        //private async void ReceivedEventCallback(ChannelDataSetDto chDataSet)
        //{
        //    try
        //    {
        //        if (!eventSubscriptions.Any())
        //        {
        //            return;
        //        }

        //        Event[] events;
        //        eventSubscriptionLock.Wait();
        //        try 
        //        {
        //            events = eventSubscriptions.Values.ToArray();
        //        }
        //        finally
        //        {
        //            eventSubscriptionLock.Release();
        //        }

        //        Parallel.ForEach(events, evt =>
        //        {
        //            evt.UpdateTelemetry(chDataSet).Wait();
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.Error(ex, "Unable to process telemetry data");
        //    }
        //}

        #region Testing

        //private void LoadFullTestLaps()
        //{
        //    var lines = File.ReadAllLines("TestLaps.csv");
        //    var st = new Stack<Lap>();
        //    for (int i = 1; i < lines.Length; i++)
        //    {
        //        var lap = ParseTestLap(lines[i]);
        //        st.Push(lap);
        //        if (!eventSubscriptions.TryGetValue(lap.EventId, out _))
        //        {
        //            Event evt = new Event(lap.EventId.ToString(), cacheMuxer, Config["ConnectionString"]);
        //            eventSubscriptions[lap.EventId] = evt;
        //        }
        //    }

        //    var eventLaps = st.GroupBy(l => l.EventId);
        //    foreach (var el in eventLaps)
        //    {
        //        foreach (var lap in el)
        //        {
        //            eventSubscriptions[el.Key].UpdateLap(lap);
        //            Logger.Trace($"Updating lap for {lap.CarNumber}");
        //            Thread.Sleep(100);
        //        }
        //    }
        //}

        //private static Lap ParseTestLap(string line)
        //{
        //    var values = line.Split('\t');
        //    var l = new Lap();
        //    l.EventId = int.Parse(values[0]);
        //    l.CarNumber = values[1];
        //    l.Timestamp = DateTime.Parse(values[2]);
        //    l.ClassName = values[3];
        //    l.PositionInRun = int.Parse(values[4]);
        //    l.CurrentLap = int.Parse(values[5]);
        //    l.LastLapTimeSeconds = double.Parse(values[6]);
        //    l.LastPitLap = int.Parse(values[7]);
        //    l.PitStops = int.Parse(values[8]);

        //    return l;
        //}

        #endregion
    }
}
