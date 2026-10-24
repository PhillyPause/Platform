using ManyHands.Data.Discord;
using ManyHands.Data.Email;
using ManyHands.Data.Lookup;
using ManyHands.Data.Workers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ManyHands.Data;

public static class ManyHandsServiceCollectionExtensions
{
    /// <summary>True when <c>ManyHands:Enabled</c> is set. Everything else hangs off this.</summary>
    public static bool IsManyHandsEnabled(this IConfiguration configuration) =>
        configuration.GetSection(ManyHandsOptions.SectionName).GetValue<bool>(nameof(ManyHandsOptions.Enabled));

    /// <summary>
    /// The data source, the repositories and the background workers. Call only
    /// when <see cref="IsManyHandsEnabled"/> is true: with it off, nothing here
    /// is registered and nothing reaches for a database.
    /// </summary>
    public static IServiceCollection AddManyHands(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Column mapping must be set before the first query, and a silently
        // unmapped record is far harder to spot than a startup failure.
        DapperConfiguration.Configure();

        var connectionString = configuration.GetConnectionString(ManyHandsOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"ManyHands:Enabled is true but ConnectionStrings:{ManyHandsOptions.ConnectionStringName} is not set.");

        services.AddOptions<ManyHandsOptions>()
            .Bind(configuration.GetSection(ManyHandsOptions.SectionName));

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddSingleton<ActivityTypeRepository>();
        services.AddSingleton<ActivityRepository>();
        services.AddSingleton<VolunteerRepository>();
        services.AddSingleton<SignupRepository>();
        services.AddSingleton<AuthTokenRepository>();
        services.AddSingleton<OutboxRepository>();
        services.AddSingleton<EmailMessageRepository>();
        services.AddSingleton<NotificationQueries>();
        services.AddSingleton<VolunteerDataRepository>();
        services.AddSingleton<MemberAdminRepository>();
        services.AddSingleton<CalendarRepository>();
        services.AddSingleton<ActivityAdminRepository>();
        services.AddSingleton<ReportingRepository>();
        services.AddSingleton<PingRepository>();
        services.AddSingleton<InviteRepository>();
        services.AddSingleton<SelfCheckInRepository>();
        services.AddSingleton<CrmRepository>();
        services.AddSingleton<CrmEmailRepository>();
        services.AddSingleton<FederalOfficialsRepository>();

        // Sign-in cookies and antiforgery tokens survive a deploy only if the
        // keys that sign them do.
        services.AddDataProtection().SetApplicationName(Core.Organisation.Name);
        services.AddOptions<KeyManagementOptions>()
            .Configure<NpgsqlDataSource>((options, db) => options.XmlRepository = new DataProtectionKeyStore(db));

        services.AddManyHandsEmail(configuration);

        // The background work, in this process: there is one instance, so
        // there is nothing to coordinate.
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<ReminderWorker>();

        services.AddFederalOfficials(configuration);
        services.AddDiscord(configuration);

        return services;
    }

    /// <summary>
    /// The bot. The token is the switch: with one, the gateway connects and is
    /// the sender; without one, every Discord call is a log line, so development
    /// and CI need no bot. The role sweep runs either way and simulates.
    /// </summary>
    private static void AddDiscord(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DiscordOptions>()
            .Bind(configuration.GetSection(DiscordOptions.SectionName))
            .Validate(o => !o.Enabled || o.HasToken,
                "Discord:Enabled is true but Discord:BotToken is not set.")
            .Validate(o => !o.HasToken || o.GuildId is not null,
                "Discord:BotToken is set but Discord:GuildId is not; the bot would not know which server is ours.")
            .Validate(o => o.SendDelay >= TimeSpan.Zero && o.SendDelay <= TimeSpan.FromSeconds(10),
                "Discord:SendDelay must be between zero and ten seconds.")
            .Validate(o => o.RoleSyncInterval >= TimeSpan.FromMinutes(1) && o.RoleSyncInterval <= TimeSpan.FromDays(1),
                "Discord:RoleSyncInterval must be between a minute and a day.")
            .ValidateOnStart();

        services.AddSingleton<DiscordQueries>();
        services.AddSingleton<DiscordLinks>();
        services.AddSingleton<DiscordCommands>();

        var options = configuration.GetSection(DiscordOptions.SectionName).Get<DiscordOptions>() ?? new();
        if (options.HasToken)
        {
            services.AddSingleton<DiscordGateway>();
            services.AddSingleton<IDiscordSender>(sp => sp.GetRequiredService<DiscordGateway>());
            services.AddHostedService(sp => sp.GetRequiredService<DiscordGateway>());
        }
        else
        {
            services.AddSingleton<IDiscordSender, LoggingDiscordSender>();
        }

        services.AddHostedService<DiscordRoleSyncWorker>();
    }

    /// <summary>
    /// The weekly refresh of the Congress directory. The client is wired either
    /// way — constructing one sends nothing — and <c>FederalOfficials:Enabled</c>,
    /// false by default, decides whether the worker ever calls it.
    /// </summary>
    private static void AddFederalOfficials(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FederalOfficialsOptions>()
            .Bind(configuration.GetSection(FederalOfficialsOptions.SectionName))
            .Validate(o => !o.Enabled || Uri.TryCreate(o.SourceUrl, UriKind.Absolute, out _),
                "FederalOfficials:SourceUrl must be an absolute URL.")
            // PeriodicTimer's ceiling is about 49 days, and a directory refreshed
            // less than monthly is not one anyone should trust.
            .Validate(o => o.RefreshInterval > TimeSpan.Zero && o.RefreshInterval <= TimeSpan.FromDays(30),
                "FederalOfficials:RefreshInterval must be between zero and 30 days.")
            .ValidateOnStart();

        services.AddHttpClient<CongressLegislatorsSource>(http =>
        {
            // Megabytes of JSON over a link we do not control, once a week.
            http.Timeout = TimeSpan.FromMinutes(2);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PhillyPause/1.0 (+https://phillypause.org)");
        })
        // Served gzipped; without this the whole uncompressed file comes down.
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

        services.AddSingleton<IFederalOfficialsSource>(sp => sp.GetRequiredService<CongressLegislatorsSource>());
        services.AddHostedService<FederalOfficialsSyncWorker>();
    }

    private static void AddManyHandsEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            // Fail at startup, not at the first volunteer signup.
            .Validate(o => !o.UsesResend || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Email:Provider is 'resend' but Email:ApiKey is not set.")
            .Validate(o => o.FromAddress.Contains('@'),
                "Email:FromAddress must be an email address.")
            .Validate(o => Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out _),
                "Email:PublicBaseUrl must be an absolute URL; it builds the link in every email.")
            .ValidateOnStart();

        var options = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new();

        if (options.UsesResend)
        {
            services.AddHttpClient<ResendEmailSender>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
                http.BaseAddress = new Uri("https://api.resend.com/");
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
                http.Timeout = TimeSpan.FromSeconds(15);
            });
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());
        }
        else
        {
            // The default. Development needs no mail account and no secrets to
            // run the whole signup and sign-in flow; links appear in the log.
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        services.AddSingleton<EmailDispatcher>();
        services.AddSingleton<OutreachEmail>();
    }

    /// <summary>
    /// Applies pending migrations. Call once, after the app is built and before
    /// it runs; it throws if a script fails, which stops the app starting.
    /// </summary>
    public static void MigrateManyHands(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var options = services.GetRequiredService<IOptions<ManyHandsOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ManyHandsDatabase));

        // Seeding writes invented people into whatever database it is pointed
        // at, and DbUp never runs a script twice, so nothing would take them
        // out again. Asked for anywhere but a developer's machine, refuse to
        // start rather than seed: the previous deployment keeps serving.
        if (options.Seed && !services.GetRequiredService<IHostEnvironment>().IsDevelopment())
            throw new InvalidOperationException(
                "ManyHands:Seed is set outside Development. Seed data is fictional and never goes in a real database.");

        ManyHandsDatabase.Migrate(
            configuration.GetConnectionString(ManyHandsOptions.ConnectionStringName)!,
            options.Seed,
            logger);
    }
}
