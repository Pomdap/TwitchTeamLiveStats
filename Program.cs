using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;
using TwitchLib.PubSub.Interfaces;

public class OnSuperviseArgs : EventArgs
{
    public string[] ChannelIds { get; set; }
}
public enum ChannelStatus
{
    Down,
    Live,
    Timeout
}

public class OnChannelUpdateArgs : EventArgs
{

    public string ChannelId { get; set; }
    public ChannelStatus Status { get; set; }
    public int Viewers { get; set; }
}

public class TeamSupervisor
{
    private readonly ILogger<TeamSupervisor> _logger;
    private readonly ITwitchPubSub _twitchPubSub;
    private readonly TeamSupervisorOptions _options;

    private Dictionary<string, DateTime> _channelslUpdate = new Dictionary<string, DateTime>();
    private ReaderWriterLockSlim _channelslUpdateLock = new ReaderWriterLockSlim();

    public event EventHandler<OnSuperviseArgs> OnSupervise;
    public event EventHandler<OnChannelUpdateArgs> OnChannelUpdate;

    public TeamSupervisor(ILoggerFactory loggerFactory, ITwitchPubSub twitchPubSub, IOptions<TeamSupervisorOptions> teamSupervisorOptions)
    {
        _options = teamSupervisorOptions.Value;

        _channelslUpdateLock.EnterWriteLock();
        foreach (var channelId in _options.ChannelIds)
        {
            _channelslUpdate[channelId] = DateTime.Now;
        }
        _channelslUpdateLock.ExitWriteLock();

        _logger = loggerFactory.CreateLogger<TeamSupervisor>();
        _twitchPubSub = twitchPubSub;

        _twitchPubSub.OnPubSubServiceConnected += TwitchPubSub_OnPubSubServiceConnected;

        _twitchPubSub.OnStreamUp += TwitchPubSub_OnStreamUp;
        _twitchPubSub.OnStreamDown += TwitchPubSub_OnStreamDown;
        _twitchPubSub.OnViewCount += TwitchPubSub_OnViewCount;
    }

    private void TwitchPubSub_OnPubSubServiceConnected(object sender, EventArgs e)
    {
        foreach (var channelId in _options.ChannelIds)
        {
            _logger.LogInformation($"Listen video playback for channel #{channelId}");
            _twitchPubSub.ListenToVideoPlayback(channelId);
        }

        _logger.LogDebug($"Send channel list");
        _twitchPubSub.SendTopics();
    }

    private void TwitchPubSub_OnStreamUp(object sender, OnStreamUpArgs e)
    {
        _channelslUpdateLock.EnterWriteLock();
        _channelslUpdate[e.ChannelId] = DateTime.Now;
        _channelslUpdateLock.ExitWriteLock();

        OnChannelUpdate?.Invoke(
            this,
            new OnChannelUpdateArgs
            {
                ChannelId = e.ChannelId,
                Status = ChannelStatus.Live
            }
        );
    }

    private void TwitchPubSub_OnStreamDown(object sender, OnStreamDownArgs e)
    {
        _channelslUpdateLock.EnterWriteLock();
        _channelslUpdate.Remove(e.ChannelId);
        _channelslUpdateLock.ExitWriteLock();

        OnChannelUpdate?.Invoke(
            this, 
            new OnChannelUpdateArgs {
                ChannelId = e.ChannelId,
                Status = ChannelStatus.Down
            }
        );
    }

    private void TwitchPubSub_OnViewCount(object sender, OnViewCountArgs e)
    {
        _channelslUpdateLock.EnterWriteLock();
        _channelslUpdate[e.ChannelId] = DateTime.Now;
        _channelslUpdateLock.ExitWriteLock();

        OnChannelUpdate?.Invoke(
            this,
            new OnChannelUpdateArgs
            {
                ChannelId = e.ChannelId,
                Status = ChannelStatus.Live,
                Viewers = e.Viewers
            }
        );
    }

    public void Supervise(CancellationToken cancellationToken = default(CancellationToken))
    {
        OnSupervise?.Invoke(this, new OnSuperviseArgs { ChannelIds = _options.ChannelIds });
        _twitchPubSub.Connect();

        var delay = TimeSpan.Parse(_options.TimeoutInterval);
        var refresh = delay / 10;

        while (!cancellationToken.IsCancellationRequested)
        {
            _channelslUpdateLock.EnterUpgradeableReadLock();
            foreach (var item in _channelslUpdate)
            {
                if (item.Value < DateTime.Now - delay)
                {
                    OnChannelUpdate?.Invoke(
                        this,
                        new OnChannelUpdateArgs
                        {
                            ChannelId = item.Key,
                            Status = ChannelStatus.Timeout,
                            Viewers = 0
                        }
                    );

                    _channelslUpdateLock.EnterWriteLock();
                    _channelslUpdate.Remove(item.Key);
                    _channelslUpdateLock.ExitWriteLock();
                }
            }
            _channelslUpdateLock.ExitUpgradeableReadLock();

            Task.Delay(
                refresh, 
                cancellationToken
            ).GetAwaiter().GetResult();
        }
    }

    ~TeamSupervisor()
    {
        _twitchPubSub.Disconnect();
    }
}

public class TeamWritter
{
    private readonly ILogger<TeamWritter> _logger;
    private TeamSupervisor _teamSupervisor;
    private TeamWritterOptions _teamWritterOptions;

    private Dictionary<string, int> _channelsViewers = new Dictionary<string, int>();
    private int _channelsViewersTotal = 0;
    private ReaderWriterLockSlim _channelsViewersLock = new ReaderWriterLockSlim();

    public TeamWritter(ILoggerFactory loggerFactory, IOptions<TeamWritterOptions> teamWritterOptions, TeamSupervisor teamSupervisor)
    {
        _logger = loggerFactory.CreateLogger<TeamWritter>();
        _teamSupervisor = teamSupervisor;
        _teamWritterOptions = teamWritterOptions.Value;

        Directory.CreateDirectory(_teamWritterOptions.OutputDirectory);
        System.IO.DirectoryInfo directoryInfo = new DirectoryInfo(_teamWritterOptions.OutputDirectory);
        foreach (FileInfo fileInfo in directoryInfo.GetFiles())
        {
            fileInfo.Delete();
        }

        teamSupervisor.OnSupervise += TeamSupervisor_OnSupervise;
        teamSupervisor.OnChannelUpdate += TeamSupervisor_OnChannelUpdate;
    }

    private void TeamSupervisor_OnChannelUpdate(object sender, OnChannelUpdateArgs e)
    {
        _logger.LogTrace($"{e.ChannelId} {e.Status} {e.Viewers}");

        _channelsViewersLock.EnterUpgradeableReadLock();

        int previousValue = _channelsViewers.GetValueOrDefault(e.ChannelId, 0);

        bool channelValueHasChanged = (previousValue != e.Viewers);
        bool deleteChannel = e.Status == ChannelStatus.Down || e.Status == ChannelStatus.Timeout;
        bool newChannel = e.Status == ChannelStatus.Live && !_channelsViewers.ContainsKey(e.ChannelId);

        if (channelValueHasChanged || deleteChannel || newChannel)
        {
            _channelsViewersLock.EnterWriteLock();
            if (newChannel)
            {
                _channelsViewers[e.ChannelId] = e.Viewers;
                if (!string.IsNullOrEmpty(_teamWritterOptions.LiveChannelCountFile))
                {
                    File.WriteAllText(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.LiveChannelCountFile), _channelsViewers.Count.ToString());
                }
            }
            
            if(deleteChannel)
            {
                _channelsViewers.Remove(e.ChannelId);
                if (!string.IsNullOrEmpty(_teamWritterOptions.LiveChannelCountFile))
                {
                    File.WriteAllText(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.LiveChannelCountFile), _channelsViewers.Count.ToString());
                }
            }
            
            if (channelValueHasChanged)
            {
                _channelsViewers[e.ChannelId] = e.Viewers;
                if (!string.IsNullOrEmpty(_teamWritterOptions.IndividualChannelFileFormat))
                {
                    File.WriteAllText(String.Format(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.IndividualChannelFileFormat), e.ChannelId), e.Viewers.ToString());
                }

                _channelsViewersTotal = _channelsViewers.Sum(data => data.Value);
                if (!string.IsNullOrEmpty(_teamWritterOptions.TotalViewersFile))
                {
                    File.WriteAllText(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.TotalViewersFile), _channelsViewersTotal.ToString());
                }
            }

            _channelsViewersLock.ExitWriteLock();
        }

        _channelsViewersLock.ExitUpgradeableReadLock();
    }

    private void TeamSupervisor_OnSupervise(object sender, OnSuperviseArgs e)
    {
        _logger.LogTrace($"{e.ChannelIds.Length}");

        _channelsViewersLock.EnterWriteLock();
        foreach (var channelId in e.ChannelIds)
        {
            if (!string.IsNullOrEmpty(_teamWritterOptions.IndividualChannelFileFormat))
            {
                File.WriteAllText(String.Format(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.IndividualChannelFileFormat), channelId), "0");
            }
        }

        if (!string.IsNullOrEmpty(_teamWritterOptions.TotalViewersFile))
        {
            File.WriteAllText(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.TotalViewersFile), "0");
        }

        if (!string.IsNullOrEmpty(_teamWritterOptions.LiveChannelCountFile))
        {
            File.WriteAllText(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.LiveChannelCountFile), "0");
        }

        if (!string.IsNullOrEmpty(_teamWritterOptions.LiveChannelTotalFile))
        {
            File.WriteAllText(Path.Combine(_teamWritterOptions.OutputDirectory, _teamWritterOptions.LiveChannelTotalFile), e.ChannelIds.Length.ToString());
        }
        _channelsViewersLock.ExitWriteLock();
    }
}

public class TeamSupervisorOptions
{
    public const string Section = "TeamSupervisor";
    public string[] ChannelIds { get; set; }
    public string TimeoutInterval { get; set; } = TimeSpan.FromMinutes(3).ToString();
}

public class TeamWritterOptions
{
    public const string Section = "TeamWritter";
    public string OutputDirectory { get; set; }
    public string TotalViewersFile { get; set; }
    public string LiveChannelCountFile { get; set; }
    public string LiveChannelTotalFile { get; set; }
    public string IndividualChannelFileFormat { get; set; }
}

public class Program
{
    private static ILogger<Program> _logger;
    private static TeamSupervisor _teamSupervisor;
    private static TeamWritter _teamWritter;

    public static void Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var serviceProvider = new ServiceCollection()
            .AddLogging(loggingBuilder => loggingBuilder.AddSimpleConsole().SetMinimumLevel(LogLevel.Trace))
            .AddSingleton<TeamSupervisor>()
            .AddSingleton<TeamWritter>()
            .AddTransient<ITwitchPubSub, TwitchPubSub>()
            .AddOptions<TeamSupervisorOptions>().Bind(config.GetSection(TeamSupervisorOptions.Section)).Services
            .AddOptions<TeamWritterOptions>().Bind(config.GetSection(TeamWritterOptions.Section)).Services
            .BuildServiceProvider();

        _logger = serviceProvider.GetService<ILoggerFactory>()
            .CreateLogger<Program>();

        _logger.LogTrace("Starting application");

        _teamSupervisor = serviceProvider.GetService<TeamSupervisor>();
        _teamWritter = serviceProvider.GetService<TeamWritter>();
        _teamSupervisor.Supervise();

        System.Console.ReadLine();
    }
}