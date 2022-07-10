using BigMission.Cache.Models.ControlLog;
using BigMission.Database.Models;
using BigMission.RaceControlLog.Configuration;
using Microsoft.Extensions.Configuration;
using NLog;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BigMission.RaceControlLog.LogProcessing
{
    /// <summary>
    /// Sends text message notifications to users.
    /// </summary>
    internal class SmsNotification : ILogProcessor
    {
        private readonly IConfiguration config;
        private ILogger Logger { get; }

        private readonly Dictionary<(int sysEvent, string car, int order), RaceControlLogEntry> last = new();
        private bool isFirstUpdate = true;

        public SmsNotification(IConfiguration config, ILogger logger)
        {
            TwilioClient.Init(config["Twilio:AccountSid"], config["Twilio:AuthToken"]);
            this.config = config;
            Logger = logger;
        }


        public async Task Process(int eventId, IEnumerable<RaceControlLogEntry> log, ConfigurationEventData configurationEventData)
        {
            var carSubs = configurationEventData.GetCarSmsSubscriptions();
            foreach (var entry in log)
            {
                await ProcessCarLogEntry(eventId, entry.Car1.ToUpper(), entry, carSubs);
                await ProcessCarLogEntry(eventId, entry.Car2.ToUpper(), entry, carSubs);
            }
            isFirstUpdate = false;
        }

        private async Task ProcessCarLogEntry(int sysEvent, string car, RaceControlLogEntry logEntry, Dictionary<(int sysEvent, string car), AbpUser[]> carSubscriptions)
        {
            // See if this car has a user subscription
            if (!string.IsNullOrWhiteSpace(car) && carSubscriptions.TryGetValue((sysEvent, car), out var subs))
            {
                var key = (sysEvent, car, logEntry.OrderId);
                if (last.TryGetValue(key, out var lastEntry))
                {
                    // See if existing entry has changed
                    if (lastEntry.HasChanged(logEntry))
                    {
                        last[key] = logEntry;
                        await SendSms(subs, logEntry, true);
                    }
                }
                else // This is a new log entry
                {
                    last[key] = logEntry;
                    await SendSms(subs, logEntry);
                }
            }
        }

        private async Task SendSms(AbpUser[] subscriptions, RaceControlLogEntry logEntry, bool isUpdate = false)
        {
            // When service is reloaded, do not immediatly send updates since there is not state to compare and we don't want to spam.
            if (isFirstUpdate) { return; }

            var time = logEntry.Timestamp.ToString("h:mm tt");
            var message = "NEW Penality: ";
            if (isUpdate)
            {
                message = "Penality Update: ";
            }
            message += $"{time} #{logEntry.Car1}";
            if (!string.IsNullOrEmpty(logEntry.Car2))
            {
                message += $"/{logEntry.Car2}";
            }
            message += $"\n{logEntry.Note}\n{logEntry.Status}";
            if (!string.IsNullOrWhiteSpace(logEntry.PenalityAction))
            {
                message += $" - {logEntry.PenalityAction}";
            }
            if (!string.IsNullOrWhiteSpace(logEntry.OtherNotes))
            {
                message += $"\n{logEntry.OtherNotes}";
            }

            foreach (var user in subscriptions)
            {
                var resource = await MessageResource.CreateAsync(
                    body: message,
                    @from: new Twilio.Types.PhoneNumber(config["Twilio:SenderNumber"]),
                    to: new Twilio.Types.PhoneNumber(user.PhoneNumber)
                );

                var logmsg = $"Sent SMS to '{user.PhoneNumber}'.";
                if (!string.IsNullOrWhiteSpace(resource.ErrorMessage))
                {
                    logmsg += $"Error: {resource.ErrorMessage}";
                }
                Logger.Debug(logmsg);
            }
        }

    }
}
