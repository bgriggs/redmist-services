using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using BigMission.Database.Models;

namespace BigMission.Database
{
    public partial class RedMist : DbContext
    {
        public RedMist()
        {
        }

        public RedMist(DbContextOptions<RedMist> options)
            : base(options)
        {
        }

        public virtual DbSet<AbpUser> AbpUsers { get; set; }
        public virtual DbSet<AlarmCondition> AlarmConditions { get; set; }
        public virtual DbSet<AlarmTrigger> AlarmTriggers { get; set; }
        public virtual DbSet<ApiKey> ApiKeys { get; set; }
        public virtual DbSet<ArchivePurgeSetting> ArchivePurgeSettings { get; set; }
        public virtual DbSet<CanAppConfig> CanAppConfigs { get; set; }
        public virtual DbSet<Car> Cars { get; set; }
        public virtual DbSet<CarAlarm> CarAlarms { get; set; }
        public virtual DbSet<CarAlarmLog> CarAlarmLogs { get; set; }
        public virtual DbSet<CarRaceLap> CarRaceLaps { get; set; }
        public virtual DbSet<ChannelLog> ChannelLogs { get; set; }
        public virtual DbSet<ChannelMapping> ChannelMappings { get; set; }
        public virtual DbSet<DeviceAppConfig> DeviceAppConfigs { get; set; }
        public virtual DbSet<EcuFuelCalcConfig> EcuFuelCalcConfigs { get; set; }
        public virtual DbSet<EventFlag> EventFlags { get; set; }
        public virtual DbSet<FuelCarAppSetting> FuelCarAppSettings { get; set; }
        public virtual DbSet<FuelRangeSetting> FuelRangeSettings { get; set; }
        public virtual DbSet<FuelRangeStint> FuelRangeStints { get; set; }
        public virtual DbSet<KeypadCarAppCanStateRule> KeypadCarAppCanStateRules { get; set; }
        public virtual DbSet<KeypadCarAppConfig> KeypadCarAppConfigs { get; set; }
        public virtual DbSet<KeypadCarAppMomentaryButtonRule> KeypadCarAppMomentaryButtonRules { get; set; }
        public virtual DbSet<RaceEventSetting> RaceEventSettings { get; set; }
        public virtual DbSet<RaceHeroSetting> RaceHeroSettings { get; set; }
        public virtual DbSet<SimulationSetting> SimulationSettings { get; set; }
        public virtual DbSet<TeamRetentionPolicy> TeamRetentionPolicies { get; set; }
        public virtual DbSet<TpmsConfig> TpmsConfigs { get; set; }
        public virtual DbSet<UdpTelemetryConfig> UdpTelemetryConfigs { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AbpUser>(entity =>
            {
                entity.HasIndex(e => e.CreatorUserId, "IX_AbpUsers_CreatorUserId");

                entity.HasIndex(e => e.DeleterUserId, "IX_AbpUsers_DeleterUserId");

                entity.HasIndex(e => e.LastModifierUserId, "IX_AbpUsers_LastModifierUserId");

                entity.HasIndex(e => new { e.TenantId, e.NormalizedEmailAddress }, "IX_AbpUsers_TenantId_NormalizedEmailAddress");

                entity.HasIndex(e => new { e.TenantId, e.NormalizedUserName }, "IX_AbpUsers_TenantId_NormalizedUserName");

                entity.Property(e => e.AuthenticationSource).HasMaxLength(64);

                entity.Property(e => e.ConcurrencyStamp).HasMaxLength(128);

                entity.Property(e => e.EmailAddress)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(e => e.EmailConfirmationCode).HasMaxLength(328);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(64);

                entity.Property(e => e.NormalizedEmailAddress)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(e => e.NormalizedUserName)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(e => e.Password)
                    .IsRequired()
                    .HasMaxLength(128);

                entity.Property(e => e.PasswordResetCode).HasMaxLength(328);

                entity.Property(e => e.PhoneNumber).HasMaxLength(32);

                entity.Property(e => e.SecurityStamp).HasMaxLength(128);

                entity.Property(e => e.Surname)
                    .IsRequired()
                    .HasMaxLength(64);

                entity.Property(e => e.UserName)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.HasOne(d => d.CreatorUser)
                    .WithMany(p => p.InverseCreatorUser)
                    .HasForeignKey(d => d.CreatorUserId);

                entity.HasOne(d => d.DeleterUser)
                    .WithMany(p => p.InverseDeleterUser)
                    .HasForeignKey(d => d.DeleterUserId);

                entity.HasOne(d => d.LastModifierUser)
                    .WithMany(p => p.InverseLastModifierUser)
                    .HasForeignKey(d => d.LastModifierUserId);
            });

            modelBuilder.Entity<AlarmCondition>(entity =>
            {
                entity.HasIndex(e => e.CarAlarmsId, "IX_AlarmConditions_CarAlarmsId");

                entity.Property(e => e.ChannelValue).HasMaxLength(25);

                entity.Property(e => e.ConditionType)
                    .IsRequired()
                    .HasMaxLength(15);

                entity.Property(e => e.OnFor).HasMaxLength(6);

                entity.HasOne(d => d.CarAlarms)
                    .WithMany(p => p.AlarmConditions)
                    .HasForeignKey(d => d.CarAlarmsId);
            });

            modelBuilder.Entity<AlarmTrigger>(entity =>
            {
                entity.HasIndex(e => e.CarAlarmsId, "IX_AlarmTriggers_CarAlarmsId");

                entity.Property(e => e.ChannelValue).HasMaxLength(25);

                entity.Property(e => e.Color).HasMaxLength(25);

                entity.Property(e => e.Message).HasMaxLength(100);

                entity.Property(e => e.Severity).HasMaxLength(20);

                entity.Property(e => e.TriggerType)
                    .IsRequired()
                    .HasMaxLength(25);

                entity.HasOne(d => d.CarAlarms)
                    .WithMany(p => p.AlarmTriggers)
                    .HasForeignKey(d => d.CarAlarmsId);
            });

            modelBuilder.Entity<ApiKey>(entity =>
            {
                entity.Property(e => e.Key)
                    .IsRequired()
                    .HasMaxLength(50);
            });

            modelBuilder.Entity<CanAppConfig>(entity =>
            {
                entity.ToTable("CanAppConfig");

                entity.Property(e => e.ApiUrl).HasMaxLength(300);

                entity.Property(e => e.Can1Enable)
                    .IsRequired()
                    .HasDefaultValueSql("(CONVERT([bit],(0)))");

                entity.Property(e => e.Can2Arg).HasMaxLength(100);

                entity.Property(e => e.Can2Bitrate).HasMaxLength(12);

                entity.Property(e => e.Can2Cmd).HasMaxLength(100);

                entity.Property(e => e.Can2Enable)
                    .IsRequired()
                    .HasDefaultValueSql("(CONVERT([bit],(0)))");

                entity.Property(e => e.CanArg).HasMaxLength(100);

                entity.Property(e => e.CanBitrate).HasMaxLength(12);

                entity.Property(e => e.CanCmd).HasMaxLength(100);

                entity.Property(e => e.CanDecoderVersion).HasDefaultValueSql("((1))");

                entity.Property(e => e.EnableLocalRaceHeroStatus)
                    .IsRequired()
                    .HasDefaultValueSql("(CONVERT([bit],(0)))");

                entity.Property(e => e.EnableModemResetWatchdog)
                    .IsRequired()
                    .HasDefaultValueSql("(CONVERT([bit],(0)))");

                entity.Property(e => e.EnableRebootOnDisconnect)
                    .IsRequired()
                    .HasDefaultValueSql("(CONVERT([bit],(0)))");

                entity.Property(e => e.SilentOnCanBus)
                    .IsRequired()
                    .HasDefaultValueSql("(CONVERT([bit],(0)))");
            });

            modelBuilder.Entity<Car>(entity =>
            {
                entity.Property(e => e.Color).HasMaxLength(20);

                entity.Property(e => e.Make).HasMaxLength(20);

                entity.Property(e => e.Model).HasMaxLength(20);

                entity.Property(e => e.Number)
                    .IsRequired()
                    .HasMaxLength(8);

                entity.Property(e => e.Transponder).HasMaxLength(20);

                entity.Property(e => e.Transponder2).HasMaxLength(20);

                entity.Property(e => e.Year).HasMaxLength(4);
            });

            modelBuilder.Entity<CarAlarm>(entity =>
            {
                entity.Property(e => e.AlarmGroup).HasMaxLength(30);

                entity.Property(e => e.ConditionOption)
                    .IsRequired()
                    .HasMaxLength(10);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(30);
            });

            modelBuilder.Entity<CarAlarmLog>(entity =>
            {
                entity.HasKey(e => new { e.AlarmId, e.Timestamp, e.IsActive });

                entity.ToTable("CarAlarmLog");
            });

            modelBuilder.Entity<CarRaceLap>(entity =>
            {
                entity.HasKey(e => new { e.EventId, e.CarNumber, e.Timestamp });

                entity.Property(e => e.CarNumber).HasMaxLength(6);

                entity.Property(e => e.ClassName).HasMaxLength(20);

                entity.Property(e => e.Flag).HasDefaultValueSql("(CONVERT([tinyint],(0)))");
            });

            modelBuilder.Entity<ChannelLog>(entity =>
            {
                entity.HasKey(e => new { e.Timestamp, e.DeviceAppId, e.ChannelId });

                entity.ToTable("ChannelLog");
            });

            modelBuilder.Entity<ChannelMapping>(entity =>
            {
                entity.Property(e => e.ChannelName).HasMaxLength(50);

                entity.Property(e => e.GroupTag).HasMaxLength(50);

                entity.Property(e => e.ReservedName).HasMaxLength(50);
            });

            modelBuilder.Entity<DeviceAppConfig>(entity =>
            {
                entity.ToTable("DeviceAppConfig");

                entity.Property(e => e.DeviceType).IsRequired();
            });

            modelBuilder.Entity<EcuFuelCalcConfig>(entity =>
            {
                entity.ToTable("EcuFuelCalcConfig");

                entity.Property(e => e.ConsumptionMode).HasMaxLength(30);

                entity.Property(e => e.OutputVolumeUnits).HasMaxLength(10);
            });

            modelBuilder.Entity<EventFlag>(entity =>
            {
                entity.Property(e => e.Flag).IsRequired();
            });

            modelBuilder.Entity<FuelCarAppSetting>(entity =>
            {
                entity.Property(e => e.FuelDatabaseConnection)
                    .IsRequired()
                    .HasMaxLength(250);
            });

            modelBuilder.Entity<FuelRangeStint>(entity =>
            {
                entity.Property(e => e.Note).HasMaxLength(1024);
            });

            modelBuilder.Entity<KeypadCarAppConfig>(entity =>
            {
                entity.ToTable("KeypadCarAppConfig");
            });

            modelBuilder.Entity<RaceEventSetting>(entity =>
            {
                entity.Property(e => e.CarIds).HasMaxLength(50);

                entity.Property(e => e.ControlLogParameter).HasMaxLength(250);

                entity.Property(e => e.ControlLogSmsUserSubscriptions).HasMaxLength(100);

                entity.Property(e => e.ControlLogType).HasMaxLength(100);

                entity.Property(e => e.EventTimeZoneId).HasMaxLength(120);

                entity.Property(e => e.RaceHeroEventId).HasMaxLength(100);
            });

            modelBuilder.Entity<TpmsConfig>(entity =>
            {
                entity.ToTable("TpmsConfig");

                entity.Property(e => e.ConvertToUsunits).HasColumnName("ConvertToUSUnits");

                entity.Property(e => e.Lfsensor).HasColumnName("LFSensor");

                entity.Property(e => e.Lrsensor).HasColumnName("LRSensor");

                entity.Property(e => e.Rfsensor).HasColumnName("RFSensor");

                entity.Property(e => e.Rrsensor).HasColumnName("RRSensor");
            });

            modelBuilder.Entity<UdpTelemetryConfig>(entity =>
            {
                entity.ToTable("UdpTelemetryConfig");

                entity.Property(e => e.DestinationIp).IsRequired();

                entity.Property(e => e.LocalNicName).IsRequired();
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
