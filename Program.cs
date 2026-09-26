using GameInventoryApi.Data;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using GameInventoryApi.Services;
using GameInventoryApi.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using System.Text;

BsonSerializer.RegisterSerializer(new EnumSerializer<RewardType>(BsonType.String));
BsonSerializer.RegisterSerializer(new EnumSerializer<PullType>(BsonType.String));
BsonSerializer.RegisterSerializer(new EnumSerializer<SceneType>(BsonType.String));

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<ServerConfig>(builder.Configuration.GetSection("ServerConfig"));

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = new MongoClient(settings.ConnectionString);
    return client.GetDatabase(settings.DatabaseName);
});

builder.Services.AddSingleton<IGridFSBucket>(sp =>
    new GridFSBucket(sp.GetRequiredService<IMongoDatabase>(), new GridFSBucketOptions
    {
        BucketName = "support_attachments"
    }));

builder.Services.AddSingleton<ServerState>();

builder.Services.AddScoped<IMongoRepository<PlayerProfile>>(sp =>
    new MongoRepository<PlayerProfile>(sp.GetRequiredService<IMongoDatabase>(), "PlayerProfiles"));

builder.Services.AddScoped<IMongoRepository<User>>(sp =>
    new MongoRepository<User>(sp.GetRequiredService<IMongoDatabase>(), "Users"));

builder.Services.AddScoped<IMongoRepository<TeamLoadoutDocument>>(sp =>
    new MongoRepository<TeamLoadoutDocument>(sp.GetRequiredService<IMongoDatabase>(), "TeamLoadouts"));

builder.Services.AddScoped<IMongoRepository<MatchSession>>(sp =>
new MongoRepository<MatchSession>(sp.GetRequiredService<IMongoDatabase>(), "MatchSessions"));

builder.Services.AddScoped<IMongoRepository<MatchHistory>>(sp =>
    new MongoRepository<MatchHistory>(sp.GetRequiredService<IMongoDatabase>(), "MatchHistory"));

builder.Services.AddScoped<IMongoRepository<MatchQueueEntry>>(sp =>
    new MongoRepository<MatchQueueEntry>(sp.GetRequiredService<IMongoDatabase>(), "MatchQueue"));

builder.Services.AddScoped<IMongoRepository<PurchaseOrder>>(sp =>
    new MongoRepository<PurchaseOrder>(sp.GetRequiredService<IMongoDatabase>(), "PurchaseOrders"));

builder.Services.AddScoped<IMongoRepository<Notification>>(sp =>
    new MongoRepository<Notification>(sp.GetRequiredService<IMongoDatabase>(), "Notifications"));

builder.Services.AddScoped<IMongoRepository<SupportMessage>>(sp =>
    new MongoRepository<SupportMessage>(sp.GetRequiredService<IMongoDatabase>(), "SupportMessages"));

builder.Services.AddScoped<IMongoRepository<TopUpPack>>(sp =>
    new MongoRepository<TopUpPack>(sp.GetRequiredService<IMongoDatabase>(), "TopUpPacks"));

builder.Services.AddScoped<IMongoRepository<UnitDefinition>>(sp =>
    new MongoRepository<UnitDefinition>(sp.GetRequiredService<IMongoDatabase>(), "UnitDefinitions"));

builder.Services.AddScoped<IMongoRepository<SkillDefinition>>(sp =>
    new MongoRepository<SkillDefinition>(sp.GetRequiredService<IMongoDatabase>(), "SkillDefinitions"));

builder.Services.AddScoped<IMongoRepository<ClassDefinition>>(sp =>
    new MongoRepository<ClassDefinition>(sp.GetRequiredService<IMongoDatabase>(), "ClassDefinitions"));

builder.Services.AddScoped<IMongoRepository<WeaponDefinition>>(sp =>
    new MongoRepository<WeaponDefinition>(sp.GetRequiredService<IMongoDatabase>(), "WeaponDefinitions"));

builder.Services.AddScoped<IMongoRepository<TrinketDefinition>>(sp =>
    new MongoRepository<TrinketDefinition>(sp.GetRequiredService<IMongoDatabase>(), "TrinketDefinitions"));

builder.Services.AddScoped<IUnitDefinitionService, UnitDefinitionService>();
builder.Services.AddScoped<ISkillDefinitionService, SkillDefinitionService>();
builder.Services.AddScoped<IClassDefinitionService, ClassDefinitionService>();
builder.Services.AddScoped<IWeaponDefinitionService, WeaponDefinitionService>();
builder.Services.AddScoped<ITrinketDefinitionService, TrinketDefinitionService>();

builder.Services.AddScoped<IMatchQueueService, MatchQueueService>();
builder.Services.AddSingleton<MatchmakingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchmakingService>());

builder.Services.AddScoped<ITeamLoadoutService, TeamLoadoutService>();

builder.Services.AddScoped<IMatchSessionService, MatchSessionService>();
builder.Services.AddScoped<IMatchHistoryService, MatchHistoryService>();
builder.Services.AddScoped<IPlayerProfileService, PlayerProfileService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<ITopUpPackService, TopUpPackService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ISupportService, SupportService>();

builder.Services.AddScoped<IMongoRepository<GachaBanner>>(sp =>
    new MongoRepository<GachaBanner>(sp.GetRequiredService<IMongoDatabase>(), "GachaBanners"));
builder.Services.AddScoped<IGachaBannerService, GachaBannerService>();
builder.Services.AddScoped<IGachaService, GachaService>();
builder.Services.AddScoped<IMongoRepository<ChapterConfig>>(sp =>
    new MongoRepository<ChapterConfig>(sp.GetRequiredService<IMongoDatabase>(), "ChapterConfigs"));
builder.Services.AddScoped<IChapterConfigService, ChapterConfigService>();

builder.Services.AddScoped<IMongoRepository<StoryProgress>>(sp =>
    new MongoRepository<StoryProgress>(sp.GetRequiredService<IMongoDatabase>(), "StoryProgress"));
builder.Services.AddScoped<IStoryProgressService, StoryProgressService>();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.Configure<SteamSettings>(builder.Configuration.GetSection("SteamSettings"));
builder.Services.AddHttpClient<ISteamAuthService, SteamAuthService>();
builder.Services.Configure<GoogleSettings>(builder.Configuration.GetSection("GoogleSettings"));
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSignalR();

builder.Services.AddAuthorization();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Enter JWT with Bearer prefix",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SupportHub>("/hubs/support");

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
    await SeedData.InitializeAsync(database);
}


Console.WriteLine("=== FULL GAME SERVER IS RUNNING ===");
Console.WriteLine("REST API     → http://localhost:5276/swagger");


app.Run();