using GameInventoryApi.Data;
using GameInventoryApi.Models;
using GameInventoryApi.Repositories;
using GameInventoryApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Text;

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
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.Configure<SteamSettings>(builder.Configuration.GetSection("SteamSettings"));
builder.Services.AddHttpClient<ISteamAuthService, SteamAuthService>();

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
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
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
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
    await SeedData.InitializeAsync(database);
}


Console.WriteLine("=== FULL GAME SERVER IS RUNNING ===");
Console.WriteLine("REST API     → http://localhost:5276/swagger");


app.Run();