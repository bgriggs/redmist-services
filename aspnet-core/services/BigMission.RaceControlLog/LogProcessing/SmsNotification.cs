using BigMission.Cache.Models.ControlLog;
using BigMission.Database.Models;
using BigMission.RaceControlLog.Configuration;
using BigMission.RaceControlLog.EventStatus;
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
        private IEventStatus EventStatus { get; }

        private readonly Dictionary<(int sysEvent, string car, int order), RaceControlLogEntry> last = new();
        private bool isFirstUpdate = true;

        public SmsNotification(IConfiguration config, ILogger logger, IEventStatus eventStatus)
        {
            TwilioClient.Init(config["Twilio:AccountSid"], config["Twilio:AuthToken"]);
            this.config = config;
            Logger = logger;
            EventStatus = eventStatus;
        }


        public async Task Process(RaceEventSetting evt, IEnumerable<RaceControlLogEntry> log, ConfigurationEventData configurationEventData)
        {
            var carSubs = configurationEventData.GetCarSmsSubscriptions();
            foreach (var entry in log)
            {
                await ProcessCarLogEntry(evt, entry.Car1.ToUpper(), entry, carSubs);
                await ProcessCarLogEntry(evt, entry.Car2.ToUpper(), entry, carSubs);
            }
            isFirstUpdate = false;
        }

        private async Task ProcessCarLogEntry(RaceEventSetting evt, string car, RaceControlLogEntry logEntry, Dictionary<(int sysEvent, string car), AbpUser[]> carSubscriptions)
        {
            // See if this car has a user subscription
            if (!string.IsNullOrWhiteSpace(car) && carSubscriptions.TryGetValue((evt.Id, car), out var subs))
            {
                // When there is a race hero event availalbe, use it to further gate SMS sending to when the event is live
                if (int.TryParse(evt.RaceHeroEventId, out int rhId))
                {
                    var rhevt = await EventStatus.GetEventStatusAsync(rhId);
                    // If there is no status available, send the SMS
                    if (!(rhevt?.IsLive ?? true))
                    {
                        Logger.Debug("Skipping SMS messges-Event is not live");
                        return;
                    }
                }
                
                var key = (evt.Id, car, logEntry.OrderId);
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
            var message = "NEW Infraction: ";
            if (isUpdate)
            {
                message = "Infraction Update: ";
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
